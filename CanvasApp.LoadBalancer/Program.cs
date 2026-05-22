using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CanvasApp.LoadBalancer
{
    internal class Program
    {
        private class BackendConfig
        {
            public string Host { get; set; }
            public int Port { get; set; }
            public int MaxConnections { get; set; }
        }

        private class LbConfig
        {
            public int ListenPort { get; set; } = 9000;
            public int HealthCheckIntervalSeconds { get; set; } = 5;
            public int HealthCheckTimeoutMs { get; set; } = 2000;
            public int BufferSize { get; set; } = 8192;
            public int PeekTimeoutMs { get; set; } = 5000;
            public int PeekMaxBytes { get; set; } = 65536;
            public List<BackendConfig> AuthServers { get; set; } = new List<BackendConfig>();
            public List<BackendConfig> CanvasServers { get; set; } = new List<BackendConfig>();
        }

        static async Task Main(string[] args)
        {
            Console.Title = "CanvasApp Load Balancer";

            LbConfig cfg;
            try { cfg = LoadConfig("appsettings.json"); }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Cannot read appsettings.json: {ex.Message}");
                Console.ReadKey();
                return;
            }

            if (cfg.CanvasServers == null || cfg.CanvasServers.Count == 0)
            {
                Console.WriteLine("[ERROR] appsettings.json has no CanvasServers configured");
                Console.ReadKey();
                return;
            }

            var servers = new List<ServerInfo>();
            foreach (var b in cfg.AuthServers ?? new List<BackendConfig>())
                servers.Add(new ServerInfo(ServerType.Auth, b.Host, b.Port, b.MaxConnections));
            foreach (var b in cfg.CanvasServers)
                servers.Add(new ServerInfo(ServerType.Canvas, b.Host, b.Port, b.MaxConnections));

            var health = new HealthChecker(
                servers,
                TimeSpan.FromSeconds(cfg.HealthCheckIntervalSeconds),
                cfg.HealthCheckTimeoutMs);
            _ = health.StartAsync();

            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(15_000);
                    Console.WriteLine("[POOL]");
                    foreach (var s in servers) Console.WriteLine($"   {s}");
                }
            });

            var lb = new LoadBalancer(
                servers,
                cfg.ListenPort,
                cfg.BufferSize,
                cfg.PeekTimeoutMs,
                cfg.PeekMaxBytes);

            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine("\n[LB] Shutdown requested");
                health.Stop();
                lb.Stop();
                e.Cancel = true;
            };

            await lb.StartAsync();
        }

        private static LbConfig LoadConfig(string fileName)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (!File.Exists(path)) path = fileName;
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<LbConfig>(json) ?? new LbConfig();
        }
    }
}
