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

        // Serializes the (re)bind branch of RouteForRoom so two concurrent joiners for the same
        // unmapped/unhealthy-bound room can't end up on different Canvas servers. The healthy-bound
        // fast path stays lock-free.
        private readonly object _routingLock = new object();

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

            // Fast path: healthy binding already in the table — lock-free.
            if (_roomRouting.TryGetValue(roomId, out var bound) && bound.IsHealthy)
            {
                Console.WriteLine($"[ROUTE] room {roomId} -> {bound.Endpoint} (sticky)");
                return bound;
            }

            // Slow path: serialize so two threads racing to (re)bind agree on one server.
            // Without this lock the window between "TryRemove dead binding" and
            // "GetOrAdd fresh binding" lets concurrent joiners pick different least-loaded
            // servers — and although GetOrAdd is itself atomic, the loser of the race
            // would have already returned its candidate before observing the winner's.
            lock (_routingLock)
            {
                if (_roomRouting.TryGetValue(roomId, out bound) && bound.IsHealthy)
                {
                    Console.WriteLine($"[ROUTE] room {roomId} -> {bound.Endpoint} (sticky)");
                    return bound;
                }

                // Drop the dead binding AND decrement its room count — without the
                // decrement, a server that goes down then comes back stays inflated forever.
                if (bound != null && _roomRouting.TryRemove(roomId, out var removed))
                {
                    removed.DecrementRoomCount();
                    Console.WriteLine($"[ROUTE] room {roomId} previous binding {removed.Endpoint} is DOWN; rebinding");
                }

                var picked = PickCanvasLeastLoaded();
                if (picked == null) return null;
                _roomRouting[roomId] = picked;
                picked.IncrementRoomCount();
                Console.WriteLine($"[ROUTE] room {roomId} -> {picked.Endpoint} (newly bound, healthyCount={_canvasPool.Count(s => s.IsHealthy)})");
                return picked;
            }
        }

        /// <summary>
        /// Read-only variant of <see cref="RouteForRoom"/> used by ROOM_DELETE and
        /// ROOM_UPDATE_PASSWORD: if a healthy binding exists we return it (so the request
        /// lands on the server that actually owns the room); if not, we fall back to a
        /// least-loaded canvas WITHOUT claiming the binding. Claiming on a delete/update
        /// would pollute the routing table with a server that doesn't own the room, and
        /// future ROOM_JOINs for that roomId would stick to the wrong server permanently.
        /// </summary>
        private ServerInfo RouteForRoomReadOnly(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return PickCanvasLeastLoaded();
            if (_roomRouting.TryGetValue(roomId, out var bound) && bound.IsHealthy)
            {
                Console.WriteLine($"[ROUTE] room {roomId} -> {bound.Endpoint} (sticky, read-only)");
                return bound;
            }
            // No mapping (LB restart / never-joined-locally room). Pick least-loaded for
            // execution — the chosen server will load the room from DB and publish
            // PEER_ROOM_DELETE / PEER_ROOM_PASSWORD_UPDATED so peers stay consistent.
            var fallback = PickCanvasLeastLoaded();
            if (fallback != null)
                Console.WriteLine($"[ROUTE] room {roomId} -> {fallback.Endpoint} (no binding, read-only fallback)");
            return fallback;
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

        /// <summary>
        /// Drop a room from the routing table after the owner deletes it. Decrements
        /// RoomCount on the bound server so the least-loaded picker doesn't develop a
        /// permanent skew toward servers that have hosted (now-deleted) rooms in the past.
        /// </summary>
        private void UnregisterRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;
            if (_roomRouting.TryRemove(roomId, out var bound))
            {
                bound.DecrementRoomCount();
                Console.WriteLine($"[ROUTE] room {roomId} unbound from {bound.Endpoint}");
            }
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

                if (type == MessageType.AUTH_LOGIN || type == MessageType.AUTH_REGISTER || type == MessageType.AUTH_SEND_OTP
                    || type == MessageType.AUTH_FORGOT_SEND_OTP || type == MessageType.AUTH_RESET_PASSWORD)
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
                else if (type == MessageType.ROOM_DELETE)
                {
                    // Read-only routing: if the room is currently bound to a healthy server use
                    // that one (so ownership check + memory cleanup happen on the actual host).
                    // If no binding exists (LB restart, table empty), fall back to least-loaded
                    // WITHOUT claiming the binding — claiming on a delete would lock future
                    // ROOM_JOINs of the same roomId onto a server that doesn't own the room.
                    var delReq = SafeGetData<DeleteRoomRequest>(msg);
                    if (delReq != null && !string.IsNullOrEmpty(delReq.RoomId))
                    {
                        target = RouteForRoomReadOnly(delReq.RoomId);
                        boundRoomId = delReq.RoomId;
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
                else if (type == MessageType.ROOM_UPDATE_PASSWORD)
                {
                    // Same read-only routing as ROOM_DELETE: prefer the bound server when
                    // present, fall back without claiming. The new hash propagates via the
                    // peer mesh (PEER_ROOM_PASSWORD_UPDATED), so a fallback target won't
                    // leave other servers with stale credentials.
                    var pwdReq = SafeGetData<UpdateRoomPasswordRequest>(msg);
                    if (pwdReq != null && !string.IsNullOrEmpty(pwdReq.RoomId))
                    {
                        target = RouteForRoomReadOnly(pwdReq.RoomId);
                        boundRoomId = pwdReq.RoomId;
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

                // ✅ For ROOM_CREATE we intercept the response to learn the new room's Id,
                // then register the routing mapping immediately. Without this, a subsequent
                // ROOM_JOIN for the freshly-created room could land on a different server
                // (least-loaded) before any user "claims" the binding.
                if (type == MessageType.ROOM_CREATE)
                {
                    await SniffAndForwardAsync(
                        backendStream, clientStream,
                        expectedType: MessageType.ROOM_CREATE_RESULT,
                        onMatched: matched =>
                        {
                            var newRoom = SafeGetData<Room>(matched);
                            if (newRoom != null && !string.IsNullOrEmpty(newRoom.Id))
                            {
                                RegisterRoom(newRoom.Id, target);
                                Console.WriteLine($"[ROUTE] claim on create: {newRoom.Id} -> {target.Endpoint}");
                            }
                        },
                        clientEp: clientEp);
                }
                else if (type == MessageType.ROOM_DELETE && !string.IsNullOrEmpty(boundRoomId))
                {
                    // Peek the response so we only drop the routing entry on a successful
                    // delete. A failed delete (e.g. non-owner or room not empty) must keep
                    // the room's mapping intact.
                    await SniffAndForwardAsync(
                        backendStream, clientStream,
                        expectedType: MessageType.ROOM_DELETE_RESULT,
                        onMatched: matched =>
                        {
                            var res = SafeGetData<DeleteRoomResult>(matched);
                            if (res != null && res.Success)
                                UnregisterRoom(boundRoomId);
                        },
                        clientEp: clientEp);
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
        /// Read backend responses line-by-line until either (a) the expected RESULT type is
        /// matched, (b) we exhaust the line budget, or (c) a read times out. Every line read
        /// — matched or not — is forwarded to the client in order, so the wire protocol stays
        /// intact even if the backend emits unrelated push/log lines before the actual result.
        /// The caller's <paramref name="onMatched"/> hook runs exactly once on the matched
        /// line so the LB can act on it (register/unregister a routing entry) without
        /// re-parsing the stream.
        /// </summary>
        private async Task SniffAndForwardAsync(
            NetworkStream backendStream,
            NetworkStream clientStream,
            string expectedType,
            Action<Message> onMatched,
            string clientEp)
        {
            const int MaxSniffLines = 5;
            bool matched = false;
            for (int i = 0; i < MaxSniffLines; i++)
            {
                string line;
                try
                {
                    line = await ReadOneLineAsync(backendStream, _peekMaxBytes, 10_000);
                }
                catch (TimeoutException)
                {
                    Console.WriteLine($"[LB] {clientEp} {expectedType} sniff timeout after {i} line(s)");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LB] {clientEp} sniff error: {ex.Message}");
                    return;
                }

                if (string.IsNullOrEmpty(line)) return; // backend closed

                // Forward each line to the client in order BEFORE deciding whether to match.
                // Doing it post-match would let an unexpected line we choose to ignore stay
                // buffered while PumpAsync starts pumping subsequent bytes — that's how the
                // "out-of-order response" bug used to surface.
                var bytes = Encoding.UTF8.GetBytes(line + "\n");
                try
                {
                    await clientStream.WriteAsync(bytes, 0, bytes.Length);
                    await clientStream.FlushAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LB] {clientEp} forward error: {ex.Message}");
                    return;
                }

                if (matched) continue;
                try
                {
                    var msg = Message.FromJson(line);
                    if (msg?.Type == expectedType)
                    {
                        try { onMatched?.Invoke(msg); }
                        catch (Exception ex) { Console.WriteLine($"[LB] {clientEp} onMatched error: {ex.Message}"); }
                        matched = true;
                        return; // hand the rest of the conversation to PumpAsync
                    }
                }
                catch { /* unparseable — already forwarded, just move on */ }
            }
            Console.WriteLine($"[LB] {clientEp} {expectedType} not seen after {MaxSniffLines} sniffed lines");
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
