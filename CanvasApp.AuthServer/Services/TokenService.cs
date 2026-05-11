using System;
using System.Text;
using CanvasApp.Common;

namespace CanvasApp.AuthServer.Services
{
    /// <summary>
    /// Creates and validates session tokens.
    /// Format: Base64( userId:username:issuedAtUnix )
    /// Token expiry: 24 hours.
    /// </summary>
    public class TokenService
    {
        private const long TokenTtlSeconds = 24 * 60 * 60;

        public string CreateToken(User user)
        {
            long issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var raw = $"{user.Id}:{user.Username}:{issuedAt}";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        }

        /// <summary>
        /// Returns the userId encoded in the token, or -1 if the token is
        /// missing, malformed, or expired.
        /// </summary>
        public int ValidateToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return -1;
            try
            {
                var raw = Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = raw.Split(':');
                if (parts.Length < 3) return -1;
                if (!int.TryParse(parts[0], out int userId)) return -1;
                if (!long.TryParse(parts[2], out long issuedAt)) return -1;

                long age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - issuedAt;
                if (age < 0 || age > TokenTtlSeconds) return -1;

                return userId;
            }
            catch { return -1; }
        }
    }
}
