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
using Newtonsoft.Json;

namespace CanvasApp.Server
{
    public class ConnectedClient
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string CurrentRoomId { get; set; }
        public TcpClient Tcp { get; set; }
        public StreamWriter Writer { get; set; }

        // StreamWriter.WriteLineAsync is NOT thread-safe — concurrent draw / chat / room-update
        // writes can interleave bytes mid-line and the client's StreamReader sees malformed JSON
        // (e.g. half of one message glued onto the start of the next). Serialise sends per client.
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public async Task SendAsync(Message msg)
        {
            await _sendLock.WaitAsync();
            try
            {
                if (Writer != null && Tcp.Connected)
                    await Writer.WriteLineAsync(msg.ToJson());
            }
            catch { /* ignore broken pipe */ }
            finally { _sendLock.Release(); }
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

        // ActionIds undone since the last snapshot. AutoSaveService trims _canvasState
        // after each snapshot, so older actions only live inside _snapshotCache — undoing
        // one of them no longer matches _canvasState. Tracking the ActionIds here lets us
        // (1) broadcast DRAW_UNDO to peers even when the action is already trimmed,
        // (2) filter the cached snapshot at join time so new joiners don't see it, and
        // (3) drop it from the next snapshot baseline. The set is cleared once a new
        // snapshot is persisted (the new baseline already reflects every undo applied
        // before it was taken).
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _undoneSinceSnapshot
            = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>();

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
        // ChatMessageDAO is wired in via SetChatDao() rather than the constructor so the existing
        // 5-arg constructor signature stays compatible. Used by Join() to fetch chat history
        // BEFORE the joiner is added to _roomClients, eliminating the history-vs-live duplicate.
        private ChatMessageDAO _chatDao;
        public void SetChatDao(ChatMessageDAO chatDao) => _chatDao = chatDao;

        private static readonly Random _rng = new Random();
        private const string InviteChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        // Set by Program.cs after construction. Used by ApplyFromPeerAsync to ignore
        // PEER_RELAY envelopes that originated on this same server (happens when the peer
        // mesh is misconfigured so a server ends up connected to itself — without this guard
        // every chat / draw / room event would be applied twice).
        public string SelfServerId { get; set; }

        // Tombstones for recently-deleted rooms. Prevent a peer that missed the
        // PEER_ROOM_DELETE (e.g. disconnected during the delete, then reconnected with
        // a stale KnownRooms list in its PEER_HELLO) from resurrecting the room via
        // RegisterPeerRoom. TTL is generous so the ghost can't sneak back even after
        // a longish peer-mesh outage.
        private readonly ConcurrentDictionary<string, DateTime> _tombstones
            = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan TombstoneTtl = TimeSpan.FromMinutes(10);

        private bool IsTombstoned(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return false;
            if (!_tombstones.TryGetValue(roomId, out var deletedAt)) return false;
            if (DateTime.UtcNow - deletedAt > TombstoneTtl)
            {
                _tombstones.TryRemove(roomId, out _);
                return false;
            }
            return true;
        }

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

        // Per-room locks for canvas loading. Without these, two clients joining at the same
        // moment would both fall through TryAdd — the winner triggers the DB load, but the
        // loser returns IMMEDIATELY and reads an empty _canvasState before the winner finishes
        // populating it. Joiners would then see a blank canvas even though the room has history.
        private readonly ConcurrentDictionary<string, object> _canvasLoadLocks
            = new ConcurrentDictionary<string, object>();

        // Lazily loads snapshot + deltas from DB on first client join.
        // After this change, only delta actions since the snapshot go into _canvasState;
        // the compressed snapshot data is cached in _snapshotCache to serve new joiners.
        private void EnsureCanvasLoaded(string roomId)
        {
            if (_snapDao == null || _drawActionDao == null) return;

            // Fast path: already loaded by an earlier caller — no need to acquire the lock.
            if (_canvasLoaded.ContainsKey(roomId)) return;

            // Slow path: serialize on the per-room lock so concurrent joiners either run the
            // load themselves OR wait for the in-progress load to finish.
            var gate = _canvasLoadLocks.GetOrAdd(roomId, _ => new object());
            lock (gate)
            {
                if (_canvasLoaded.ContainsKey(roomId)) return; // someone else won the race

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

                    // Mark loaded LAST — late readers of the fast path will only short-circuit
                    // once state is fully populated.
                    _canvasLoaded[roomId] = true;

                    Console.WriteLine($"[DB] Canvas loaded for room {roomId}: " +
                                      $"{(snap != null ? "snapshot" : "no snapshot")} + {deltas.Count} deltas");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DB] EnsureCanvasLoaded failed for {roomId}: {ex.Message}");
                    // Leave _canvasLoaded unset so the next joiner retries the load.
                }
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

        // ── Owner-only room admin (called by ROOM_DELETE / ROOM_UPDATE_PASSWORD) ────

        /// <summary>
        /// Returns the in-memory Room (NOT cloned). Caller treats it as read-only.
        /// </summary>
        public Room GetRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return null;
            _rooms.TryGetValue(roomId, out var r);
            return r;
        }

