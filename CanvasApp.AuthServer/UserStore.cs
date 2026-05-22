using System;
using System.Security.Cryptography;
using System.Text;
using MySql.Data.MySqlClient;
using CanvasApp.Common;

namespace CanvasApp.AuthServer
{
    public class UserStore
    {
        private readonly string _connectionString;

        // HMAC secret for signing auth tokens. Both AuthServer (issues) and CanvasServer
        // (verifies) read this — set CANVASAPP_JWT_SECRET to the same value on both. The
        // hardcoded fallback is fine for a school demo but MUST be overridden in production:
        // without it, anyone who can read this source can forge tokens for any user.
        private const string DefaultJwtSecret = "CanvasApp_NT106_Network_2026_dev_secret_!@#$%";
        private static byte[] _jwtSecretCache;
        private static byte[] GetJwtSecret()
        {
            if (_jwtSecretCache != null) return _jwtSecretCache;
            var env = Environment.GetEnvironmentVariable("CANVASAPP_JWT_SECRET");
            var raw = string.IsNullOrWhiteSpace(env) ? DefaultJwtSecret : env;
            _jwtSecretCache = Encoding.UTF8.GetBytes(raw);
            return _jwtSecretCache;
        }

        // Pre-computed BCrypt hash used to equalise login-failure timing when the username
        // doesn't exist. Without this, attackers can tell registered usernames apart from
        // non-existent ones by measuring response time (BCrypt.Verify ≈ 100ms vs early return).
        private static readonly string _dummyBcryptHash =
            BCrypt.Net.BCrypt.HashPassword("dummy-timing-equaliser", 12);

        public UserStore(string connectionString)
        {
            _connectionString = connectionString;
            InitializeDatabase();
            MigrateSchema();
        }

