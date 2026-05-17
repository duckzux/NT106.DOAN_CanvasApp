using System;
using System.Configuration;
using System.Threading.Tasks;

namespace CanvasApp.AuthServer
{
    class Program
    {
        private const int PORT = 9001;

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

            UserStore store;
            try
            {
                store = new UserStore(connectionString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Cannot connect to database: {ex.Message}");
                Console.WriteLine("  Make sure MySQL is running and the connection string in App.config is correct.");
                Console.ReadKey();
                return;
            }

            var server = new AuthServer(PORT, store);
            await server.StartAsync();
        }
    }
}
