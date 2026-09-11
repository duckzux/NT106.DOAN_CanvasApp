using CanvasApp.Common;

namespace CanvasApp.AuthServer.Services
{
    /// <summary>
    /// Thin facade over <see cref="UserStore.IssueToken"/> / <see cref="UserStore.VerifyToken"/>.
    /// Format: "<base64-payload>.<base64-HMAC-SHA256>" — payload is "userId:username:issuedAtUnix".
    /// Token expiry: 24 hours. Signed with the JWT secret from env CANVASAPP_JWT_SECRET
    /// (shared with CanvasServer so it can verify tokens issued here).
    /// </summary>
    public class TokenService
    {
        public string CreateToken(User user) => UserStore.IssueToken(user.Id, user.Username);

        public int ValidateToken(string token) => UserStore.VerifyToken(token);
    }
}
