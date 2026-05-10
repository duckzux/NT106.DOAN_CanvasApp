using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CanvasApp.Common;
using Newtonsoft.Json;

namespace CanvasApp.AuthServer
{
    /// <summary>
    /// Lưu user vào file JSON local, dễ thay bằng MySQL sau.
    /// Password đã hash bằng BCrypt khi lưu.
    /// </summary>
    public class UserStore
    {
        private readonly string _filePath;
        private readonly ConcurrentDictionary<string, User> _users = new ConcurrentDictionary<string, User>();
        private int _nextId = 1;
        private readonly object _saveLock = new object();

        public UserStore(string filePath = "users.json")
        {
            _filePath = filePath;
            Load();
        }

        private void Load()
        {
            if (!File.Exists(_filePath)) return;
            try
            {
                var json = File.ReadAllText(_filePath);
                var list = JsonConvert.DeserializeObject<List<User>>(json) ?? new List<User>();
                foreach (var u in list)
                {
                    _users[u.Username.ToLower()] = u;
                    if (u.Id >= _nextId) _nextId = u.Id + 1;
                }
                Console.WriteLine($"[UserStore] Loaded {_users.Count} users");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserStore] Load error: {ex.Message}");
            }
        }

        private void Save()
        {
            lock (_saveLock)
            {
                try
                {
                    var list = _users.Values.ToList();
                    File.WriteAllText(_filePath, JsonConvert.SerializeObject(list, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UserStore] Save error: {ex.Message}");
                }
            }
        }

        public RegisterResult Register(RegisterRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return new RegisterResult { Success = false, Message = "Username/password không hợp lệ" };

            if (_users.ContainsKey(req.Username.ToLower()))
                return new RegisterResult { Success = false, Message = "Username đã tồn tại" };

            var user = new User
            {
                Id = System.Threading.Interlocked.Increment(ref _nextId),
                Username = req.Username,
                Email = req.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, 12)
            };

            _users[user.Username.ToLower()] = user;
            Save();
            return new RegisterResult { Success = true, Message = "Đăng ký thành công" };
        }

        public LoginResult Login(LoginRequest req)
        {
            if (string.IsNullOrEmpty(req.Username) ||
                !_users.TryGetValue(req.Username.ToLower(), out var user))
                return new LoginResult { Success = false, Message = "Sai tài khoản hoặc mật khẩu" };

            if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
                return new LoginResult { Success = false, Message = "Sai tài khoản hoặc mật khẩu" };

            // Token đơn giản: base64(userId:username:timestamp). 
            // Thay bằng JWT khi cần production.
            var raw = $"{user.Id}:{user.Username}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));

            return new LoginResult
            {
                Success = true,
                Message = "OK",
                Token = token,
                User = new User { Id = user.Id, Username = user.Username, Email = user.Email, AvatarColor = user.AvatarColor }
            };
        }

        /// <summary>Verify token, trả về userId nếu hợp lệ; -1 nếu không.</summary>
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
