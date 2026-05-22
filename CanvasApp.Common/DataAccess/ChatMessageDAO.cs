using System.Collections.Generic;
using CanvasApp.Common;
using MySql.Data.MySqlClient;

namespace CanvasApp.Common.DataAccess
{
    public class ChatMessageDAO
    {
        public void Insert(string roomId, int userId, string message)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(@"
                    INSERT INTO chat_messages (room_id, user_id, message, sent_at)
                    VALUES (@r, @u, @m, NOW())", conn);
                cmd.Parameters.AddWithValue("@r", roomId);
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@m", message);
                cmd.ExecuteNonQuery();
            }
        }

        public List<ChatMessage> GetByRoom(string roomId, int limit = 50)
        {
            var messages = new List<ChatMessage>();
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(@"
                    SELECT cm.user_id, u.username, cm.message,
                           UNIX_TIMESTAMP(cm.sent_at) * 1000 AS ts
                    FROM chat_messages cm
                    JOIN users u ON cm.user_id = u.id
                    WHERE cm.room_id = @r
                    ORDER BY cm.id DESC
                    LIMIT @l", conn);
                cmd.Parameters.AddWithValue("@r", roomId);
                cmd.Parameters.AddWithValue("@l", limit);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // Insert at front so the final list is chronological
                        messages.Insert(0, new ChatMessage
                        {
                            UserId = reader.GetInt32("user_id"),
                            Username = reader.GetString("username"),
                            Text = reader.GetString("message"),
                            Timestamp = reader.GetInt64("ts")
                        });
                    }
                }
            }
            return messages;
        }
    }
}
