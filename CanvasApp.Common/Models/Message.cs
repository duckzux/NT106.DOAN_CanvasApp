using Newtonsoft.Json;

namespace CanvasApp.Common
{
    /// <summary>
    /// Message dùng cho tất cả giao tiếp TCP giữa client và server.
    /// Format: JSON + newline delimiter.
    /// </summary>
    public class Message
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("data")]
        public object Data { get; set; }

        [JsonProperty("token")]
        public string Token { get; set; }

        public Message() { }

        public Message(string type, object data = null, string token = null)
        {
            Type = type;
            Data = data;
            Token = token;
        }

        public string ToJson() => JsonConvert.SerializeObject(this);

        public static Message FromJson(string json) =>
            JsonConvert.DeserializeObject<Message>(json);

        /// <summary>Lấy Data dạng object cụ thể (T).</summary>
        public T GetData<T>() =>
            JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(Data));
    }

    /// <summary>Các loại message trong protocol.</summary>
    public static class MessageType
    {
        // Auth
        public const string AUTH_LOGIN = "AUTH_LOGIN";
        public const string AUTH_LOGIN_RESULT = "AUTH_LOGIN_RESULT";
        public const string AUTH_REGISTER = "AUTH_REGISTER";
        public const string AUTH_REGISTER_RESULT = "AUTH_REGISTER_RESULT";
        // Email-OTP for sign-up. Client first calls AUTH_SEND_OTP (username+email);
        // server emails a 6-digit code and returns an OtpToken. Client then resends
        // AUTH_REGISTER with that token + code so the server can verify before insert.
        public const string AUTH_SEND_OTP = "AUTH_SEND_OTP";
        public const string AUTH_SEND_OTP_RESULT = "AUTH_SEND_OTP_RESULT";
        // Forgot-password: AUTH_FORGOT_SEND_OTP emails an OTP bound to the user's
        // account; AUTH_RESET_PASSWORD verifies that OTP + sets the new password.
        public const string AUTH_FORGOT_SEND_OTP = "AUTH_FORGOT_SEND_OTP";
        public const string AUTH_FORGOT_SEND_OTP_RESULT = "AUTH_FORGOT_SEND_OTP_RESULT";
        public const string AUTH_RESET_PASSWORD = "AUTH_RESET_PASSWORD";
        public const string AUTH_RESET_PASSWORD_RESULT = "AUTH_RESET_PASSWORD_RESULT";

        // Room
        public const string ROOM_LIST = "ROOM_LIST";
        public const string ROOM_LIST_RESULT = "ROOM_LIST_RESULT";
        public const string ROOM_CREATE = "ROOM_CREATE";
        public const string ROOM_CREATE_RESULT = "ROOM_CREATE_RESULT";
        public const string ROOM_JOIN = "ROOM_JOIN";
        public const string ROOM_JOIN_RESULT = "ROOM_JOIN_RESULT";
        public const string ROOM_LEAVE = "ROOM_LEAVE";
        public const string ROOM_UPDATE = "ROOM_UPDATE";
        // Routing-only "pre-join" sent through the LoadBalancer. Server verifies the room
        // exists + password matches and returns ServerHost/ServerPort. Crucially it does NOT
        // register the user into the room and does NOT broadcast — so the LB-routed socket
        // can close without firing a spurious join→leave→join sequence. The real ROOM_JOIN
        // is then sent over the persistent direct connection.
        public const string ROOM_RESOLVE = "ROOM_RESOLVE";
        public const string ROOM_RESOLVE_RESULT = "ROOM_RESOLVE_RESULT";

        // Drawing (broadcast)
        public const string DRAW_START = "DRAW_START";
        public const string DRAW_MOVE = "DRAW_MOVE";
        public const string DRAW_END = "DRAW_END";
        public const string DRAW_SHAPE = "DRAW_SHAPE";
        public const string DRAW_TEXT = "DRAW_TEXT";
        public const string DRAW_CLEAR = "DRAW_CLEAR";
        public const string DRAW_UNDO = "DRAW_UNDO";
        public const string DRAW_FILL = "DRAW_FILL";
        // Place an image on the canvas. Payload: DrawAction with Type="image", ActionId, Points=[topLeft, bottomRight], ImageData=base64.
        public const string DRAW_IMAGE = "DRAW_IMAGE";
        // Update an existing image's position/size. Payload: DrawAction with ActionId, Points=[topLeft, bottomRight]. ImageData omitted.
        public const string DRAW_IMAGE_TRANSFORM = "DRAW_IMAGE_TRANSFORM";

        // Canvas state sync
        public const string CANVAS_STATE = "CANVAS_STATE";

        // Room - invite code
        public const string ROOM_JOIN_BY_CODE = "ROOM_JOIN_BY_CODE";
        public const string RESOLVE_INVITE_CODE = "RESOLVE_INVITE_CODE";
        public const string RESOLVE_INVITE_CODE_RESULT = "RESOLVE_INVITE_CODE_RESULT";

        // Chat
        public const string CHAT_MESSAGE = "CHAT_MESSAGE";
        public const string CHAT_HISTORY = "CHAT_HISTORY";
        public const string CHAT_FILE = "CHAT_FILE";

        // Generic
        public const string ERROR = "ERROR";
        public const string PING = "PING";
        public const string PONG = "PONG";

        // Inter-Canvas-server mesh broadcast envelope. Wraps an Inner message so peers can
        // replay a draw/chat/room event to their own local clients without re-publishing.
        public const string PEER_RELAY = "PEER_RELAY";
        // Sent by a Canvas server when its local member list for a room changes, so peers
        // can include the remote members in their unified member list / room user counts.
        public const string PEER_MEMBER_SYNC = "PEER_MEMBER_SYNC";
        // Sent right after a peer connection opens — declares which logical server this is
        // and (optionally) snapshots its current per-room membership so the receiver doesn't
        // have to wait for the next change to learn the world state.
        public const string PEER_HELLO = "PEER_HELLO";
        // Sent when a Canvas server creates a new room so peers can add it to their own
        // `_rooms` / invite-code maps. Without this, rooms created at runtime are invisible
        // to peers until restart (which reloads everything from DB via LoadActiveRooms).
        public const string PEER_ROOM_CREATE = "PEER_ROOM_CREATE";
        // Heartbeat from one peer to another to detect half-open connections.
        // Receiver responds with PEER_PONG on the same connection.
        public const string PEER_PING = "PEER_PING";
        public const string PEER_PONG = "PEER_PONG";
        // Sent when a peer reconnects to sync all committed draw actions (DRAW_END, DRAW_SHAPE)
        // so live clients don't miss shapes drawn during the outage.
        public const string PEER_CANVAS_SYNC = "PEER_CANVAS_SYNC";
    }

    public class PeerRelayPayload
    {
        public string RoomId { get; set; }
        public string OriginServerId { get; set; }
        public Message Inner { get; set; }
    }

    public class PeerMembersPayload
    {
        public string RoomId { get; set; }
        public string OriginServerId { get; set; }
        public System.Collections.Generic.List<RoomMember> Members { get; set; }
    }

    public class PeerHelloPayload
    {
        public string ServerId { get; set; }
        // Optional: roomId → members snapshot at the moment of connect.
        public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<RoomMember>> Rooms { get; set; }
        // Every room this server is aware of (from DB load + peer notifications). Receiver
        // adds any room IDs it doesn't already know to its own `_rooms` / invite-code maps.
        // Lets a freshly-started peer learn rooms created while it was offline.
        public System.Collections.Generic.List<Room> KnownRooms { get; set; }
    }
}
