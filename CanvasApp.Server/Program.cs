using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CanvasApp.Common;
using CanvasApp.Common.DataAccess;
using Newtonsoft.Json.Linq;

namespace CanvasApp.Server
{
    class Program
    {
        private const int DefaultPort = 9002;
        private static RoomManager _roomManager;
        private static AutoSaveService _autoSave;
        private static DrawActionDAO _drawActionDao;
        private static ChatMessageDAO _chatDao;
        private static PeerManager _peerManager;
        // Used by PEER_RELAY envelopes so a publisher can identify itself to receivers.
        private static string _serverId;
        // Server's public address (returned to clients for direct connection after LB redirect).
        // Auto-detect IP LAN lúc khởi động → đổi WiFi không cần sửa code.
        private static string _serverHost = GetLocalLanIp();
        private static int _serverPort = DefaultPort;

        // Max payload size per message. Rejects oversized payloads.
        // File attachments (CHAT_FILE) can be up to 2 MB raw → ~2.7 MB base64; allow 4 MB total.
        private const int MaxPayloadBytes = 4_194_304;
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
                    // Hand the chat DAO over so RoomManager.Join() can snapshot chat history
                    // atomically with admission.
                    _roomManager.SetChatDao(_chatDao);

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

            // Resolution order: args[0] (e.g. `CanvasApp.Server.exe 9003`) → Config/appsettings.json → default.
            // CLI arg ưu tiên hơn để dễ chạy nhiều instance từ Visual Studio
            // (Project → Debug → Application arguments).
            int port;
            if (args != null && args.Length > 0 && int.TryParse(args[0], out var argPort))
            {
                port = argPort;
                Console.WriteLine($"[CONFIG] Server port from CLI arg: {port}");
            }
            else
            {
                port = LoadServerPortOrDefault();
            }

            try { Console.Title = $"CanvasServer :{port}"; } catch { }

            // ✅ Set server's public address (used when joining room to tell client where to connect)
            _serverPort = port;

            // Peer mesh setup — runs in parallel with the client listener.
            //   • Peer listen port = client port + 100 (convention: 9002 ↔ 9102, 9003 ↔ 9103)
            //   • Peers list: CLI arg [1] (comma-sep "host:peerPort") wins, else appsettings.
            //   • ServerId: "canvas-{clientPort}" — unique per instance on the box.
            _serverId = $"canvas-{port}";
            // Let RoomManager.ApplyFromPeerAsync drop self-loop PEER_RELAY envelopes.
            if (_roomManager != null) _roomManager.SelfServerId = _serverId;
            int peerListenPort = port + 100;
            var rawPeers = args != null && args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
                ? args[1].Split(',')
                : LoadPeersFromConfig();

            // Strip self-loop entries BEFORE PeerManager opens any outbound socket. Multiple
            // canvas servers commonly share the same appsettings.json (e.g. both 9002 and 9003
            // read "Peers": ["127.0.0.1:9103"]) — server 9003 would then try to connect to its
            // own peer listener, hit the self-loop guard on the inbound side, reconnect, repeat
            // → 100s of log lines per second. Filtering here prevents the loop entirely.
            var peerAddresses = FilterSelfPeers(rawPeers, peerListenPort);

