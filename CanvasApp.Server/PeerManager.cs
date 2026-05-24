using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CanvasApp.Common;

namespace CanvasApp.Server
{
    /// <summary>
    /// Maintains one persistent outbound TCP connection to each configured peer Canvas server.
    /// Used to publish PEER_RELAY / PEER_MEMBER_SYNC / PEER_HELLO envelopes so all servers in
    /// the mesh see every room event regardless of which server received it from a client.
    /// </summary>
    public class PeerManager
    {
        public string SelfServerId { get; }
        private readonly List<PeerEndpoint> _peers = new List<PeerEndpoint>();
        private readonly Func<PeerHelloPayload> _helloBuilder;

        public PeerManager(string selfServerId, IEnumerable<string> peerAddresses, Func<PeerHelloPayload> helloBuilder)
        {
            SelfServerId = selfServerId;
            _helloBuilder = helloBuilder;
            if (peerAddresses != null)
            {
                foreach (var addr in peerAddresses)
                {
                    if (string.IsNullOrWhiteSpace(addr)) continue;
                    var parts = addr.Split(':');
                    if (parts.Length != 2 || !int.TryParse(parts[1], out var port)) continue;
                    _peers.Add(new PeerEndpoint { Host = parts[0].Trim(), Port = port, Owner = this });
                }
            }
        }

        public void Start()
        {
            foreach (var p in _peers)
                _ = Task.Run(() => p.ConnectLoopAsync());
        }

        /// <summary>Send the message to every peer with an established outbound connection.</summary>
        public async Task PublishAsync(Message msg)
        {
            if (_peers.Count == 0) return;
            var json = msg.ToJson();
            foreach (var p in _peers)
                await p.SendRawAsync(json);
        }

        internal PeerHelloPayload BuildHello() => _helloBuilder?.Invoke();

        /// <summary>Called when a peer reconnects after being down, to push canvas state re-sync.</summary>
        public Action<string> OnPeerReconnected { get; set; }

        // ── Per-peer outbound connection ─────────────────────────────────────

        private class PeerEndpoint
        {
            public string Host;
            public int Port;
            public PeerManager Owner;

            private TcpClient _tcp;
            private StreamWriter _writer;
            private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
            private bool _wasConnected; // track if we've connected before (for reconnect callback)
            private bool _lastConnectFailed;

            public async Task ConnectLoopAsync()
            {
                int attempts = 0;
                while (true)
                {
                    try
                    {
                        _tcp = new TcpClient();
                        var connect = _tcp.ConnectAsync(Host, Port);
                        var done = await Task.WhenAny(connect, Task.Delay(3000));
                        if (done != connect || connect.IsFaulted)
                        {
                            _tcp?.Close();
                            _tcp = null;
                            throw new Exception("connect timed out");
                        }

                        // Set TCP keepalive to detect half-open connections faster (~25 seconds)
                        try
                        {
                            _tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                            {
                                var ka = new byte[12];
                                BitConverter.GetBytes(1u).CopyTo(ka, 0);      // enable
                                BitConverter.GetBytes(10_000u).CopyTo(ka, 4); // time = 10 s
                                BitConverter.GetBytes(5_000u).CopyTo(ka, 8);  // interval = 5 s
                                _tcp.Client.IOControl(IOControlCode.KeepAliveValues, ka, null);
                            }
                        }
                        catch { /* keepalive set failed, but connection is still open */ }

                        var stream = _tcp.GetStream();
                        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

                        // First message after connect: declare who we are and (optionally) what we know.
                        var hello = Owner.BuildHello();
                        if (hello != null)
                        {
                            var helloMsg = new Message(MessageType.PEER_HELLO, hello);
                            await _writer.WriteLineAsync(helloMsg.ToJson());
                        }

                        bool wasReconnect = _wasConnected;
                        _wasConnected = true;
                        _lastConnectFailed = false;

                        Console.WriteLine($"[PEER] outbound -> {Host}:{Port} connected (reconnect={wasReconnect})");
                        attempts = 0;

                        // Trigger canvas state re-sync when peer reconnects after downtime
                        if (wasReconnect) Owner.OnPeerReconnected?.Invoke(Host + ":" + Port);

                        // Start heartbeat task: send PEER_PING every 15 seconds
                        var heartbeatCts = new CancellationTokenSource();
                        var heartbeatTask = Task.Run(async () =>
                        {
                            while (!heartbeatCts.Token.IsCancellationRequested)
                            {
                                try
                                {
                                    await Task.Delay(15_000, heartbeatCts.Token);
                                    if (!heartbeatCts.Token.IsCancellationRequested && _writer != null)
                                        await SendRawAsync(new Message(MessageType.PEER_PING, null).ToJson());
                                }
                                catch { }
                            }
                        }, heartbeatCts.Token);

                        // Read loop: keep connection alive and detect drops.
                        // Reads chunks (not just 1 byte) to process PEER_PONG + heartbeat messages.
                        var buf = new byte[4096];
                        int noDataTimeoutMs = 30_000;  // 30 seconds with no data = dead connection
                        var lastDataTime = DateTime.UtcNow;

                        while (true)
                        {
                            int remaining = (int)(noDataTimeoutMs - (DateTime.UtcNow - lastDataTime).TotalMilliseconds);
                            if (remaining <= 0) throw new Exception("heartbeat timeout");

                            var readTask = stream.ReadAsync(buf, 0, buf.Length);
                            var delayTask = Task.Delay(remaining);
                            var completedTask = await Task.WhenAny(readTask, delayTask);

                            if (completedTask == readTask)
                            {
                                int n = await readTask;
                                if (n == 0) throw new Exception("peer closed");
                                lastDataTime = DateTime.UtcNow;  // data arrived, connection is alive
                            }
                            else
                            {
                                throw new Exception("heartbeat timeout");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!_lastConnectFailed)
                        {
                            Console.WriteLine($"[PEER] outbound {Host}:{Port} error: {ex.Message} (will retry silently...)");
                            _lastConnectFailed = true;
                        }
                        // Reconnect with exponential backoff capped at 30s, starting at 500ms
                    }
                    finally
                    {
                        try { _tcp?.Close(); } catch { }
                        _tcp = null;
                        _writer = null;
                    }

                    int delayMs = Math.Min(30_000, 500 * (int)Math.Pow(2, Math.Min(attempts, 6)));
                    attempts++;
                    await Task.Delay(delayMs);
                }
            }

            public async Task SendRawAsync(string json)
            {
                var w = _writer;
                if (w == null) return; // not connected yet
                await _sendLock.WaitAsync();
                try { await w.WriteLineAsync(json); }
                catch { /* drop detected by ConnectLoopAsync read */ }
                finally { _sendLock.Release(); }
            }
        }
    }
}
