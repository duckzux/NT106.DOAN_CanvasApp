using System;
using System.Collections.Generic;
using CanvasApp.Common;
using MySql.Data.MySqlClient;

namespace CanvasApp.Common.DataAccess
{
    public class RoomMemberDAO
    {
        public void Insert(string roomId, int userId, string role)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(@"
                    INSERT INTO room_members(room_id, user_id, role)
                    VALUES(@r, @u, @role)", conn);
                cmd.Parameters.AddWithValue("@r", roomId);
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@role", role.ToUpper());
                cmd.ExecuteNonQuery();
            }
        }

        public RoomMember Find(string roomId, int userId)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(@"
                    SELECT rm.user_id, u.username, rm.role, u.avatar_color
                    FROM room_members rm
                    JOIN users u ON rm.user_id = u.id
                    WHERE rm.room_id=@r AND rm.user_id=@u", conn);
                cmd.Parameters.AddWithValue("@r", roomId);
                cmd.Parameters.AddWithValue("@u", userId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new RoomMember
                    {
                        UserId = reader.GetInt32("user_id"),
                        Username = reader.GetString("username"),
                        Role = reader.GetString("role"),
                        AvatarColor = reader.GetString("avatar_color")
                    };
                }
            }
        }

        public void UpdateLastSeen(string roomId, int userId)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(
                    "UPDATE room_members SET last_seen_at=NOW() WHERE room_id=@r AND user_id=@u", conn);
                cmd.Parameters.AddWithValue("@r", roomId);
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.ExecuteNonQuery();
            }
        }

        public List<RoomMember> GetMembers(string roomId)
        {
            var members = new List<RoomMember>();
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(@"
                    SELECT rm.user_id, u.username, rm.role, u.avatar_color
                    FROM room_members rm
                    JOIN users u ON rm.user_id = u.id
                    WHERE rm.room_id=@r
                    ORDER BY rm.joined_at ASC", conn);
                cmd.Parameters.AddWithValue("@r", roomId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        members.Add(new RoomMember
                        {
                            UserId = reader.GetInt32("user_id"),
                            Username = reader.GetString("username"),
                            Role = reader.GetString("role"),
                            AvatarColor = reader.GetString("avatar_color")
                        });
                    }
                }
            }
            return members;
        }
    }
}
