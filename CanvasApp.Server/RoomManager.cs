using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CanvasApp.Common;
using CanvasApp.Common.DataAccess;

namespace CanvasApp.Server
{
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

    public class RoomManager
    {
        private readonly ConcurrentDictionary<string, Room> _rooms
            = new ConcurrentDictionary<string, Room>();
        private readonly ConcurrentDictionary<string, List<DrawAction>> _canvasState
            = new ConcurrentDictionary<string, List<DrawAction>>();
        private readonly ConcurrentDictionary<string, List<ConnectedClient>> _roomClients
            = new ConcurrentDictionary<string, List<ConnectedClient>>();
        private readonly ConcurrentDictionary<string, RoomState> _roomStates
            = new ConcurrentDictionary<string, RoomState>();

        // Per-(roomId:userId) undo stack
        private readonly ConcurrentDictionary<string, ConcurrentStack<long>> _undoStacks
            = new ConcurrentDictionary<string, ConcurrentStack<long>>();

        // Lazy-load flag — runs once per room per server lifetime
        private readonly ConcurrentDictionary<string, bool> _canvasLoaded
            = new ConcurrentDictionary<string, bool>();

        // Invite code → roomId lookup (case-sensitive uppercase keys)
        private readonly ConcurrentDictionary<string, string> _codeToRoomId
            = new ConcurrentDictionary<string, string>();

        // All connected TCP clients (for lobby broadcasts)
        private readonly ConcurrentDictionary<ConnectedClient, byte> _allClients
            = new ConcurrentDictionary<ConnectedClient, byte>();

        // Compressed snapshot cache per room (avoids keeping full action list in RAM)
        private readonly ConcurrentDictionary<string, string> _snapshotCache
            = new ConcurrentDictionary<string, string>();

        // Timestamp when a room became empty (for idle-room cleanup)
        private readonly ConcurrentDictionary<string, DateTime> _lastEmptyTime
            = new ConcurrentDictionary<string, DateTime>();

        // Peer-server members per room: peerServerId → roomId → members. Mirror of what other
        // Canvas servers in the mesh have told us via PEER_MEMBER_SYNC. Merged into GetMembers()
        // and GetRoomList() so clients see a unified view regardless of which server they hit.
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<RoomMember>>> _peerMembers
            = new ConcurrentDictionary<string, ConcurrentDictionary<string, List<RoomMember>>>();

        // Optional DB dependencies — null when DB is not configured
        private readonly RoomDAO _roomDao;
        private readonly RoomMemberDAO _memberDao;
        private readonly PersistenceQueue _queue;
        private readonly CanvasSnapshotDAO _snapDao;
        private readonly DrawActionDAO _drawActionDao;

        private static readonly Random _rng = new Random();
        private const string InviteChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public RoomManager(
            RoomDAO roomDao = null,
            RoomMemberDAO memberDao = null,
            PersistenceQueue queue = null,
            CanvasSnapshotDAO snapDao = null,
            DrawActionDAO drawActionDao = null)
        {
            _roomDao = roomDao;
            _memberDao = memberDao;
            _queue = queue;
            _snapDao = snapDao;
            _drawActionDao = drawActionDao;
        }

        // ── All-client tracking (for lobby broadcasts) ────────────────

        public void RegisterClient(ConnectedClient client) =>
            _allClients[client] = 0;

        public void UnregisterClient(ConnectedClient client) =>
            _allClients.TryRemove(client, out _);

        // Sends msg to every client that is currently in the lobby (not inside a room)
        public async Task BroadcastToLobbyAsync(Message msg, ConnectedClient except = null)
        {
            foreach (var kv in _allClients)
            {
                var c = kv.Key;
                if (c == except) continue;
                if (!string.IsNullOrEmpty(c.CurrentRoomId)) continue;
                await c.SendAsync(msg);
            }
        }

        // ── Startup: load active rooms from DB ────────────────────────

