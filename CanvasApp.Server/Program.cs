using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CanvasApp.Common;
using CanvasApp.Common.DataAccess;

namespace CanvasApp.Server
{
    class Program
    {
        private const int PORT = 9002;
        private static RoomManager _roomManager;
        private static AutoSaveService _autoSave;
        private static DrawActionDAO _drawActionDao;
        private static ChatMessageDAO _chatDao;

        // Max payload size per message (64 KB). Rejects oversized action_data.
        private const int MaxPayloadBytes = 65_536;
        // Max room name length
        private const int MaxRoomNameLength = 100;
        // Regex for HTML-style color string (#RRGGBB or #RGB)
        private static readonly Regex ColorRegex = new Regex(@"^#[0-9A-Fa-f]{3}([0-9A-Fa-f]{3})?$");

        static async Task Main(string[] args)
        {
            var connStr = ConfigurationManager.ConnectionStrings["CanvasDb"]?.ConnectionString;
            if (!string.IsNullOrWhiteSpace(connStr))
            {
                try
                {
                    DatabaseManager.Initialize(connStr);
                    Console.WriteLine("[DB] DatabaseManager initialized");

                    var roomDao = new RoomDAO();
                    var memberDao = new RoomMemberDAO();
                    _drawActionDao = new DrawActionDAO();
                    var snapshotDao = new CanvasSnapshotDAO();
                    _chatDao = new ChatMessageDAO();

                    var persistenceQueue = new PersistenceQueue(_drawActionDao);
                    _roomManager = new RoomManager(roomDao, memberDao, persistenceQueue, snapshotDao, _drawActionDao);

                    var cts = new CancellationTokenSource();
                    _ = persistenceQueue.StartAsync(cts.Token);

                    _autoSave = new AutoSaveService(_roomManager, snapshotDao, _drawActionDao);

                    _roomManager.LoadActiveRooms();

                    Console.WriteLine("[DB] PersistenceQueue, AutoSaveService, and room state ready");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DB] Init failed — running in-memory only: {ex.Message}");
                    _roomManager = new RoomManager();
                }
            }
            else
            {
                Console.WriteLine("[DB] No 'CanvasDb' connection string — running in-memory only");
                _roomManager = new RoomManager();
            }

            var listener = new TcpListener(IPAddress.Any, PORT);
            listener.Start();
            Console.WriteLine("╔══════════════════════════════════════╗");
            Console.WriteLine($"║   CANVAS SERVER listening on :{PORT}    ║");
            Console.WriteLine("╚══════════════════════════════════════╝");

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
            _roomManager.RegisterClient(client);

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
                _roomManager.UnregisterClient(client);

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

                    // Notify lobby clients so their user-count badges update
                    var lobbyUpdate = new Message(MessageType.ROOM_LIST_RESULT,
                        new RoomListResult { Rooms = _roomManager.GetRoomList() });
                    await _roomManager.BroadcastToLobbyAsync(lobbyUpdate);

                    if (_autoSave != null && _roomManager.IsRoomEmpty(leftRoomId))
                        _autoSave.ForceSnapshot(leftRoomId);
                }

