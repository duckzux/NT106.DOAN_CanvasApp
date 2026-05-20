using System.Threading;

namespace CanvasApp.Server
{
    // Per-room metadata for DB persistence tracking.
    // The in-memory canvas state (draw action list) lives in RoomManager._canvasState.
    public class RoomState
    {
        public string RoomId { get; }

        private long _seqNo = 0;
        public int DirtyActionCount = 0;   // actions since last snapshot (read/written with Interlocked)

        private int _snapshotVersion = 0;

        public RoomState(string roomId)
        {
            RoomId = roomId;
        }

        // Returns the next monotonically-increasing sequence number for this room.
        public long NextSeqNo()
        {
            return Interlocked.Increment(ref _seqNo);
        }

        public long LastSeqNo => Interlocked.Read(ref _seqNo);

        // Seed the seq counter from a loaded snapshot or delta batch (called on DB recovery).
        public void InitSeqNo(long seq)
        {
            Interlocked.Exchange(ref _seqNo, seq);
        }

        // Advance the seq counter to at least `incoming` — used when a peer Canvas server
        // relays an action so subsequent local NextSeqNo() calls don't collide with it.
        // Safe under contention: compare-and-swap loop, only moves the counter forward.
        public void AdvanceSeqIfGreater(long incoming)
        {
            while (true)
            {
                long cur = Interlocked.Read(ref _seqNo);
                if (incoming <= cur) return;
                if (Interlocked.CompareExchange(ref _seqNo, incoming, cur) == cur) return;
            }
        }

        // Returns the next snapshot version atomically (safe for concurrent ForceSnapshot + Tick).
        public int NextSnapshotVersion()
        {
            return Interlocked.Increment(ref _snapshotVersion);
        }

        public int SnapshotVersion => _snapshotVersion;

        // Seed snapshot version from DB (called on room recovery).
        public void SnapshotVersionFromDb(int version)
        {
            Interlocked.Exchange(ref _snapshotVersion, version);
        }
    }
}