        // Set of roomIds currently mid-delete. Join must refuse to add a client to a room
        // whose id is in this set, otherwise a delete that observed IsRoomEmpty=true and then
        // unwound the dictionaries could race with a concurrent Join, leaving the joiner
        // attached to a phantom roomClients list that no broadcast reaches.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _deleting
            = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Atomic delete: returns false if another caller is already deleting this room, OR if
        /// a client occupies the room. Holds the roomClients lock for the recheck so a Join
        /// that wins the race adds itself before the empty-check, blocking the delete.
        /// </summary>
        public bool TryDeleteRoomIfEmpty(string roomId, out string failReason)
        {
            failReason = null;
            if (!_rooms.TryGetValue(roomId, out var room)) { failReason = "Phòng không tồn tại"; return false; }
            if (!_deleting.TryAdd(roomId, 0)) { failReason = "Đang xử lý xóa phòng khác"; return false; }

            try
            {
                // Recheck membership under the same lock Join uses so a race-winning joiner
                // can't slip in between "empty" and the TryRemove below.
                if (_roomClients.TryGetValue(roomId, out var list))
                {
                    lock (list)
                    {
                        if (list.Count > 0) { failReason = "Phòng còn người trong — không thể xóa"; return false; }
                    }
                }
                return DeleteRoomInternal(roomId, room);
            }
            finally { _deleting.TryRemove(roomId, out _); }
        }

