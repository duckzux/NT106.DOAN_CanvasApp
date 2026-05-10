using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CanvasApp.Common;

namespace CanvasApp.AuthServer
{
    class Program
    {
        private const int PORT = 9001;
        private static UserStore _store;

        static async Task Main(string[] args)
        {
            _store = new UserStore("users.json");

            var listener = new TcpListener(IPAddress.Any, PORT);
            listener.Start();
            Console.WriteLine($"╔══════════════════════════════════════╗");
            Console.WriteLine($"║   AUTH SERVER listening on :{PORT}     ║");
            Console.WriteLine($"╚══════════════════════════════════════╝");

            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClient(client));
            }
        }

        private static async Task HandleClient(TcpClient client)
        {
            var endpoint = client.Client.RemoteEndPoint.ToString();
            Console.WriteLine($"[+] Connect from {endpoint}");
            try
            {
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                {
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        Message response = ProcessMessage(line);
                        if (response != null)
                            await writer.WriteLineAsync(response.ToJson());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] {endpoint} error: {ex.Message}");
            }
            finally
            {
                client.Close();
                Console.WriteLine($"[-] Disconnect {endpoint}");
            }
        }

        private static Message ProcessMessage(string raw)
        {
            try
            {
                var msg = Message.FromJson(raw);
                switch (msg.Type)
                {
                    case MessageType.AUTH_LOGIN:
                        var loginReq = msg.GetData<LoginRequest>();
                        var loginRes = _store.Login(loginReq);
                        Console.WriteLine($"  LOGIN '{loginReq.Username}' → {(loginRes.Success ? "OK" : "FAIL")}");
                        return new Message(MessageType.AUTH_LOGIN_RESULT, loginRes);

                    case MessageType.AUTH_REGISTER:
                        var regReq = msg.GetData<RegisterRequest>();
                        var regRes = _store.Register(regReq);
                        Console.WriteLine($"  REGISTER '{regReq.Username}' → {(regRes.Success ? "OK" : "FAIL")}");
                        return new Message(MessageType.AUTH_REGISTER_RESULT, regRes);

                    case MessageType.PING:
                        return new Message(MessageType.PONG);

                    default:
                        return new Message(MessageType.ERROR, new { message = "Unknown message type" });
                }
            }
            catch (Exception ex)
            {
                return new Message(MessageType.ERROR, new { message = ex.Message });
            }
        }
    }
}
