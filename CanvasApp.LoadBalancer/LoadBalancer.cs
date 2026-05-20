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
        /// Consistent hashing for room affinity: roomId maps to same server deterministically.
        /// Uses CRC32(roomId) % healthyCanvasCount, so all LBs (even after restart) route
        /// the same roomId to the same server.Fallback to least-loaded if chosen server unhealthy.
        /// </summary>
        private ServerInfo RouteForRoom(string roomId)
        {
            var healthy = _canvasPool.Where(s => s.IsHealthy).ToList();
            if (healthy.Count == 0) return null;

            // Compute consistent hash and pick server
            uint hash = ComputeCrc32(roomId);
            int idx = (int)(hash % (uint)healthy.Count);
            var preferred = healthy[idx];

            Console.WriteLine($"[ROUTE] room {roomId} -> {preferred.Endpoint} (hash={hash}, idx={idx}, healthyCount={healthy.Count})");
            return preferred;
        }

        /// <summary>CRC32 hash for consistent hashing — deterministic across all instances.</summary>
        private static uint ComputeCrc32(string str)
        {
            if (string.IsNullOrEmpty(str)) return 0;
            const uint poly = 0xedb88320;
            uint crc = 0xffffffff;
            foreach (char c in str)
            {
                crc ^= c;
                for (int i = 0; i < 8; i++)
                    crc = (crc >> 1) ^ ((crc & 1) == 1 ? poly : 0);
            }
            return crc ^ 0xffffffff;
        }

        // With consistent hashing, routes are deterministic and don't need cleanup.
        // These methods are kept for API compatibility but do nothing.

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
