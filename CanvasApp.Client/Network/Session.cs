using CanvasApp.Common;

namespace CanvasApp.Client
{
    /// <summary>
    /// Singleton chứa thông tin phiên của user hiện tại.
    /// Tất cả Form đều có thể truy cập qua Session.Current. 
    /// </summary>
    public static class Session
    {
        public static User CurrentUser { get; set; }
        public static string Token { get; set; }
        public static bool IsLoggedIn => CurrentUser != null && !string.IsNullOrEmpty(Token);

        // ✅ Load Balancer addresses (client chỉ biết LB, không hard-code Canvas Server)
        public const string LB_HOST = "127.0.0.1";
        public const int LB_PORT = 9000;

        // Auth Server
        public const string AUTH_HOST = "127.0.0.1";
        public const int AUTH_PORT = 9001;

        // ✅ Canvas Server addresses (được set runtime sau khi LB redirect)
        public static string CanvasHost { get; set; } = "127.0.0.1";
        public static int CanvasPort { get; set; } = 9002;

        public static void Clear()
        {
            CurrentUser = null;
            Token = null;
            CanvasHost = null;
            CanvasPort = 0;
        }
    }
}
    