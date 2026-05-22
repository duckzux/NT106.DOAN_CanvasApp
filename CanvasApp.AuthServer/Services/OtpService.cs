using System;
using System.Configuration;
using System.Security.Cryptography;
using CanvasApp.Common;

namespace CanvasApp.AuthServer.Services
{
    /// <summary>
    /// Email-OTP business logic: generates a 6-digit code, hashes it with BCrypt,
    /// persists via <see cref="OtpStore"/>, and dispatches the mail via SMTP.
    /// Verification is single-shot — a successful match marks the row used so
    /// the same OtpToken can't be replayed.
    /// </summary>
    public class OtpService
    {
        private const int MaxAttempts = 5;
        // Rate limit: at most 1 OTP per email per minute, and 5 per hour. Tuned for demo —
        // forgot-password + register flows should never legitimately exceed these in normal use.
        private const int RateLimitPerMinute = 1;
        private const int RateLimitPerHour = 5;

        private readonly OtpStore _store;
        private readonly SmtpEmailSender _mailer;
        private readonly int _expirationMinutes;

        public OtpService(OtpStore store, SmtpEmailSender mailer)
        {
            _store = store;
            _mailer = mailer;
            _expirationMinutes = int.TryParse(ConfigurationManager.AppSettings["OtpExpirationMinutes"], out var m) && m > 0
                ? m : 5;
        }

        // Returns null on success, or a user-facing error string when the email has hit a limit.
        // Centralised here so both SendOtp and SendForgotPasswordOtp share identical thresholds.
        private string CheckRateLimit(string email)
        {
            try
            {
                if (_store.CountRecentByEmail(email, DateTime.UtcNow.AddMinutes(-1)) >= RateLimitPerMinute)
                    return "Vui lòng đợi 1 phút trước khi yêu cầu mã mới";
                if (_store.CountRecentByEmail(email, DateTime.UtcNow.AddHours(-1)) >= RateLimitPerHour)
                    return "Đã yêu cầu quá nhiều mã trong 1 giờ — thử lại sau";
            }
            catch (Exception ex)
            {
                // DB hiccup shouldn't block legitimate sends; log and continue.
                Console.WriteLine($"[OtpService] rate-limit query failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Forgot-password OTP: caller supplies only the email. We look the user up by
        /// email, generate a fresh code bound to that account, persist it in the same
        /// OTP table, and email the code. The follow-up <c>AUTH_RESET_PASSWORD</c>
        /// reuses <see cref="Verify"/> with the canonical username we just looked up.
        /// </summary>
        public Message SendForgotPasswordOtp(ForgotPasswordSendOtpRequest req, UserStore userStore)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Email))
                return new Message(MessageType.AUTH_FORGOT_SEND_OTP_RESULT,
                    new ForgotPasswordSendOtpResult { Success = false, Message = "Thiếu email" });

            var email = req.Email.Trim();
            if (!email.Contains("@") || !email.Contains("."))
                return new Message(MessageType.AUTH_FORGOT_SEND_OTP_RESULT,
                    new ForgotPasswordSendOtpResult { Success = false, Message = "Email không hợp lệ" });

            var user = userStore.FindByEmail(email);
            if (user == null)
                return new Message(MessageType.AUTH_FORGOT_SEND_OTP_RESULT,
                    new ForgotPasswordSendOtpResult { Success = false, Message = "Không tìm thấy tài khoản với email này" });

            var rateError = CheckRateLimit(email);
            if (rateError != null)
                return new Message(MessageType.AUTH_FORGOT_SEND_OTP_RESULT,
                    new ForgotPasswordSendOtpResult { Success = false, Message = rateError });

            var code = GenerateSixDigitCode();
            var token = Guid.NewGuid().ToString();
            var expires = DateTime.UtcNow.AddMinutes(_expirationMinutes);
            var codeHash = BCrypt.Net.BCrypt.HashPassword(code, 10);

            try
            {
                _store.Save(token, user.Username, email, codeHash, expires);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OtpService] Save error (forgot): {ex.Message}");
                return new Message(MessageType.AUTH_FORGOT_SEND_OTP_RESULT,
                    new ForgotPasswordSendOtpResult { Success = false, Message = "Lỗi server khi lưu mã" });
            }

            Console.WriteLine($"  [OtpService] FORGOT-OTP for {email} (user='{user.Username}') = {code} (token={token}, exp={_expirationMinutes}m)");

