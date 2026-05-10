using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CanvasApp.Common;

namespace CanvasApp.Client
{
    /// <summary>
    /// Client kết nối ngắn hạn đến AuthServer: gửi login/register, đóng kết nối.
    /// </summary>
    public static class AuthClient
    {
        public static async Task<LoginResult> LoginAsync(string username, string password)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    var task = tcp.ConnectAsync(Session.AUTH_HOST, Session.AUTH_PORT);
                    if (await Task.WhenAny(task, Task.Delay(3000)) != task)
                        return new LoginResult { Success = false, Message = "Không kết nối được AuthServer (timeout)" };

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

        public static async Task<RegisterResult> RegisterAsync(string username, string password, string email)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    var task = tcp.ConnectAsync(Session.AUTH_HOST, Session.AUTH_PORT);
                    if (await Task.WhenAny(task, Task.Delay(3000)) != task)
                        return new RegisterResult { Success = false, Message = "Không kết nối được AuthServer" };

                    using (var stream = tcp.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    {
                        var req = new Message(MessageType.AUTH_REGISTER,
                            new RegisterRequest { Username = username, Password = password, Email = email });
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
