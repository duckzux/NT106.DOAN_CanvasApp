using System;
using System.Collections.Generic;
using System.Threading;
using CanvasApp.Common;
using CanvasApp.Common.DataAccess;
using Newtonsoft.Json;

namespace CanvasApp.Server
{
    // Periodically captures canvas snapshots and GCs stale draw_actions.
    // Also removes rooms that have been empty for more than 30 minutes.
    // Fires every 60 seconds; also triggered on-demand (e.g. when a room empties).
    public class AutoSaveService
    {
        private readonly RoomManager _rooms;
        private readonly CanvasSnapshotDAO _snapDao;
        private readonly DrawActionDAO _actionDao;
        private readonly Timer _timer;

        private const int TickIntervalMs = 60_000;
        private const int KeepSnapshots = 5;
        // Safety margin: don't GC actions that could still be in the PersistenceQueue.
        // The queue flushes in batches of 200, so leave at least 200 actions intact.
        private const long GcSafetyMargin = 200;
        private static readonly TimeSpan IdleRoomThreshold = TimeSpan.FromMinutes(30);

        public AutoSaveService(RoomManager rooms, CanvasSnapshotDAO snapDao, DrawActionDAO actionDao)
        {
            _rooms = rooms;
            _snapDao = snapDao;
            _actionDao = actionDao;
            _timer = new Timer(_ => Tick(), null, TickIntervalMs, TickIntervalMs);
        }

        private void Tick()
        {
            foreach (var roomId in _rooms.GetDirtyRoomIds())
            {
                try { TakeSnapshot(roomId); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AutoSave] Snapshot failed for {roomId}: {ex.Message}");
                }
            }

            CheckIdleRooms();
        }

        // Force a snapshot immediately (e.g. on DRAW_CLEAR or last user leaves).
        public void ForceSnapshot(string roomId)
        {
            try { TakeSnapshot(roomId); }
            catch (Exception ex)
            {
                Console.WriteLine($"[AutoSave] Forced snapshot failed for {roomId}: {ex.Message}");
            }
        }

        private void TakeSnapshot(string roomId)
        {
            var (actions, lastSeq, version) = _rooms.PrepareSnapshot(roomId);
            if (actions == null || actions.Count == 0) return;

            var data = SnapshotHelper.Compress(JsonConvert.SerializeObject(actions));

            _snapDao.Insert(new CanvasSnapshot
            {
                RoomId = roomId,
                Version = version,
                SnapshotData = data,
                ActionSeqAt = lastSeq,
                ByteSize = data.Length
            });

            // Keep last N snapshots; prune draw_actions older than the oldest kept snapshot.
            // FIX: subtract GcSafetyMargin to avoid racing with PersistenceQueue flushes.
            _snapDao.PruneOlderThan(roomId, KeepSnapshots);
            var oldestSeq = _snapDao.GetOldestKeptSeq(roomId);
            if (oldestSeq > GcSafetyMargin)
                _actionDao.DeleteOlderThanSeq(roomId, oldestSeq - GcSafetyMargin);

            // Update in-memory snapshot cache and trim _canvasState to only post-snapshot deltas.
            // This keeps RAM bounded: _canvasState never grows past ~200 actions between ticks.
            _rooms.SetSnapshotCache(roomId, data);
            _rooms.TrimCanvasState(roomId, lastSeq);

            Console.WriteLine($"[AutoSave] Snapshot v{version} for room {roomId} " +
                              $"({actions.Count} actions, {data.Length} bytes, seq≤{lastSeq})");
        }

        private void CheckIdleRooms()
        {
            foreach (var roomId in _rooms.GetIdleRoomIds(IdleRoomThreshold))
            {
                try { _rooms.RemoveIdleRoom(roomId); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AutoSave] RemoveIdleRoom failed for {roomId}: {ex.Message}");
                }
            }
        }

        // Kept for backward-compat callers; delegates to SnapshotHelper.
        public static List<DrawAction> Decompress(string base64Data) =>
            SnapshotHelper.Decompress(base64Data);

        public void Dispose() => _timer?.Dispose();
    }
}
