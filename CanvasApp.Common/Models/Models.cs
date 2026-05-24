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
        [JsonProperty("inviteCode")] public string InviteCode { get; set; }

        [JsonIgnore] public string PasswordHash { get; set; }
    }

    /// <summary>1 user đang trong room.</summary>
    public class RoomMember
    {
        [JsonProperty("userId")] public int UserId { get; set; }
        [JsonProperty("username")] public string Username { get; set; }
        [JsonProperty("role")] public string Role { get; set; } = "Member";   // Owner | Member | Viewer
        [JsonProperty("avatarColor")] public string AvatarColor { get; set; } = "#6c5ce7";
    }

    /// <summary>Broadcast khi có thay đổi membership trong room.</summary>
    public class RoomMembersUpdate
    {
        [JsonProperty("members")] public List<RoomMember> Members { get; set; } = new List<RoomMember>();
        [JsonProperty("joinedUsername")] public string JoinedUsername { get; set; }   // null nếu không phải join event
        [JsonProperty("leftUsername")] public string LeftUsername { get; set; }       // null nếu không phải leave event
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
        // Email-OTP gate: client must first obtain an OtpToken via AUTH_SEND_OTP
        // and pass the matching 6-digit OtpCode back here. Server rejects the
        // request if either is missing or invalid.
        public string OtpToken { get; set; }
        public string OtpCode { get; set; }
    }

    public class RegisterResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    public class SendOtpRequest
    {
        public string Username { get; set; }
        public string Email { get; set; }
    }

    public class SendOtpResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string OtpToken { get; set; }
        public int ExpiresInSeconds { get; set; }
    }

    // ── Forgot-password: client supplies only the email; server looks up the
    // user, generates an OTP bound to that account, and emails the code.
    public class ForgotPasswordSendOtpRequest
    {
        public string Email { get; set; }
    }

    public class ForgotPasswordSendOtpResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string OtpToken { get; set; }
        public int ExpiresInSeconds { get; set; }
    }

    public class ResetPasswordRequest
    {
        public string Email { get; set; }
        public string NewPassword { get; set; }
        public string OtpToken { get; set; }
        public string OtpCode { get; set; }
    }

    public class ResetPasswordResult
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
        public bool RequiresPassword { get; set; }
        public Room Room { get; set; }
        // Compressed baseline snapshot (base64 GZip). Null when no snapshot exists yet.
        // Client should decompress this first, then apply CanvasState deltas on top.
        public string SnapshotData { get; set; }
        public List<DrawAction> CanvasState { get; set; } = new List<DrawAction>();
        public List<RoomMember> Members { get; set; } = new List<RoomMember>();
        // Chat history fetched atomically with admission — the server fetches this BEFORE
        // adding the joiner to _roomClients so any later live broadcast contains only
        // messages newer than the snapshot, avoiding the history-vs-live duplicate bug.
        public List<ChatMessage> ChatHistory { get; set; } = new List<ChatMessage>();

        // ✅ Canvas Server addresses (returned by LoadBalancer to direct client connection)
        public string ServerHost { get; set; }
        public int ServerPort { get; set; }
    }

    public class ResolveRoomRequest
    {
        public string RoomId { get; set; }
        public string Password { get; set; }
    }

    public class ResolveRoomResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public bool RequiresPassword { get; set; }
        public string ServerHost { get; set; }
        public int ServerPort { get; set; }
    }

    public class InviteCodeRequest
    {
        public string InviteCode { get; set; }
        public string Password { get; set; }
        public string RoomId { get; set; }  // Resolved roomId for LoadBalancer affinity routing
    }

    public class ResolveInviteCodeRequest
    {
        public string InviteCode { get; set; }
    }

    public class ResolveInviteCodeResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string RoomId { get; set; }
    }

    public class ChatHistoryResult
    {
        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }

    public class RoomListResult
    {
        public List<Room> Rooms { get; set; } = new List<Room>();
    }

    // ── Owner-only management payloads (lobby-side) ──────────────────
    public class DeleteRoomRequest
    {
        public string RoomId { get; set; }
    }

    public class DeleteRoomResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    public class UpdateRoomPasswordRequest
    {
        public string RoomId { get; set; }
        // Empty/null means "remove password" (public room).
        public string NewPassword { get; set; }
    }

    public class UpdateRoomPasswordResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public bool HasPassword { get; set; }
    }

    // Pushed via the peer mesh after the owning server applies a password change.
    // Carries the new BCrypt hash directly so peers don't have to round-trip to DB.
    public class PeerRoomPasswordPayload
    {
        public string RoomId { get; set; }
        public string PasswordHash { get; set; }   // null/empty = password removed
    }

    // ── Draw action ──────────────────────────────────────────────────
    public class DrawAction
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("userId")] public int UserId { get; set; }
        [JsonProperty("color")] public string Color { get; set; }
        [JsonProperty("thickness")] public int Thickness { get; set; }
        [JsonProperty("points")] public List<PointF> Points { get; set; } = new List<PointF>();
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("filled")] public bool Filled { get; set; }
        [JsonProperty("timestamp")] public long Timestamp { get; set; }
        // Client-generated stable identifier for cross-client undo/redo lookup.
        [JsonProperty("actionId")] public string ActionId { get; set; }
        // Base64-encoded image bytes; only set for "image" CREATE actions, omitted on transforms.
        [JsonProperty("imageData", NullValueHandling = NullValueHandling.Ignore)]
        public string ImageData { get; set; }

        // DB-only metadata — not sent to clients
        [JsonIgnore] public string RoomId { get; set; }
        [JsonIgnore] public long SeqNo { get; set; }
        [JsonIgnore] public bool IsUndone { get; set; }
    }

    // Payload broadcast when an action is undone — identifies which action to remove.
    public class UndoNotification
    {
        [JsonProperty("actionId")] public string ActionId { get; set; }
    }

    // ── Canvas snapshot ──────────────────────────────────────────────
    public class CanvasSnapshot
    {
        public long Id { get; set; }
        public string RoomId { get; set; }
        public int Version { get; set; }
        public string SnapshotData { get; set; }   // compressed+base64 JSON of committed actions
        public long ActionSeqAt { get; set; }       // last draw_action seq_no included in this snapshot
        public int ByteSize { get; set; }
        public DateTime CreatedAt { get; set; }
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
        // File attachment fields (null for regular text messages)
        [JsonProperty("fileName")] public string FileName { get; set; }
        [JsonProperty("fileData")] public string FileData { get; set; }
        [JsonProperty("fileSizeBytes")] public long FileSizeBytes { get; set; }
    }

    // ── Peer mesh payload for canvas state sync after reconnect ──
    public class PeerCanvasSyncPayload
    {
        [JsonProperty("roomId")] public string RoomId { get; set; }
        [JsonProperty("serverId")] public string OriginServerId { get; set; }
        [JsonProperty("actions")] public List<DrawAction> Actions { get; set; } = new List<DrawAction>();
    }

    public class CursorUpdatePayload
    {
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        [JsonProperty("username")] public string Username { get; set; }
        [JsonProperty("color")] public string Color { get; set; }
    }
}