        private void InitializeDatabase()
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();

                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS users (
                        id              INT AUTO_INCREMENT PRIMARY KEY,
                        username        VARCHAR(50)  NOT NULL UNIQUE,
                        email           VARCHAR(100) DEFAULT '',
                        password_hash   VARCHAR(255) NOT NULL,
                        avatar_color    VARCHAR(20)  DEFAULT '#7856CF',
                        created_at      DATETIME     DEFAULT CURRENT_TIMESTAMP,
                        last_login_at   DATETIME     NULL
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
                cmd.ExecuteNonQuery();

                // password_hash replaces old is_password_protected + password columns
                // template stores the room template (e.g. Blank)
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS rooms (
                        id              VARCHAR(36)  PRIMARY KEY,
                        name            VARCHAR(100) NOT NULL,
                        owner_id        INT,
                        password_hash   VARCHAR(255) NULL,
                        max_users       INT          DEFAULT 8,
                        is_active       BOOLEAN      DEFAULT TRUE,
                        template        VARCHAR(50)  DEFAULT 'Blank',
                        created_at      DATETIME     DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (owner_id) REFERENCES users(id)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
                cmd.ExecuteNonQuery();

                // last_seen_at tracks when a member last disconnected
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS room_members (
                        room_id         VARCHAR(36),
                        user_id         INT,
                        role            ENUM('OWNER','MEMBER','VIEWER') DEFAULT 'MEMBER',
                        joined_at       DATETIME DEFAULT CURRENT_TIMESTAMP,
                        last_seen_at    DATETIME DEFAULT CURRENT_TIMESTAMP,
                        PRIMARY KEY (room_id, user_id),
                        FOREIGN KEY (room_id) REFERENCES rooms(id),
                        FOREIGN KEY (user_id) REFERENCES users(id)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
                cmd.ExecuteNonQuery();

                // action_seq_at + byte_size added for delta-sync and monitoring
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS canvas_snapshots (
                        id              BIGINT AUTO_INCREMENT PRIMARY KEY,
                        room_id         VARCHAR(36),
                        version         INT          NOT NULL,
                        snapshot_data   LONGTEXT     NOT NULL,
                        action_seq_at   BIGINT       NOT NULL DEFAULT 0,
                        byte_size       INT          NOT NULL DEFAULT 0,
                        created_at      DATETIME     DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (room_id) REFERENCES rooms(id)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
                cmd.ExecuteNonQuery();

                // seq_no for deterministic ordering; is_undone for soft undo; client_ts from sender
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS draw_actions (
                        id              BIGINT AUTO_INCREMENT PRIMARY KEY,
                        room_id         VARCHAR(36),
                        user_id         INT,
                        seq_no          BIGINT       NOT NULL DEFAULT 0,
                        action_type     VARCHAR(20),
                        action_data     LONGTEXT,
                        client_ts       BIGINT       NOT NULL DEFAULT 0,
                        server_ts       DATETIME     DEFAULT CURRENT_TIMESTAMP,
                        is_undone       TINYINT(1)   NOT NULL DEFAULT 0,
                        INDEX idx_room_seq (room_id, seq_no),
                        INDEX idx_gc (room_id, server_ts)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS chat_messages (
                        id              BIGINT AUTO_INCREMENT PRIMARY KEY,
                        room_id         VARCHAR(36),
                        user_id         INT,
                        message         TEXT         NOT NULL,
                        sent_at         DATETIME     DEFAULT CURRENT_TIMESTAMP,
                        INDEX idx_chat_room (room_id, id)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
                cmd.ExecuteNonQuery();

                Console.WriteLine("[UserStore] Database tables ready");
            }
        }

        // Adds columns introduced after the initial schema, silently ignores if already present.
        private void MigrateSchema()
        {
            var migrations = new[]
            {
                "ALTER TABLE users      ADD COLUMN last_login_at  DATETIME     NULL",
                "ALTER TABLE rooms      ADD COLUMN password_hash  VARCHAR(255) NULL",
                "ALTER TABLE rooms      ADD COLUMN template        VARCHAR(50)  DEFAULT 'Blank'",
                "ALTER TABLE rooms      ADD COLUMN invite_code     VARCHAR(8)   NULL",
                "ALTER TABLE room_members ADD COLUMN last_seen_at DATETIME     DEFAULT CURRENT_TIMESTAMP",
                "ALTER TABLE draw_actions ADD COLUMN seq_no       BIGINT       NOT NULL DEFAULT 0",
                "ALTER TABLE draw_actions ADD COLUMN is_undone    TINYINT(1)   NOT NULL DEFAULT 0",
                "ALTER TABLE draw_actions ADD COLUMN client_ts    BIGINT       NOT NULL DEFAULT 0",
                "ALTER TABLE canvas_snapshots ADD COLUMN action_seq_at BIGINT  NOT NULL DEFAULT 0",
                "ALTER TABLE canvas_snapshots ADD COLUMN byte_size     INT     NOT NULL DEFAULT 0",
            };

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                foreach (var sql in migrations)
                {
                    try
                    {
                        using (var cmd = new MySqlCommand(sql, conn))
                            cmd.ExecuteNonQuery();
                    }
                    catch (MySqlException ex) when (ex.Number == 1060)
                    {
                        // Duplicate column — already migrated, skip
                    }
                }

                // Two-step: convert empty-string emails to NULL first (so UNIQUE doesn't
                // refuse rows that all share an empty email), then add the UNIQUE constraint.
                // Wrapped separately so a pre-existing duplicate doesn't roll back column adds.
                try
                {
                    using (var cmd = new MySqlCommand("UPDATE users SET email = NULL WHERE email = ''", conn))
                        cmd.ExecuteNonQuery();
                }
                catch (Exception ex) { Console.WriteLine($"[UserStore] email empty→NULL failed: {ex.Message}"); }

                try
                {
                    using (var cmd = new MySqlCommand("ALTER TABLE users ADD UNIQUE KEY uk_users_email (email)", conn))
                        cmd.ExecuteNonQuery();
                    Console.WriteLine("[UserStore] Added UNIQUE constraint on users.email");
                }
                catch (MySqlException ex) when (ex.Number == 1061 || ex.Number == 1068)
                {
                    // 1061 = duplicate key name, 1068 = multiple primary key — already migrated
                }
                catch (MySqlException ex) when (ex.Number == 1062)
                {
                    Console.WriteLine("[UserStore] ⚠ Cannot add UNIQUE(email): table already has duplicate emails. " +
                                      "Clean up manually then restart server.");
                }
                catch (Exception ex) { Console.WriteLine($"[UserStore] uk_users_email migration failed: {ex.Message}"); }
            }
            Console.WriteLine("[UserStore] Schema migration complete");
        }

        public RegisterResult Register(RegisterRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return new RegisterResult { Success = false, Message = "Username/password không hợp lệ" };

            // Store empty/whitespace emails as SQL NULL so the UNIQUE(email) constraint
            // doesn't refuse multiple "no-email" accounts (NULL ≠ NULL in MySQL UNIQUE).
            object emailParam = string.IsNullOrWhiteSpace(req.Email)
                ? (object)DBNull.Value
                : req.Email.Trim();

            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = @"INSERT INTO users (username, email, password_hash)
                                        VALUES (@Username, @Email, @PasswordHash)";
                    cmd.Parameters.AddWithValue("@Username", req.Username);
                    cmd.Parameters.AddWithValue("@Email", emailParam);
                    cmd.Parameters.AddWithValue("@PasswordHash", BCrypt.Net.BCrypt.HashPassword(req.Password, 12));
                    cmd.ExecuteNonQuery();
                }
                Console.WriteLine($"[UserStore] Registered user '{req.Username}'");
                return new RegisterResult { Success = true, Message = "Đăng ký thành công" };
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                // 1062 = duplicate-entry on a UNIQUE index. The constraint name is in the
                // error message ("Duplicate entry 'x' for key 'uk_users_email'"); use it to
                // tell the user WHICH field clashed instead of always saying "username".
                bool emailClash = ex.Message != null && ex.Message.IndexOf("email", StringComparison.OrdinalIgnoreCase) >= 0;
                return new RegisterResult
                {
                    Success = false,
                    Message = emailClash ? "Email đã được sử dụng cho tài khoản khác" : "Username đã tồn tại"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserStore] Register error: {ex.Message}");
                return new RegisterResult { Success = false, Message = "Lỗi server" };
            }
        }

        public LoginResult Login(LoginRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Username))
                return new LoginResult { Success = false, Message = "Sai tài khoản hoặc mật khẩu" };

