using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CanvasApp.LoadBalancer
{
    /// <summary>
    /// Định kỳ TCP-connect tới từng backend.
    /// Server bị mark DOWN sau <see cref="ServerInfo.FailThreshold"/> lần fail liên tiếp.
    /// Một lần probe thành công → reset FailCount về 0 và đưa trở lại UP.
    /// </summary>
    public class HealthChecker
    {
        private readonly IReadOnlyList<ServerInfo> _servers;
        private readonly TimeSpan _interval;
        private readonly int _timeoutMs;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public event Action<ServerInfo, bool> OnHealthChanged;

        public HealthChecker(IReadOnlyList<ServerInfo> servers, TimeSpan interval, int timeoutMs = 2000)
        {
            _servers = servers ?? throw new ArgumentNullException(nameof(servers));
            _interval = interval;
            _timeoutMs = timeoutMs;
        }

        public Task StartAsync() => Task.Run(LoopAsync);

        public void Stop() => _cts.Cancel();

        private async Task LoopAsync()
        {
            await ProbeAllAsync();

            while (!_cts.IsCancellationRequested)
            {
                try { await Task.Delay(_interval, _cts.Token); }
                catch (TaskCanceledException) { return; }

                await ProbeAllAsync();
            }
        }

        private async Task ProbeAllAsync()
        {
            var tasks = new List<Task>(_servers.Count);
            foreach (var s in _servers) tasks.Add(ProbeAsync(s));
            await Task.WhenAll(tasks);
        }

        private async Task ProbeAsync(ServerInfo s)
        {
            bool wasUp = s.IsHealthy;
            bool connected = false;
            string err = null;

            using (var tcp = new TcpClient())
            {
                try
                {
                    var connect = tcp.ConnectAsync(s.Host, s.Port);
                    var done = await Task.WhenAny(connect, Task.Delay(_timeoutMs, _cts.Token));
                    if (done == connect && !connect.IsFaulted) connected = true;
                    else if (connect.IsFaulted) err = connect.Exception?.GetBaseException().Message;
                    else err = $"timeout >{_timeoutMs}ms";
                }
                catch (Exception ex) { err = ex.Message; }
            }

            s.LastHealthCheckAt = DateTime.UtcNow;

            if (connected)
            {
                s.LastHealthError = null;
                int prev = s.ResetFailCount();
                if (!wasUp)
                {
                    Console.WriteLine($"[HEALTH] {s.Type} {s.Endpoint} -> UP (recovered)");
                    OnHealthChanged?.Invoke(s, true);
                }
            }
            else
            {
                s.LastHealthError = err;
                int newFails = s.IncrementFailCount();
                if (wasUp && newFails >= ServerInfo.FailThreshold)
                {
                    Console.WriteLine($"[HEALTH] {s.Type} {s.Endpoint} -> DOWN " +
                                      $"({newFails} fails: {err})");
                    OnHealthChanged?.Invoke(s, false);
                }
                else if (wasUp)
                {
                    Console.WriteLine($"[HEALTH] {s.Type} {s.Endpoint} probe {newFails}/{ServerInfo.FailThreshold} failed ({err})");
                }
            }
        }
    }
}
