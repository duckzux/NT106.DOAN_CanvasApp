using MySql.Data.MySqlClient;
using System.Configuration;

namespace CanvasApp.Common.DataAccess
{
    public static class DatabaseManager
    {
        private static string _connStr;

        public static bool IsInitialized => _connStr != null;

        public static void Initialize(string connectionString)
        {
            _connStr = connectionString;
        }

        public static MySqlConnection OpenConnection()
        {
            var conn = new MySqlConnection(_connStr);
            conn.Open();
            return conn;
        }
    }
}
