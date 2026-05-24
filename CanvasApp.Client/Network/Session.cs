using System;
using System.IO;
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

        // Server IP được đọc từ file server.txt cạnh exe (1 dòng duy nhất chứa IP).
        // Đổi WiFi → mở Notepad sửa server.txt → chạy lại Client, KHÔNG cần rebuild.
        public static readonly string LB_HOST   = LoadServerIp();
        public const          int    LB_PORT   = 9000;

        public static readonly string AUTH_HOST = LB_HOST;
        public const          int    AUTH_PORT = 9001;

        // Canvas server runtime address (set sau khi LB redirect qua ROOM_RESOLVE_RESULT)
        public static string CanvasHost { get; set; } = LB_HOST;
        public static int    CanvasPort { get; set; } = 9002;

        public static void Clear()
        {
            CurrentUser = null;
            Token = null;
            CanvasHost = null;
            CanvasPort = 0;
        }

        private static string LoadServerIp()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server.txt");
                if (File.Exists(path))
                {
                    var ip = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(ip)) return ip;
                }
            }
            catch { /* fallback dưới */ }
            return "127.0.0.1";
        }
    }
}
