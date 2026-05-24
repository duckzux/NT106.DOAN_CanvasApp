using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CanvasApp.Common;
using CanvasApp.Common.Utils;

namespace CanvasApp.Client
{
    /// <summary>
    /// Client kết nối ngắn hạn đến LoadBalancer: gửi login/register, đóng kết nối.
    /// LB peek message đầu tiên (AUTH_LOGIN/AUTH_REGISTER) và round-robin sang Auth pool.
    /// </summary>
    public static class AuthClient
    {
        // Returns the line read, an empty string on graceful EOF, or null on timeout.
        // StreamReader.ReadLineAsync has no native timeout — without this wrapper a wedged
        // server would leave the calling form hung indefinitely.
        private static async Task<string> ReadLineWithTimeoutAsync(StreamReader reader, int timeoutMs)
        {
            var readTask = reader.ReadLineAsync();
            var done = await Task.WhenAny(readTask, Task.Delay(timeoutMs));
            if (done != readTask) return null; // timeout
            return readTask.Result ?? string.Empty;
        }


        public static async Task<LoginResult> LoginAsync(string username, string password)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    var task = tcp.ConnectAsync(Session.LB_HOST, Session.LB_PORT);
                    if (await Task.WhenAny(task, Task.Delay(3000)) != task)
                        return new LoginResult { Success = false, Message = "Không kết nối được Load Balancer (timeout)" };

                    await task; // Propagate connection refused or socket errors nicely

