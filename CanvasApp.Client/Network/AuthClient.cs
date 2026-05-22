using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CanvasApp.Common;

namespace CanvasApp.Client
{
    /// <summary>
    /// Client kết nối ngắn hạn đến LoadBalancer: gửi login/register, đóng kết nối.
    /// LB peek message đầu tiên (AUTH_LOGIN/AUTH_REGISTER) và round-robin sang Auth pool.
    /// </summary>
    public static class AuthClient
    {
        public static async Task<LoginResult> LoginAsync(string username, string password)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    var task = tcp.ConnectAsync(Session.LB_HOST, Session.LB_PORT);
                    if (await Task.WhenAny(task, Task.Delay(3000)) != task)
                        return new LoginResult { Success = false, Message = "Không kết nối được Load Balancer (timeout)" };

                    using (var stream = tcp.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    {
                        var req = new Message(MessageType.AUTH_LOGIN,
                            new LoginRequest { Username = username, Password = password });
                        await writer.WriteLineAsync(req.ToJson());

                        var line = await reader.ReadLineAsync();
                        if (string.IsNullOrEmpty(line))
                            return new LoginResult { Success = false, Message = "Server không trả lời" };

                        var resMsg = Message.FromJson(line);
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

                    using (var stream = tcp.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    {
                        var req = new Message(MessageType.AUTH_SEND_OTP,
                            new SendOtpRequest { Username = username, Email = email });
                        await writer.WriteLineAsync(req.ToJson());

                        // SMTP send can take a few seconds — give the server room before giving up.
                        var readTask = reader.ReadLineAsync();
                        if (await Task.WhenAny(readTask, Task.Delay(20000)) != readTask)
                            return new SendOtpResult { Success = false, Message = "Server không trả lời (timeout 20s)" };

                        var line = await readTask;
                        if (string.IsNullOrEmpty(line))
                            return new SendOtpResult { Success = false, Message = "Server không trả lời" };

                        var resMsg = Message.FromJson(line);
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

                    using (var stream = tcp.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    {
                        var req = new Message(MessageType.AUTH_FORGOT_SEND_OTP,
                            new ForgotPasswordSendOtpRequest { Email = email });
                        await writer.WriteLineAsync(req.ToJson());

                        // Same SMTP-slowness allowance as the registration OTP flow.
                        var readTask = reader.ReadLineAsync();
                        if (await Task.WhenAny(readTask, Task.Delay(20000)) != readTask)
                            return new ForgotPasswordSendOtpResult { Success = false, Message = "Server không trả lời (timeout 20s)" };

                        var line = await readTask;
                        if (string.IsNullOrEmpty(line))
                            return new ForgotPasswordSendOtpResult { Success = false, Message = "Server không trả lời" };

                        var resMsg = Message.FromJson(line);
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
                        await writer.WriteLineAsync(req.ToJson());

                        var line = await reader.ReadLineAsync();
                        if (string.IsNullOrEmpty(line))
                            return new ResetPasswordResult { Success = false, Message = "Server không trả lời" };

                        var resMsg = Message.FromJson(line);
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
                        await writer.WriteLineAsync(req.ToJson());

                        var line = await reader.ReadLineAsync();
                        if (string.IsNullOrEmpty(line))
                            return new RegisterResult { Success = false, Message = "Server không trả lời" };

                        var resMsg = Message.FromJson(line);
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