            if (!_mailer.IsConfigured)
            {
                return new Message(MessageType.AUTH_FORGOT_SEND_OTP_RESULT, new ForgotPasswordSendOtpResult
                {
                    Success = true,
                    OtpToken = token,
                    ExpiresInSeconds = _expirationMinutes * 60,
                    Message = "Mã đã sinh (SMTP chưa cấu hình — xem console server để lấy mã)"
                });
            }

            try
            {
                _mailer.Send(email, "Đặt lại mật khẩu CanvasApp", BuildResetHtmlBody(user.Username, code));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OtpService] SMTP send error (forgot): {ex.Message}");
                return new Message(MessageType.AUTH_FORGOT_SEND_OTP_RESULT,
                    new ForgotPasswordSendOtpResult { Success = false, Message = "Không gửi được email: " + ex.Message });
            }

            return new Message(MessageType.AUTH_FORGOT_SEND_OTP_RESULT, new ForgotPasswordSendOtpResult
            {
                Success = true,
                OtpToken = token,
                ExpiresInSeconds = _expirationMinutes * 60,
                Message = "Đã gửi mã đặt lại mật khẩu đến email"
            });
        }

        public Message SendOtp(SendOtpRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Email))
                return new Message(MessageType.AUTH_SEND_OTP_RESULT,
                    new SendOtpResult { Success = false, Message = "Thiếu username hoặc email" });

            var username = req.Username.Trim();
            var email = req.Email.Trim();

            if (username.Length < 3)
                return new Message(MessageType.AUTH_SEND_OTP_RESULT,
                    new SendOtpResult { Success = false, Message = "Username phải có ít nhất 3 ký tự" });

            if (!email.Contains("@") || !email.Contains("."))
                return new Message(MessageType.AUTH_SEND_OTP_RESULT,
                    new SendOtpResult { Success = false, Message = "Email không hợp lệ" });

            // Pre-flight: don't send a code if username is already taken — the user
            // would just hit a duplicate-key error after typing the OTP.
            if (_store.UsernameExists(username))
                return new Message(MessageType.AUTH_SEND_OTP_RESULT,
                    new SendOtpResult { Success = false, Message = "Username đã tồn tại" });

            var rateError = CheckRateLimit(email);
            if (rateError != null)
                return new Message(MessageType.AUTH_SEND_OTP_RESULT,
                    new SendOtpResult { Success = false, Message = rateError });

            var code = GenerateSixDigitCode();
            var token = Guid.NewGuid().ToString();
            var expires = DateTime.UtcNow.AddMinutes(_expirationMinutes);
            var codeHash = BCrypt.Net.BCrypt.HashPassword(code, 10);

            try
            {
                _store.Save(token, username, email, codeHash, expires);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OtpService] Save error: {ex.Message}");
                return new Message(MessageType.AUTH_SEND_OTP_RESULT,
                    new SendOtpResult { Success = false, Message = "Lỗi server khi lưu mã" });
            }

            // Always log to console — handy in dev, and if SMTP is misconfigured the
            // user can still grab the code from the AuthServer terminal.
            Console.WriteLine($"  [OtpService] OTP for {email} (user='{username}') = {code} (token={token}, exp={_expirationMinutes}m)");

            if (!_mailer.IsConfigured)
            {
                return new Message(MessageType.AUTH_SEND_OTP_RESULT, new SendOtpResult
                {
                    Success = true,
                    OtpToken = token,
                    ExpiresInSeconds = _expirationMinutes * 60,
                    Message = "Mã đã sinh (SMTP chưa cấu hình — xem console server để lấy mã)"
                });
            }

            try
            {
                _mailer.Send(email, "Mã xác thực CanvasApp", BuildHtmlBody(username, code));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OtpService] SMTP send error: {ex.Message}");
                return new Message(MessageType.AUTH_SEND_OTP_RESULT,
                    new SendOtpResult { Success = false, Message = "Không gửi được email: " + ex.Message });
            }

            return new Message(MessageType.AUTH_SEND_OTP_RESULT, new SendOtpResult
            {
                Success = true,
                OtpToken = token,
                ExpiresInSeconds = _expirationMinutes * 60,
                Message = "Đã gửi mã xác thực đến email"
            });
        }

        /// <summary>
        /// Single-shot verify: matches code, locks the row on success, increments attempts on failure.
        /// Returns (true, email) only when the token+code pair is valid and unused. The username/email
        /// validated here MUST match what the caller supplies in the follow-up <see cref="RegisterRequest"/>.
        /// </summary>
        public (bool ok, string message, OtpStore.OtpRecord record) Verify(string token, string code, string expectedUsername, string expectedEmail)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(code))
                return (false, "Thiếu mã xác thực", null);

            var rec = _store.Find(token);
            if (rec == null)
                return (false, "Mã xác thực không tồn tại", null);

            if (rec.Used)
                return (false, "Mã xác thực đã được sử dụng", null);

            if (DateTime.UtcNow > rec.ExpiresUtc)
                return (false, "Mã xác thực đã hết hạn", null);

            if (rec.Attempts >= MaxAttempts)
                return (false, "Mã xác thực đã bị khoá do nhập sai nhiều lần", null);

            // Bind the OTP to the original (username, email) pair so the caller can't swap them.
            if (!string.Equals(rec.Username, expectedUsername, StringComparison.Ordinal) ||
                !string.Equals(rec.Email, expectedEmail, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  [OtpService] VERIFY mismatch: stored=('{rec.Username}','{rec.Email}') vs sent=('{expectedUsername}','{expectedEmail}')");
                return (false, "Mã xác thực không khớp tài khoản/email", null);
            }

            if (!BCrypt.Net.BCrypt.Verify(code, rec.CodeHash))
            {
                try { _store.IncrementAttempts(token); } catch { }
                return (false, "Mã xác thực không đúng", null);
            }

            _store.MarkUsed(token);
            return (true, "OK", rec);
        }

        private static string GenerateSixDigitCode()
        {
            // RNGCryptoServiceProvider → uniform 000000–999999.
            var bytes = new byte[4];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(bytes);
            uint v = BitConverter.ToUInt32(bytes, 0) % 1000000u;
            return v.ToString("D6");
        }

        private static string BuildHtmlBody(string username, string code)
        {
            return
                "<div style='font-family:Segoe UI,Arial,sans-serif;background:#f5f3ff;padding:32px;color:#1f2937'>" +
                "  <div style='max-width:480px;margin:0 auto;background:#fff;border-radius:12px;padding:32px;box-shadow:0 4px 16px rgba(120,86,207,0.15)'>" +
                "    <h2 style='color:#7856CF;margin:0 0 16px'>CanvasApp · Xác thực email</h2>" +
                "    <p>Xin chào <b>" + System.Net.WebUtility.HtmlEncode(username) + "</b>,</p>" +
                "    <p>Mã xác thực cho lần đăng ký của bạn là:</p>" +
                "    <div style='font-size:36px;font-weight:700;letter-spacing:8px;text-align:center;color:#7856CF;padding:16px;background:#f5f3ff;border-radius:8px;margin:16px 0'>" +
                code +
                "    </div>" +
                "    <p style='color:#6b7280;font-size:13px'>Mã có hiệu lực trong 5 phút. Nếu bạn không yêu cầu, hãy bỏ qua email này.</p>" +
                "  </div>" +
                "</div>";
        }

        private static string BuildResetHtmlBody(string username, string code)
        {
            return
                "<div style='font-family:Segoe UI,Arial,sans-serif;background:#f5f3ff;padding:32px;color:#1f2937'>" +
                "  <div style='max-width:480px;margin:0 auto;background:#fff;border-radius:12px;padding:32px;box-shadow:0 4px 16px rgba(120,86,207,0.15)'>" +
                "    <h2 style='color:#7856CF;margin:0 0 16px'>CanvasApp · Đặt lại mật khẩu</h2>" +
                "    <p>Xin chào <b>" + System.Net.WebUtility.HtmlEncode(username) + "</b>,</p>" +
                "    <p>Mã xác thực để đặt lại mật khẩu của bạn là:</p>" +
                "    <div style='font-size:36px;font-weight:700;letter-spacing:8px;text-align:center;color:#7856CF;padding:16px;background:#f5f3ff;border-radius:8px;margin:16px 0'>" +
                code +
                "    </div>" +
                "    <p style='color:#6b7280;font-size:13px'>Mã có hiệu lực trong 5 phút. Nếu bạn không yêu cầu đổi mật khẩu, hãy bỏ qua email này.</p>" +
                "  </div>" +
                "</div>";
        }
    }
}
