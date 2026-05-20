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
                                    // Replay committed draw actions from the peer that we may have missed during downtime.
                                    // Dedup by ActionId to avoid re-applying the same action twice.
                                    var localState = roomManager.GetCanvasState(payload.RoomId);
                                    if (localState != null)
                                    {
                                        foreach (var action in payload.Actions)
                                        {
                                            bool exists = localState.Any(a => !string.IsNullOrEmpty(action.ActionId)
                                                                              && a.ActionId == action.ActionId);
                                            if (!exists) localState.Add(action);
                                        }
                                    }
                                }
                                break;
                            }

                            case MessageType.PEER_MEMBER_SYNC:
                            {
                                var payload = msg.GetData<PeerMembersPayload>();
                                if (payload != null && !string.IsNullOrEmpty(payload.RoomId))
                                {
                                    roomManager.UpdatePeerMembers(payload.OriginServerId, payload.RoomId, payload.Members);
                                    // Refresh lobby user-count badges — this is the missing link
                                    // that previously let lobby clients see stale counts when a
                                    // user joined/left a room on a different Canvas server.
                                    await roomManager.BroadcastLobbyRoomListAsync();
                                    // Push updated member list to local room clients so their UI stays current
                                    await roomManager.BroadcastRoomMembersAsync(payload.RoomId);
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
