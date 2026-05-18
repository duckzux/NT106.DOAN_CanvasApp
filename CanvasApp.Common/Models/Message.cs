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

        // Room
        public const string ROOM_LIST = "ROOM_LIST";
        public const string ROOM_LIST_RESULT = "ROOM_LIST_RESULT";
        public const string ROOM_CREATE = "ROOM_CREATE";
        public const string ROOM_CREATE_RESULT = "ROOM_CREATE_RESULT";
        public const string ROOM_JOIN = "ROOM_JOIN";
        public const string ROOM_JOIN_RESULT = "ROOM_JOIN_RESULT";
        public const string ROOM_LEAVE = "ROOM_LEAVE";
        public const string ROOM_UPDATE = "ROOM_UPDATE";

        // Drawing (broadcast)
        public const string DRAW_START = "DRAW_START";
        public const string DRAW_MOVE = "DRAW_MOVE";
        public const string DRAW_END = "DRAW_END";
        public const string DRAW_SHAPE = "DRAW_SHAPE";
        public const string DRAW_TEXT = "DRAW_TEXT";
        public const string DRAW_CLEAR = "DRAW_CLEAR";
        public const string DRAW_UNDO = "DRAW_UNDO";

        // Canvas state sync
        public const string CANVAS_STATE = "CANVAS_STATE";

        // Room - invite code
        public const string ROOM_JOIN_BY_CODE = "ROOM_JOIN_BY_CODE";

        // Chat
        public const string CHAT_MESSAGE = "CHAT_MESSAGE";
        public const string CHAT_HISTORY = "CHAT_HISTORY";

        // Generic
        public const string ERROR = "ERROR";
        public const string PING = "PING";
        public const string PONG = "PONG";
    }
}
