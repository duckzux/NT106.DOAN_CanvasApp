using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CanvasApp.Common;

namespace CanvasApp.Client
{
    /// <summary>
    /// Persistent TCP connection to CanvasServer.
    /// Handles: send/receive, heartbeat ping every 10s, exponential-backoff
    /// reconnect on drop, and room-state re-sync after reconnect.
    /// </summary>
    public class CanvasClient
    {
        private static CanvasClient _instance;
        public static CanvasClient Instance => _instance ?? (_instance = new CanvasClient());

        private TcpClient _tcp;
        private StreamReader _reader;
        private StreamWriter _writer;
        private bool _running;
        private bool _reconnectEnabled;
        private int _reconnectAttempts;
        private string _currentRoomId;

        // Serialize concurrent sends so lines never interleave
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private const int HeartbeatIntervalMs  = 10_000;
        private const int BaseReconnectDelayMs = 1_000;
        private const int MaxReconnectDelayMs  = 30_000;
        private const int MaxReconnectAttempts = 10;
        private const int ConnectTimeoutMs     = 5_000;

        public bool IsConnected => _tcp?.Connected == true && _running;

        /// <summary>Fired for every inbound message (PONG excluded).</summary>
        public event Action<Message> OnMessageReceived;

        /// <summary>Fired once when the socket drops and all reconnect attempts fail.</summary>
        public event Action OnDisconnected;

        /// <summary>Fired after a successful automatic reconnect.</summary>
        public event Action OnReconnected;

        // ── Connect / Disconnect ──────────────────────────────────────────

        public async Task<bool> ConnectAsync()
        {
            _reconnectEnabled = true;
            _reconnectAttempts = 0;
            return await TryConnectAsync();
        }

        private async Task<bool> TryConnectAsync()
        {
            try
            {
                _tcp = new TcpClient();
                var connectTask = _tcp.ConnectAsync(Session.CANVAS_HOST, Session.CANVAS_PORT);
                if (await Task.WhenAny(connectTask, Task.Delay(ConnectTimeoutMs)) != connectTask
                    || connectTask.IsFaulted)
                    return false;

                var stream = _tcp.GetStream();
                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                _running = true;
                _ = Task.Run(ReceiveLoop);
                _ = Task.Run(HeartbeatLoop);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Disconnect()
        {
            _reconnectEnabled = false;
            _running = false;
            try { _tcp?.Close(); } catch { }
            _tcp    = null;
            _reader = null;
            _writer = null;
        }

        // ── Send ──────────────────────────────────────────────────────────

        public async Task SendAsync(Message msg)
        {
            if (!IsConnected || _writer == null) return;
            msg.Token = Session.Token;
            await _sendLock.WaitAsync();
            try
            {
                await _writer.WriteLineAsync(msg.ToJson());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CanvasClient] Send error: {ex.Message}");
            }
            finally
            {
                _sendLock.Release();
            }
        }

        // ── Receive loop ──────────────────────────────────────────────────

        private async Task ReceiveLoop()
        {
            try
            {
                while (_running && _reader != null)
                {
                    var line = await _reader.ReadLineAsync();
                    if (line == null) break;

                    Message msg;
                    try { msg = Message.FromJson(line); }
                    catch { continue; }

                    // Track current room so we can re-sync after reconnect
                    if (msg.Type == MessageType.ROOM_JOIN_RESULT)
                    {
                        var result = msg.GetData<JoinRoomResult>();
                        if (result?.Success == true)
                            _currentRoomId = result.Room?.Id;
                    }
                    else if (msg.Type == MessageType.ROOM_LEAVE)
                    {
                        _currentRoomId = null;
                    }

                    // Silently swallow PONG — it only exists to keep the connection alive
                    if (msg.Type == MessageType.PONG) continue;

                    OnMessageReceived?.Invoke(msg);
                }
            }
            catch { /* socket closed */ }
            finally
            {
                _running = false;
                if (_reconnectEnabled)
                    _ = Task.Run(ReconnectLoop);
                else
                    OnDisconnected?.Invoke();
            }
        }

        // ── Heartbeat ─────────────────────────────────────────────────────

        private async Task HeartbeatLoop()
        {
            while (_running)
            {
                await Task.Delay(HeartbeatIntervalMs);
                if (!_running || _writer == null) break;

                await _sendLock.WaitAsync();
                try
                {
                    if (_writer != null)
                        await _writer.WriteLineAsync(new Message(MessageType.PING, null, Session.Token).ToJson());
                }
                catch { /* ReceiveLoop will detect the drop */ }
                finally { _sendLock.Release(); }
            }
        }

        // ── Reconnect with exponential backoff ────────────────────────────

        private async Task ReconnectLoop()
        {
            while (_reconnectEnabled && _reconnectAttempts < MaxReconnectAttempts)
            {
                int delay = Math.Min(BaseReconnectDelayMs << _reconnectAttempts, MaxReconnectDelayMs);
                Console.WriteLine($"[CanvasClient] Reconnect in {delay / 1000.0:F1}s " +
                                  $"(attempt {_reconnectAttempts + 1}/{MaxReconnectAttempts})");
                await Task.Delay(delay);

                if (!_reconnectEnabled) return;

                if (await TryConnectAsync())
                {
                    _reconnectAttempts = 0;
                    Console.WriteLine("[CanvasClient] Reconnected");
                    OnReconnected?.Invoke();

                    // Re-send token so server can re-authenticate the session
                    await SendAsync(new Message(MessageType.PING));

                    // Request full canvas state if we were in a room
                    if (!string.IsNullOrEmpty(_currentRoomId))
                        await SendAsync(new Message(MessageType.CANVAS_STATE,
                            new { roomId = _currentRoomId }));
                    return;
                }

                _reconnectAttempts++;
            }

            Console.WriteLine("[CanvasClient] Reconnect failed — giving up");
            OnDisconnected?.Invoke();
        }

        // ── Helper methods ────────────────────────────────────────────────

        public Task RequestRoomListAsync() =>
            SendAsync(new Message(MessageType.ROOM_LIST));

        public Task CreateRoomAsync(CreateRoomRequest req) =>
            SendAsync(new Message(MessageType.ROOM_CREATE, req));

        public Task JoinRoomAsync(string roomId, string password)
        {
            _currentRoomId = roomId;
            return SendAsync(new Message(MessageType.ROOM_JOIN,
                new JoinRoomRequest { RoomId = roomId, Password = password }));
        }

        public Task LeaveRoomAsync()
        {
            _currentRoomId = null;
            return SendAsync(new Message(MessageType.ROOM_LEAVE));
        }

        public Task SendDrawAsync(string type, DrawAction action) =>
            SendAsync(new Message(type, action));

        public Task SendChatAsync(string text) =>
            SendAsync(new Message(MessageType.CHAT_MESSAGE, new ChatMessage { Text = text }));

        /// <summary>Explicitly request a full canvas snapshot from the server.</summary>
        public Task RequestCanvasStateAsync() =>
            SendAsync(new Message(MessageType.CANVAS_STATE));
    }
}
