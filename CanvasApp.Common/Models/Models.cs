using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CanvasApp.Common
{
    public class User
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("username")] public string Username { get; set; }
        [JsonProperty("email")] public string Email { get; set; }
        [JsonProperty("avatarColor")] public string AvatarColor { get; set; } = "#7856CF";
        // PasswordHash chỉ tồn tại ở server, không gửi qua mạng
        [JsonIgnore] public string PasswordHash { get; set; }
    }

    public class Room
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("ownerId")] public int OwnerId { get; set; }
        [JsonProperty("ownerName")] public string OwnerName { get; set; }
        [JsonProperty("hasPassword")] public bool HasPassword { get; set; }
        [JsonProperty("template")] public string Template { get; set; } = "Blank";
        [JsonProperty("maxUsers")] public int MaxUsers { get; set; } = 4;
        [JsonProperty("currentUsers")] public int CurrentUsers { get; set; }
        [JsonProperty("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Server-side only
        [JsonIgnore] public string PasswordHash { get; set; }
    }

    // ── Auth payloads ────────────────────────────────────────────────
    public class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class LoginResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Token { get; set; }
        public User User { get; set; }
    }

    public class RegisterRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string Email { get; set; }
    }

    public class RegisterResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    // ── Room payloads ────────────────────────────────────────────────
    public class CreateRoomRequest
    {
        public string Name { get; set; }
        public string Password { get; set; }
        public string Template { get; set; }
        public int MaxUsers { get; set; }
    }

    public class JoinRoomRequest
    {
        public string RoomId { get; set; }
        public string Password { get; set; }
    }

    public class JoinRoomResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public Room Room { get; set; }
        public List<DrawAction> CanvasState { get; set; } = new List<DrawAction>();
    }

    public class RoomListResult
    {
        public List<Room> Rooms { get; set; } = new List<Room>();
    }

    // ── Draw action ──────────────────────────────────────────────────
    public class DrawAction
    {
        [JsonProperty("type")] public string Type { get; set; }   // pen, rect, circle, line, text...
        [JsonProperty("userId")] public int UserId { get; set; }
        [JsonProperty("color")] public string Color { get; set; }
        [JsonProperty("thickness")] public int Thickness { get; set; }
        [JsonProperty("points")] public List<PointF> Points { get; set; } = new List<PointF>();
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("filled")] public bool Filled { get; set; }
        [JsonProperty("timestamp")] public long Timestamp { get; set; }
    }

    public class PointF
    {
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        public PointF() { }
        public PointF(float x, float y) { X = x; Y = y; }
    }

    public class ChatMessage
    {
        [JsonProperty("userId")] public int UserId { get; set; }
        [JsonProperty("username")] public string Username { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("timestamp")] public long Timestamp { get; set; }
    }
}
