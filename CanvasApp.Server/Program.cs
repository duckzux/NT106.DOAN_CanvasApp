using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CanvasApp.Common;

namespace CanvasApp.Server
{
    class Program
    {
        private const int PORT = 9002;
        private static readonly RoomManager _roomManager = new RoomManager();

        static async Task Main(string[] args)
        {
            var listener = new TcpListener(IPAddress.Any, PORT);
            listener.Start();
            Console.WriteLine($"╔══════════════════════════════════════╗");
            Console.WriteLine($"║   CANVAS SERVER listening on :{PORT}    ║");
            Console.WriteLine($"╚══════════════════════════════════════╝");

            while (true)
            {
                var tcp = await listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClient(tcp));
            }
        }

        private static async Task HandleClient(TcpClient tcp)
        {
            var endpoint = tcp.Client.RemoteEndPoint.ToString();
            Console.WriteLine($"[+] Client {endpoint} connected");

            var client = new ConnectedClient { Tcp = tcp };

            try
            {
                using (var stream = tcp.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                {
                    client.Writer = writer;
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        await ProcessAsync(client, line);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] {endpoint} {ex.Message}");
            }
            finally
            {
                // Khi disconnect: leave room, broadcast cập nhật cho người còn lại
                var leftRoomId = client.CurrentRoomId;
                var leftUsername = client.Username;
                _roomManager.Leave(client);

                if (!string.IsNullOrEmpty(leftRoomId))
                {
                    await _roomManager.BroadcastAsync(leftRoomId,
                        new Message(MessageType.ROOM_UPDATE, new RoomMembersUpdate
                        {
                            Members = _roomManager.GetMembers(leftRoomId),
                            LeftUsername = leftUsername
                        }));
                }

                tcp.Close();
                Console.WriteLine($"[-] {endpoint} disconnected");
            }
        }

        private static async Task ProcessAsync(ConnectedClient client, string raw)
        {
            try
            {
                var msg = Message.FromJson(raw);

                if (msg.Type != MessageType.PING)
                {
                    int userId = AuthServer.UserStore.VerifyToken(msg.Token ?? "");
                    if (userId < 0)
                    {
                        await client.SendAsync(new Message(MessageType.ERROR, new { message = "Invalid token" }));
                        return;
                    }
                    if (client.UserId == 0)
                    {
                        client.UserId = userId;
                        var rawTok = Encoding.UTF8.GetString(Convert.FromBase64String(msg.Token));
                        client.Username = rawTok.Split(':')[1];
                    }
                }

                switch (msg.Type)
                {
                    case MessageType.ROOM_LIST:
                        await client.SendAsync(new Message(MessageType.ROOM_LIST_RESULT,
                            new RoomListResult { Rooms = _roomManager.GetRoomList() }));
                        break;

                    case MessageType.ROOM_CREATE:
                        var createReq = msg.GetData<CreateRoomRequest>();
                        var room = _roomManager.CreateRoom(createReq, client);
                        await client.SendAsync(new Message(MessageType.ROOM_CREATE_RESULT, room));
                        break;

                    case MessageType.ROOM_JOIN:
                        var joinReq = msg.GetData<JoinRoomRequest>();
                        var joinRes = _roomManager.Join(joinReq, client);
                        await client.SendAsync(new Message(MessageType.ROOM_JOIN_RESULT, joinRes));

                        // Broadcast cập nhật members cho TẤT CẢ user trong room (gồm cả người vừa join)
                        if (joinRes.Success)
                        {
                            await _roomManager.BroadcastAsync(joinRes.Room.Id,
                                new Message(MessageType.ROOM_UPDATE, new RoomMembersUpdate
                                {
                                    Members = _roomManager.GetMembers(joinRes.Room.Id),
                                    JoinedUsername = client.Username
                                }), sender: client);  // sender = client để client tự cập nhật từ joinRes
                        }
                        break;

                    case MessageType.ROOM_LEAVE:
                        var leftRoom = client.CurrentRoomId;
                        var leftName = client.Username;
                        _roomManager.Leave(client);
                        if (!string.IsNullOrEmpty(leftRoom))
                        {
                            await _roomManager.BroadcastAsync(leftRoom,
                                new Message(MessageType.ROOM_UPDATE, new RoomMembersUpdate
                                {
                                    Members = _roomManager.GetMembers(leftRoom),
                                    LeftUsername = leftName
                                }));
                        }
                        break;

                    case MessageType.DRAW_START:
                    case MessageType.DRAW_MOVE:
                    case MessageType.DRAW_END:
                    case MessageType.DRAW_SHAPE:
                    case MessageType.DRAW_TEXT:
                        if (!string.IsNullOrEmpty(client.CurrentRoomId))
                        {
                            if (msg.Type != MessageType.DRAW_START && msg.Type != MessageType.DRAW_MOVE)
                            {
                                var action = msg.GetData<DrawAction>();
                                action.UserId = client.UserId;
                                _roomManager.RecordDrawAction(client.CurrentRoomId, action);
                            }
                            await _roomManager.BroadcastAsync(client.CurrentRoomId, msg, client);
                        }
                        break;

                    case MessageType.DRAW_CLEAR:
                        if (!string.IsNullOrEmpty(client.CurrentRoomId))
                        {
                            _roomManager.ClearCanvas(client.CurrentRoomId);
                            await _roomManager.BroadcastAsync(client.CurrentRoomId, msg);
                        }
                        break;

                    case MessageType.CHAT_MESSAGE:
                        if (!string.IsNullOrEmpty(client.CurrentRoomId))
                        {
                            var chat = msg.GetData<ChatMessage>();
                            chat.UserId = client.UserId;
                            chat.Username = client.Username;
                            chat.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            // Broadcast TẤT CẢ (kể cả sender) để mọi người thấy giống nhau
                            await _roomManager.BroadcastAsync(client.CurrentRoomId,
                                new Message(MessageType.CHAT_MESSAGE, chat));
                        }
                        break;

                    case MessageType.PING:
                        await client.SendAsync(new Message(MessageType.PONG));
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [!] Process error: {ex.Message}");
                await client.SendAsync(new Message(MessageType.ERROR, new { message = ex.Message }));
            }
        }
    }
}