            // Trim so accidental trailing space doesn't yield a confusing "sai tài khoản"
            // for an otherwise-correct username (MySQL VARCHAR preserves spaces).
            var username = req.Username.Trim();
            var password = req.Password ?? "";

            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = @"SELECT id, username, email, password_hash, avatar_color
                                        FROM users WHERE username = @Username";
                    cmd.Parameters.AddWithValue("@Username", username);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            // Equalise timing with the "valid username, wrong password" path —
                            // otherwise the response time difference leaks which usernames exist
                            // in the DB (a timing-side-channel user enumeration attack).
                            BCrypt.Net.BCrypt.Verify(password, _dummyBcryptHash);
                            return new LoginResult { Success = false, Message = "Sai tài khoản hoặc mật khẩu" };
                        }

                        var passwordHash = reader.GetString("password_hash");
                        if (!BCrypt.Net.BCrypt.Verify(password, passwordHash))
                            return new LoginResult { Success = false, Message = "Sai tài khoản hoặc mật khẩu" };

                        var user = new User
                        {
                            Id = reader.GetInt32("id"),
                            Username = reader.GetString("username"),
                            // Email column is now nullable (so the UNIQUE constraint can apply
                            // without forcing every legacy row to have a real address).
                            Email = reader.IsDBNull(reader.GetOrdinal("email")) ? null : reader.GetString("email"),
                            AvatarColor = reader.GetString("avatar_color")
                        };

                        var token = IssueToken(user.Id, user.Username);

                        // Update last login timestamp (best-effort — don't fail login on error)
                        try
                        {
                            using (var updateConn = new MySqlConnection(_connectionString))
                            {
                                updateConn.Open();
                                var updateCmd = updateConn.CreateCommand();
                                updateCmd.CommandText = "UPDATE users SET last_login_at=NOW() WHERE id=@Id";
                                updateCmd.Parameters.AddWithValue("@Id", user.Id);
                                updateCmd.ExecuteNonQuery();
                            }
                        }
                        catch { /* non-critical */ }