                tcp.Close();
                Console.WriteLine($"[-] {endpoint} disconnected");
            }
        }

        private static async Task ProcessAsync(ConnectedClient client, string raw)
        {
            try
            {
                // Reject oversized payloads before JSON parsing
                if (Encoding.UTF8.GetByteCount(raw) > MaxPayloadBytes)
                {
                    await client.SendAsync(new Message(MessageType.ERROR,
                        new { message = "Payload quá lớn (tối đa 64 KB)" }));
                    return;
                }

                var msg = Message.FromJson(raw);

                if (msg.Type != MessageType.PING)
                {
                    int userId = AuthServer.UserStore.VerifyToken(msg.Token ?? "");
                    if (userId < 0)
                    {
                        await client.SendAsync(new Message(MessageType.ERROR,
                            new { message = "Token không hợp lệ hoặc đã hết hạn" }));
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
                    {
                        var createReq = msg.GetData<CreateRoomRequest>();
                        if (createReq == null
                            || string.IsNullOrWhiteSpace(createReq.Name)
                            || createReq.Name.Length > MaxRoomNameLength)
                        {
                            await client.SendAsync(new Message(MessageType.ERROR,
                                new { message = $"Tên phòng phải từ 1 đến {MaxRoomNameLength} ký tự" }));
                            break;
                        }

                        var room = _roomManager.CreateRoom(createReq, client);
                        await client.SendAsync(new Message(MessageType.ROOM_CREATE_RESULT, room));

                        // Notify all other lobby clients about the new room
                        var newRoomList = new Message(MessageType.ROOM_LIST_RESULT,
                            new RoomListResult { Rooms = _roomManager.GetRoomList() });
                        await _roomManager.BroadcastToLobbyAsync(newRoomList, except: client);
                        break;
                    }

                    case MessageType.ROOM_JOIN:
                    {
                        var joinReq = msg.GetData<JoinRoomRequest>();
                        await HandleJoin(client, joinReq);
                        break;
                    }

                    case MessageType.ROOM_JOIN_BY_CODE:
                    {
                        var codeReq = msg.GetData<InviteCodeRequest>();
                        var targetRoom = _roomManager.GetRoomByInviteCode(codeReq?.InviteCode);
                        if (targetRoom == null)
                        {
                            await client.SendAsync(new Message(MessageType.ROOM_JOIN_RESULT,
                                new JoinRoomResult { Success = false, Message = "Mã mời không hợp lệ" }));
                            break;
                        }
                        await HandleJoin(client, new JoinRoomRequest
                        {
                            RoomId = targetRoom.Id,
                            Password = codeReq.Password
                        });
                        break;
                    }

                    case MessageType.ROOM_LEAVE:
                    {
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

                            // Notify lobby clients so user counts refresh
                            var lobbyMsg = new Message(MessageType.ROOM_LIST_RESULT,
                                new RoomListResult { Rooms = _roomManager.GetRoomList() });
                            await _roomManager.BroadcastToLobbyAsync(lobbyMsg);
                        }
                        break;
                    }

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
                                if (action != null)
                                {
                                    // Sanitise color — reject obviously invalid values
                                    if (!string.IsNullOrEmpty(action.Color)
                                        && !ColorRegex.IsMatch(action.Color))
                                        action.Color = "#000000";

                                    action.UserId = client.UserId;
                                    _roomManager.RecordDrawAction(client.CurrentRoomId, action);
                                }
                            }
                            await _roomManager.BroadcastAsync(client.CurrentRoomId, msg, client);
                        }
                        break;

                    case MessageType.DRAW_UNDO:
                        if (!string.IsNullOrEmpty(client.CurrentRoomId))
                        {
                            var seqNo = _roomManager.UndoLastAction(client.CurrentRoomId, client.UserId);
                            if (seqNo > 0)
                            {
                                if (_drawActionDao != null)
                                    Task.Run(() =>
                                    {
                                        try { _drawActionDao.MarkUndone(client.CurrentRoomId, seqNo); }
                                        catch (Exception ex) { Console.WriteLine($"  [DB] MarkUndone failed: {ex.Message}"); }
                                    });

                                await _roomManager.BroadcastAsync(client.CurrentRoomId, msg, client);
                            }
                        }
                        break;

                    case MessageType.DRAW_CLEAR:
                        if (!string.IsNullOrEmpty(client.CurrentRoomId))
                        {
                            _roomManager.ClearCanvas(client.CurrentRoomId);
                            await _roomManager.BroadcastAsync(client.CurrentRoomId, msg);
                            _autoSave?.ForceSnapshot(client.CurrentRoomId);
                        }
                        break;

                    case MessageType.CHAT_MESSAGE:
                        if (!string.IsNullOrEmpty(client.CurrentRoomId))
                        {
                            var chat = msg.GetData<ChatMessage>();
                            if (chat == null || string.IsNullOrWhiteSpace(chat.Text)) break;

                            // Trim message to a reasonable length
                            if (chat.Text.Length > 1000) chat.Text = chat.Text.Substring(0, 1000);

                            chat.UserId = client.UserId;
                            chat.Username = client.Username;
                            chat.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                            // Persist asynchronously (fire-and-forget)
                            if (_chatDao != null)
                            {
                                var roomId = client.CurrentRoomId;
                                var userId = client.UserId;
                                var text = chat.Text;
                                Task.Run(() =>
                                {
                                    try { _chatDao.Insert(roomId, userId, text); }
                                    catch (Exception ex) { Console.WriteLine($"  [DB] ChatInsert failed: {ex.Message}"); }
                                });
                            }

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

        // Shared join logic used by both ROOM_JOIN and ROOM_JOIN_BY_CODE.
        private static async Task HandleJoin(ConnectedClient client, JoinRoomRequest joinReq)
        {
            var joinRes = _roomManager.Join(joinReq, client);
            await client.SendAsync(new Message(MessageType.ROOM_JOIN_RESULT, joinRes));

            if (!joinRes.Success) return;

            // Send chat history to the joining client
            if (_chatDao != null)
            {
                try
                {
                    var history = _chatDao.GetByRoom(joinRes.Room.Id, 50);
                    if (history.Count > 0)
                        await client.SendAsync(new Message(MessageType.CHAT_HISTORY,
                            new ChatHistoryResult { Messages = history }));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [DB] ChatHistory fetch failed: {ex.Message}");
                }
            }

            // Notify other clients in the room about the new member
            await _roomManager.BroadcastAsync(joinRes.Room.Id,
                new Message(MessageType.ROOM_UPDATE, new RoomMembersUpdate
                {
                    Members = _roomManager.GetMembers(joinRes.Room.Id),
                    JoinedUsername = client.Username
                }), sender: client);

            // Notify lobby clients so user counts refresh
            var lobbyMsg = new Message(MessageType.ROOM_LIST_RESULT,
                new RoomListResult { Rooms = _roomManager.GetRoomList() });
            await _roomManager.BroadcastToLobbyAsync(lobbyMsg);
        }
    }
}
