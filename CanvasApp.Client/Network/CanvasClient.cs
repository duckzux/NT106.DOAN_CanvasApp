using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CanvasApp.Common;

namespace CanvasApp.Client
{
    /// <summary>
    /// Persistent TCP client. Kết nối 1 lần sau khi login, dùng cho toàn bộ
    /// session của user (lobby, room, canvas, chat).
    /// </summary>
    public class CanvasClient
    {
        private static CanvasClient _instance;
        public static CanvasClient Instance => _instance ?? (_instance = new CanvasClient());

        private TcpClient _tcp;
        private StreamReader _reader;
        private StreamWriter _writer;
        private bool _running;

        public bool IsConnected => _tcp != null && _tcp.Connected;

        // Events: Form đăng ký để nhận message
        public event Action<Message> OnMessageReceived;
        public event Action OnDisconnected;

        public async Task<bool> ConnectAsync()
        {
            try
            {
                _tcp = new TcpClient();
                var connectTask = _tcp.ConnectAsync(Session.CANVAS_HOST, Session.CANVAS_PORT);
                if (await Task.WhenAny(connectTask, Task.Delay(3000)) != connectTask)
                    return false;

                var stream = _tcp.GetStream();
                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                _running = true;
                _ = Task.Run(ReceiveLoop);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task SendAsync(Message msg)
        {
            if (_writer == null || !IsConnected) return;
            msg.Token = Session.Token;
            try
            {
                await _writer.WriteLineAsync(msg.ToJson());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Send error: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            _running = false;
            try { _tcp?.Close(); } catch { }
            _tcp = null;
            _reader = null;
            _writer = null;
        }

        private async Task ReceiveLoop()
        {
            try
            {
                while (_running && _reader != null)
                {
                    var line = await _reader.ReadLineAsync();
                    if (line == null) break;
                    var msg = Message.FromJson(line);
                    OnMessageReceived?.Invoke(msg);
                }
            }
            catch { /* socket closed */ }
            finally
            {
                _running = false;
                OnDisconnected?.Invoke();
            }
        }

        // ── Helper methods ──────────────────────────────────────────────

        public Task RequestRoomListAsync() =>
            SendAsync(new Message(MessageType.ROOM_LIST));

        public Task CreateRoomAsync(CreateRoomRequest req) =>
            SendAsync(new Message(MessageType.ROOM_CREATE, req));

        public Task JoinRoomAsync(string roomId, string password) =>
            SendAsync(new Message(MessageType.ROOM_JOIN, new JoinRoomRequest { RoomId = roomId, Password = password }));

        public Task LeaveRoomAsync() =>
            SendAsync(new Message(MessageType.ROOM_LEAVE));

        public Task SendDrawAsync(string type, DrawAction action) =>
            SendAsync(new Message(type, action));

        public Task SendChatAsync(string text) =>
            SendAsync(new Message(MessageType.CHAT_MESSAGE, new ChatMessage { Text = text }));
    }
}
