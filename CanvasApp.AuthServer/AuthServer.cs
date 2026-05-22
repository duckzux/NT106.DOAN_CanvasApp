using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace CanvasApp.AuthServer
{
    /// <summary>
    /// TcpListener on port 9001. Accepts clients and spawns an AuthHandler per connection.
    /// </summary>
    public class AuthServer
    {
        private readonly TcpListener _listener;
        private readonly AuthHandler _handler;

        public AuthServer(int port, UserStore store, OtpStore otpStore)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _handler = new AuthHandler(store, otpStore);
        }

        public async Task StartAsync()
        {
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Console.WriteLine($"╔══════════════════════════════════════╗");
            Console.WriteLine($"║   AUTH SERVER listening on :{port}     ║");
            Console.WriteLine($"╚══════════════════════════════════════╝");

            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(() => _handler.HandleClientAsync(client));
            }
        }
    }
}