                        return new LoginResult
                        {
                            Success = true,
                            Message = "OK",
                            Token = token,
                            User = user
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserStore] Login error: {ex.Message}");
                return new LoginResult { Success = false, Message = "Lỗi server" };
            }
        }

        /// <summary>
        /// Look up a user by email (case-insensitive via the column's default collation).
        /// Returns the basic profile (id + username) or null if no row matches.
        /// </summary>
        public User FindByEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = @"SELECT id, username, email, avatar_color
                                        FROM users WHERE email = @Email LIMIT 1";
                    cmd.Parameters.AddWithValue("@Email", email);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read()) return null;
                        // username + email are non-null here (username is UNIQUE NOT NULL;
                        // email matched a non-NULL value in the WHERE), but avatar_color is
                        // nullable for legacy rows — guard the read so we don't throw
                        // InvalidOperationException mid forgot-password flow.
                        int emailOrd = reader.GetOrdinal("email");
                        int avatarOrd = reader.GetOrdinal("avatar_color");
                        return new User
                        {
                            Id = reader.GetInt32("id"),
                            Username = reader.GetString("username"),
                            Email = reader.IsDBNull(emailOrd) ? null : reader.GetString(emailOrd),
                            AvatarColor = reader.IsDBNull(avatarOrd) ? null : reader.GetString(avatarOrd),
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserStore] FindByEmail error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Rewrites the password hash for the given user. Used by the forgot-password
        /// flow after OTP verification succeeds. Returns true if exactly one row was updated.
        /// </summary>
        public bool UpdatePassword(int userId, string newPlainPassword)
        {
            if (userId <= 0 || string.IsNullOrEmpty(newPlainPassword)) return false;
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE users SET password_hash=@Hash WHERE id=@Id";
                    cmd.Parameters.AddWithValue("@Hash", BCrypt.Net.BCrypt.HashPassword(newPlainPassword, 12));
                    cmd.Parameters.AddWithValue("@Id", userId);
                    return cmd.ExecuteNonQuery() == 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserStore] UpdatePassword error: {ex.Message}");
                return false;
            }
        }

        // Token format: "<base64-payload>.<base64-hmac>"
        // Payload: "userId:username:issuedAtUnix"
        // HMAC:    HMAC-SHA256(payload, GetJwtSecret())
        // Without the signature, anyone who could read this source (or guess the legacy
        // base64-only format) was able to forge tokens for arbitrary userIds.
        private const long TokenTtlSeconds = 24 * 60 * 60;

        internal static string IssueToken(int userId, string username)
        {
            long issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var payload = Encoding.UTF8.GetBytes($"{userId}:{username}:{issuedAt}");
            byte[] sig;
            using (var hmac = new HMACSHA256(GetJwtSecret()))
                sig = hmac.ComputeHash(payload);
            return Convert.ToBase64String(payload) + "." + Convert.ToBase64String(sig);
        }

        public static int VerifyToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return -1;
            try
            {
                // Legacy tokens (pre-HMAC) had no '.' — reject them outright so a stale client
                // doesn't bypass signing. Users just re-login to get a fresh signed token.
                var sepIdx = token.IndexOf('.');
                if (sepIdx <= 0 || sepIdx >= token.Length - 1) return -1;

                var payloadBytes = Convert.FromBase64String(token.Substring(0, sepIdx));
                var providedSig = Convert.FromBase64String(token.Substring(sepIdx + 1));

                byte[] expectedSig;
                using (var hmac = new HMACSHA256(GetJwtSecret()))
                    expectedSig = hmac.ComputeHash(payloadBytes);

                // Constant-time compare so a fast-fail bit doesn't leak via timing.
                if (!FixedTimeEquals(providedSig, expectedSig)) return -1;

                var raw = Encoding.UTF8.GetString(payloadBytes);
                var parts = raw.Split(':');
                if (parts.Length < 3) return -1;
                if (!long.TryParse(parts[2], out long issuedAt)) return -1;
                long age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - issuedAt;
                if (age < 0 || age > TokenTtlSeconds) return -1;
                if (!int.TryParse(parts[0], out int userId)) return -1;
                return userId;
            }
            catch { return -1; }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>
        /// Extract the username from a verified token. Callers MUST call
        /// <see cref="VerifyToken"/> first — this method only does payload parsing,
        /// it does NOT re-check the HMAC signature.
        /// Returns null if the token is malformed.
        /// </summary>
        public static string ExtractUsername(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            try
            {
                // New format: "<base64-payload>.<base64-hmac>" — payload = "userId:username:issuedAt"
                var sepIdx = token.IndexOf('.');
                if (sepIdx <= 0) return null;
                var payloadBytes = Convert.FromBase64String(token.Substring(0, sepIdx));
                var raw = Encoding.UTF8.GetString(payloadBytes);
                var parts = raw.Split(':');
                return parts.Length >= 2 ? parts[1] : null;
            }
            catch { return null; }
        }
    }
}
