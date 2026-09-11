using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CanvasApp.Common;

namespace CanvasApp.Common.DataAccess
{
    public class PersistenceQueue
    {
        private readonly BlockingCollection<DrawAction> _queue = new BlockingCollection<DrawAction>(boundedCapacity: 10000);
        private readonly DrawActionDAO _dao;
        private int _dropCount = 0;
        // Counts batch-level failures so an external check (or human eyeballing the log) can
        // tell whether the DB has been chronically failing — useful when the per-row fallback
        // also fails and we're forced to drop actions to avoid stalling the consumer thread.
        private int _batchFailCount = 0;
        public int DropCount => Volatile.Read(ref _dropCount);
        public int BatchFailCount => Volatile.Read(ref _batchFailCount);

        public PersistenceQueue(DrawActionDAO dao)
        {
            _dao = dao;
        }

        public void Enqueue(DrawAction action)
        {
            if (_queue.IsAddingCompleted) return;

            if (!_queue.TryAdd(action))
            {
                // Queue is full — action will not be persisted; log so it's visible
                int dropped = Interlocked.Increment(ref _dropCount);
                if (dropped % 100 == 1)   // log every 100th drop to avoid spam
                    Console.WriteLine($"[PersistenceQueue] WARNING: queue full, {dropped} actions dropped (room={action.RoomId}, seq={action.SeqNo})");
            }
        }

        public Task StartAsync(CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var batch = new List<DrawAction>(200);
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        DrawAction first;
                        // TryTake throws OperationCanceledException when ct fires — catch it below
                        if (!_queue.TryTake(out first, 500, ct))
                            continue;

                        batch.Add(first);

                        DrawAction extra;
                        while (batch.Count < 200 && _queue.TryTake(out extra))
                            batch.Add(extra);

                        FlushBatchWithFallback(batch);
                        batch.Clear();
                    }
                    catch (OperationCanceledException)
                    {
                        // Normal shutdown — drain whatever is still queued before exiting
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[PersistenceQueue] Unexpected error: " + ex.Message);
                        batch.Clear();
                    }
                }

                // Final drain on graceful shutdown
                if (batch.Count > 0)
                {
                    FlushBatchWithFallback(batch);
                    batch.Clear();
                }

                DrawAction remaining;
                while (_queue.TryTake(out remaining))
                    batch.Add(remaining);

                if (batch.Count > 0)
                {
                    FlushBatchWithFallback(batch);
                    batch.Clear();
                }

            }, ct);
        }

        // Tries InsertBatch first (fast path). On failure, falls back to inserting actions
        // one-by-one so a single bad row (constraint violation, oversized payload, etc.)
        // doesn't take down the entire batch. Only rows whose individual insert ALSO fails
        // are counted as dropped — and that's logged so the failure surface is visible
        // instead of silently swallowed.
        private void FlushBatchWithFallback(List<DrawAction> batch)
        {
            if (batch == null || batch.Count == 0) return;

            try
            {
                _dao.InsertBatch(batch);
                return;
            }
            catch (Exception ex)
            {
                int fails = Interlocked.Increment(ref _batchFailCount);
                Console.WriteLine($"[PersistenceQueue] InsertBatch failed ({fails} total): {ex.Message}; retrying per-row");
            }

            int dropped = 0;
            int saved = 0;
            foreach (var action in batch)
            {
                try
                {
                    // Reuse the batch DAO with a singleton list — keeps the DAO surface area small.
                    _dao.InsertBatch(new List<DrawAction> { action });
                    saved++;
                }
                catch (Exception ex)
                {
                    dropped++;
                    int total = Interlocked.Increment(ref _dropCount);
                    // Log a sample (every 50th) of per-row drops to avoid swamping the console.
                    if (total % 50 == 1)
                        Console.WriteLine($"[PersistenceQueue] per-row insert failed (room={action?.RoomId}, seq={action?.SeqNo}): {ex.Message} — {total} total drops");
                }
            }
            if (saved > 0 || dropped > 0)
                Console.WriteLine($"[PersistenceQueue] per-row fallback: saved={saved}, dropped={dropped}");
        }

        public void Stop()
        {
            _queue.CompleteAdding();
        }
    }
}