        /// <summary>
        /// Soft-delete used by peer sync: blindly drop the room from in-memory + DB. Skips the
        /// "is empty" check because the originating server already enforced it.
        /// </summary>
        public bool DeleteRoom(string roomId)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return false;
            return DeleteRoomInternal(roomId, room);
        }

        private bool DeleteRoomInternal(string roomId, Room room)
        {
            if (!_rooms.TryRemove(roomId, out _)) return false;

            _canvasState.TryRemove(roomId, out _);
            _roomClients.TryRemove(roomId, out _);
            _roomStates.TryRemove(roomId, out _);
            _canvasLoaded.TryRemove(roomId, out _);
            _snapshotCache.TryRemove(roomId, out _);
            _undoneSinceSnapshot.TryRemove(roomId, out _);
            _lastEmptyTime.TryRemove(roomId, out _);
            _undoStacks.TryRemove(roomId, out _);
            if (!string.IsNullOrEmpty(room.InviteCode))
                _codeToRoomId.TryRemove(room.InviteCode, out _);

            // Record the tombstone BEFORE the DB write so a peer racing to register the
            // room via PEER_HELLO is rejected even if the DB call is slow.
            _tombstones[roomId] = DateTime.UtcNow;

            // Also forget any peer-reported memberships for this room so a late
            // PEER_MEMBER_SYNC doesn't leave phantom counts dangling in GetRoomList().
            foreach (var perPeer in _peerMembers.Values)
                perPeer.TryRemove(roomId, out _);

            if (_roomDao != null)
            {
                try { _roomDao.Delete(roomId); }
                catch (Exception ex) { Console.WriteLine($"  [DB] DeleteRoom persist failed: {ex.Message}"); }
            }
            Console.WriteLine($"  [Room] Deleted '{room.Name}' ({roomId})");
            return true;
        }

        /// <summary>
        /// Update (or remove if newPassword is null/empty) the room's password.
        /// Returns the new hash (or null if password was removed); returns null + false via
        /// out param if the room is not found. Mutates the in-memory Room under a lock so
        /// Resolve/Join callers never observe a half-updated state (HasPassword=true with
        /// PasswordHash=null would make BCrypt.Verify throw).
        /// </summary>
        public bool UpdateRoomPassword(string roomId, string newPassword, out string newHash)
        {
            newHash = null;
            if (!_rooms.TryGetValue(roomId, out var room)) return false;

            string hash = string.IsNullOrEmpty(newPassword)
                ? null
                : BCrypt.Net.BCrypt.HashPassword(newPassword, 10);

            // Atomic from any other reader's perspective: HasPassword and PasswordHash
            // must flip together. lock(room) is shared with Resolve below.
            lock (room)
            {
                room.PasswordHash = hash;
                room.HasPassword = hash != null;
            }

            if (_roomDao != null)
            {
                try { _roomDao.UpdatePasswordHash(roomId, hash); }
                catch (Exception ex) { Console.WriteLine($"  [DB] UpdateRoomPassword persist failed: {ex.Message}"); }
            }
            Console.WriteLine($"  [Room] Password updated for '{room.Name}' ({roomId}) hasPwd={room.HasPassword}");
            newHash = hash;
            return true;
        }

        /// <summary>
        /// Apply a PEER_ROOM_PASSWORD_UPDATED envelope: copy the hash from the originating
        /// server into local memory only (no DB write — origin already did that, and double
        /// writing would defeat the unique-writer guarantee for the rooms table).
        /// </summary>
        public bool ApplyPeerPasswordHash(string roomId, string passwordHash)
        {
            if (string.IsNullOrEmpty(roomId)) return false;
            if (!_rooms.TryGetValue(roomId, out var room)) return false;
            lock (room)
            {
                room.PasswordHash = string.IsNullOrEmpty(passwordHash) ? null : passwordHash;
                room.HasPassword = !string.IsNullOrEmpty(passwordHash);
            }
            Console.WriteLine($"  [Room] Peer password sync applied to '{room.Name}' ({roomId}) hasPwd={room.HasPassword}");
            return true;
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
            // Snapshot peer dicts once so the per-room loop below sees a consistent set of
            // peers — same rationale as GetMembers: a peer reconnect mid-iteration could
            // otherwise inflate or deflate the count for some rooms but not others.
            var perPeerSnapshot = _peerMembers.Values.ToArray();

            var result = new List<Room>(_rooms.Count);
            foreach (var room in _rooms.Values)
            {
                int peerCount = 0;
                foreach (var perPeer in perPeerSnapshot)
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

            // Snapshot the (HasPassword, PasswordHash) pair under the lock that
            // UpdateRoomPassword takes, so we never see HasPassword=true with a null hash.
            bool needsPwd;
            string hash;
            lock (room) { needsPwd = room.HasPassword; hash = room.PasswordHash; }

            if (needsPwd)
            {
                if (string.IsNullOrEmpty(req.Password))
                    return new ResolveRoomResult
                    {
                        Success = false,
                        Message = "Phòng yêu cầu mật khẩu",
                        RequiresPassword = true
                    };
                if (string.IsNullOrEmpty(hash) || !BCrypt.Net.BCrypt.Verify(req.Password, hash))
                    return new ResolveRoomResult { Success = false, Message = "Sai mật khẩu" };
            }

            return new ResolveRoomResult { Success = true, Message = "OK" };
        }

        // ── Join ──────────────────────────────────────────────────────

        public JoinRoomResult Join(JoinRoomRequest req, ConnectedClient client)
        {
            if (!_rooms.TryGetValue(req.RoomId, out var room))
                return new JoinRoomResult { Success = false, Message = "Room không tồn tại" };

            // Refuse joins for rooms mid-delete so we never end up attached to a roomClients
            // list that's about to be discarded by TryDeleteRoomIfEmpty.
            if (_deleting.ContainsKey(req.RoomId))
                return new JoinRoomResult { Success = false, Message = "Phòng đang được xóa, vui lòng thử phòng khác" };

            // Same atomic snapshot as Resolve — see comment there.
            bool needsPwd;
            string hash;
            lock (room) { needsPwd = room.HasPassword; hash = room.PasswordHash; }

            if (needsPwd)
            {
                if (string.IsNullOrEmpty(req.Password))
                    return new JoinRoomResult
                    {
                        Success = false,
                        Message = "Phòng yêu cầu mật khẩu",
                        RequiresPassword = true
                    };
                if (string.IsNullOrEmpty(hash) || !BCrypt.Net.BCrypt.Verify(req.Password, hash))
                    return new JoinRoomResult { Success = false, Message = "Sai mật khẩu" };
            }

            EnsureCanvasLoaded(req.RoomId);

            _roomClients.GetOrAdd(room.Id, _ => new List<ConnectedClient>());

            // Atomically admit the client, fetch chat history, and snapshot the canvas under
            // the same locks that draws and chat broadcasts use. The chat-history fetch must
            // be INSIDE lock(clientList) (the same lock that RecordAndSnapshotChat takes for
            // persisting + snapshotting the broadcast list) so a chat message can never be
            // both persisted-before-fetch AND broadcast-after-admit — that's the duplicate
            // window the previous "fetch before admit" layout had. State-list lock similarly
            // covers admit+stateList-snapshot atomic with concurrent draws.
            var clientList = _roomClients[room.Id];
            var stateList = _canvasState.GetOrAdd(room.Id, _ => new List<DrawAction>());
            List<DrawAction> deltas;
            List<ChatMessage> historySnapshot = null;
            lock (stateList)
            {
                lock (clientList)
                {
                    if (clientList.Count >= room.MaxUsers)
                        return new JoinRoomResult { Success = false, Message = "Phòng đầy" };

                    client.CurrentRoomId = room.Id;
                    clientList.Add(client);
                    room.CurrentUsers = clientList.Count;

                    if (_chatDao != null)
                    {
                        try { historySnapshot = _chatDao.GetByRoom(room.Id, 50); }
                        catch (Exception ex) { Console.WriteLine($"  [DB] ChatHistory fetch failed: {ex.Message}"); }
                    }
                }
                deltas = stateList.ToList();
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

            // Pull the compressed snapshot AFTER admit+snapshot so the joiner sees a coherent
            // (snapshot, deltas) pair — the cache key is stable so the read order doesn't
            // race with anything mutating it.
            _snapshotCache.TryGetValue(room.Id, out var snapshotData);

            // Strip undone actions out of the cached baseline before handing it to the joiner.
            // Without this, an action that was undone after the most recent snapshot would still
            // appear on the new client's canvas — the undo never reaches them because they never
            // had it in live history to begin with.
            snapshotData = FilterSnapshotForUndone(room.Id, snapshotData);
            // Same filter for the post-snapshot deltas (rare: an action could be added to state
            // and then trimmed-then-undone in tight sequence on a misbehaving client; cheap to apply).
            if (_undoneSinceSnapshot.TryGetValue(room.Id, out var undoneSet) && undoneSet.Count > 0)
                deltas = deltas.Where(a => string.IsNullOrEmpty(a.ActionId) || !undoneSet.ContainsKey(a.ActionId)).ToList();

            return new JoinRoomResult
            {
                Success = true,
                Message = "OK",
                Room = room,
                SnapshotData = snapshotData,
                CanvasState = deltas,
                Members = GetMembers(room.Id),
                ChatHistory = historySnapshot ?? new List<ChatMessage>()
            };
        }

        // Removes ActionIds in _undoneSinceSnapshot[roomId] from the compressed snapshot
        // baseline and re-compresses it. Returns the original string when there's nothing
        // to filter, no cached set, or decompression fails (fail-open so a corrupt
        // snapshot can't lock out the room).
        private string FilterSnapshotForUndone(string roomId, string snapshotData)
        {
            if (string.IsNullOrEmpty(snapshotData)) return snapshotData;
            if (!_undoneSinceSnapshot.TryGetValue(roomId, out var undoneSet) || undoneSet.Count == 0)
                return snapshotData;
            try
            {
                var baseline = SnapshotHelper.Decompress(snapshotData);
                int before = baseline.Count;
                baseline.RemoveAll(a => !string.IsNullOrEmpty(a.ActionId) && undoneSet.ContainsKey(a.ActionId));
                if (baseline.Count == before) return snapshotData;
                return SnapshotHelper.Compress(JsonConvert.SerializeObject(baseline));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [Room] FilterSnapshotForUndone({roomId}) failed: {ex.Message}");
                return snapshotData;
            }
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
            // Snapshot _peerMembers.Values into an array FIRST so a peer connect/disconnect
            // happening mid-iteration (UpdatePeerMembers / DropPeer) doesn't make the outer
            // enumeration unstable. ConcurrentDictionary enumeration is thread-safe but does
            // not guarantee a point-in-time view — without the snapshot, a peer reconnect
            // could cause the same caller to see different perPeer dicts on successive
            // iterations of the outer loop.
            var perPeerSnapshot = _peerMembers.Values.ToArray();
            foreach (var perPeer in perPeerSnapshot)
            {
                if (!perPeer.TryGetValue(roomId, out var peerList)) continue;
                // peerList is an immutable list reference once stored — UpdatePeerMembers
                // REPLACES the slot with a brand-new list rather than mutating in place, so
                // we can iterate without holding a lock. The previous lock(peerList) was
                // belt-and-braces over a list that's already effectively read-only.
                foreach (var m in peerList)
                {
                    if (!seenUserIds.Add(m.UserId)) continue;
                    members.Add(m);
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

        // ── Chat persist + broadcast ──────────────────────────────────

        /// <summary>
        /// Persists a chat message AND snapshots the broadcast list under one
        /// <c>lock(clientList)</c> so the snapshot is taken atomically with the persist. Join()
        /// takes the same lock around admit + history fetch, so a chat can never be persisted
        /// before a joiner's history fetch AND broadcast after that joiner is admitted — which
        /// was the duplicate window the previous "fire-and-forget Insert + concurrent
        /// Broadcast" pair created. Callers iterate the returned snapshot to send asynchronously
        /// (outside the lock) without risk of duplicate delivery to a newly-joined client.
        /// </summary>
        public ConnectedClient[] RecordAndSnapshotChat(string roomId, ChatMessage chat)
        {
            if (!_roomClients.TryGetValue(roomId, out var list)) return Array.Empty<ConnectedClient>();
            ConnectedClient[] snapshot;
            lock (list)
            {
                if (_chatDao != null && chat != null && !string.IsNullOrEmpty(chat.Text))
                {
                    try { _chatDao.Insert(roomId, chat.UserId, chat.Text); }
                    catch (Exception ex) { Console.WriteLine($"  [DB] ChatInsert failed: {ex.Message}"); }
                }
                snapshot = list.ToArray();
            }
            return snapshot;
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

        /// <summary>
        /// Merge actions received via PEER_CANVAS_SYNC into local canvas state. Holds the
        /// state list lock for the whole loop so a concurrent local DRAW_END can't interleave
        /// AddRange-style mutations. Dedupes by ActionId when present, falling back to SeqNo
        /// otherwise — without the fallback, legacy DB rows without an ActionId would be
        /// re-added on every peer reconnect (the previous "string.IsNullOrEmpty" guard made
        /// the existence check return false for those rows, causing unbounded duplication).
        /// </summary>
        public int ApplyPeerCanvasSync(string roomId, List<DrawAction> incoming)
        {
            if (string.IsNullOrEmpty(roomId) || incoming == null || incoming.Count == 0) return 0;
            if (!_canvasState.TryGetValue(roomId, out var state)) return 0;

            int added = 0;
            lock (state)
            {
                // Build lookup sets once instead of scanning state for every incoming action.
                var idSet = new HashSet<string>(StringComparer.Ordinal);
                var seqSet = new HashSet<long>();
                foreach (var a in state)
                {
                    if (!string.IsNullOrEmpty(a.ActionId)) idSet.Add(a.ActionId);
                    if (a.SeqNo > 0) seqSet.Add(a.SeqNo);
                }

                foreach (var action in incoming)
                {
                    bool dup;
                    if (!string.IsNullOrEmpty(action.ActionId))
                        dup = idSet.Contains(action.ActionId);
                    else if (action.SeqNo > 0)
                        dup = seqSet.Contains(action.SeqNo);
                    else
                        // No identifier at all — can't dedupe; skip to be safe rather than risk
                        // unbounded duplication on every reconnect.
                        dup = true;

                    if (!dup)
                    {
                        // Backfill synthetic ActionId so subsequent dedupes (and client-side
                        // undo lookups) work even if the originating server never set one.
                        if (string.IsNullOrEmpty(action.ActionId) && action.SeqNo > 0)
                            action.ActionId = "srv-" + action.SeqNo;

                        state.Add(action);
                        if (!string.IsNullOrEmpty(action.ActionId)) idSet.Add(action.ActionId);
                        if (action.SeqNo > 0) seqSet.Add(action.SeqNo);
                        added++;
                    }
                }
            }
            return added;
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

        // Removes a single action by ActionId (used by text-edit / targeted delete and the
        // regular client undo button). Returns (seqNo, actionId) when the action lives in
        // _canvasState, and (-1, actionId) when it has already been trimmed out by a
        // snapshot — the caller still wants to broadcast DRAW_UNDO in that case because
        // peers hold the action in their local _history and snapshot baselines on disk
        // still reference it. Returns (-1, null) only when no actionId was supplied.
        public (long SeqNo, string ActionId) UndoActionById(string roomId, string actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return (-1, null);

            long seqNo = -1;
            if (_canvasState.TryGetValue(roomId, out var state))
            {
                lock (state)
                {
                    var found = state.FirstOrDefault(a => a.ActionId == actionId);
                    if (found != null)
                    {
                        seqNo = found.SeqNo;
                        state.RemoveAll(a => a.ActionId == actionId);
                    }
                }
            }

            // Always track the ActionId so the next snapshot drops it and new joiners
            // don't see the undone action in the cached baseline. We do this even when
            // the action was just removed from _canvasState because _snapshotCache may
            // still contain a prior copy (e.g. the action was carried forward from an
            // earlier snapshot generation that was never re-taken).
            var set = _undoneSinceSnapshot.GetOrAdd(roomId, _ => new ConcurrentDictionary<string, byte>());
            set.TryAdd(actionId, 0);

            // Stale SeqNo may linger in _undoStacks; UndoLastAction silently skips missing entries.
            return (seqNo, actionId);
        }

        public void ClearCanvas(string roomId)
        {
            if (_canvasState.TryGetValue(roomId, out var state))
                lock (state) { state.Clear(); }

            // Also clear the snapshot cache so new joiners get an empty canvas
            _snapshotCache.TryRemove(roomId, out _);
            _undoneSinceSnapshot.TryRemove(roomId, out _);

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

            // Drop anything undone since the last snapshot. Without this, the next baseline
            // would re-include actions that the user already removed via undo — they'd
            // resurface on the next join.
            if (_undoneSinceSnapshot.TryGetValue(roomId, out var undoneSet) && undoneSet.Count > 0)
                copy.RemoveAll(a => !string.IsNullOrEmpty(a.ActionId) && undoneSet.ContainsKey(a.ActionId));

            return (copy, lastSeq, version);
        }

        // Called by AutoSaveService after the new snapshot is persisted to DB.
        // Updates the in-memory cache so new joiners get the fresh compressed baseline.
        // Also clears the post-snapshot undone-set: the new baseline already excludes those
        // ActionIds (PrepareSnapshot filtered them), so they no longer need filtering on join.
        public void SetSnapshotCache(string roomId, string compressedData)
        {
            _snapshotCache[roomId] = compressedData;
            _undoneSinceSnapshot.TryRemove(roomId, out _);
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
            _undoneSinceSnapshot.TryRemove(roomId, out _);
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
            // Refuse resurrection: if we recently deleted this room, a peer's stale
            // KnownRooms (e.g. they missed our PEER_ROOM_DELETE during a disconnect)
            // would otherwise re-add it to _rooms and lobby clients would see it back.
            if (IsTombstoned(room.Id))
            {
                Console.WriteLine($"  [Peer] Refused tombstoned room '{room.Name}' ({room.Id})");
                return false;
            }
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
            // Self-loop guard: a misconfigured peer list can leave a server connected back
            // to itself. Without this, every local broadcast would also arrive as a PEER_RELAY
            // and be re-broadcast — making chat messages and join notifications appear twice.
            if (!string.IsNullOrEmpty(SelfServerId)
                && string.Equals(originServerId, SelfServerId, StringComparison.Ordinal))
                return;
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
                    if (notif != null && !string.IsNullOrEmpty(notif.ActionId))
                    {
                        if (loaded && _canvasState.TryGetValue(roomId, out var state))
                            lock (state) { state.RemoveAll(a => a.ActionId == notif.ActionId); }
                        // Track even when the action is missing from _canvasState — the peer may
                        // have undone an action that's still in this server's cached snapshot.
                        var set = _undoneSinceSnapshot.GetOrAdd(roomId, _ => new ConcurrentDictionary<string, byte>());
                        set.TryAdd(notif.ActionId, 0);
                    }
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
