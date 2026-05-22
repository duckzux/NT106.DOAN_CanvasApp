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

                        try
                        {
                            _dao.InsertBatch(batch);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[PersistenceQueue] InsertBatch failed: " + ex.Message);
                            // Actions in this batch are lost; future batches will still be attempted
                        }
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
                    try { _dao.InsertBatch(batch); }
                    catch { /* best-effort on shutdown */ }
                }

                DrawAction remaining;
                while (_queue.TryTake(out remaining))
                    batch.Add(remaining);

                if (batch.Count > 0)
                {
                    try { _dao.InsertBatch(batch); }
                    catch { /* best-effort on shutdown */ }
                }

            }, ct);
        }

        public void Stop()
        {
            _queue.CompleteAdding();
        }
    }
}
