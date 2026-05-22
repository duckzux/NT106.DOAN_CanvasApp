using System;
using System.Configuration;
using System.Threading.Tasks;

namespace CanvasApp.AuthServer
{
    class Program
    {
        private const int DefaultPort = 9001;

        static async Task Main(string[] args)
        {
            var connectionString = ConfigurationManager.ConnectionStrings["CanvasDb"]?.ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.WriteLine("[ERROR] Connection string 'CanvasDb' not found in App.config");
                Console.WriteLine("  Example: Server=localhost;Port=3306;Database=canvasapp;Uid=root;Pwd=;CharSet=utf8mb4;SslMode=None;");
                Console.ReadKey();
                return;
            }

            // Resolution order: args[0] (e.g. `CanvasApp.AuthServer.exe 9011`) → App.config AuthPort → default.
            // CLI arg ưu tiên hơn để dễ chạy nhiều instance từ Visual Studio (Project → Debug → Application arguments).
            var port = DefaultPort;
            if (args != null && args.Length > 0 && int.TryParse(args[0], out var argPort))
            {
                port = argPort;
            }
            else
            {
                var portSetting = ConfigurationManager.AppSettings["AuthPort"];
                if (!string.IsNullOrWhiteSpace(portSetting) && int.TryParse(portSetting, out var parsedPort))
                {
                    port = parsedPort;
                }
            }

            try { Console.Title = $"AuthServer :{port}"; } catch { }

            UserStore store;
            OtpStore otpStore;
            try
            {
                store = new UserStore(connectionString);
                otpStore = new OtpStore(connectionString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Cannot connect to database: {ex.Message}");
                Console.WriteLine("  Make sure MySQL is running and the connection string in App.config is correct.");
                Console.ReadKey();
                return;
            }

            // Background OTP cleanup: drop rows whose grace window has passed so the
            // table stays bounded. Runs every 30 minutes — first sweep happens right away.
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        int n = otpStore.DeleteExpired(graceHours: 24);
                        if (n > 0) Console.WriteLine($"[OtpStore] cleaned {n} expired OTP row(s)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[OtpStore] cleanup error: {ex.Message}");
                    }
                    await Task.Delay(TimeSpan.FromMinutes(30));
                }
            });

            var server = new AuthServer(port, store, otpStore);
            await server.StartAsync();
        }
    }
}