            _peerManager = new PeerManager(_serverId, peerAddresses,
                () => new PeerHelloPayload
                {
                    ServerId = _serverId,
                    Rooms = _roomManager.GetAllLocalMembers(),
                    // Ship the room list too so a freshly-reconnecting peer learns about
                    // rooms created during its downtime (DB load only runs once at startup).
                    KnownRooms = _roomManager.GetAllKnownRooms()
                });
            // When a peer reconnects after being down, sync canvas state for active rooms
            _peerManager.OnPeerReconnected += async (peerAddr) =>
            {
                try
                {
                    foreach (var roomId in _roomManager.GetDirtyRoomIds())
                    {
                        var canvasState = _roomManager.GetCanvasState(roomId);
                        if (canvasState != null && canvasState.Count > 0)
                        {
                            var payload = new PeerCanvasSyncPayload
                            {
                                RoomId = roomId,
                                OriginServerId = _serverId,
                                Actions = canvasState
                            };
                            var syncMsg = new Message(MessageType.PEER_CANVAS_SYNC, payload);
                            await _peerManager.PublishAsync(syncMsg);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PEER] Canvas sync on reconnect failed: {ex.Message}");
                }
            };
            _peerManager.Start();

            var peerListener = new TcpListener(IPAddress.Any, peerListenPort);
            peerListener.Start();
            Console.WriteLine($"[PEER] inbound listener on :{peerListenPort}");
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    var peerTcp = await peerListener.AcceptTcpClientAsync();
                    _ = Task.Run(() => PeerHandler.HandleAsync(peerTcp, _roomManager));
                }
            });

            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine("╔══════════════════════════════════════╗");
            Console.WriteLine($"║   CANVAS SERVER listening on :{port}    ║");
            Console.WriteLine("╚══════════════════════════════════════╝");

            while (true)
            {
                var tcp = await listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClient(tcp));
            }
        }

        // Wrap an inner message in a PEER_RELAY envelope and push it to every connected peer.
        // Callers should fire this AFTER BroadcastAsync has fanned the message out locally —
        // peer servers then broadcast the same inner message to their own clients in the room.
        private static Task PublishToPeersAsync(string roomId, Message inner)
        {
            if (_peerManager == null || string.IsNullOrEmpty(roomId)) return Task.CompletedTask;
            var envelope = new Message(MessageType.PEER_RELAY, new PeerRelayPayload
            {
                RoomId = roomId,
                OriginServerId = _serverId,
                Inner = inner
            });
            return _peerManager.PublishAsync(envelope);
        }

        // Tell peers the up-to-date local membership for a room. Fired after every local
        // join/leave so peers' merged GetRoomList() / GetMembers() reflect the change.
        private static Task PublishLocalMembersAsync(string roomId)
        {
            if (_peerManager == null || string.IsNullOrEmpty(roomId)) return Task.CompletedTask;
            var payload = new PeerMembersPayload
            {
                RoomId = roomId,
                OriginServerId = _serverId,
                Members = _roomManager.GetLocalMembers(roomId)
            };
            return _peerManager.PublishAsync(new Message(MessageType.PEER_MEMBER_SYNC, payload));
        }

        // Drops "host:port" entries whose port matches this server's peer-listen port AND whose
        // host is loopback / localhost (the most common self-loop case for a school demo on one
        // machine). Returns the remaining peer addresses unchanged.
        private static string[] FilterSelfPeers(string[] peers, int selfPeerPort)
        {
            if (peers == null || peers.Length == 0) return new string[0];
            var keep = new System.Collections.Generic.List<string>(peers.Length);
            foreach (var raw in peers)
            {
                var addr = raw?.Trim();
                if (string.IsNullOrEmpty(addr)) continue;

                var parts = addr.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
                {
                    keep.Add(addr);
                    continue;
                }
                var host = parts[0].Trim();
                bool isLocal = host == "127.0.0.1" || host == "localhost" || host == "::1";
                if (isLocal && port == selfPeerPort)
                {
                    Console.WriteLine($"[PEER] Skipping self-loop peer entry {addr}");
                    continue;
                }
                keep.Add(addr);
            }
            return keep.ToArray();
        }

        // Dò IPv4 LAN của máy (loại loopback + APIPA 169.254.x.x).
        // Dùng cho ServerHost trả về client trong ROOM_RESOLVE_RESULT / ROOM_JOIN_RESULT.
        private static string GetLocalLanIp()
        {
            try
            {
                var ip = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                             && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .FirstOrDefault(s => !s.StartsWith("169.254."));
                if (!string.IsNullOrEmpty(ip))
                {
                    Console.WriteLine($"[CONFIG] Auto-detected LAN IP: {ip}");
                    return ip;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CONFIG] LAN IP detect failed: {ex.Message}");
            }
            Console.WriteLine("[CONFIG] Fallback to 127.0.0.1");
            return "127.0.0.1";
        }

        private static string[] LoadPeersFromConfig()
        {
            try
            {
                var cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "appsettings.json");
                if (!File.Exists(cfgPath)) cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (!File.Exists(cfgPath)) return new string[0];

                var jo = JObject.Parse(File.ReadAllText(cfgPath));
                var arr = jo.SelectToken("ServerSettings.Peers") as JArray;
                if (arr == null) return new string[0];
                var list = new System.Collections.Generic.List<string>();
                foreach (var t in arr)
                    if (t.Type == JTokenType.String) list.Add(t.Value<string>());
                return list.ToArray();
            }
            catch { return new string[0]; }
        }

        private static int LoadServerPortOrDefault()
        {
            try
            {
                var cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "appsettings.json");
                if (!File.Exists(cfgPath)) cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (!File.Exists(cfgPath)) return DefaultPort;

                var json = File.ReadAllText(cfgPath);
                var jo = JObject.Parse(json);
                var token = jo.SelectToken("ServerSettings.Port");
                if (token != null && token.Type == JTokenType.Integer)
                {
                    var port = token.Value<int>();
                    Console.WriteLine($"[CONFIG] Server port loaded from {cfgPath}: {port}");
                    return port;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CONFIG] Failed to load port from appsettings.json: {ex.Message}");
            }

            return DefaultPort;
        }

        private static async Task HandleClient(TcpClient tcp)
        {
            var endpoint = tcp.Client.RemoteEndPoint.ToString();
            // Silent probes (LB health check: connect + close, 0 bytes) stay un-logged.
            bool gotData = false;

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
                        if (!gotData)
                        {
                            gotData = true;
                            Console.WriteLine($"[+] Client {endpoint} connected");
                        }
                        await ProcessAsync(client, line);
                    }
                }
            }
            catch (Exception ex)
            {
                if (gotData)
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
                    var updateMsg = new Message(MessageType.ROOM_UPDATE, new RoomMembersUpdate
                    {
                        Members = _roomManager.GetMembers(leftRoomId),
                        LeftUsername = leftUsername
                    });
                    await _roomManager.BroadcastAsync(leftRoomId, updateMsg);
                    // Peer mesh: tell other Canvas servers about the leave + new member list
                    // so their merged GetMembers()/GetRoomList() drop this user immediately.
                    await PublishToPeersAsync(leftRoomId, updateMsg);
                    await PublishLocalMembersAsync(leftRoomId);

                    // Notify lobby clients so their user-count badges update
                    var lobbyUpdate = new Message(MessageType.ROOM_LIST_RESULT,
                        new RoomListResult { Rooms = _roomManager.GetRoomList() });
                    await _roomManager.BroadcastToLobbyAsync(lobbyUpdate);

                    if (_autoSave != null && _roomManager.IsRoomEmpty(leftRoomId))
                        _autoSave.ForceSnapshot(leftRoomId);
                }

                tcp.Close();
                if (gotData)
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
                        // Token is now "<base64-payload>.<base64-hmac>" — use the shared
                        // extractor instead of decoding the whole string as base64, which would
                        // throw on the '.' separator. VerifyToken has already validated the HMAC,
                        // so this is just a payload parse.
                        client.Username = AuthServer.UserStore.ExtractUsername(msg.Token) ?? "";
                    }
                }

                switch (msg.Type)
                {
                    case MessageType.ROOM_LIST:
                        await client.SendAsync(new Message(MessageType.ROOM_LIST_RESULT,
                            new RoomListResult { Rooms = _roomManager.GetRoomList() }));
                        break;

                    case MessageType.RESOLVE_INVITE_CODE:
                    {
                        var resolveReq = msg.GetData<ResolveInviteCodeRequest>();
                        var room = _roomManager.GetRoomByInviteCode(resolveReq?.InviteCode);
                        var result = room != null
                            ? new ResolveInviteCodeResult { Success = true, RoomId = room.Id }
                            : new ResolveInviteCodeResult { Success = false, Message = "Mã mời không hợp lệ" };
                        await client.SendAsync(new Message(MessageType.RESOLVE_INVITE_CODE_RESULT, result));
                        break;
                    }

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

                        // Tell peers about the new room BEFORE the local lobby broadcast so
                        // any subsequent peer events for this room (e.g. an immediate join
                        // arriving via PEER_RELAY) won't be dropped by peer's "unknown room"
                        // guard in ApplyFromPeerAsync.
                        if (_peerManager != null)
                            await _peerManager.PublishAsync(new Message(MessageType.PEER_ROOM_CREATE, room));

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

                    case MessageType.ROOM_RESOLVE:
                    {
                        // Routing-only query from the LB-routed short-lived socket: verify the
                        // room + password and return where to direct-connect. NO Join, NO broadcast.
                        var resolveReq = msg.GetData<ResolveRoomRequest>();
                        var resolveRes = _roomManager.Resolve(resolveReq);
                        if (resolveRes.Success)
                        {
                            resolveRes.ServerHost = _serverHost;
                            resolveRes.ServerPort = _serverPort;
                        }
                        await client.SendAsync(new Message(MessageType.ROOM_RESOLVE_RESULT, resolveRes));
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

                    case MessageType.ROOM_DELETE:
                    {
                        var delReq = msg.GetData<DeleteRoomRequest>();
                        var room = _roomManager.GetRoom(delReq?.RoomId);
                        if (room == null)
                        {
                            await client.SendAsync(new Message(MessageType.ROOM_DELETE_RESULT,
                                new DeleteRoomResult { Success = false, Message = "Phòng không tồn tại" }));
                            break;
                        }
                        if (room.OwnerId != client.UserId)
                        {
                            await client.SendAsync(new Message(MessageType.ROOM_DELETE_RESULT,
                                new DeleteRoomResult { Success = false, Message = "Chỉ chủ phòng mới xóa được" }));
                            break;
                        }
                        // TryDeleteRoomIfEmpty atomically rechecks "no clients" under the
                        // same lock that Join uses, so a concurrent join can't slip in between
                        // the empty check and the actual removal.
                        if (!_roomManager.TryDeleteRoomIfEmpty(room.Id, out var failReason))
                        {
                            await client.SendAsync(new Message(MessageType.ROOM_DELETE_RESULT,
                                new DeleteRoomResult { Success = false, Message = failReason ?? "Không thể xóa phòng" }));
                            break;
                        }
                        await client.SendAsync(new Message(MessageType.ROOM_DELETE_RESULT,
                            new DeleteRoomResult { Success = true, Message = "Đã xóa phòng" }));

                        // Tell peers to drop the room too. PEER_ROOM_DELETE is intentionally
                        // NOT a PEER_RELAY envelope — every peer must process it regardless of
                        // whether they have local clients in the room.
                        if (_peerManager != null)
                            await _peerManager.PublishAsync(new Message(MessageType.PEER_ROOM_DELETE, room.Id));

                        // Refresh lobby on this server. Peers run their own
                        // BroadcastLobbyRoomListAsync after handling PEER_ROOM_DELETE.
                        var listMsg = new Message(MessageType.ROOM_LIST_RESULT,
                            new RoomListResult { Rooms = _roomManager.GetRoomList() });
                        await _roomManager.BroadcastToLobbyAsync(listMsg);
                        break;
                    }

                    case MessageType.ROOM_UPDATE_PASSWORD:
                    {
                        var pwdReq = msg.GetData<UpdateRoomPasswordRequest>();
                        var room = _roomManager.GetRoom(pwdReq?.RoomId);
                        if (room == null)
                        {
                            await client.SendAsync(new Message(MessageType.ROOM_UPDATE_PASSWORD_RESULT,
                                new UpdateRoomPasswordResult { Success = false, Message = "Phòng không tồn tại" }));
                            break;
                        }
                        if (room.OwnerId != client.UserId)
                        {
                            await client.SendAsync(new Message(MessageType.ROOM_UPDATE_PASSWORD_RESULT,
                                new UpdateRoomPasswordResult { Success = false, Message = "Chỉ chủ phòng mới đổi được mật khẩu" }));
                            break;
                        }
                        _roomManager.UpdateRoomPassword(room.Id, pwdReq.NewPassword, out var newHash);
                        await client.SendAsync(new Message(MessageType.ROOM_UPDATE_PASSWORD_RESULT,
                            new UpdateRoomPasswordResult
                            {
                                Success = true,
                                Message = string.IsNullOrEmpty(pwdReq.NewPassword) ? "Đã bỏ mật khẩu" : "Đã đổi mật khẩu",
                                HasPassword = !string.IsNullOrEmpty(pwdReq.NewPassword)
                            }));

                        // Tell peers about the new hash so their in-memory Room stays in sync.
                        // We ship the hash (not the plaintext) so peers don't double-bcrypt.
                        if (_peerManager != null)
                        {
                            await _peerManager.PublishAsync(new Message(MessageType.PEER_ROOM_PASSWORD_UPDATED,
                                new PeerRoomPasswordPayload { RoomId = room.Id, PasswordHash = newHash }));
                        }

                        // Refresh lobby so the lock icon updates everywhere
                        var listMsg = new Message(MessageType.ROOM_LIST_RESULT,
                            new RoomListResult { Rooms = _roomManager.GetRoomList() });
                        await _roomManager.BroadcastToLobbyAsync(listMsg);
                        break;
                    }

                    case MessageType.ROOM_LEAVE:
                    {
                        var leftRoom = client.CurrentRoomId;
                        var leftName = client.Username;
                        _roomManager.Leave(client);
                        if (!string.IsNullOrEmpty(leftRoom))
                        {
                            var update = new Message(MessageType.ROOM_UPDATE, new RoomMembersUpdate
                            {
                                Members = _roomManager.GetMembers(leftRoom),
                                LeftUsername = leftName
                            });
                            await _roomManager.BroadcastAsync(leftRoom, update);
                            await PublishToPeersAsync(leftRoom, update);
                            await PublishLocalMembersAsync(leftRoom);

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
                    case MessageType.DRAW_IMAGE:
                    case MessageType.DRAW_IMAGE_TRANSFORM:
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
                                    // RecordDrawAction stamped action.SeqNo/ActionId — re-serialize
                                    // so peers receive the same canonical payload local clients see.
                                    msg = new Message(msg.Type, action) { Token = msg.Token };
                                }
                            }
                            await _roomManager.BroadcastAsync(client.CurrentRoomId, msg, client);
                            await PublishToPeersAsync(client.CurrentRoomId, msg);
                        }
                        break;

                    case MessageType.DRAW_FILL:
                        if (!string.IsNullOrEmpty(client.CurrentRoomId))
                        {
                            var fillAct = msg.GetData<DrawAction>();
                            if (fillAct != null)
                            {
                                fillAct.UserId = client.UserId;
                                _roomManager.RecordDrawAction(client.CurrentRoomId, fillAct);
                                msg = new Message(msg.Type, fillAct) { Token = msg.Token };
                            }
                            await _roomManager.BroadcastAsync(client.CurrentRoomId, msg, client);
                            await PublishToPeersAsync(client.CurrentRoomId, msg);
                        }
                        break;

                    case MessageType.DRAW_UNDO:
                        if (!string.IsNullOrEmpty(client.CurrentRoomId))
                        {
                            // Optional targeted undo (e.g. text-edit): payload may carry an explicit ActionId.
                            // Fallback to the user's last action otherwise.
                            var requestedNotif = msg.GetData<UndoNotification>();
                            (long SeqNo, string ActionId) undone;
                            if (requestedNotif != null && !string.IsNullOrEmpty(requestedNotif.ActionId))
                                undone = _roomManager.UndoActionById(client.CurrentRoomId, requestedNotif.ActionId);
                            else
                                undone = _roomManager.UndoLastAction(client.CurrentRoomId, client.UserId);
                            // Broadcast whenever we have an ActionId, even if the action was already
                            // trimmed out of _canvasState by a snapshot (SeqNo will be -1 then).
                            // Peers still hold the action in their local _history and need DRAW_UNDO
                            // to remove it; RoomManager has recorded the ActionId in
                            // _undoneSinceSnapshot so it's stripped from the cached baseline on
                            // subsequent joins. Skipping the broadcast here was the multi-client
                            // undo-not-propagating bug: any action older than the 60-second snapshot
                            // tick stayed visible on peers.
                            if (!string.IsNullOrEmpty(undone.ActionId))
                            {
                                if (_drawActionDao != null && undone.SeqNo > 0)
                                {
                                    var seqNo = undone.SeqNo;
                                    _ = Task.Run(() =>
                                    {
                                        try { _drawActionDao.MarkUndone(client.CurrentRoomId, seqNo); }
                                        catch (Exception ex) { Console.WriteLine($"  [DB] MarkUndone failed: {ex.Message}"); }
                                    });
                                }

                                // Sender already applied the undo optimistically; only peers need the notification.
                                var undoMsg = new Message(MessageType.DRAW_UNDO,
                                    new UndoNotification { ActionId = undone.ActionId });
                                await _roomManager.BroadcastAsync(client.CurrentRoomId, undoMsg, client);
                                await PublishToPeersAsync(client.CurrentRoomId, undoMsg);
                            }
                        }
                        break;

                    case MessageType.DRAW_CLEAR:
                        if (!string.IsNullOrEmpty(client.CurrentRoomId))
                        {
                            _roomManager.ClearCanvas(client.CurrentRoomId);
                            await _roomManager.BroadcastAsync(client.CurrentRoomId, msg, sender: client);
                            await PublishToPeersAsync(client.CurrentRoomId, msg);
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

                            var chatMsg = new Message(MessageType.CHAT_MESSAGE, chat);
                            // RecordAndSnapshotChat persists + snapshots the broadcast list under
                            // a single lock that Join() also takes around admit + history fetch.
                            // That single lock kills the persist-before-fetch + broadcast-after-
                            // admit duplicate window: any joiner is either (a) in the snapshot
                            // and gets the chat live (so this chat isn't in their history fetch
                            // because the fetch ran after the persist's lock release), or (b) not
                            // in the snapshot and gets the chat via history (because their fetch
                            // saw the persisted row before they were admitted). Never both.
                            var snapshot = _roomManager.RecordAndSnapshotChat(client.CurrentRoomId, chat);
                            foreach (var c in snapshot)
                                await c.SendAsync(chatMsg);
                            await PublishToPeersAsync(client.CurrentRoomId, chatMsg);
                        }
                        break;

                    case MessageType.CHAT_FILE:
                        if (!string.IsNullOrEmpty(client.CurrentRoomId))
                        {
                            var fileMsg = msg.GetData<ChatMessage>();
                            if (fileMsg == null
                                || string.IsNullOrWhiteSpace(fileMsg.FileName)
                                || string.IsNullOrEmpty(fileMsg.FileData)) break;

                            fileMsg.UserId = client.UserId;
                            fileMsg.Username = client.Username;
                            fileMsg.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                            // Broadcast file to room; do not persist to DB. Same echo-to-sender
                            // semantics as CHAT_MESSAGE so the uploader's panel renders the file.
                            var fileEnvelope = new Message(MessageType.CHAT_FILE, fileMsg);
                            await _roomManager.BroadcastAsync(client.CurrentRoomId, fileEnvelope);
                            await PublishToPeersAsync(client.CurrentRoomId, fileEnvelope);
                        }
                        break;

                    case MessageType.CURSOR_UPDATE:
                        if (!string.IsNullOrEmpty(client.CurrentRoomId))
                        {
                            await _roomManager.BroadcastAsync(client.CurrentRoomId, msg, client);
                            await PublishToPeersAsync(client.CurrentRoomId, msg);
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

            // ✅ Add Canvas Server address to response so client can direct-connect after LB redirect
            if (joinRes.Success)
            {
                joinRes.ServerHost = _serverHost;
                joinRes.ServerPort = _serverPort;
            }

            // Step 1: Send immediate join result. Chat history is now embedded in joinRes
            // (snapshotted under the same logical step as admission to _roomClients), so a
            // CHAT_MESSAGE that races a join can't show up both in history and as a live
            // broadcast on the joiner's side.
            await client.SendAsync(new Message(MessageType.ROOM_JOIN_RESULT, joinRes));

            if (!joinRes.Success) return;

            // Step 2: Publish the new member to peer servers
            await PublishLocalMembersAsync(joinRes.Room.Id);

            // Step 4: Notify OTHER clients in the room about the new member (exclude the joining client)
            var joinUpdate = new Message(MessageType.ROOM_UPDATE, new RoomMembersUpdate
            {
                Members = _roomManager.GetMembers(joinRes.Room.Id),
                JoinedUsername = client.Username
            });
            await _roomManager.BroadcastAsync(joinRes.Room.Id, joinUpdate, sender: client);
            await PublishToPeersAsync(joinRes.Room.Id, joinUpdate);

            // Step 5: Notify lobby clients so user counts refresh
            var lobbyMsg = new Message(MessageType.ROOM_LIST_RESULT,
                new RoomListResult { Rooms = _roomManager.GetRoomList() });
            await _roomManager.BroadcastToLobbyAsync(lobbyMsg);
        }
    }
}
