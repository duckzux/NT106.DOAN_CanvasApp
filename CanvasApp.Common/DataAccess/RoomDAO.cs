using System;
using System.Collections.Generic;
using CanvasApp.Common;
using MySql.Data.MySqlClient;

namespace CanvasApp.Common.DataAccess
{
    public class RoomDAO
    {
        public void Insert(Room r)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(@"
                    INSERT INTO rooms(id, name, owner_id, password_hash, max_users, is_active, template, invite_code)
                    VALUES(@id, @n, @o, @p, @m, TRUE, @t, @ic)", conn);
                cmd.Parameters.AddWithValue("@id", r.Id);
                cmd.Parameters.AddWithValue("@n", r.Name);
                cmd.Parameters.AddWithValue("@o", r.OwnerId);
                cmd.Parameters.AddWithValue("@p", (object)r.PasswordHash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@m", r.MaxUsers);
                cmd.Parameters.AddWithValue("@t", r.Template ?? "Blank");
                cmd.Parameters.AddWithValue("@ic", (object)r.InviteCode ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        public Room FindById(string roomId)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(@"
                    SELECT r.id, r.name, r.owner_id, u.username AS owner_name,
                           r.password_hash, r.max_users, r.is_active, r.template, r.created_at, r.invite_code
                    FROM rooms r
                    JOIN users u ON r.owner_id = u.id
                    WHERE r.id=@id", conn);
                cmd.Parameters.AddWithValue("@id", roomId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return MapRow(reader);
                }
            }
        }

        public List<Room> GetAllActive()
        {
            var rooms = new List<Room>();
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(@"
                    SELECT r.id, r.name, r.owner_id, u.username AS owner_name,
                           r.password_hash, r.max_users, r.is_active, r.template, r.created_at, r.invite_code
                    FROM rooms r
                    JOIN users u ON r.owner_id = u.id
                    WHERE r.is_active = TRUE
                    ORDER BY r.created_at DESC", conn);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        rooms.Add(MapRow(reader));
                }
            }
            return rooms;
        }

        public void SetActive(string roomId, bool active)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(
                    "UPDATE rooms SET is_active=@a WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@a", active);
                cmd.Parameters.AddWithValue("@id", roomId);
                cmd.ExecuteNonQuery();
            }
        }

        // Pass null/empty hash to make the room public again.
        public void UpdatePasswordHash(string roomId, string passwordHash)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(
                    "UPDATE rooms SET password_hash=@p WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@p",
                    string.IsNullOrEmpty(passwordHash) ? (object)DBNull.Value : passwordHash);
                cmd.Parameters.AddWithValue("@id", roomId);
                cmd.ExecuteNonQuery();
            }
        }

        private Room MapRow(MySqlDataReader r)
        {
            var pwdOrd = r.GetOrdinal("password_hash");
            var icOrd  = r.GetOrdinal("invite_code");
            return new Room
            {
                Id = r.GetString("id"),
                Name = r.GetString("name"),
                OwnerId = r.GetInt32("owner_id"),
                OwnerName = r.GetString("owner_name"),
                PasswordHash = r.IsDBNull(pwdOrd) ? null : r.GetString(pwdOrd),
                HasPassword = !r.IsDBNull(pwdOrd),
                MaxUsers = r.GetInt32("max_users"),
                Template = r.IsDBNull(r.GetOrdinal("template")) ? "Blank" : r.GetString("template"),
                CreatedAt = r.GetDateTime("created_at"),
                InviteCode = r.IsDBNull(icOrd) ? null : r.GetString(icOrd)
            };
        }
    }
}
