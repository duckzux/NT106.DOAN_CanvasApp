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
        }

        private void InitializeDatabase()
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS users (
                        id INT AUTO_INCREMENT PRIMARY KEY,
                        username VARCHAR(50) NOT NULL UNIQUE,
                        email VARCHAR(100) DEFAULT '',
                        password_hash VARCHAR(255) NOT NULL,
                        avatar_color VARCHAR(20) DEFAULT '#7856CF',
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS rooms (
                        id VARCHAR(36) PRIMARY KEY,
                        name VARCHAR(100) NOT NULL,
                        owner_id INT,
                        max_users INT DEFAULT 8,
                        is_password_protected BOOLEAN DEFAULT FALSE,
                        password VARCHAR(255) DEFAULT NULL,
                        is_active BOOLEAN DEFAULT TRUE,
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (owner_id) REFERENCES users(id)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS room_members (
                        room_id VARCHAR(36),
                        user_id INT,
                        role ENUM('OWNER','MEMBER','VIEWER') DEFAULT 'MEMBER',
                        joined_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        PRIMARY KEY (room_id, user_id),
                        FOREIGN KEY (room_id) REFERENCES rooms(id),
                        FOREIGN KEY (user_id) REFERENCES users(id)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS canvas_snapshots (
                        id INT AUTO_INCREMENT PRIMARY KEY,
                        room_id VARCHAR(36),
                        snapshot_data LONGTEXT,
                        version INT NOT NULL,
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (room_id) REFERENCES rooms(id)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS draw_actions (
                        id BIGINT AUTO_INCREMENT PRIMARY KEY,
                        room_id VARCHAR(36),
                        user_id INT,
                        action_type VARCHAR(20),
                        action_data TEXT,
                        timestamp BIGINT NOT NULL
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
                cmd.ExecuteNonQuery();
                Console.WriteLine("[UserStore] Database ready");
            }
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

        public static int VerifyToken(string token)
        {
            try
            {
                var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = raw.Split(':');
                if (parts.Length < 3) return -1;
                if (int.TryParse(parts[0], out int userId)) return userId;
                return -1;
            }
            catch { return -1; }
        }
    }
}
