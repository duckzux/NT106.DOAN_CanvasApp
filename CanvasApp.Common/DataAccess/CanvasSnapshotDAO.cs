using System;
using System.Collections.Generic;
using CanvasApp.Common;
using MySql.Data.MySqlClient;

namespace CanvasApp.Common.DataAccess
{
    public class CanvasSnapshotDAO
    {
        public void Insert(CanvasSnapshot snap)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                // INSERT IGNORE: multiple Canvas servers may autosave the same room concurrently and
                // each computes (version = MAX(version)+1) from its local view. Without IGNORE the
                // second writer would crash on the (room_id, version) unique key; with IGNORE we
                // silently drop the loser — the deltas in draw_actions remain the source of truth,
                // so missing a snapshot only costs a slightly longer replay on next load.
                var cmd = new MySqlCommand(@"
                    INSERT IGNORE INTO canvas_snapshots(room_id, version, snapshot_data, action_seq_at, byte_size)
                    VALUES(@r, @v, @d, @seq, @sz)", conn);
                cmd.Parameters.AddWithValue("@r", snap.RoomId);
                cmd.Parameters.AddWithValue("@v", snap.Version);
                cmd.Parameters.AddWithValue("@d", snap.SnapshotData);
                cmd.Parameters.AddWithValue("@seq", snap.ActionSeqAt);
                cmd.Parameters.AddWithValue("@sz", snap.ByteSize);
                cmd.ExecuteNonQuery();
            }
        }

        public CanvasSnapshot GetLatest(string roomId)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(@"
                    SELECT id, room_id, version, snapshot_data, action_seq_at, byte_size, created_at
                    FROM canvas_snapshots
                    WHERE room_id=@r
                    ORDER BY version DESC
                    LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@r", roomId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return MapRow(reader);
                }
            }
        }

        // Deletes all but the N most recent snapshots for a room.
        public void PruneOlderThan(string roomId, int keepLastN)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(@"
                    DELETE FROM canvas_snapshots
                    WHERE room_id=@r
                      AND id NOT IN (
                          SELECT id FROM (
                              SELECT id FROM canvas_snapshots
                              WHERE room_id=@r
                              ORDER BY version DESC
                              LIMIT @n
                          ) AS keep
                      )", conn);
                cmd.Parameters.AddWithValue("@r", roomId);
                cmd.Parameters.AddWithValue("@n", keepLastN);
                cmd.ExecuteNonQuery();
            }
        }

        // Returns the action_seq_at of the oldest retained snapshot (used for GC of draw_actions).
        public long GetOldestKeptSeq(string roomId)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(@"
                    SELECT MIN(action_seq_at)
                    FROM canvas_snapshots
                    WHERE room_id=@r", conn);
                cmd.Parameters.AddWithValue("@r", roomId);
                var result = cmd.ExecuteScalar();
                if (result == null || result == DBNull.Value) return 0;
                return Convert.ToInt64(result);
            }
        }

        private CanvasSnapshot MapRow(MySqlDataReader r)
        {
            return new CanvasSnapshot
            {
                Id = r.GetInt64("id"),
                RoomId = r.GetString("room_id"),
                Version = r.GetInt32("version"),
                SnapshotData = r.GetString("snapshot_data"),
                ActionSeqAt = r.GetInt64("action_seq_at"),
                ByteSize = r.GetInt32("byte_size"),
                CreatedAt = r.GetDateTime("created_at")
            };
        }
    }
}
