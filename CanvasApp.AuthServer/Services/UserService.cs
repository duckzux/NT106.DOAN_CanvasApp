using System;
using CanvasApp.Common;

namespace CanvasApp.AuthServer.Services
{
    /// <summary>
    /// Register: validates input, BCrypt-hashes the password via UserStore, persists to DB.
    /// </summary>
    public class UserService
    {
        private readonly UserStore _store;

        public UserService(UserStore store)
        {
            _store = store;
        }

        public Message Register(RegisterRequest req)
        {
            if (req == null)
                return new Message(MessageType.AUTH_REGISTER_RESULT,
                    new RegisterResult { Success = false, Message = "Request không hợp lệ" });

            if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length < 3)
                return new Message(MessageType.AUTH_REGISTER_RESULT,
                    new RegisterResult { Success = false, Message = "Username phải có ít nhất 3 ký tự" });

            if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
                return new Message(MessageType.AUTH_REGISTER_RESULT,
                    new RegisterResult { Success = false, Message = "Mật khẩu phải có ít nhất 6 ký tự" });

            Console.WriteLine($"  [UserService] REGISTER '{req.Username}'");
            var result = _store.Register(req);
            Console.WriteLine($"  [UserService] REGISTER '{req.Username}' → {(result.Success ? "OK" : result.Message)}");
            return new Message(MessageType.AUTH_REGISTER_RESULT, result);
        }
    }
}
