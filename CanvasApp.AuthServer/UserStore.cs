using System;
using CanvasApp.Common;
using MySql.Data.MySqlClient;

namespace CanvasApp.AuthServer
{
    public class UserStore
    {
        private readonly string _connectionString;

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
            }
            Console.WriteLine("[UserStore] Schema migration complete");
        }

        public RegisterResult Register(RegisterRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return new RegisterResult { Success = false, Message = "Username/password không hợp lệ" };

            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = @"INSERT INTO users (username, email, password_hash)
                                        VALUES (@Username, @Email, @PasswordHash)";
                    cmd.Parameters.AddWithValue("@Username", req.Username);
                    cmd.Parameters.AddWithValue("@Email", req.Email ?? "");
                    cmd.Parameters.AddWithValue("@PasswordHash", BCrypt.Net.BCrypt.HashPassword(req.Password, 12));
                    cmd.ExecuteNonQuery();
                }
                Console.WriteLine($"[UserStore] Registered user '{req.Username}'");
                return new RegisterResult { Success = true, Message = "Đăng ký thành công" };
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return new RegisterResult { Success = false, Message = "Username đã tồn tại" };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserStore] Register error: {ex.Message}");
                return new RegisterResult { Success = false, Message = "Lỗi server" };
            }
        }

        public LoginResult Login(LoginRequest req)
        {
            if (string.IsNullOrEmpty(req.Username))
                return new LoginResult { Success = false, Message = "Sai tài khoản hoặc mật khẩu" };

            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = @"SELECT id, username, email, password_hash, avatar_color
                                        FROM users WHERE username = @Username";
                    cmd.Parameters.AddWithValue("@Username", req.Username);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return new LoginResult { Success = false, Message = "Sai tài khoản hoặc mật khẩu" };

                        var passwordHash = reader.GetString("password_hash");
                        if (!BCrypt.Net.BCrypt.Verify(req.Password, passwordHash))
                            return new LoginResult { Success = false, Message = "Sai tài khoản hoặc mật khẩu" };

                        var user = new User
                        {
                            Id = reader.GetInt32("id"),
                            Username = reader.GetString("username"),
                            Email = reader.GetString("email"),
                            AvatarColor = reader.GetString("avatar_color")
                        };

                        var raw = $"{user.Id}:{user.Username}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));

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
                        return new User
                        {
                            Id = reader.GetInt32("id"),
                            Username = reader.GetString("username"),
                            Email = reader.GetString("email"),
                            AvatarColor = reader.GetString("avatar_color"),
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

        public static int VerifyToken(string token)
        {
            try
            {
                var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = raw.Split(':');
                if (parts.Length < 3) return -1;
                if (!long.TryParse(parts[2], out long issuedAt)) return -1;
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - issuedAt > 86400) return -1;
                if (int.TryParse(parts[0], out int userId)) return userId;
                return -1;
            }
            catch { return -1; }
        }
    }
}
