using System;
using System.Collections.Generic;
using System.Text;
using CanvasApp.Common;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;

namespace CanvasApp.Common.DataAccess
{
    public class DrawActionDAO
    {
        public void InsertBatch(List<DrawAction> actions)
        {
            if (actions == null || actions.Count == 0) return;

            using (var conn = DatabaseManager.OpenConnection())
            using (var tx = conn.BeginTransaction())
            {
                var sb = new StringBuilder(
                    "INSERT INTO draw_actions(room_id, user_id, seq_no, action_type, action_data, client_ts) VALUES ");
                var cmd = new MySqlCommand { Connection = conn, Transaction = tx };

                for (int i = 0; i < actions.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.AppendFormat("(@r{0},@u{0},@s{0},@t{0},@d{0},@c{0})", i);
                    var a = actions[i];
                    cmd.Parameters.AddWithValue("@r" + i, a.RoomId);
                    cmd.Parameters.AddWithValue("@u" + i, a.UserId);
                    cmd.Parameters.AddWithValue("@s" + i, a.SeqNo);
                    cmd.Parameters.AddWithValue("@t" + i, a.Type ?? "STROKE");
                    cmd.Parameters.AddWithValue("@d" + i, JsonConvert.SerializeObject(a));
                    cmd.Parameters.AddWithValue("@c" + i, a.Timestamp);
                }

                cmd.CommandText = sb.ToString();
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
        }

        public List<DrawAction> GetSinceSeq(string roomId, long seqNo)
        {
            var actions = new List<DrawAction>();
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(@"
                    SELECT seq_no, action_type, action_data, client_ts, is_undone
                    FROM draw_actions
                    WHERE room_id=@r AND seq_no > @s AND is_undone=FALSE
                    ORDER BY seq_no", conn);
                cmd.Parameters.AddWithValue("@r", roomId);
                cmd.Parameters.AddWithValue("@s", seqNo);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var json = reader.GetString("action_data");
                        var action = JsonConvert.DeserializeObject<DrawAction>(json) ?? new DrawAction();
                        action.SeqNo = reader.GetInt64("seq_no");
                        action.IsUndone = reader.GetBoolean("is_undone");
                        action.RoomId = roomId;
                        actions.Add(action);
                    }
                }
            }
            return actions;
        }

        public void MarkUndone(string roomId, long seqNo)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(
                    "UPDATE draw_actions SET is_undone=TRUE WHERE room_id=@r AND seq_no=@s", conn);
                cmd.Parameters.AddWithValue("@r", roomId);
                cmd.Parameters.AddWithValue("@s", seqNo);
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteOlderThanSeq(string roomId, long seqNo)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(
                    "DELETE FROM draw_actions WHERE room_id=@r AND seq_no < @s", conn);
                cmd.Parameters.AddWithValue("@r", roomId);
                cmd.Parameters.AddWithValue("@s", seqNo);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
