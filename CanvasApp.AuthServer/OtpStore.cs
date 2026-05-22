using System;
using MySql.Data.MySqlClient;

namespace CanvasApp.AuthServer
{
    /// <summary>
    /// Persistence for email-verification OTP codes. Lives in MySQL (not in-memory)
    /// because the LoadBalancer round-robins AUTH connections — the AUTH_SEND_OTP
    /// and the follow-up AUTH_REGISTER may land on different AuthServer instances,
    /// so they must share state through the database.
    /// </summary>
    public class OtpStore
    {
        private readonly string _connectionString;

        public OtpStore(string connectionString)
        {
            _connectionString = connectionString;
            InitializeSchema();
        }

        private void InitializeSchema()
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS email_otp_codes (
                        token         CHAR(36)     PRIMARY KEY,
                        username      VARCHAR(50)  NOT NULL,
                        email         VARCHAR(100) NOT NULL,
                        code_hash     VARCHAR(255) NOT NULL,
                        expires_at    DATETIME     NOT NULL,
                        attempts      INT          NOT NULL DEFAULT 0,
                        used          TINYINT(1)   NOT NULL DEFAULT 0,
                        created_at    DATETIME     DEFAULT CURRENT_TIMESTAMP,
                        INDEX idx_otp_email (email)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
                cmd.ExecuteNonQuery();
            }
            Console.WriteLine("[OtpStore] email_otp_codes table ready");
        }

        public void Save(string token, string username, string email, string codeHash, DateTime expiresUtc)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO email_otp_codes
                                    (token, username, email, code_hash, expires_at)
                                    VALUES (@Token, @Username, @Email, @CodeHash, @ExpiresAt)";
                cmd.Parameters.AddWithValue("@Token", token);
                cmd.Parameters.AddWithValue("@Username", username);
                cmd.Parameters.AddWithValue("@Email", email);
                cmd.Parameters.AddWithValue("@CodeHash", codeHash);
                cmd.Parameters.AddWithValue("@ExpiresAt", expiresUtc);
                cmd.ExecuteNonQuery();
            }
        }

        public class OtpRecord
        {
            public string Token;
            public string Username;
            public string Email;
            public string CodeHash;
            public DateTime ExpiresUtc;
            public int Attempts;
            public bool Used;
        }

        public OtpRecord Find(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT token, username, email, code_hash, expires_at, attempts, used
                                    FROM email_otp_codes WHERE token = @Token";
                cmd.Parameters.AddWithValue("@Token", token);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new OtpRecord
                    {
                        Token = reader["token"].ToString(),
                        Username = reader.GetString("username"),
                        Email = reader.GetString("email"),
                        CodeHash = reader.GetString("code_hash"),
                        ExpiresUtc = DateTime.SpecifyKind(reader.GetDateTime("expires_at"), DateTimeKind.Utc),
                        Attempts = reader.GetInt32("attempts"),
                        Used = reader.GetInt32("used") != 0,
                    };
                }
            }
        }

        public void IncrementAttempts(string token)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"UPDATE email_otp_codes SET attempts = attempts + 1 WHERE token = @Token";
                cmd.Parameters.AddWithValue("@Token", token);
                cmd.ExecuteNonQuery();
            }
        }

        public void MarkUsed(string token)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"UPDATE email_otp_codes SET used = 1 WHERE token = @Token";
                cmd.Parameters.AddWithValue("@Token", token);
                cmd.ExecuteNonQuery();
            }
        }

        public bool UsernameExists(string username)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM users WHERE username = @Username LIMIT 1";
                cmd.Parameters.AddWithValue("@Username", username);
                return cmd.ExecuteScalar() != null;
            }
        }

        /// <summary>Counts OTPs issued for <paramref name="email"/> since <paramref name="sinceUtc"/>.
        /// Used by the rate-limiter so a single email can't trigger unlimited SMTP sends.</summary>
        public int CountRecentByEmail(string email, DateTime sinceUtc)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM email_otp_codes WHERE email = @Email AND created_at >= @Since";
                cmd.Parameters.AddWithValue("@Email", email);
                cmd.Parameters.AddWithValue("@Since", sinceUtc);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>Drops OTP rows that expired more than <paramref name="graceHours"/> hours ago.
        /// Run periodically from a background task so the table doesn't grow unbounded.</summary>
        public int DeleteExpired(int graceHours = 24)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM email_otp_codes WHERE expires_at < @Cutoff";
                cmd.Parameters.AddWithValue("@Cutoff", DateTime.UtcNow.AddHours(-graceHours));
                return cmd.ExecuteNonQuery();
            }
        }
    }
}
