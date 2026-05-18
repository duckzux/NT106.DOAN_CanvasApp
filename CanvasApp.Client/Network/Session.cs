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

        public static void Clear()
        {
            CurrentUser = null;
            Token = null;
        }

        // Cấu hình server (đổi IP/port khi deploy)
        public const string AUTH_HOST = "127.0.0.1";
        public const int AUTH_PORT = 9001;
        public const string CANVAS_HOST = "127.0.0.1";
        public const int CANVAS_PORT = 9002;
    }
}
