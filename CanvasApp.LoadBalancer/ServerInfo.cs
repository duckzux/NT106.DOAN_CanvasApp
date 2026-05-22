using System;
using System.Threading;

namespace CanvasApp.LoadBalancer
{
    public enum ServerType
    {
        Auth,
        Canvas
    }

    /// <summary>
    /// Một backend (Auth hoặc Canvas) được LoadBalancer điều phối.
    /// Trạng thái Up/Down dựa trên FailCount: server bị mark Down sau 3 lần probe fail liên tiếp.
    /// </summary>
    public class ServerInfo
    {
        public ServerType Type { get; }
        public string Host { get; }
        public int Port { get; }
        public int MaxConnections { get; }

        public const int FailThreshold = 3;

        private int _failCount;
        public int FailCount => Volatile.Read(ref _failCount);
        public bool IsHealthy => FailCount < FailThreshold;

        private int _active;
        public int ActiveConnections => Volatile.Read(ref _active);

        private int _roomCount;
        public int RoomCount => Volatile.Read(ref _roomCount);

        public DateTime LastHealthCheckAt { get; set; } = DateTime.MinValue;
        public string LastHealthError { get; set; }

        public string Endpoint => $"{Host}:{Port}";

        public ServerInfo(ServerType type, string host, int port, int maxConnections = 0)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("host required", nameof(host));
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            Type = type;
            Host = host;
            Port = port;
            MaxConnections = maxConnections;
        }

        public bool TryAcquireConnection()
        {
            if (!IsHealthy) return false;
            int next = Interlocked.Increment(ref _active);
            if (MaxConnections > 0 && next > MaxConnections)
            {
                Interlocked.Decrement(ref _active);
                return false;
            }
            return true;
        }

        public void ReleaseConnection()
        {
            int v = Interlocked.Decrement(ref _active);
            if (v < 0) Interlocked.Exchange(ref _active, 0);
        }

        public void IncrementRoomCount() => Interlocked.Increment(ref _roomCount);

        public void DecrementRoomCount()
        {
            int v = Interlocked.Decrement(ref _roomCount);
            if (v < 0) Interlocked.Exchange(ref _roomCount, 0);
        }

        /// <summary>Returns the previous FailCount.</summary>
        public int ResetFailCount() => Interlocked.Exchange(ref _failCount, 0);

        /// <summary>Returns the new FailCount.</summary>
        public int IncrementFailCount() => Interlocked.Increment(ref _failCount);

        public override string ToString() =>
            $"{Type} {Endpoint} [{(IsHealthy ? "UP" : "DOWN")}] " +
            $"conns={ActiveConnections}" + (MaxConnections > 0 ? $"/{MaxConnections}" : "") +
            $" rooms={RoomCount} fails={FailCount}";
    }
}