                    using (var stream = tcp.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    {
                        var req = new Message(MessageType.AUTH_LOGIN,
                            new LoginRequest { Username = username, Password = password });
                        await writer.WriteLineAsync(MessageCrypto.Serialize(req));

                        // Bounded read so a stalled Auth server doesn't hang the UI thread
                        // forever (the underlying socket has no read deadline by default).
                        var line = await ReadLineWithTimeoutAsync(reader, 10_000);
                        if (line == null)
                            return new LoginResult { Success = false, Message = "Server không trả lời (timeout 10s)" };
                        if (line.Length == 0)
                            return new LoginResult { Success = false, Message = "Server không trả lời" };

                        var resMsg = MessageCrypto.Deserialize(line);
                        return resMsg.GetData<LoginResult>();
                    }
                }
            }
            catch (Exception ex)
            {
                return new LoginResult { Success = false, Message = $"Lỗi kết nối: {ex.Message}" };
            }
        }

        public static async Task<SendOtpResult> SendOtpAsync(string username, string email)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    var task = tcp.ConnectAsync(Session.LB_HOST, Session.LB_PORT);
                    if (await Task.WhenAny(task, Task.Delay(3000)) != task)
                        return new SendOtpResult { Success = false, Message = "Không kết nối được Load Balancer" };

                    await task; // Propagate connection refused or socket errors nicely

                    using (var stream = tcp.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    {
                        var req = new Message(MessageType.AUTH_SEND_OTP,
                            new SendOtpRequest { Username = username, Email = email });
                        await writer.WriteLineAsync(MessageCrypto.Serialize(req));

                        // SMTP send can take a few seconds — give the server room before giving up.
                        var readTask = reader.ReadLineAsync();
                        if (await Task.WhenAny(readTask, Task.Delay(20000)) != readTask)
                            return new SendOtpResult { Success = false, Message = "Server không trả lời (timeout 20s)" };

                        var line = await readTask;
                        if (string.IsNullOrEmpty(line))
                            return new SendOtpResult { Success = false, Message = "Server không trả lời" };

                        var resMsg = MessageCrypto.Deserialize(line);
                        return resMsg.GetData<SendOtpResult>();
                    }
                }
            }
            catch (Exception ex)
            {
                return new SendOtpResult { Success = false, Message = $"Lỗi: {ex.Message}" };
            }
        }

        public static async Task<ForgotPasswordSendOtpResult> SendForgotPasswordOtpAsync(string email)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    var task = tcp.ConnectAsync(Session.LB_HOST, Session.LB_PORT);
                    if (await Task.WhenAny(task, Task.Delay(3000)) != task)
                        return new ForgotPasswordSendOtpResult { Success = false, Message = "Không kết nối được Load Balancer" };

                    await task; // Propagate connection refused or socket errors nicely

                    using (var stream = tcp.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    {
                        var req = new Message(MessageType.AUTH_FORGOT_SEND_OTP,
                            new ForgotPasswordSendOtpRequest { Email = email });
                        await writer.WriteLineAsync(MessageCrypto.Serialize(req));

                        // Same SMTP-slowness allowance as the registration OTP flow.
                        var readTask = reader.ReadLineAsync();
                        if (await Task.WhenAny(readTask, Task.Delay(20000)) != readTask)
                            return new ForgotPasswordSendOtpResult { Success = false, Message = "Server không trả lời (timeout 20s)" };

                        var line = await readTask;
                        if (string.IsNullOrEmpty(line))
                            return new ForgotPasswordSendOtpResult { Success = false, Message = "Server không trả lời" };

                        var resMsg = MessageCrypto.Deserialize(line);
                        return resMsg.GetData<ForgotPasswordSendOtpResult>();
                    }
                }
            }
            catch (Exception ex)
            {
                return new ForgotPasswordSendOtpResult { Success = false, Message = $"Lỗi: {ex.Message}" };
            }
        }

        public static async Task<ResetPasswordResult> ResetPasswordAsync(string email, string newPassword, string otpToken, string otpCode)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    var task = tcp.ConnectAsync(Session.LB_HOST, Session.LB_PORT);
                    if (await Task.WhenAny(task, Task.Delay(3000)) != task)
                        return new ResetPasswordResult { Success = false, Message = "Không kết nối được Load Balancer" };

                    await task; // Propagate connection refused or socket errors nicely

                    using (var stream = tcp.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    {
                        var req = new Message(MessageType.AUTH_RESET_PASSWORD,
                            new ResetPasswordRequest
                            {
                                Email = email,
                                NewPassword = newPassword,
                                OtpToken = otpToken,
                                OtpCode = otpCode,
                            });
                        await writer.WriteLineAsync(MessageCrypto.Serialize(req));

                        var line = await ReadLineWithTimeoutAsync(reader, 10_000);
                        if (line == null)
                            return new ResetPasswordResult { Success = false, Message = "Server không trả lời (timeout 10s)" };
                        if (line.Length == 0)
                            return new ResetPasswordResult { Success = false, Message = "Server không trả lời" };

                        var resMsg = MessageCrypto.Deserialize(line);
                        return resMsg.GetData<ResetPasswordResult>();
                    }
                }
            }
            catch (Exception ex)
            {
                return new ResetPasswordResult { Success = false, Message = $"Lỗi: {ex.Message}" };
            }
        }

        public static async Task<RegisterResult> RegisterAsync(string username, string password, string email, string otpToken, string otpCode)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    var task = tcp.ConnectAsync(Session.LB_HOST, Session.LB_PORT);
                    if (await Task.WhenAny(task, Task.Delay(3000)) != task)
                        return new RegisterResult { Success = false, Message = "Không kết nối được Load Balancer" };

                    await task; // Propagate connection refused or socket errors nicely

                    using (var stream = tcp.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    {
                        var req = new Message(MessageType.AUTH_REGISTER,
                            new RegisterRequest
                            {
                                Username = username,
                                Password = password,
                                Email = email,
                                OtpToken = otpToken,
                                OtpCode = otpCode,
                            });
                        await writer.WriteLineAsync(MessageCrypto.Serialize(req));

                        var line = await ReadLineWithTimeoutAsync(reader, 10_000);
                        if (line == null)
                            return new RegisterResult { Success = false, Message = "Server không trả lời (timeout 10s)" };
                        if (line.Length == 0)
                            return new RegisterResult { Success = false, Message = "Server không trả lời" };

                        var resMsg = MessageCrypto.Deserialize(line);
                        return resMsg.GetData<RegisterResult>();
                    }
                }
            }
            catch (Exception ex)
            {
                return new RegisterResult { Success = false, Message = $"Lỗi: {ex.Message}" };
            }
        }
    }
}
