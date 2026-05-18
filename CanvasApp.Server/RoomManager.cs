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

        public List<Room> GetRoomList() => _rooms.Values.ToList();

        public Room GetRoomByInviteCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            code = code.ToUpper();
            return _codeToRoomId.TryGetValue(code, out var roomId)
                && _rooms.TryGetValue(roomId, out var room) ? room : null;
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

        // ── Draw actions ──────────────────────────────────────────────

        public void RecordDrawAction(string roomId, DrawAction action)
        {
            if (!_canvasState.TryGetValue(roomId, out var state)) return;
            if (!_roomStates.TryGetValue(roomId, out var rs)) return;

            action.SeqNo = rs.NextSeqNo();
            action.RoomId = roomId;
            Interlocked.Increment(ref rs.DirtyActionCount);

            lock (state) { state.Add(action); }

            var undoKey = roomId + ":" + action.UserId;
            _undoStacks.GetOrAdd(undoKey, _ => new ConcurrentStack<long>()).Push(action.SeqNo);

            _queue?.Enqueue(action);
        }

        public long UndoLastAction(string roomId, int userId)
        {
            var undoKey = roomId + ":" + userId;
            if (!_undoStacks.TryGetValue(undoKey, out var stack)) return -1;

            long seqNo;
            if (!stack.TryPop(out seqNo)) return -1;

            if (_canvasState.TryGetValue(roomId, out var state))
                lock (state) { state.RemoveAll(a => a.SeqNo == seqNo); }

            return seqNo;
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
    }
}