        public void LoadActiveRooms()
        {
            if (_roomDao == null) return;
            try
            {
                var rooms = _roomDao.GetAllActive();
                foreach (var room in rooms)
                {
                    _rooms[room.Id] = room;
                    _canvasState[room.Id] = new List<DrawAction>();
                    _roomClients[room.Id] = new List<ConnectedClient>();
                    _roomStates[room.Id] = new RoomState(room.Id);
                    if (!string.IsNullOrEmpty(room.InviteCode))
                        _codeToRoomId[room.InviteCode] = room.Id;
                }
                Console.WriteLine($"[DB] Loaded {rooms.Count} active rooms from database");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] LoadActiveRooms failed: {ex.Message}");
            }
        }

        // Lazily loads snapshot + deltas from DB on first client join.
        // After this change, only delta actions since the snapshot go into _canvasState;
        // the compressed snapshot data is cached in _snapshotCache to serve new joiners.
        private void EnsureCanvasLoaded(string roomId)
        {
            if (_snapDao == null || _drawActionDao == null) return;
            if (!_canvasLoaded.TryAdd(roomId, true)) return;

            try
            {
                var snap = _snapDao.GetLatest(roomId);
                long startSeq = 0;

                if (snap != null)
                {
                    // Store compressed snapshot for serving new joiners — no decompression needed here
                    _snapshotCache[roomId] = snap.SnapshotData;
                    startSeq = snap.ActionSeqAt;

                    if (_roomStates.TryGetValue(roomId, out var rs))
                    {
                        rs.InitSeqNo(snap.ActionSeqAt);
                        rs.SnapshotVersionFromDb(snap.Version);
                    }
                }

                // Only load deltas (not the full snapshot) into _canvasState
                var deltas = _drawActionDao.GetSinceSeq(roomId, startSeq);
                if (deltas.Count > 0)
                {
                    // Backfill synthetic ActionId for legacy DB rows so cross-client undo can target them.
                    foreach (var d in deltas)
                        if (string.IsNullOrEmpty(d.ActionId))
                            d.ActionId = "srv-" + d.SeqNo;

                    if (_canvasState.TryGetValue(roomId, out var stateRef))
                        lock (stateRef) { stateRef.AddRange(deltas); }

                    if (_roomStates.TryGetValue(roomId, out var rsRef))
                        rsRef.InitSeqNo(deltas[deltas.Count - 1].SeqNo);
                }

                Console.WriteLine($"[DB] Canvas loaded for room {roomId}: " +
                                  $"{(snap != null ? "snapshot" : "no snapshot")} + {deltas.Count} deltas");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] EnsureCanvasLoaded failed for {roomId}: {ex.Message}");
                _canvasLoaded.TryRemove(roomId, out _);
            }
        }

        // ── Room creation ─────────────────────────────────────────────

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
                PasswordHash = string.IsNullOrEmpty(req.Password)
                    ? null
                    : BCrypt.Net.BCrypt.HashPassword(req.Password, 10),
                InviteCode = GenerateInviteCode()
            };

            _rooms[room.Id] = room;
            _canvasState[room.Id] = new List<DrawAction>();
            _roomClients[room.Id] = new List<ConnectedClient>();
            _roomStates[room.Id] = new RoomState(room.Id);
            _canvasLoaded[room.Id] = true;
            _codeToRoomId[room.InviteCode] = room.Id;

            if (_roomDao != null)
            {
                try
                {
                    _roomDao.Insert(room);
                    _memberDao.Insert(room.Id, owner.UserId, "OWNER");
                    Console.WriteLine($"  [DB] Room '{room.Name}' ({room.Id}) persisted");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [DB] CreateRoom persist failed: {ex.Message}");
                }
            }

            Console.WriteLine($"  [Room] Created '{room.Name}' ({room.Id}) " +
                              $"code={room.InviteCode} by {owner.Username}");
            return room;
        }

        private string GenerateInviteCode()
        {
            string code;
            do
            {
                var chars = new char[6];
                for (int i = 0; i < 6; i++)
                    chars[i] = InviteChars[_rng.Next(InviteChars.Length)];
                code = new string(chars);
            } while (_codeToRoomId.ContainsKey(code));
            return code;
        }

        // ── Room listing & invite-code lookup ─────────────────────────

        public List<Room> GetRoomList()
        {
            // Return shallow clones with CurrentUsers reflecting local + peer counts. We don't
            // mutate the Room objects in _rooms because their CurrentUsers field is also touched
            // by Join/Leave under different locks — keeping the merged view as ephemeral copies
            // avoids racing on a shared field.
            var result = new List<Room>(_rooms.Count);
            foreach (var room in _rooms.Values)
            {
                int peerCount = 0;
                foreach (var perPeer in _peerMembers.Values)
                    if (perPeer.TryGetValue(room.Id, out var pm))
                        peerCount += pm.Count;

                int localCount = 0;
                if (_roomClients.TryGetValue(room.Id, out var list))
                    lock (list) { localCount = list.Count; }

                result.Add(new Room
                {
                    Id = room.Id,
                    Name = room.Name,
                    OwnerId = room.OwnerId,
                    OwnerName = room.OwnerName,
                    Template = room.Template,
                    MaxUsers = room.MaxUsers,
                    CurrentUsers = localCount + peerCount,
                    HasPassword = room.HasPassword,
                    PasswordHash = room.PasswordHash,
                    InviteCode = room.InviteCode
                });
            }
            return result;
        }

        public Room GetRoomByInviteCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            code = code.ToUpper();
            return _codeToRoomId.TryGetValue(code, out var roomId)
                && _rooms.TryGetValue(roomId, out var room) ? room : null;
        }

        // ── Resolve (pre-join routing query, no state change) ─────────
        // Used by the LB-routed short-lived socket to validate the room + password without
        // adding the user to _roomClients or broadcasting. The actual Join happens on the
        // client's persistent direct connection.
        public ResolveRoomResult Resolve(ResolveRoomRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.RoomId))
                return new ResolveRoomResult { Success = false, Message = "RoomId không hợp lệ" };

            if (!_rooms.TryGetValue(req.RoomId, out var room))
                return new ResolveRoomResult { Success = false, Message = "Room không tồn tại" };

            if (room.HasPassword)
            {
                if (string.IsNullOrEmpty(req.Password))
                    return new ResolveRoomResult
                    {
                        Success = false,
                        Message = "Phòng yêu cầu mật khẩu",
                        RequiresPassword = true
                    };
                if (!BCrypt.Net.BCrypt.Verify(req.Password, room.PasswordHash))
                    return new ResolveRoomResult { Success = false, Message = "Sai mật khẩu" };
            }

            return new ResolveRoomResult { Success = true, Message = "OK" };
        }

        // ── Join ──────────────────────────────────────────────────────

        public JoinRoomResult Join(JoinRoomRequest req, ConnectedClient client)
        {
            if (!_rooms.TryGetValue(req.RoomId, out var room))
                return new JoinRoomResult { Success = false, Message = "Room không tồn tại" };

            if (room.HasPassword)
            {
                if (string.IsNullOrEmpty(req.Password))
                    return new JoinRoomResult
                    {
                        Success = false,
                        Message = "Phòng yêu cầu mật khẩu",
                        RequiresPassword = true
                    };
                if (!BCrypt.Net.BCrypt.Verify(req.Password, room.PasswordHash))
                    return new JoinRoomResult { Success = false, Message = "Sai mật khẩu" };
            }

            EnsureCanvasLoaded(req.RoomId);

            _roomClients.GetOrAdd(room.Id, _ => new List<ConnectedClient>());

            lock (_roomClients[room.Id])
            {
                if (_roomClients[room.Id].Count >= room.MaxUsers)
                    return new JoinRoomResult { Success = false, Message = "Phòng đầy" };

                client.CurrentRoomId = room.Id;
                _roomClients[room.Id].Add(client);
                room.CurrentUsers = _roomClients[room.Id].Count;
            }

            // Room is now occupied — clear idle timer
            _lastEmptyTime.TryRemove(room.Id, out _);

            if (_memberDao != null)
            {
                try
                {
                    var existing = _memberDao.Find(room.Id, client.UserId);
                    if (existing == null)
                        _memberDao.Insert(room.Id, client.UserId, "MEMBER");
                    else
                        _memberDao.UpdateLastSeen(room.Id, client.UserId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [DB] Join persist failed: {ex.Message}");
                }
            }

            Console.WriteLine($"  [Room] {client.Username} joined '{room.Name}' ({room.CurrentUsers}/{room.MaxUsers})");

            // Build canvas join state: compressed snapshot (if any) + in-memory deltas
            _snapshotCache.TryGetValue(room.Id, out var snapshotData);
            List<DrawAction> deltas;
            if (_canvasState.TryGetValue(room.Id, out var stateList))
                lock (stateList) { deltas = stateList.ToList(); }
            else
                deltas = new List<DrawAction>();

            return new JoinRoomResult
            {
                Success = true,
                Message = "OK",
                Room = room,
                SnapshotData = snapshotData,
                CanvasState = deltas,
                Members = GetMembers(room.Id)
            };
        }

        // ── Leave ─────────────────────────────────────────────────────

        public void Leave(ConnectedClient client)
        {
            if (string.IsNullOrEmpty(client.CurrentRoomId)) return;

            var roomId = client.CurrentRoomId;

            if (_roomClients.TryGetValue(roomId, out var list))
            {
                bool isEmpty;
                lock (list)
                {
                    list.Remove(client);
                    if (_rooms.TryGetValue(roomId, out var room))
                        room.CurrentUsers = list.Count;
                    isEmpty = list.Count == 0;
                }

                if (isEmpty)
                    _lastEmptyTime[roomId] = DateTime.UtcNow;
            }

            if (_memberDao != null)
            {
                try { _memberDao.UpdateLastSeen(roomId, client.UserId); }
                catch (Exception ex) { Console.WriteLine($"  [DB] Leave update failed: {ex.Message}"); }
            }

            Console.WriteLine($"  [Room] {client.Username} left {roomId}");
            client.CurrentRoomId = null;
        }

        // ── Members ───────────────────────────────────────────────────

        public List<RoomMember> GetMembers(string roomId)
        {
            var members = new List<RoomMember>();
            var seenUserIds = new HashSet<int>();
            if (!_rooms.TryGetValue(roomId, out var room)) return members;

            var colors = new[] { "#7856CF", "#F9A826", "#0DBF7E", "#E74C3C", "#3498DB", "#9B59B6", "#1ABC9C", "#E67E22" };

            // Local clients first.
            if (_roomClients.TryGetValue(roomId, out var list))
            {
                ConnectedClient[] snapshot;
                lock (list) { snapshot = list.ToArray(); }

                foreach (var c in snapshot)
                {
                    if (!seenUserIds.Add(c.UserId)) continue;
                    members.Add(new RoomMember
                    {
                        UserId = c.UserId,
                        Username = c.Username,
                        Role = c.UserId == room.OwnerId ? "Owner" : "Member",
                        AvatarColor = colors[Math.Abs(c.UserId) % colors.Length]
                    });
                }
            }

            // Then anyone connected to a peer server — deduped by UserId so the same user
            // reported by two servers (rare, but possible during reconnect races) appears once.
            foreach (var perPeer in _peerMembers.Values)
            {
                if (!perPeer.TryGetValue(roomId, out var peerList)) continue;
                lock (peerList)
                {
                    foreach (var m in peerList)
                    {
                        if (!seenUserIds.Add(m.UserId)) continue;
                        members.Add(m);
                    }
                }
            }
            return members;
        }

        // Local-only members snapshot — used to publish PEER_MEMBER_SYNC envelopes to peers
        // (we never relay the peer-contributed members back; that would cause echo loops).
        public List<RoomMember> GetLocalMembers(string roomId)
        {
            var members = new List<RoomMember>();
            if (!_roomClients.TryGetValue(roomId, out var list)) return members;
            if (!_rooms.TryGetValue(roomId, out var room)) return members;

            ConnectedClient[] snapshot;
            lock (list) { snapshot = list.ToArray(); }

            var colors = new[] { "#7856CF", "#F9A826", "#0DBF7E", "#E74C3C", "#3498DB", "#9B59B6", "#1ABC9C", "#E67E22" };
            foreach (var c in snapshot)
            {
                members.Add(new RoomMember
                {
                    UserId = c.UserId,
                    Username = c.Username,
                    Role = c.UserId == room.OwnerId ? "Owner" : "Member",
                    AvatarColor = colors[Math.Abs(c.UserId) % colors.Length]
                });
            }
            return members;
        }

        // Snapshot of every room → local members. Sent in PEER_HELLO so a freshly-connected
        // peer immediately knows our world without having to wait for the next change event.
        public Dictionary<string, List<RoomMember>> GetAllLocalMembers()
        {
            var result = new Dictionary<string, List<RoomMember>>();
            foreach (var roomId in _rooms.Keys)
            {
                var local = GetLocalMembers(roomId);
                if (local.Count > 0) result[roomId] = local;
            }
            return result;
        }

        // ── Broadcast ─────────────────────────────────────────────────

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

        /// <summary>
        /// Broadcasts current merged member list (with JoinedUsername=null) to all clients in room.
        /// Called after PEER_MEMBER_SYNC arrives to keep local panels up-to-date without chat notification.
        /// </summary>
        public async Task BroadcastRoomMembersAsync(string roomId)
        {
            var members = GetMembers(roomId);
            if (members.Count == 0) return;
            var msg = new Message(MessageType.ROOM_UPDATE, new RoomMembersUpdate { Members = members });
            await BroadcastAsync(roomId, msg);
        }

        /// <summary>Get canvas state list for a room (used by PEER_CANVAS_SYNC dedup).</summary>
        public List<DrawAction> GetCanvasState(string roomId)
        {
            return _canvasState.TryGetValue(roomId, out var state) ? state : null;
        }

        // ── Draw actions ──────────────────────────────────────────────

        public void RecordDrawAction(string roomId, DrawAction action)
        {
            if (!_canvasState.TryGetValue(roomId, out var state)) return;
            if (!_roomStates.TryGetValue(roomId, out var rs)) return;

            action.SeqNo = rs.NextSeqNo();
            action.RoomId = roomId;
            if (string.IsNullOrEmpty(action.ActionId))
                action.ActionId = "srv-" + action.SeqNo;
            Interlocked.Increment(ref rs.DirtyActionCount);

            lock (state) { state.Add(action); }

            var undoKey = roomId + ":" + action.UserId;
            _undoStacks.GetOrAdd(undoKey, _ => new ConcurrentStack<long>()).Push(action.SeqNo);

            _queue?.Enqueue(action);
        }

        // Undoes the most recent action by this user in this room.
        // Returns (seqNo, actionId) if undone; (-1, null) if no action to undo.
        public (long SeqNo, string ActionId) UndoLastAction(string roomId, int userId)
        {
            var undoKey = roomId + ":" + userId;
            if (!_undoStacks.TryGetValue(undoKey, out var stack)) return (-1, null);

            long seqNo;
            if (!stack.TryPop(out seqNo)) return (-1, null);

            string actionId = null;
            if (_canvasState.TryGetValue(roomId, out var state))
            {
                lock (state)
                {
                    var found = state.FirstOrDefault(a => a.SeqNo == seqNo);
                    if (found != null) actionId = found.ActionId;
                    state.RemoveAll(a => a.SeqNo == seqNo);
                }
            }

            if (string.IsNullOrEmpty(actionId)) actionId = "srv-" + seqNo;
            return (seqNo, actionId);
        }

        public void ClearCanvas(string roomId)
        {
            if (_canvasState.TryGetValue(roomId, out var state))
                lock (state) { state.Clear(); }

            // Also clear the snapshot cache so new joiners get an empty canvas
            _snapshotCache.TryRemove(roomId, out _);

            if (_roomStates.TryGetValue(roomId, out var rs))
                Interlocked.Exchange(ref rs.DirtyActionCount, 0);

            foreach (var key in _undoStacks.Keys.ToList())
            {
                if (key.StartsWith(roomId + ":"))
                    _undoStacks.TryRemove(key, out _);
            }
        }

        // ── Snapshot support ──────────────────────────────────────────

        public IEnumerable<string> GetDirtyRoomIds()
        {
            foreach (var kv in _roomStates)
                if (kv.Value.DirtyActionCount > 0)
                    yield return kv.Key;
        }

        // Returns the FULL canvas state (cached snapshot decompressed + current deltas)
        // so AutoSaveService can create a new DB snapshot.
        public (List<DrawAction> actions, long lastSeq, int version) PrepareSnapshot(string roomId)
        {
            if (!_canvasState.TryGetValue(roomId, out var state)) return (null, 0, 0);
            if (!_roomStates.TryGetValue(roomId, out var rs)) return (null, 0, 0);

            // Decompress previous snapshot baseline outside the lock (CPU-heavy work)
            List<DrawAction> baseline = null;
            if (_snapshotCache.TryGetValue(roomId, out var cached) && !string.IsNullOrEmpty(cached))
            {
                try { baseline = SnapshotHelper.Decompress(cached); }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [AutoSave] Failed to decompress snapshot for {roomId}: {ex.Message}");
                }
            }

            List<DrawAction> copy;
            long lastSeq;
            int version;

            lock (state)
            {
                if (baseline != null)
                {
                    copy = new List<DrawAction>(baseline.Count + state.Count);
                    copy.AddRange(baseline);
                    copy.AddRange(state);
                }
                else
                {
                    copy = state.ToList();
                }
                lastSeq = rs.LastSeqNo;
                version = rs.NextSnapshotVersion();
                Interlocked.Exchange(ref rs.DirtyActionCount, 0);
            }

            return (copy, lastSeq, version);
        }

        // Called by AutoSaveService after the new snapshot is persisted to DB.
        // Updates the in-memory cache so new joiners get the fresh compressed baseline.
        public void SetSnapshotCache(string roomId, string compressedData)
        {
            _snapshotCache[roomId] = compressedData;
        }

        // Trims _canvasState to only keep actions with SeqNo > upToSeq.
        // Called after SetSnapshotCache so the snapshot covers everything up to upToSeq.
        public void TrimCanvasState(string roomId, long upToSeq)
        {
            if (!_canvasState.TryGetValue(roomId, out var state)) return;
            lock (state) { state.RemoveAll(a => a.SeqNo <= upToSeq); }
        }

        // ── Idle room cleanup ─────────────────────────────────────────

        // Returns IDs of rooms that have been empty for longer than the given threshold.
        public IEnumerable<string> GetIdleRoomIds(TimeSpan idleThreshold)
        {
            foreach (var kv in _lastEmptyTime.ToArray())
            {
                if (DateTime.UtcNow - kv.Value > idleThreshold)
                    yield return kv.Key;
            }
        }

        // Removes all in-memory state for a room and soft-deletes it in the DB.
        public void RemoveIdleRoom(string roomId)
        {
            if (!_rooms.TryRemove(roomId, out var room)) return;

            if (!string.IsNullOrEmpty(room.InviteCode))
                _codeToRoomId.TryRemove(room.InviteCode, out _);

            _canvasState.TryRemove(roomId, out _);
            _roomClients.TryRemove(roomId, out _);
            _roomStates.TryRemove(roomId, out _);
            _canvasLoaded.TryRemove(roomId, out _);
            _snapshotCache.TryRemove(roomId, out _);
            _lastEmptyTime.TryRemove(roomId, out _);

            foreach (var key in _undoStacks.Keys.Where(k => k.StartsWith(roomId + ":")).ToList())
                _undoStacks.TryRemove(key, out _);

            if (_roomDao != null)
            {
                try { _roomDao.SetActive(roomId, false); }
                catch (Exception ex) { Console.WriteLine($"  [DB] RemoveIdleRoom failed: {ex.Message}"); }
            }

            Console.WriteLine($"  [Room] Removed idle room {roomId}");
        }

        // ── Utility ───────────────────────────────────────────────────

        public bool RoomExists(string roomId) => _rooms.ContainsKey(roomId);

        public bool IsRoomEmpty(string roomId)
        {
            if (!_roomClients.TryGetValue(roomId, out var list)) return true;
            lock (list) { return list.Count == 0; }
        }

        // ── Peer mesh (Hướng C) ───────────────────────────────────────
        //
        // The mesh design:
        //   • Each Canvas server still owns its own clients, in-memory canvas state, and DB
        //     persistence. We do NOT pick a single "primary" per room.
        //   • Every locally-originated room event (DRAW_*, CHAT_*, ROOM_UPDATE, DRAW_UNDO,
        //     DRAW_CLEAR, DRAW_FILL) is wrapped in a PEER_RELAY envelope and pushed to every
        //     peer Canvas server in PeerManager.
        //   • Receiving servers call ApplyFromPeerAsync below: apply to local _canvasState so
        //     new joiners on that server see the action, broadcast to local clients so live
        //     viewers see it, but DO NOT persist to DB (the originator already did) and DO
        //     NOT re-publish to other peers (avoids broadcast storms).
        //   • Member lists are kept eventually-consistent via PEER_MEMBER_SYNC. _peerMembers
        //     holds what each peer told us; GetMembers/GetRoomList merge them with ours.

        public void UpdatePeerMembers(string peerId, string roomId, List<RoomMember> members)
        {
            if (string.IsNullOrEmpty(peerId) || string.IsNullOrEmpty(roomId)) return;
            var perPeer = _peerMembers.GetOrAdd(peerId, _ => new ConcurrentDictionary<string, List<RoomMember>>());
            perPeer[roomId] = members ?? new List<RoomMember>();
        }

        /// <summary>
        /// Register a room created by a peer Canvas server so that local clients can see it
        /// in their room list and accept join requests for it. Initialises the same in-memory
        /// containers that `CreateRoom` would, but DOES NOT touch the DB (the originator
        /// already persisted it) and does NOT mark `_canvasLoaded` so the first local joiner
        /// still triggers `EnsureCanvasLoaded` from DB.
        /// </summary>
        public bool RegisterPeerRoom(Room room)
        {
            if (room == null || string.IsNullOrEmpty(room.Id)) return false;
            if (!_rooms.TryAdd(room.Id, room)) return false; // already known

            _canvasState[room.Id] = new List<DrawAction>();
            _roomClients[room.Id] = new List<ConnectedClient>();
            _roomStates[room.Id] = new RoomState(room.Id);
            if (!string.IsNullOrEmpty(room.InviteCode))
                _codeToRoomId[room.InviteCode] = room.Id;

            Console.WriteLine($"  [Peer] Registered remote room '{room.Name}' ({room.Id}) code={room.InviteCode}");
            return true;
        }

        /// <summary>Every room this server is aware of, used to seed PEER_HELLO snapshots.</summary>
        public List<Room> GetAllKnownRooms() => _rooms.Values.ToList();

        /// <summary>
        /// Push a fresh ROOM_LIST_RESULT (with merged peer counts) to every local lobby client.
        /// Called by PeerHandler whenever a peer event changes what GetRoomList would return,
        /// so lobby badges stay in sync even when membership changes on a different server.
        /// </summary>
        public async Task BroadcastLobbyRoomListAsync()
        {
            var msg = new Message(MessageType.ROOM_LIST_RESULT,
                new RoomListResult { Rooms = GetRoomList() });
            await BroadcastToLobbyAsync(msg);
        }

        /// <summary>Forgets all peer-contributed state for a peer that just disconnected.</summary>
        public void DropPeer(string peerId)
        {
            if (string.IsNullOrEmpty(peerId)) return;
            _peerMembers.TryRemove(peerId, out _);
        }

        /// <summary>
        /// Applies an envelope-inner message that arrived from another Canvas server: replays
        /// it on local canvas state (if any clients here might want it) and broadcasts to local
        /// clients. Never writes to DB and never re-relays.
        /// </summary>
        public async Task ApplyFromPeerAsync(string roomId, string originServerId, Message inner)
        {
            if (inner == null || string.IsNullOrEmpty(roomId)) return;
            // Don't ghost-create rooms — if this server has never heard of the room, skip the
            // action. A future join on this server will load fresh state from DB.
            if (!_rooms.ContainsKey(roomId)) return;

            // Only mutate in-memory canvas state if this server has already loaded the room
            // from DB. Otherwise the next EnsureCanvasLoaded will pull everything from disk
            // (including this action, since the originating server persisted it).
            bool loaded = _canvasLoaded.ContainsKey(roomId);

            switch (inner.Type)
            {
                case MessageType.DRAW_END:
                case MessageType.DRAW_SHAPE:
                case MessageType.DRAW_TEXT:
                case MessageType.DRAW_FILL:
                {
                    var action = inner.GetData<DrawAction>();
                    if (action != null && loaded)
                    {
                        if (_canvasState.TryGetValue(roomId, out var state))
                            lock (state) { state.Add(action); }
                        if (_roomStates.TryGetValue(roomId, out var rs))
                            rs.AdvanceSeqIfGreater(action.SeqNo);
                    }
                    break;
                }

                case MessageType.DRAW_UNDO:
                {
                    var notif = inner.GetData<UndoNotification>();
                    if (notif != null && loaded && _canvasState.TryGetValue(roomId, out var state))
                        lock (state) { state.RemoveAll(a => a.ActionId == notif.ActionId); }
                    break;
                }

                case MessageType.DRAW_CLEAR:
                {
                    if (loaded && _canvasState.TryGetValue(roomId, out var state))
                        lock (state) { state.Clear(); }
                    _snapshotCache.TryRemove(roomId, out _);
                    break;
                }

                case MessageType.ROOM_UPDATE:
                {
                    // Rebuild ROOM_UPDATE with THIS server's merged member list (not peer's stale list)
                    // Preserve JoinedUsername/LeftUsername so notifications work, but sync member panel
                    var peerUpdate = inner.GetData<RoomMembersUpdate>();
                    var freshUpdate = new Message(MessageType.ROOM_UPDATE, new RoomMembersUpdate
                    {
                        Members = GetMembers(roomId),
                        JoinedUsername = peerUpdate?.JoinedUsername,
                        LeftUsername = peerUpdate?.LeftUsername
                    });
                    await BroadcastAsync(roomId, freshUpdate);
                    return;  // don't fall through to the generic broadcast below
                }

                // DRAW_START / DRAW_MOVE / CHAT_* — purely live signals, nothing to apply to
                // the persistent state. Just rebroadcast below.
            }

            await BroadcastAsync(roomId, inner);
        }
    }
}
