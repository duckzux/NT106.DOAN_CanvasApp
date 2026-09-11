using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CanvasApp.AuthServer.Services;
using CanvasApp.Common;
using CanvasApp.Common.Utils;

namespace CanvasApp.AuthServer
{
    /// <summary>
    /// Handles a single client connection: reads line-delimited JSON messages,
    /// routes to AuthService / UserService, and writes back the response.
    /// </summary>
    public class AuthHandler
    {
        private readonly AuthService _authService;
        private readonly UserService _userService;
        private readonly OtpService _otpService;

        public AuthHandler(UserStore store, OtpStore otpStore)
        {
            var mailer = new SmtpEmailSender();
            _otpService = new OtpService(otpStore, mailer);
            _authService = new AuthService(store);
            _userService = new UserService(store, _otpService);
        }

        public async Task HandleClientAsync(TcpClient client)
        {
            var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "?";
            // Silent probes (LB health check: connect + close, 0 bytes) stay un-logged.
            bool gotData = false;
            try
            {
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                {
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (!gotData)
                        {
                            gotData = true;
                            Console.WriteLine($"[+] Auth connect {endpoint}");
                        }
                        // ProcessMessage already wraps its body in try/catch and returns an
                        // ERROR message on failure. The extra guard here is for the cases that
                        // would otherwise leave the client hanging: ProcessMessage returning
                        // null (shouldn't happen in current code paths, but cheap to defend
                        // against), or the WriteLineAsync itself throwing on a flaky socket.
                        // Without this, the AuthClient end would burn its 10s read timeout
                        // before learning the request failed.
                        Message response = null;
                        try
                        {
                            response = ProcessMessage(line) ?? new Message(MessageType.ERROR,
                                new { message = "Server không xử lý được yêu cầu" });
                        }
                        catch (Exception ex)
                        {
                            response = new Message(MessageType.ERROR, new { message = ex.Message });
                        }
                        try { await writer.WriteLineAsync(MessageCrypto.Serialize(response)); }
                        catch (Exception ex)
                        {
                            // Write failed — socket likely broken. Don't try again; exit the
                            // loop so the connection is torn down cleanly.
                            Console.WriteLine($"[!] {endpoint} write failed: {ex.Message}");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                if (gotData)
                    Console.WriteLine($"[!] {endpoint}: {ex.Message}");
            }
            finally
            {
                client.Close();
                if (gotData)
                    Console.WriteLine($"[-] Auth disconnect {endpoint}");
            }
        }

        private Message ProcessMessage(string raw)
        {
            try
            {
                var msg = MessageCrypto.Deserialize(raw);
                switch (msg.Type)
                {
                    case MessageType.AUTH_LOGIN:
                        return _authService.Login(msg.GetData<LoginRequest>());

                    case MessageType.AUTH_REGISTER:
                        return _userService.Register(msg.GetData<RegisterRequest>());

                    case MessageType.AUTH_SEND_OTP:
                        return _otpService.SendOtp(msg.GetData<SendOtpRequest>());

                    case MessageType.AUTH_FORGOT_SEND_OTP:
                        return _userService.SendForgotPasswordOtp(msg.GetData<ForgotPasswordSendOtpRequest>());

                    case MessageType.AUTH_RESET_PASSWORD:
                        return _userService.ResetPassword(msg.GetData<ResetPasswordRequest>());

                    case MessageType.PING:
                        return new Message(MessageType.PONG);

                    default:
                        return new Message(MessageType.ERROR, new { message = $"Unknown type: {msg.Type}" });
                }
            }
            catch (Exception ex)
            {
                return new Message(MessageType.ERROR, new { message = ex.Message });
            }
        }
    }
}
