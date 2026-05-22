using System;
using System.Configuration;
using System.Threading;
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

            // Background OTP cleanup: drop rows whose grace window has passed so the table
            // stays bounded. Runs every 30 minutes — first sweep happens right away.
            // Token-aware so a Ctrl+C / process-exit can interrupt the 30-minute Delay and
            // exit cleanly between sweeps. Without this the loop would keep the process
            // alive across shutdown, potentially mid DB-cleanup-query.
            var shutdownCts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                // Don't terminate immediately — let the cancellation propagate so background
                // tasks can drain. The second Ctrl+C still kills the process via the default.
                if (!shutdownCts.IsCancellationRequested)
                {
                    e.Cancel = true;
                    shutdownCts.Cancel();
                    Console.WriteLine("[AuthServer] Shutdown requested — draining background tasks…");
                }
            };

            var cleanupTask = Task.Run(async () =>
            {
                while (!shutdownCts.IsCancellationRequested)
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
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(30), shutdownCts.Token);
                    }
                    catch (OperationCanceledException) { break; }
                }
                Console.WriteLine("[OtpStore] cleanup loop exited");
            }, shutdownCts.Token);

            var server = new AuthServer(port, store, otpStore);
            await server.StartAsync();
        }
    }
}
