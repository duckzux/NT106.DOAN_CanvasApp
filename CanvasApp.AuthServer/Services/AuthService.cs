using System;
using CanvasApp.Common;

namespace CanvasApp.AuthServer.Services
{
    /// <summary>
    /// Login: looks up user in DB, verifies BCrypt hash, issues a token.
    /// </summary>
    public class AuthService
    {
        private readonly UserStore _store;

        public AuthService(UserStore store)
        {
            _store = store;
        }

        public Message Login(LoginRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Username))
                return new Message(MessageType.AUTH_LOGIN_RESULT,
                    new LoginResult { Success = false, Message = "Thông tin đăng nhập không hợp lệ" });

            Console.WriteLine($"  [AuthService] LOGIN '{req.Username}'");
            var result = _store.Login(req);
            Console.WriteLine($"  [AuthService] LOGIN '{req.Username}' → {(result.Success ? "OK" : result.Message)}");
            return new Message(MessageType.AUTH_LOGIN_RESULT, result);
        }
    }
}
