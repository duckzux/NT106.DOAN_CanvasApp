using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CanvasApp.Common;

namespace CanvasApp.Client
{
    /// <summary>
    /// Short-lived TCP requests to the LoadBalancer. Every request opens a fresh socket so
    /// the LB peeks the request type (e.g. ROOM_JOIN) on the FIRST message and routes by
    /// room affinity — sharing one socket across multiple messages (ROOM_LIST then ROOM_JOIN)
    /// would pin every later message to whichever server the LB picked for the first one,
    /// breaking same-room stickiness.
    ///
    /// Used by LobbyForm for ROOM_LIST / ROOM_CREATE / ROOM_JOIN / ROOM_JOIN_BY_CODE /
    /// RESOLVE_INVITE_CODE. The persistent <see cref="CanvasClient"/> connection is opened
    /// only AFTER a successful join, directly to the Canvas Server returned by the LB.
    /// </summary>
    public static class LobbyClient
    {
        private const int ConnectTimeoutMs = 5_000;
        private const int ReadTimeoutMs    = 10_000;

        public static Task<RoomListResult> GetRoomListAsync() =>
            QueryAsync<RoomListResult>(
                new Message(MessageType.ROOM_LIST),
                MessageType.ROOM_LIST_RESULT);

        public static Task<Room> CreateRoomAsync(CreateRoomRequest req) =>
            QueryAsync<Room>(
                new Message(MessageType.ROOM_CREATE, req),
                MessageType.ROOM_CREATE_RESULT);

        public static Task<JoinRoomResult> JoinRoomAsync(string roomId, string password) =>
            QueryAsync<JoinRoomResult>(
                new Message(MessageType.ROOM_JOIN,
                    new JoinRoomRequest { RoomId = roomId, Password = password }),
                MessageType.ROOM_JOIN_RESULT);

        public static Task<ResolveInviteCodeResult> ResolveInviteCodeAsync(string code) =>
            QueryAsync<ResolveInviteCodeResult>(
                new Message(MessageType.RESOLVE_INVITE_CODE,
                    new ResolveInviteCodeRequest { InviteCode = code }),
                MessageType.RESOLVE_INVITE_CODE_RESULT);

        public static Task<JoinRoomResult> JoinRoomByCodeAsync(string inviteCode, string password, string roomId) =>
            QueryAsync<JoinRoomResult>(
                new Message(MessageType.ROOM_JOIN_BY_CODE,
                    new InviteCodeRequest { InviteCode = inviteCode, Password = password, RoomId = roomId }),
                MessageType.ROOM_JOIN_RESULT);

        /// <summary>
        /// Open TCP to LB → send one request → read lines until a message of
        /// <paramref name="terminalType"/> arrives (or stream ends) → close.
        /// Any non-terminal messages (e.g. lobby pushes) on the same socket are discarded —
        /// the lobby refreshes manually instead of relying on push.
        /// </summary>
        private static async Task<TResult> QueryAsync<TResult>(Message request, string terminalType)
            where TResult : class
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    var connect = tcp.ConnectAsync(Session.LB_HOST, Session.LB_PORT);
                    if (await Task.WhenAny(connect, Task.Delay(ConnectTimeoutMs)) != connect || connect.IsFaulted)
                        return null;

                    using (var stream = tcp.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    {
                        request.Token = Session.Token;
                        await writer.WriteLineAsync(request.ToJson());

                        while (true)
                        {
                            var readTask = reader.ReadLineAsync();
                            var done = await Task.WhenAny(readTask, Task.Delay(ReadTimeoutMs));
                            if (done != readTask) return null;

                            var line = await readTask;
                            if (line == null) return null;

                            Message msg;
                            try { msg = Message.FromJson(line); }
                            catch { continue; }

                            if (msg.Type == terminalType)
                                return msg.GetData<TResult>();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LobbyClient] Query {request?.Type} failed: {ex.Message}");
                return null;
            }
        }
    }
}
