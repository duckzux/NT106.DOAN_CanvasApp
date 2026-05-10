using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CanvasApp.Common;

namespace CanvasApp.Server
{
    /// <summary>
    /// Đại diện 1 client đang kết nối đến Canvas Server.
    /// </summary>
    public class ConnectedClient
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string CurrentRoomId { get; set; }
        public TcpClient Tcp { get; set; }
        public StreamWriter Writer { get; set; }

        public async Task SendAsync(Message msg)
        {
            try
            {
                if (Writer != null && Tcp.Connected)
                    await Writer.WriteLineAsync(msg.ToJson());
            }
            catch { /* ignore broken pipe */ }
        }
    }

    /// <summary>
    /// Quản lý toàn bộ room + canvas state. Thread-safe.
    /// </summary>
    public class RoomManager
    {
        private readonly ConcurrentDictionary<string, Room> _rooms = new ConcurrentDictionary<string, Room>();
        private readonly ConcurrentDictionary<string, List<DrawAction>> _canvasState = new ConcurrentDictionary<string, List<DrawAction>>();
        private readonly ConcurrentDictionary<string, List<ConnectedClient>> _roomClients = new ConcurrentDictionary<string, List<ConnectedClient>>();

        public Room CreateRoom(CreateRoomRequest req, ConnectedClient owner)
        {
            var room = new Room
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(),
                Name = req.Name,
                OwnerId = owner.UserId,
                OwnerName = owner.Username,
                Template = req.Template ?? "Blank",
                MaxUsers = req.MaxUsers > 0 ? req.MaxUsers : 4,
                CurrentUsers = 0,
                HasPassword = !string.IsNullOrEmpty(req.Password),
                PasswordHash = string.IsNullOrEmpty(req.Password) ? null : BCrypt.Net.BCrypt.HashPassword(req.Password, 10)
            };

            _rooms[room.Id] = room;
            _canvasState[room.Id] = new List<DrawAction>();
            _roomClients[room.Id] = new List<ConnectedClient>();
            Console.WriteLine($"  [Room] Created '{room.Name}' ({room.Id}) by {owner.Username}");
            return room;
        }

        public List<Room> GetRoomList() => _rooms.Values.ToList();

        public JoinRoomResult Join(JoinRoomRequest req, ConnectedClient client)
        {
            if (!_rooms.TryGetValue(req.RoomId, out var room))
                return new JoinRoomResult { Success = false, Message = "Room không tồn tại" };

            if (room.HasPassword)
            {
                if (string.IsNullOrEmpty(req.Password) ||
                    !BCrypt.Net.BCrypt.Verify(req.Password, room.PasswordHash))
                    return new JoinRoomResult { Success = false, Message = "Sai mật khẩu" };
            }

            if (room.CurrentUsers >= room.MaxUsers)
                return new JoinRoomResult { Success = false, Message = "Phòng đầy" };

            client.CurrentRoomId = room.Id;
            _roomClients.GetOrAdd(room.Id, _ => new List<ConnectedClient>());
            lock (_roomClients[room.Id])
            {
                _roomClients[room.Id].Add(client);
                room.CurrentUsers = _roomClients[room.Id].Count;
            }

            Console.WriteLine($"  [Room] {client.Username} joined '{room.Name}' ({room.CurrentUsers}/{room.MaxUsers})");

            return new JoinRoomResult
            {
                Success = true,
                Message = "OK",
                Room = room,
                CanvasState = _canvasState.TryGetValue(room.Id, out var state) ? state.ToList() : new List<DrawAction>()
            };
        }

        public void Leave(ConnectedClient client)
        {
            if (string.IsNullOrEmpty(client.CurrentRoomId)) return;

            if (_roomClients.TryGetValue(client.CurrentRoomId, out var list))
            {
                lock (list)
                {
                    list.Remove(client);
                    if (_rooms.TryGetValue(client.CurrentRoomId, out var room))
                        room.CurrentUsers = list.Count;
                }
            }
            Console.WriteLine($"  [Room] {client.Username} left {client.CurrentRoomId}");
            client.CurrentRoomId = null;
        }

        /// <summary>Broadcast message đến toàn bộ client trong room (trừ sender).</summary>
        public async Task BroadcastAsync(string roomId, Message msg, ConnectedClient sender = null)
        {
            if (!_roomClients.TryGetValue(roomId, out var list)) return;
            ConnectedClient[] snapshot;
            lock (list) { snapshot = list.ToArray(); }
            foreach (var c in snapshot)
            {
                if (sender != null && c == sender) continue;
                await c.SendAsync(msg);
            }
        }

        /// <summary>Lưu draw action vào canvas state để client mới join có thể replay.</summary>
        public void RecordDrawAction(string roomId, DrawAction action)
        {
            if (!_canvasState.TryGetValue(roomId, out var state)) return;
            lock (state) { state.Add(action); }
        }

        public void ClearCanvas(string roomId)
        {
            if (_canvasState.TryGetValue(roomId, out var state))
                lock (state) { state.Clear(); }
        }
    }
}
