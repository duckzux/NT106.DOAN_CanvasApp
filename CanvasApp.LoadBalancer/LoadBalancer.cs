using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CanvasApp.Common;

namespace CanvasApp.LoadBalancer
{
    /// <summary>
    /// TCP proxy 2 pool: Auth (round-robin) + Canvas (least-loaded by RoomCount + room-affinity).
    /// Mỗi connection mới: LB peek message JSON đầu tiên để biết pool cần điều hướng và
    /// (nếu là <see cref="MessageType.ROOM_JOIN"/>) tra cứu routing table theo RoomId.
    /// </summary>
    public class LoadBalancer
    {
        private readonly IReadOnlyList<ServerInfo> _authPool;
        private readonly IReadOnlyList<ServerInfo> _canvasPool;
        private readonly int _listenPort;
        private readonly int _bufferSize;
        private readonly int _peekTimeoutMs;
        private readonly int _peekMaxBytes;

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private int _authRR;
        private int _canvasRR;

        // ── Room routing table ─────────────────────────────────────────────
        // Sticky mapping: roomId → Canvas server currently hosting that room.
        // First user to join a not-yet-mapped room picks the least-loaded canvas; every later
        // joiner of the same room is steered to the same server so canvas state stays consistent.
        // Mapping is also populated by sniffing ROOM_CREATE_RESULT so creators "claim" the server.
        private readonly ConcurrentDictionary<string, ServerInfo> _roomRouting
            = new ConcurrentDictionary<string, ServerInfo>(StringComparer.OrdinalIgnoreCase);

        public LoadBalancer(
            IEnumerable<ServerInfo> servers,
            int listenPort,
            int bufferSize = 8192,
            int peekTimeoutMs = 5000,
            int peekMaxBytes = 65536)
        {
            if (servers == null) throw new ArgumentNullException(nameof(servers));
            var list = servers.ToList();
            _authPool = list.Where(s => s.Type == ServerType.Auth).ToList();
            _canvasPool = list.Where(s => s.Type == ServerType.Canvas).ToList();
            if (_canvasPool.Count == 0) throw new ArgumentException("Canvas pool is empty");
            _listenPort = listenPort;
            _bufferSize = bufferSize;
            _peekTimeoutMs = peekTimeoutMs;
            _peekMaxBytes = peekMaxBytes;
        }

        public async Task StartAsync()
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, _listenPort);
            _listener.Start();

