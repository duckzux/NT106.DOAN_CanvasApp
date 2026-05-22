using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CanvasApp.Common;

namespace CanvasApp.Server
{
    /// <summary>
    /// Accepts inbound TCP connections from other Canvas servers (the "subscribe" side of the
    /// mesh). Each frame is a line-delimited JSON Message — PEER_RELAY, PEER_MEMBER_SYNC, or
    /// PEER_HELLO. Dispatches them to RoomManager for local-only application (no re-publish,
    /// no DB write — the originating server already did both of those things).
    /// </summary>
    public static class PeerHandler
    {
        public static async Task HandleAsync(TcpClient tcp, RoomManager roomManager)
        {
            var remote = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
            string peerId = null;
            try
            {
                var stream = tcp.GetStream();
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true })
                {
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        Message msg;
                        try { msg = Message.FromJson(line); }
                        catch { continue; }

                        switch (msg.Type)
                        {
                            case MessageType.PEER_HELLO:
                            {
                                var hello = msg.GetData<PeerHelloPayload>();
                                if (hello != null)
                                {
                                    // Drop self-connections at the door. If the peer list is
                                    // misconfigured (e.g. two servers share the same appsettings
                                    // and one ends up pointing at its own peer listener), the
                                    // accepted socket would be looping every event back to us —
                                    // doubling chat / join notifications. Bail out before we
                                    // record self as a "peer".
                                    if (!string.IsNullOrEmpty(roomManager.SelfServerId)
                                        && string.Equals(hello.ServerId, roomManager.SelfServerId, StringComparison.Ordinal))
                                    {
                                        Console.WriteLine($"[PEER] inbound <- {remote} dropped (self-loop, id={hello.ServerId})");
                                        return;
                                    }
                                    peerId = hello.ServerId;
                                    Console.WriteLine($"[PEER] inbound <- {remote} hello (id={peerId}, " +
                                                      $"rooms={hello.KnownRooms?.Count ?? 0}, " +
                                                      $"memberSets={hello.Rooms?.Count ?? 0})");
                                    // First: catch up on any rooms we don't have yet (e.g. created
                                    // on the peer while this server was offline).
                                    if (hello.KnownRooms != null)
                                    {
                                        foreach (var room in hello.KnownRooms)
                                            roomManager.RegisterPeerRoom(room);
                                    }
                                    // Then: replay the peer's current local membership per room.
                                    if (hello.Rooms != null)
                                    {
                                        foreach (var kv in hello.Rooms)
                                            roomManager.UpdatePeerMembers(peerId, kv.Key, kv.Value);
                                    }
                                    // Push the merged view to local lobby clients so their room
                                    // counts reflect the peer state we just learned about.
                                    await roomManager.BroadcastLobbyRoomListAsync();
                                }
                                break;
                            }

                            case MessageType.PEER_ROOM_CREATE:
                            {
                                var room = msg.GetData<Room>();
                                if (room != null && roomManager.RegisterPeerRoom(room))
                                    await roomManager.BroadcastLobbyRoomListAsync();
                                break;
                            }

                            case MessageType.PEER_ROOM_DELETE:
                            {
                                // Payload is just the roomId string. Drop the room from this
                                // server's in-memory state and push a refreshed lobby list so
                                // local lobby clients see the card disappear immediately.
                                var roomId = msg.GetData<string>();
                                if (!string.IsNullOrEmpty(roomId) && roomManager.DeleteRoom(roomId))
                                    await roomManager.BroadcastLobbyRoomListAsync();
                                break;
                            }

                            case MessageType.PEER_ROOM_PASSWORD_UPDATED:
                            {
                                var payload = msg.GetData<PeerRoomPasswordPayload>();
                                if (payload != null && roomManager.ApplyPeerPasswordHash(payload.RoomId, payload.PasswordHash))
                                    await roomManager.BroadcastLobbyRoomListAsync();
                                break;
                            }

                            case MessageType.PEER_RELAY:
                            {
                                var payload = msg.GetData<PeerRelayPayload>();
                                if (payload?.Inner != null && !string.IsNullOrEmpty(payload.RoomId))
                                    await roomManager.ApplyFromPeerAsync(payload.RoomId, payload.OriginServerId, payload.Inner);
                                break;
                            }

                            case MessageType.PEER_PING:
                            {
                                // Respond to heartbeat
                                await writer.WriteLineAsync(new Message(MessageType.PEER_PONG, null).ToJson());
                                break;
                            }

                            case MessageType.PEER_CANVAS_SYNC:
                            {
                                var payload = msg.GetData<PeerCanvasSyncPayload>();
                                if (payload?.Actions != null && !string.IsNullOrEmpty(payload.RoomId))
                                {
                                    // RoomManager.ApplyPeerCanvasSync holds the state lock for the
                                    // entire merge and dedupes by ActionId (or SeqNo fallback for
                                    // legacy rows without an id), preventing both interleaved
                                    // writes and unbounded duplication on repeated reconnects.
                                    int added = roomManager.ApplyPeerCanvasSync(payload.RoomId, payload.Actions);
                                    if (added > 0)
                                        Console.WriteLine($"[PEER] canvas sync for {payload.RoomId}: +{added} action(s)");
                                }
                                break;
                            }

                            case MessageType.PEER_MEMBER_SYNC:
                            {
                                var payload = msg.GetData<PeerMembersPayload>();
                                if (payload != null && !string.IsNullOrEmpty(payload.RoomId))
                                {
                                    roomManager.UpdatePeerMembers(payload.OriginServerId, payload.RoomId, payload.Members);
                                    // Refresh lobby user-count badges so lobby clients see the
                                    // updated counts when a user joined/left a room on a
                                    // different Canvas server.
                                    await roomManager.BroadcastLobbyRoomListAsync();
                                    // NOTE: do NOT call BroadcastRoomMembersAsync here. The
                                    // matching PEER_RELAY ROOM_UPDATE that follows already
                                    // re-broadcasts a fresh member list (rebuilt by
                                    // ApplyFromPeerAsync). Doing it here too produced two
                                    // ROOM_UPDATE messages per logical join/leave — the source
                                    // of the "1 user counted as 2 / chat shown twice" bug.
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PEER] inbound {remote} error: {ex.Message}");
            }
            finally
            {
                if (peerId != null) roomManager.DropPeer(peerId);
                try { tcp.Close(); } catch { }
                Console.WriteLine($"[PEER] inbound <- {remote} closed");
            }
        }
    }
}
