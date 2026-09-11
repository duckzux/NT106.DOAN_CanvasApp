using System;
using CanvasApp.Common;
using MySql.Data.MySqlClient;
using CanvasApp.Common.Models;

namespace CanvasApp.Common.DataAccess
{
    public class UserDAO
    {
        public int Insert(string username, string passwordHash, string email)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(
                    "INSERT INTO users(username, password_hash, email) VALUES(@u, @h, @e); " +
                    "SELECT LAST_INSERT_ID();", conn);
                cmd.Parameters.AddWithValue("@u", username);
                cmd.Parameters.AddWithValue("@h", passwordHash);
                cmd.Parameters.AddWithValue("@e", email ?? "");
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public User FindByUsername(string username)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(
                    "SELECT id, username, password_hash, email, avatar_color " +
                    "FROM users WHERE username=@u LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@u", username);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    return new User
                    {
                        Id = r.GetInt32("id"),
                        Username = r.GetString("username"),
                        PasswordHash = r.GetString("password_hash"),
                        Email = r.IsDBNull(r.GetOrdinal("email")) ? null : r.GetString("email"),
                        AvatarColor = r.GetString("avatar_color")
                    };
                }
            }
        }

        public void UpdateLastLogin(int userId)
        {
            using (var conn = DatabaseManager.OpenConnection())
            {
                var cmd = new MySqlCommand(
                    "UPDATE users SET last_login_at=NOW() WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@id", userId);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