            Console.WriteLine("╔══════════════════════════════════════════════╗");
            Console.WriteLine($"║   LOAD BALANCER listening on :{_listenPort}             ║");
            Console.WriteLine("╠══════════════════════════════════════════════╣");
            Console.WriteLine($"║   Auth pool:   {_authPool.Count} backend(s)                  ║");
            Console.WriteLine($"║   Canvas pool: {_canvasPool.Count} backend(s)                  ║");
            Console.WriteLine("╚══════════════════════════════════════════════╝");
            foreach (var s in _authPool)   Console.WriteLine($"   AUTH   • {s.Endpoint}");
            foreach (var s in _canvasPool) Console.WriteLine($"   CANVAS • {s.Endpoint}");

            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(); }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LB] Accept error: {ex.Message}");
                    continue;
                }
                _ = Task.Run(() => HandleClientAsync(client));
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
        }

        // ── Pool picking ──────────────────────────────────────────────────

        private ServerInfo PickAuth()
        {
            var healthy = _authPool.Where(s => s.IsHealthy).ToList();
            if (healthy.Count == 0) return null;
            int idx = (Interlocked.Increment(ref _authRR) & 0x7fffffff) % healthy.Count;
            return healthy[idx];
        }

        /// <summary>Least-loaded Canvas: ưu tiên RoomCount thấp, sau đó ActiveConnections, round-robin tiebreak.</summary>
        private ServerInfo PickCanvasLeastLoaded()
        {
            var healthy = _canvasPool.Where(s => s.IsHealthy).ToList();
            if (healthy.Count == 0) return null;

            int minRooms = healthy.Min(s => s.RoomCount);
            var topRooms = healthy.Where(s => s.RoomCount == minRooms).ToList();
            if (topRooms.Count == 1) return topRooms[0];

            int minConns = topRooms.Min(s => s.ActiveConnections);
            var topConns = topRooms.Where(s => s.ActiveConnections == minConns).ToList();
            if (topConns.Count == 1) return topConns[0];

            int idx = (Interlocked.Increment(ref _canvasRR) & 0x7fffffff) % topConns.Count;
            return topConns[idx];
        }

        /// <summary>
        /// Room-affinity routing via an explicit table:
        ///   1) If <paramref name="roomId"/> is already bound to a healthy server → return it.
        ///   2) Otherwise pick the least-loaded canvas, store the mapping, return it.
        /// Stickiness is what guarantees that every user joining the same room reaches the same
        /// Canvas Server — that server's in-RAM <c>RoomManager</c> holds the canonical draw
        /// actions and member list, so co-located clients can't drift out of sync.
        /// </summary>
        private ServerInfo RouteForRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return PickCanvasLeastLoaded();

            // Sticky lookup
            if (_roomRouting.TryGetValue(roomId, out var bound))
            {
                if (bound.IsHealthy)
                {
                    Console.WriteLine($"[ROUTE] room {roomId} -> {bound.Endpoint} (sticky)");
                    return bound;
                }
                // Bound server is down — drop the mapping so we can pick a fresh one
                _roomRouting.TryRemove(roomId, out _);
                Console.WriteLine($"[ROUTE] room {roomId} previous binding {bound.Endpoint} is DOWN; rebinding");
            }

            var picked = PickCanvasLeastLoaded();
            if (picked == null) return null;

            // First writer wins — concurrent joins for a brand-new room all settle on one server
            var actual = _roomRouting.GetOrAdd(roomId, picked);
            // Only the writer that actually inserted the mapping should increment the counter
            // (so concurrent losers don't double-count). ReferenceEquals because GetOrAdd
            // returns the existing value if it lost the race.
            if (ReferenceEquals(actual, picked))
                actual.IncrementRoomCount();
            Console.WriteLine($"[ROUTE] room {roomId} -> {actual.Endpoint} (newly bound, healthyCount={_canvasPool.Count(s => s.IsHealthy)})");
            return actual;
        }

        /// <summary>
        /// Register a room → server binding from outside the per-connection path. Used after
        /// sniffing ROOM_CREATE_RESULT so the creator's server is locked in before the first
        /// remote join arrives.
        /// </summary>
        private void RegisterRoom(string roomId, ServerInfo server)
        {
            if (string.IsNullOrEmpty(roomId) || server == null) return;
            bool inserted = false;
            _roomRouting.AddOrUpdate(
                roomId,
                _ => { inserted = true; return server; },
                (key, existing) =>
                {
                    if (existing.IsHealthy) return existing;
                    // Replacing an unhealthy mapping: decrement the dead one, increment fresh.
                    existing.DecrementRoomCount();
                    inserted = true;
                    return server;
                });
            if (inserted) server.IncrementRoomCount();
        }

        // ── Per-connection proxy ──────────────────────────────────────────

        private async Task HandleClientAsync(TcpClient client)
        {
            var clientEp = SafeEndpoint(client);
            ServerInfo target = null;
            TcpClient backend = null;
            string boundRoomId = null;
            string firstLine = null;

            try
            {
                var clientStream = client.GetStream();

                // 1) Peek the first JSON line so we can route by Type / RoomId.
                try { firstLine = await ReadOneLineAsync(clientStream, _peekMaxBytes, _peekTimeoutMs); }
                catch (TimeoutException) { Console.WriteLine($"[LB] {clientEp} dropped — no first message in {_peekTimeoutMs}ms"); return; }
                catch (Exception ex)     { Console.WriteLine($"[LB] {clientEp} peek error: {ex.Message}"); return; }

                if (string.IsNullOrEmpty(firstLine)) { Console.WriteLine($"[LB] {clientEp} closed before first message"); return; }

                // 2) Parse & decide.
                Message msg = null;
                try { msg = Message.FromJson(firstLine); } catch { /* malformed → default canvas */ }

                string type = msg?.Type ?? "";

                if (type == MessageType.AUTH_LOGIN || type == MessageType.AUTH_REGISTER)
                {
                    target = PickAuth();
                    if (target == null)
                    {
                        Console.WriteLine($"[LB] {clientEp} rejected — no healthy Auth backend");
                        return;
                    }
                }
                else if (type == MessageType.ROOM_JOIN)
                {
                    var req = SafeGetData<JoinRoomRequest>(msg);
                    if (req != null && !string.IsNullOrEmpty(req.RoomId))
                    {
                        target = RouteForRoom(req.RoomId);
                        boundRoomId = req.RoomId;
                    }
                    else
                    {
                        target = PickCanvasLeastLoaded();
                    }
                    if (target == null)
                    {
                        Console.WriteLine($"[LB] {clientEp} rejected — no healthy Canvas backend");
                        return;
                    }
                }
                else if (type == MessageType.ROOM_RESOLVE)
                {
                    // Same affinity as ROOM_JOIN — the resolve must land on the server that
                    // owns the room so the password check uses the canonical room metadata.
                    var req = SafeGetData<ResolveRoomRequest>(msg);
                    if (req != null && !string.IsNullOrEmpty(req.RoomId))
                    {
                        target = RouteForRoom(req.RoomId);
                        boundRoomId = req.RoomId;
                    }
                    else
                    {
                        target = PickCanvasLeastLoaded();
                    }
                    if (target == null)
                    {
                        Console.WriteLine($"[LB] {clientEp} rejected — no healthy Canvas backend");
                        return;
                    }
                }
                else if (type == MessageType.ROOM_JOIN_BY_CODE)
                {
                    // If roomId is already resolved (from RESOLVE_INVITE_CODE), use room affinity
                    var codeReq = SafeGetData<InviteCodeRequest>(msg);
                    if (codeReq != null && !string.IsNullOrEmpty(codeReq.RoomId))
                    {
                        target = RouteForRoom(codeReq.RoomId);
                        boundRoomId = codeReq.RoomId;
                    }
                    else
                    {
                        // Fallback: route to least-loaded (user hasn't called RESOLVE_INVITE_CODE first)
                        target = PickCanvasLeastLoaded();
                    }
                    if (target == null)
                    {
                        Console.WriteLine($"[LB] {clientEp} rejected — no healthy Canvas backend");
                        return;
                    }
                }
                else
                {
                    // ROOM_LIST, ROOM_CREATE, RESOLVE_INVITE_CODE, PING, malformed → Canvas least-loaded
                    target = PickCanvasLeastLoaded();
                    if (target == null)
                    {
                        Console.WriteLine($"[LB] {clientEp} rejected — no healthy Canvas backend");
                        return;
                    }
                }

                if (!target.TryAcquireConnection())
                {
                    Console.WriteLine($"[LB] {clientEp} rejected — {target.Endpoint} at MaxConnections");
                    return;
                }

                // 3) Open the backend socket.
                backend = new TcpClient();
                try
                {
                    var connect = backend.ConnectAsync(target.Host, target.Port);
                    var done = await Task.WhenAny(connect, Task.Delay(5000));
                    if (done != connect || connect.IsFaulted)
                    {
                        Console.WriteLine($"[LB] {clientEp} -> {target.Endpoint} connect failed; demoting");
                        target.IncrementFailCount(); target.IncrementFailCount(); target.IncrementFailCount(); // mark Down immediately
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LB] {clientEp} -> {target.Endpoint} connect error: {ex.Message}");
                    target.IncrementFailCount(); target.IncrementFailCount(); target.IncrementFailCount();
                    return;
                }

                Console.WriteLine($"[LB] {clientEp} -> {target} (first={type})");

                // 4) Forward the peeked first line, then pump bidirectionally.
                var backendStream = backend.GetStream();
                var firstBytes = Encoding.UTF8.GetBytes(firstLine + "\n");
                await backendStream.WriteAsync(firstBytes, 0, firstBytes.Length);
                await backendStream.FlushAsync();

                // ✅ For ROOM_CREATE we intercept the first response line to learn the new
                // room's Id, then register the routing mapping immediately. Without this,
                // a subsequent ROOM_JOIN for the freshly-created room could land on a
                // different server (least-loaded) before any user "claims" the binding.
                if (type == MessageType.ROOM_CREATE)
                {
                    try
                    {
                        var responseLine = await ReadOneLineAsync(backendStream, _peekMaxBytes, 10_000);
                        if (!string.IsNullOrEmpty(responseLine))
                        {
                            try
                            {
                                var respMsg = Message.FromJson(responseLine);
                                if (respMsg?.Type == MessageType.ROOM_CREATE_RESULT)
                                {
                                    var newRoom = SafeGetData<Room>(respMsg);
                                    if (newRoom != null && !string.IsNullOrEmpty(newRoom.Id))
                                    {
                                        RegisterRoom(newRoom.Id, target);
                                        Console.WriteLine($"[ROUTE] claim on create: {newRoom.Id} -> {target.Endpoint}");
                                    }
                                }
                            }
                            catch { /* unparseable — just forward */ }

                            var respBytes = Encoding.UTF8.GetBytes(responseLine + "\n");
                            await clientStream.WriteAsync(respBytes, 0, respBytes.Length);
                            await clientStream.FlushAsync();
                        }
                    }
                    catch (TimeoutException) { Console.WriteLine($"[LB] {clientEp} ROOM_CREATE_RESULT timeout"); }
                    catch (Exception ex) { Console.WriteLine($"[LB] {clientEp} sniff error: {ex.Message}"); }
                }

                var c2s = PumpAsync(client, backend, _bufferSize);
                var s2c = PumpAsync(backend, client, _bufferSize);
                await Task.WhenAny(c2s, s2c);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LB] {clientEp} proxy error: {ex.Message}");
            }
            finally
            {
                try { backend?.Close(); } catch { }
                try { client.Close(); } catch { }
                if (target != null)
                {
                    target.ReleaseConnection();
                    Console.WriteLine($"[LB] {clientEp} closed (backend {target.Endpoint} conns={target.ActiveConnections})");
                }
            }
        }

        private static async Task PumpAsync(TcpClient src, TcpClient dst, int bufferSize)
        {
            var buf = new byte[bufferSize];
            var inStream = src.GetStream();
            var outStream = dst.GetStream();
            try
            {
                int n;
                while ((n = await inStream.ReadAsync(buf, 0, buf.Length)) > 0)
                {
                    await outStream.WriteAsync(buf, 0, n);
                    await outStream.FlushAsync();
                }
            }
            catch (IOException) { /* peer closed */ }
            catch (ObjectDisposedException) { /* socket disposed */ }
            finally
            {
                try { dst.Client.Shutdown(SocketShutdown.Send); } catch { }
            }
        }

        /// <summary>
        /// Đọc 1 dòng JSON đầu từ NetworkStream (byte-by-byte, không buffer thừa).
        /// Bytes sau '\n' đầu tiên vẫn còn nguyên trong OS socket buffer — PumpAsync sẽ đọc tiếp.
        /// </summary>
        private static async Task<string> ReadOneLineAsync(NetworkStream stream, int maxBytes, int timeoutMs)
        {
            var buf = new List<byte>(256);
            var one = new byte[1];
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (buf.Count < maxBytes)
            {
                int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0) throw new TimeoutException();

                var readTask = stream.ReadAsync(one, 0, 1);
                var done = await Task.WhenAny(readTask, Task.Delay(remaining));
                if (done != readTask) throw new TimeoutException();

                int n = await readTask;
                if (n == 0) return buf.Count == 0 ? null : StripBom(Encoding.UTF8.GetString(buf.ToArray()));
                if (one[0] == (byte)'\n') break;
                if (one[0] != (byte)'\r') buf.Add(one[0]);
            }
            return StripBom(Encoding.UTF8.GetString(buf.ToArray()));
        }

        // Clients open StreamWriter(stream, Encoding.UTF8) which emits a UTF-8 BOM on the first write.
        // StreamReader-based backends strip it automatically, but our byte-level peek does not — without
        // this, the BOM makes the first JSON unparseable and routing falls through to the Canvas default.
        private const char Utf8Bom = '﻿';
        private static string StripBom(string s) =>
            !string.IsNullOrEmpty(s) && s[0] == Utf8Bom ? s.Substring(1) : s;

        private static T SafeGetData<T>(Message msg) where T : class
        {
            try { return msg?.GetData<T>(); }
            catch { return null; }
        }

        private static string SafeEndpoint(TcpClient c)
        {
            try { return c.Client.RemoteEndPoint?.ToString() ?? "?"; }
            catch { return "?"; }
        }
    }
}
