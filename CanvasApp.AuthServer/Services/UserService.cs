using System;
using CanvasApp.Common;

namespace CanvasApp.AuthServer.Services
{
    /// <summary>
    /// Register: validates input, verifies the email-OTP issued via OtpService,
    /// BCrypt-hashes the password via UserStore, persists to DB.
    /// </summary>
    public class UserService
    {
        private readonly UserStore _store;
        private readonly OtpService _otpService;

        public UserService(UserStore store, OtpService otpService)
        {
            _store = store;
            _otpService = otpService;
        }

        public Message Register(RegisterRequest req)
        {
            if (req == null)
                return new Message(MessageType.AUTH_REGISTER_RESULT,
                    new RegisterResult { Success = false, Message = "Request không hợp lệ" });

            req.Username = req.Username?.Trim();
            req.Email = req.Email?.Trim();

            if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length < 3)
                return new Message(MessageType.AUTH_REGISTER_RESULT,
                    new RegisterResult { Success = false, Message = "Username phải có ít nhất 3 ký tự" });

            if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
                return new Message(MessageType.AUTH_REGISTER_RESULT,
                    new RegisterResult { Success = false, Message = "Mật khẩu phải có ít nhất 6 ký tự" });

            if (string.IsNullOrWhiteSpace(req.Email))
                return new Message(MessageType.AUTH_REGISTER_RESULT,
                    new RegisterResult { Success = false, Message = "Email không được trống" });

            // Email-OTP must pass before we ever touch the users table.
            var verify = _otpService.Verify(req.OtpToken, req.OtpCode, req.Username, req.Email);
            if (!verify.ok)
            {
                Console.WriteLine($"  [UserService] REGISTER '{req.Username}' rejected: {verify.message}");
                return new Message(MessageType.AUTH_REGISTER_RESULT,
                    new RegisterResult { Success = false, Message = verify.message });
            }

            Console.WriteLine($"  [UserService] REGISTER '{req.Username}'");
            var result = _store.Register(req);
            Console.WriteLine($"  [UserService] REGISTER '{req.Username}' → {(result.Success ? "OK" : result.Message)}");
            return new Message(MessageType.AUTH_REGISTER_RESULT, result);
        }

        public Message SendOtp(SendOtpRequest req) => _otpService.SendOtp(req);

        public Message SendForgotPasswordOtp(ForgotPasswordSendOtpRequest req) =>
            _otpService.SendForgotPasswordOtp(req, _store);

        /// <summary>
        /// Reset password: look up user by email, verify OTP against the stored
        /// (username, email) pair, then rewrite the password hash.
        /// </summary>
        public Message ResetPassword(ResetPasswordRequest req)
        {
            if (req == null)
                return new Message(MessageType.AUTH_RESET_PASSWORD_RESULT,
                    new ResetPasswordResult { Success = false, Message = "Request không hợp lệ" });

            req.Email = req.Email?.Trim();

            if (string.IsNullOrWhiteSpace(req.Email))
                return new Message(MessageType.AUTH_RESET_PASSWORD_RESULT,
                    new ResetPasswordResult { Success = false, Message = "Email không được trống" });

            if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
                return new Message(MessageType.AUTH_RESET_PASSWORD_RESULT,
                    new ResetPasswordResult { Success = false, Message = "Mật khẩu phải có ít nhất 6 ký tự" });

            var user = _store.FindByEmail(req.Email);
            if (user == null)
                return new Message(MessageType.AUTH_RESET_PASSWORD_RESULT,
                    new ResetPasswordResult { Success = false, Message = "Không tìm thấy tài khoản với email này" });

            var verify = _otpService.Verify(req.OtpToken, req.OtpCode, user.Username, req.Email);
            if (!verify.ok)
            {
                Console.WriteLine($"  [UserService] RESET '{user.Username}' rejected: {verify.message}");
                return new Message(MessageType.AUTH_RESET_PASSWORD_RESULT,
                    new ResetPasswordResult { Success = false, Message = verify.message });
            }

            var ok = _store.UpdatePassword(user.Id, req.NewPassword);
            Console.WriteLine($"  [UserService] RESET '{user.Username}' → {(ok ? "OK" : "FAILED")}");
            return new Message(MessageType.AUTH_RESET_PASSWORD_RESULT, new ResetPasswordResult
            {
                Success = ok,
                Message = ok ? "Đặt lại mật khẩu thành công" : "Không cập nhật được mật khẩu",
            });
        }
    }
}
