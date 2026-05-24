# Cấu trúc tất cả Message trong CanvasApp

> **Format chung**: mọi message là 1 dòng JSON, kết thúc bằng `\n`.
> ```json
> { "type": "<MESSAGE_TYPE>", "data": { ... }, "token": "<authToken>|null" }
> ```
> - `type`: hằng số trong `MessageType` (file [Message.cs](../../CanvasApp.Common/Models/Message.cs)) — là nguồn duy nhất; file `MessageType.cs` cũ là stub rỗng, để cho khả năng tương thích build cũ.
> - `data`: payload tuỳ theo `type` (đối tượng JSON)
> - `token`: chuỗi `base64(payload).base64(HMAC-SHA256(payload, secret))` với `payload = "userId:username:issuedAt"`. Secret lấy từ env `CANVASAPP_JWT_SECRET` (fallback hardcoded dev). TTL 24h. Verify bằng `FixedTimeEquals` (constant-time MAC). Bắt buộc với hầu hết request sau đăng nhập; `null` với AUTH_*, PING, và peer-mesh.
>
> **Mã hoá payload (opt-in):** module AES-256-CBC + HMAC-SHA256 ở [`Utils/MessageCrypto.cs`](../../CanvasApp.Common/Utils/MessageCrypto.cs) wrap `data` thành `{ _enc: "base64(IV‖CT‖MAC)", _v: 1 }`. `type` + `token` giữ plaintext để LB peek route và AuthServer verify. Mặc định đang TẮT trên dây để demo dễ thấy JSON qua Wireshark.

---

## 📡 Các luồng kết nối

| Luồng | Người gửi → Người nhận | Loại kết nối |
|-------|------------------------|--------------|
| **AUTH** | Client → LoadBalancer → AuthServer | Short-lived (1 request/socket) |
| **LOBBY** | Client → LoadBalancer → Canvas Server | Short-lived |
| **REALTIME** | Client ↔ Canvas Server (direct) | Persistent (cả phiên ở trong room) |
| **MESH** | Canvas Server ↔ Canvas Server | Persistent (kết nối nội bộ giữa các canvas server) |

---

## 1. 🔐 AUTH — Đăng nhập / Đăng ký / Quên mật khẩu

> **Tất cả AUTH_*** đi qua **LoadBalancer → AuthServer**, qua connection ngắn hạn.

### `AUTH_LOGIN` — Client → AuthServer
Đăng nhập bằng username + password.

```json
{
  "type": "AUTH_LOGIN",
  "data": { "Username": "ndln", "Password": "secret123" },
  "token": null
}
```

### `AUTH_LOGIN_RESULT` — AuthServer → Client
```json
{
  "type": "AUTH_LOGIN_RESULT",
  "data": {
    "Success": true,
    "Message": "OK",
    "Token": "MTpuZGxuOjE3NDY4NjQwMDA=",
    "User": { "Id": 1, "Username": "ndln", "Email": "n@uit.edu.vn", "AvatarColor": "#3498db" }
  }
}
```

### `AUTH_SEND_OTP` — Client → AuthServer (đăng ký bước 1)
Yêu cầu gửi OTP 6 số đến email để xác thực trước khi register.

```json
{
  "type": "AUTH_SEND_OTP",
  "data": { "Username": "newuser", "Email": "new@gmail.com" }
}
```

### `AUTH_SEND_OTP_RESULT` — AuthServer → Client
```json
{
  "type": "AUTH_SEND_OTP_RESULT",
  "data": {
    "Success": true,
    "Message": "Đã gửi mã xác thực",
    "OtpToken": "9f8c7a1b-...",
    "ExpiresInSeconds": 300
  }
}
```

### `AUTH_REGISTER` — Client → AuthServer (đăng ký bước 2)
Hoàn tất đăng ký với OTP đã nhận.

```json
{
  "type": "AUTH_REGISTER",
  "data": {
    "Username": "newuser",
    "Password": "Pwd@123",
    "Email": "new@gmail.com",
    "OtpToken": "9f8c7a1b-...",
    "OtpCode": "482917"
  }
}
```

### `AUTH_REGISTER_RESULT` — AuthServer → Client
```json
{ "type": "AUTH_REGISTER_RESULT", "data": { "Success": true, "Message": "Đăng ký thành công" } }
```

### `AUTH_FORGOT_SEND_OTP` — Client → AuthServer (quên mật khẩu bước 1)
```json
{ "type": "AUTH_FORGOT_SEND_OTP", "data": { "Email": "n@uit.edu.vn" } }
```

### `AUTH_FORGOT_SEND_OTP_RESULT` — AuthServer → Client
```json
{
  "type": "AUTH_FORGOT_SEND_OTP_RESULT",
  "data": { "Success": true, "OtpToken": "...", "ExpiresInSeconds": 300, "Message": "Đã gửi mã" }
}
```

### `AUTH_RESET_PASSWORD` — Client → AuthServer (quên mật khẩu bước 2)
```json
{
  "type": "AUTH_RESET_PASSWORD",
  "data": {
    "Email": "n@uit.edu.vn",
    "NewPassword": "NewPwd@456",
    "OtpToken": "...",
    "OtpCode": "739204"
  }
}
```

### `AUTH_RESET_PASSWORD_RESULT` — AuthServer → Client
```json
{ "type": "AUTH_RESET_PASSWORD_RESULT", "data": { "Success": true, "Message": "Đã đổi mật khẩu" } }
```

---

## 1.1 📧 Chi tiết flow gửi OTP qua email

> Áp dụng cho cả **đăng ký** (`AUTH_SEND_OTP`) lẫn **quên mật khẩu** (`AUTH_FORGOT_SEND_OTP`). Code: [OtpService.cs](../../CanvasApp.AuthServer/Services/OtpService.cs), [OtpStore.cs](../../CanvasApp.AuthServer/OtpStore.cs), [SmtpEmailSender.cs](../../CanvasApp.AuthServer/Services/SmtpEmailSender.cs).

### Sơ đồ flow (đăng ký — quên mật khẩu tương tự)

```
┌────────┐                ┌────────┐               ┌──────────┐         ┌────────┐
│ Client │                │  Auth  │               │  MySQL   │         │ Gmail  │
│        │                │ Server │               │email_otp │         │  SMTP  │
└───┬────┘                └───┬────┘               └─────┬────┘         └────┬───┘
    │  AUTH_SEND_OTP          │                          │                   │
    │ {Username, Email}       │                          │                   │
    ├────────────────────────▶│                          │                   │
    │                         │ CheckRateLimit(email)    │                   │
    │                         ├─SELECT COUNT(*)─────────▶│                   │
    │                         │◀─count───────────────────┤                   │
    │                         │                          │                   │
    │                         │ UsernameExists?          │                   │
    │                         ├──SELECT 1 FROM users────▶│                   │
    │                         │                          │                   │
    │                         │ code = RNG.6digits()     │                   │
    │                         │ token = GUID             │                   │
    │                         │ codeHash = BCrypt(code,  │                   │
    │                         │   workFactor=10)         │                   │
    │                         │                          │                   │
    │                         │ Save(token, username,    │                   │
    │                         │   email, codeHash,       │                   │
    │                         │   expires=now+5m,        │                   │
    │                         │   created=UtcNow)        │                   │
    │                         ├──INSERT─────────────────▶│                   │
    │                         │                          │                   │
    │                         │ SmtpClient.Send(         │                   │
    │                         │   to=email,              │                   │
    │                         │   body=HTML{code})       │                   │
    │                         ├──────────────────────────────────────────────▶│
    │                         │                          │           SMTPS:587│
    │                         │                          │           STARTTLS │
    │                         │                          │           AUTH PLAIN│
    │                         │                          │                   │
    │ AUTH_SEND_OTP_RESULT    │                          │                   │
    │ {Success, OtpToken,     │                          │                   │
    │  ExpiresInSeconds:300}  │                          │                   │
    │◀────────────────────────┤                          │                   │
    │                         │                          │                   │
    │   (user mở mail, đọc code 6 số)                    │                   │
    │                         │                          │                   │
    │  AUTH_REGISTER          │                          │                   │
    │ {Username, Password,    │                          │                   │
    │  Email, OtpToken,       │                          │                   │
    │  OtpCode:"482917"}      │                          │                   │
    ├────────────────────────▶│                          │                   │
    │                         │ Verify(token, code, ...) │                   │
    │                         ├─SELECT...WHERE token────▶│                   │
    │                         │◀─OtpRecord───────────────┤                   │
    │                         │ BCrypt.Verify(code,      │                   │
    │                         │   record.CodeHash)       │                   │
    │                         │ MarkUsed(token) (atomic) │                   │
    │                         ├─UPDATE used=1───────────▶│                   │
    │                         │                          │                   │
    │                         │ UserStore.Register(...)  │                   │
    │                         ├─INSERT users────────────▶│                   │
    │ AUTH_REGISTER_RESULT    │                          │                   │
    │◀────────────────────────┤                          │                   │
```

### Tại sao tách `OtpToken` và `OtpCode`?

- `OtpCode` = 6 số gửi qua **email** (kênh phụ — out-of-band).
- `OtpToken` = GUID gửi qua **TCP** (kênh chính), client nhớ và gửi lại khi verify.
- Khi `AUTH_REGISTER` đi, server tra DB bằng `OtpToken` để lấy `codeHash`, rồi `BCrypt.Verify(OtpCode, codeHash)`. **Server không bao giờ lưu code plaintext.**
- Lợi ích: kẻ tấn công sniff được token cũng phải có mail mới biết code; sniff được code cũng phải có token mới biết bind với username nào.

### Schema bảng `email_otp_codes`

```sql
CREATE TABLE email_otp_codes (
    token       CHAR(36)     PRIMARY KEY,    -- GUID, gửi cho client
    username    VARCHAR(50)  NOT NULL,       -- bind code → username
    email       VARCHAR(100) NOT NULL,       -- bind code → email
    code_hash   VARCHAR(255) NOT NULL,       -- BCrypt(code, workFactor=10)
    expires_at  DATETIME     NOT NULL,       -- UTC, mặc định +5 phút
    attempts    INT          NOT NULL DEFAULT 0,  -- guard brute-force, max 5
    used        TINYINT(1)   NOT NULL DEFAULT 0,  -- single-shot
    created_at  DATETIME,                    -- UTC (set tường minh, KHÔNG dùng MySQL default)
    INDEX idx_otp_email (email)
);
```

> ⚠ **Bug đã fix:** `created_at` trước đây dùng `DEFAULT CURRENT_TIMESTAMP` — MySQL ghi giờ local server (ICT). Rate-limit query so với `DateTime.UtcNow.AddMinutes(-1)` (giờ UTC) → `created_at` luôn lớn hơn 7 tiếng → rate-limit "kẹt" 7 tiếng ở VN. Giờ luôn set tường minh `@CreatedAt = DateTime.UtcNow`.

### Sinh OTP code (cryptographically random)

```csharp
private static string GenerateSixDigitCode()
{
    var bytes = new byte[4];
    using (var rng = new RNGCryptoServiceProvider())
        rng.GetBytes(bytes);
    uint v = BitConverter.ToUInt32(bytes, 0) % 1000000u;
    return v.ToString("D6");   // "000000" → "999999"
}
```

- Dùng `RNGCryptoServiceProvider` (CSPRNG), **không** dùng `Random` (predictable seed = current time).
- 4 bytes → uint → mod 1_000_000 → có **modulo bias rất nhỏ** (chấp nhận được cho 6-digit OTP, không phải crypto key).

### Rate-limit (chống spam mail / DoS)

```csharp
private const int RateLimitPerMinute = 1;
private const int RateLimitPerHour   = 5;

private string CheckRateLimit(string email)
{
    if (_store.CountRecentByEmail(email, DateTime.UtcNow.AddMinutes(-1)) >= 1)
        return "Vui lòng đợi 1 phút trước khi yêu cầu mã mới";
    if (_store.CountRecentByEmail(email, DateTime.UtcNow.AddHours(-1)) >= 5)
        return "Đã yêu cầu quá nhiều mã trong 1 giờ — thử lại sau";
    return null;
}
```

- Per-email không per-IP → 1 user spam mail dù đổi IP vẫn bị chặn.
- Bị bypass nếu attacker đổi email mỗi lần — không phòng được user enumeration. Có thể thêm per-IP rate-limit sau.

### Verify (single-shot, brute-force guard)

```csharp
public (bool ok, string message, OtpRecord record) Verify(token, code, expectedUser, expectedEmail, ...)
{
    var rec = _store.Find(token);
    if (rec == null)                               return "Mã không tồn tại";
    if (rec.Used)                                  return "Mã đã được sử dụng";
    if (DateTime.UtcNow > rec.ExpiresUtc)          return "Mã hết hạn";
    if (rec.Attempts >= 5)                         return "Mã bị khoá do nhập sai 5 lần";

    // Bind: code phải sinh cho ĐÚNG (username, email) — chống code-swap
    if (rec.Username != expectedUser ||
        !string.Equals(rec.Email, expectedEmail, OrdinalIgnoreCase))
        return "Không khớp tài khoản/email";

    if (!BCrypt.Net.BCrypt.Verify(code, rec.CodeHash))
    {
        _store.IncrementAttempts(token);   // 5 lần sai → khoá
        return "Mã không đúng";
    }
    if (markUsedOnSuccess) _store.MarkUsed(token);  // single-shot
    return ok;
}
```

- `markUsedOnSuccess=false` dùng cho `AUTH_RESET_PASSWORD`: chỉ burn token **sau khi** `UpdatePassword` thành công — tránh case DB lỗi giữa chừng, OTP bị burn nhưng password chưa đổi → user phải xin mã mới.

### SMTP (Gmail App Password)

```
SmtpHost:    smtp.gmail.com
SmtpPort:    587   (submission, STARTTLS)
SmtpEnableSsl: true  → SmtpClient nâng cấp TLS sau khi greet
Credentials: NetworkCredential(user, AppPassword)
                ↑ App Password 16 ký tự, KHÔNG dùng password Gmail thật
Timeout:     15s
```

> Setup Gmail: bật 2FA → tạo App Password tại myaccount.google.com/apppasswords → lưu vào `App.config` (`SmtpPassword`). Production: chuyển qua env var hoặc Key Vault, không commit vào git.

### Tổng kết phòng chống tấn công

| Tấn công | Phòng chống |
|---|---|
| Sniff TCP để đoán code | Code chỉ ở mail (out-of-band), payload TCP chỉ có `OtpToken` GUID |
| Brute-force 6 số (1M permutations) | `attempts >= 5` khoá token; expiry 5 phút |
| Spam mail (DoS user inbox / SMTP cost) | Rate-limit 1/phút, 5/giờ per email |
| Replay code đã dùng | `used = 1` sau lần verify thành công đầu |
| Sniff DB dump | `code_hash = BCrypt(code, 10)` — không có plaintext |
| Code-swap (verify code của email A cho email B) | Bind `(username, email)` check trong `Verify()` |
| Race: DB lỗi giữa chừng đổi password → OTP đã burn | `markUsedOnSuccess=false` + burn sau khi commit |
| MitM đọc nội dung mail | TLS giữa AuthServer ↔ Gmail (STARTTLS:587); end-to-mailbox vẫn dựa Gmail TLS |

---

## 2. 🏠 LOBBY & ROOM ADMIN — Client → Canvas Server (qua LB)

> Mỗi request mở connection ngắn hạn; LB peek `type` + (nếu có) `RoomId` để route đúng server.

### `ROOM_LIST` — Client → Canvas Server
Lấy danh sách tất cả phòng active. LB route đến canvas server least-loaded.

```json
{ "type": "ROOM_LIST", "data": null, "token": "MTpuZGxuOjE3..." }
```

### `ROOM_LIST_RESULT` — Canvas Server → Client
```json
{
  "type": "ROOM_LIST_RESULT",
  "data": {
    "Rooms": [
      {
        "Id": "A3B7C9D2",
        "Name": "Test Room",
        "OwnerId": 1,
        "OwnerName": "ndln",
        "HasPassword": false,
        "MaxUsers": 8,
        "CurrentUsers": 2,
        "Template": "Blank",
        "InviteCode": "9WGCK5"
      }
    ]
  }
}
```

### `ROOM_CREATE` — Client → Canvas Server
LB route least-loaded; sniff `ROOM_CREATE_RESULT` để bind room → server.

```json
{
  "type": "ROOM_CREATE",
  "data": { "Name": "New Room", "Password": "", "Template": "Blank", "MaxUsers": 4 }
}
```

### `ROOM_CREATE_RESULT` — Canvas Server → Client
Trả về phòng vừa tạo. LB sniff để register routing.

```json
{
  "type": "ROOM_CREATE_RESULT",
  "data": { "Id": "X9Y2Z5W7", "Name": "New Room", "OwnerId": 1, "HasPassword": false, "InviteCode": "AB3X7Z" }
}
```

### `ROOM_RESOLVE` — Client → Canvas Server (pre-join check)
Verify room tồn tại + password đúng, **không add client vào room**. Trả về `ServerHost:Port` để client direct-connect.

```json
{ "type": "ROOM_RESOLVE", "data": { "RoomId": "A3B7C9D2", "Password": "" } }
```

### `ROOM_RESOLVE_RESULT` — Canvas Server → Client
```json
{
  "type": "ROOM_RESOLVE_RESULT",
  "data": { "Success": true, "ServerHost": "127.0.0.1", "ServerPort": 9002, "RequiresPassword": false }
}
```

### `RESOLVE_INVITE_CODE` — Client → Canvas Server
Đổi mã mời 6 ký tự sang `RoomId`. Bất kỳ canvas nào trả lời được (mọi peer share `_codeToRoomId`).

```json
{ "type": "RESOLVE_INVITE_CODE", "data": { "InviteCode": "9WGCK5" } }
```

### `RESOLVE_INVITE_CODE_RESULT` — Canvas Server → Client
```json
{ "type": "RESOLVE_INVITE_CODE_RESULT", "data": { "Success": true, "RoomId": "A3B7C9D2" } }
```

### `ROOM_DELETE` — Client (owner) → Canvas Server
Xóa phòng (sticky route đến server giữ room).

```json
{ "type": "ROOM_DELETE", "data": { "RoomId": "A3B7C9D2" } }
```

### `ROOM_DELETE_RESULT` — Canvas Server → Client
LB sniff để gọi `UnregisterRoom` (giảm `RoomCount` của backend).

```json
{ "type": "ROOM_DELETE_RESULT", "data": { "Success": true, "Message": "Đã xóa phòng" } }
```

### `ROOM_UPDATE_PASSWORD` — Client (owner) → Canvas Server
Đổi/bỏ mật khẩu phòng. Server PEER_RELAY hash mới sang các peer.

```json
{ "type": "ROOM_UPDATE_PASSWORD", "data": { "RoomId": "A3B7C9D2", "NewPassword": "newpwd123" } }
```

### `ROOM_UPDATE_PASSWORD_RESULT` — Canvas Server → Client
```json
{
  "type": "ROOM_UPDATE_PASSWORD_RESULT",
  "data": { "Success": true, "Message": "Đã đổi mật khẩu", "HasPassword": true }
}
```

---

## 3. 🎨 REALTIME ROOM — Client ↔ Canvas Server (persistent direct)

> Sau `ROOM_RESOLVE`, client direct-connect đến canvas server bằng socket persistent.

### `ROOM_JOIN` — Client → Canvas Server
Thực sự gia nhập room (add vào `_roomClients`).

```json
{ "type": "ROOM_JOIN", "data": { "RoomId": "A3B7C9D2", "Password": "" } }
```

### `ROOM_JOIN_RESULT` — Canvas Server → Client
Trả về snapshot canvas + chat history + members (atomic snapshot).

```json
{
  "type": "ROOM_JOIN_RESULT",
  "data": {
    "Success": true,
    "Room": { "Id": "A3B7C9D2", "Name": "Test Room", "OwnerId": 1, "MaxUsers": 8 },
    "SnapshotData": "H4sIAAAAAA...",
    "CanvasState": [ { "type": "STROKE", "userId": 1, "color": "#000", "points": [...], "actionId": "srv-42" } ],
    "Members": [ { "userId": 1, "username": "ndln", "role": "OWNER" } ],
    "ChatHistory": [ { "userId": 2, "username": "zhang", "text": "hello", "timestamp": 1746864000000 } ],
    "ServerHost": "127.0.0.1",
    "ServerPort": 9002
  }
}
```

### `ROOM_JOIN_BY_CODE` — Client → Canvas Server
Gia nhập bằng mã mời (sau khi đã RESOLVE_INVITE_CODE để có RoomId cho room-affinity).

```json
{
  "type": "ROOM_JOIN_BY_CODE",
  "data": { "InviteCode": "9WGCK5", "Password": "", "RoomId": "A3B7C9D2" }
}
```

### `ROOM_LEAVE` — Client → Canvas Server
Rời room (chủ động). Server broadcast `ROOM_UPDATE` cho các client còn lại.

```json
{ "type": "ROOM_LEAVE", "data": null }
```

### `ROOM_UPDATE` — Canvas Server → Client (broadcast)
Server đẩy khi danh sách member thay đổi (có người join/leave).

```json
{
  "type": "ROOM_UPDATE",
  "data": {
    "Members": [ { "userId": 1, "username": "ndln" }, { "userId": 2, "username": "zhang" } ],
    "JoinedUsername": "zhang",
    "LeftUsername": null
  }
}
```

---

## 4. ✏️ DRAWING — Client ↔ Canvas Server (broadcast)

> Vẽ vector. `DRAW_START`/`DRAW_MOVE` là live signal (không persist); `DRAW_END`/`DRAW_SHAPE`/... mới persist vào `draw_actions`.

### `DRAW_START` — Client → Canvas Server → các Client khác trong room
Bắt đầu nét (live). Không lưu DB.

```json
{
  "type": "DRAW_START",
  "data": { "type": "STROKE", "userId": 1, "color": "#7856CF", "thickness": 3, "points": [{ "x": 100, "y": 200 }] }
}
```

### `DRAW_MOVE` — Client → Canvas Server → broadcast
Điểm tiếp theo (live).

```json
{ "type": "DRAW_MOVE", "data": { "userId": 1, "points": [{ "x": 105, "y": 203 }] } }
```

### `DRAW_END` — Client → Canvas Server → broadcast
Kết thúc nét. Server persist vào `draw_actions` + gán `SeqNo` + `ActionId`.

```json
{
  "type": "DRAW_END",
  "data": {
    "type": "STROKE",
    "userId": 1,
    "color": "#7856CF",
    "thickness": 3,
    "points": [{ "x": 100, "y": 200 }, { "x": 105, "y": 203 }, ...],
    "actionId": "cli-abc123",
    "timestamp": 1746864000123
  }
}
```

### `DRAW_SHAPE` — Client → Canvas Server → broadcast
Vẽ shape (rectangle/circle/line/arrow). Lưu DB tương tự DRAW_END.

```json
{
  "type": "DRAW_SHAPE",
  "data": {
    "type": "RECTANGLE",
    "userId": 1,
    "color": "#000",
    "thickness": 2,
    "filled": false,
    "points": [{ "x": 100, "y": 100 }, { "x": 300, "y": 200 }],
    "actionId": "cli-shape-1"
  }
}
```

### `DRAW_TEXT` — Client → Canvas Server → broadcast
Vẽ text.

```json
{
  "type": "DRAW_TEXT",
  "data": { "type": "TEXT", "userId": 1, "text": "Hello!", "points": [{ "x": 200, "y": 150 }], "color": "#FF0000" }
}
```

### `DRAW_FILL` — Client → Canvas Server → broadcast
Đổ màu (flood fill / fill shape).

```json
{ "type": "DRAW_FILL", "data": { "userId": 1, "color": "#FFFF00", "points": [{ "x": 250, "y": 175 }] } }
```

### `DRAW_UNDO` — Client → Canvas Server → broadcast
Hoàn tác action gần nhất của user.

```json
{ "type": "DRAW_UNDO", "data": { "actionId": "cli-abc123" } }
```

### `DRAW_CLEAR` — Client → Canvas Server → broadcast
Xóa toàn bộ canvas.

```json
{ "type": "DRAW_CLEAR", "data": null }
```

### `DRAW_IMAGE` — Client → Canvas Server → broadcast
Đặt 1 ảnh bitmap lên canvas (import từ máy hoặc paste). `Points` là 2 điểm xác định bounding box (topLeft, bottomRight). `ImageData` là base64 của byte[] PNG/JPEG.

```json
{
  "type": "DRAW_IMAGE",
  "data": {
    "type": "image",
    "userId": 1,
    "actionId": "cli-img-1",
    "points": [{ "x": 100, "y": 100 }, { "x": 400, "y": 300 }],
    "imageData": "iVBORw0KGgo..."
  }
}
```

### `DRAW_IMAGE_TRANSFORM` — Client → Canvas Server → broadcast
Di chuyển/scale ảnh đã có. Server **mutate** action hiện có (theo `actionId`) thay vì insert mới. Payload **không có** `imageData` — chỉ cập nhật `points`.

```json
{
  "type": "DRAW_IMAGE_TRANSFORM",
  "data": { "userId": 1, "actionId": "cli-img-1", "points": [{ "x": 150, "y": 120 }, { "x": 500, "y": 380 }] }
}
```

### `CANVAS_STATE` — Client → Canvas Server → trả về Client
Yêu cầu re-sync canvas state (snapshot + deltas) — dùng khi reconnect.

```json
{ "type": "CANVAS_STATE", "data": null }
```

Response: server gửi lại snapshot tương tự `ROOM_JOIN_RESULT` (chỉ phần canvas).

---

## 5. 💬 CHAT — Client ↔ Canvas Server (broadcast)

### `CHAT_MESSAGE` — Client → Canvas Server → broadcast (kể cả sender)
```json
{ "type": "CHAT_MESSAGE", "data": { "text": "hello world" } }
```

Server echo lại với metadata:
```json
{
  "type": "CHAT_MESSAGE",
  "data": { "userId": 1, "username": "ndln", "text": "hello world", "timestamp": 1746864000000 }
}
```

### `CHAT_FILE` — Client → Canvas Server → broadcast
File attachment (base64, ≤ 2MB).

```json
{
  "type": "CHAT_FILE",
  "data": {
    "userId": 1,
    "username": "ndln",
    "fileName": "design.png",
    "fileData": "iVBORw0KGgoAAAANS...",
    "fileSizeBytes": 102400
  }
}
```

### `CHAT_HISTORY` — *deprecated*
> Chat history giờ embedded trong `ROOM_JOIN_RESULT.ChatHistory` (fix race history-vs-live).

---

## 6. 🛡️ SYSTEM

### `PING` — Client → Canvas Server
Heartbeat mỗi 10 giây. Không cần token.

```json
{ "type": "PING", "data": null }
```

### `PONG` — Canvas Server → Client
```json
{ "type": "PONG", "data": null }
```

### `ERROR` — Server → Client
Báo lỗi (token invalid, payload quá lớn, ...).

```json
{ "type": "ERROR", "data": { "message": "Token không hợp lệ hoặc đã hết hạn" } }
```

---

## 7. 🌐 PEER MESH — Canvas Server ↔ Canvas Server

> Mỗi canvas server mở persistent connection sang các peer khác (port = client port + 100).
> Format: `Message` chuẩn, nhưng chỉ gửi giữa các server, **không bao giờ đi tới client**.

### `PEER_HELLO` — Outbound peer → Inbound peer (ngay sau connect)
Khai báo self + snapshot world state.

```json
{
  "type": "PEER_HELLO",
  "data": {
    "ServerId": "canvas-9002",
    "Rooms": { "A3B7C9D2": [ { "userId": 1, "username": "ndln" } ] },
    "KnownRooms": [ { "Id": "A3B7C9D2", "Name": "Test", "OwnerId": 1 } ]
  }
}
```

> Self-loop guard: nếu `ServerId == SelfServerId` thì receiver đóng connection ngay.

### `PEER_RELAY` — Canvas → Canvas (envelope cho mọi event broadcast)
Wrap 1 `inner` message (chat/draw/room-update) để peer replay sang local clients.

```json
{
  "type": "PEER_RELAY",
  "data": {
    "RoomId": "A3B7C9D2",
    "OriginServerId": "canvas-9002",
    "Inner": { "type": "CHAT_MESSAGE", "data": { "userId": 1, "username": "ndln", "text": "hi" } }
  }
}
```

Receiver gọi `ApplyFromPeerAsync` → broadcast `Inner` sang local clients. **KHÔNG re-publish** (chống loop).

### `PEER_MEMBER_SYNC` — Canvas → Canvas
Báo cho peer biết danh sách member local hiện tại của 1 room.

```json
{
  "type": "PEER_MEMBER_SYNC",
  "data": {
    "RoomId": "A3B7C9D2",
    "OriginServerId": "canvas-9002",
    "Members": [ { "userId": 1, "username": "ndln" } ]
  }
}
```

### `PEER_ROOM_CREATE` — Canvas → Canvas
Báo cho peer biết có room mới (để add vào `_rooms` + invite code map ngay, không phải đợi DB reload).

```json
{
  "type": "PEER_ROOM_CREATE",
  "data": { "Id": "X9Y2Z5W7", "Name": "New Room", "OwnerId": 1, "InviteCode": "AB3X7Z" }
}
```

### `PEER_ROOM_DELETE` — Canvas → Canvas
Báo peer xóa room khỏi state in-memory.

```json
{ "type": "PEER_ROOM_DELETE", "data": "A3B7C9D2" }
```

### `PEER_ROOM_PASSWORD_UPDATED` — Canvas → Canvas
Đồng bộ password hash mới khi owner đổi mật khẩu.

```json
{
  "type": "PEER_ROOM_PASSWORD_UPDATED",
  "data": { "RoomId": "A3B7C9D2", "PasswordHash": "$2a$10$..." }
}
```

> `PasswordHash = null/""` nghĩa là **bỏ mật khẩu** (room thành public).

### `PEER_CANVAS_SYNC` — Canvas → Canvas (sau reconnect)
Khi peer reconnect lại, server gửi tất cả `DrawAction` đã commit của các room dirty để peer replay missed actions.

```json
{
  "type": "PEER_CANVAS_SYNC",
  "data": {
    "RoomId": "A3B7C9D2",
    "OriginServerId": "canvas-9002",
    "Actions": [
      { "type": "STROKE", "userId": 1, "actionId": "srv-42", "seqNo": 42, "points": [...] },
      { "type": "RECTANGLE", "userId": 2, "actionId": "srv-43", "seqNo": 43, "points": [...] }
    ]
  }
}
```

> Receiver dedupe theo `ActionId` (primary) hoặc `SeqNo` (fallback cho legacy rows).

### `PEER_PING` / `PEER_PONG` — Canvas ↔ Canvas
Heartbeat giữa 2 peer mỗi 15 giây để phát hiện half-open connection.

```json
{ "type": "PEER_PING", "data": null }
{ "type": "PEER_PONG", "data": null }
```

---

## 8. 🔐 Cryptography — toàn bộ primitive đang dùng

> Thư viện: .NET Framework 4.8 `System.Security.Cryptography` + `BCrypt.Net-Next` (NuGet). Tất cả crypto là **standard-library**, không tự cài (homemade crypto = nguy hiểm).

### Bản đồ: chỗ nào dùng cái gì

| Mục đích | Primitive | Code |
|---|---|---|
| Hash password user | `BCrypt(password, workFactor=12)` | [UserStore.cs:220, 379](../../CanvasApp.AuthServer/UserStore.cs#L220) |
| Hash OTP code | `BCrypt(code, workFactor=10)` | [OtpService.cs:94](../../CanvasApp.AuthServer/Services/OtpService.cs#L94) |
| Hash password phòng vẽ | `BCrypt(roomPwd, workFactor=10)` | `RoomManager.cs` |
| Auth token (JWT-lite) | `HMAC-SHA256(payload, secret)` + base64 | [UserStore.cs:398-446](../../CanvasApp.AuthServer/UserStore.cs#L398-L446) |
| Random OTP code 6 số | `RNGCryptoServiceProvider` (CSPRNG) | [OtpService.cs:278](../../CanvasApp.AuthServer/Services/OtpService.cs#L278) |
| Token GUID | `Guid.NewGuid()` (v4 random) | [OtpService.cs:92](../../CanvasApp.AuthServer/Services/OtpService.cs#L92) |
| Mã hoá payload (opt-in) | `AES-256-CBC` + `HMAC-SHA256` (Encrypt-then-MAC) | [AesHelper.cs](../../CanvasApp.Common/Utils/AesHelper.cs), [MessageCrypto.cs](../../CanvasApp.Common/Utils/MessageCrypto.cs) |
| So sánh MAC / token | `FixedTimeEquals` (constant-time XOR) | [UserStore.cs:440](../../CanvasApp.AuthServer/UserStore.cs#L440), [AesHelper.cs:129](../../CanvasApp.Common/Utils/AesHelper.cs#L129) |
| Chống timing-side-channel login | `BCrypt.Verify(dummyHash)` khi user không tồn tại | [UserStore.cs:272](../../CanvasApp.AuthServer/UserStore.cs#L272) |

### 8.1. BCrypt — hash password & OTP

```csharp
// Lưu
string hash = BCrypt.Net.BCrypt.HashPassword(plaintext, workFactor: 12);
// Verify
bool ok = BCrypt.Net.BCrypt.Verify(plaintext, hash);
```

- **Adaptive hash** với salt sinh trong hash (lưu chung trong chuỗi `$2a$12$<salt><hash>`).
- `workFactor=12` (password user): ≈100ms/hash → brute-force 100M password mất ~115 ngày trên 1 GPU.
- `workFactor=10` (OTP code): nhanh hơn (~25ms) vì OTP entropy thấp (1M), expiry 5 phút, mỗi token chỉ verify được 5 lần — không cần 12.
- BCrypt **không reversible** → server bị hack DB, attacker không lấy được plaintext.
- Salt random per-hash → 2 user cùng password sẽ có hash khác nhau, không rainbow-table được.

### 8.2. HMAC-SHA256 — auth token (JWT-lite)

Format token gửi giữa Auth/Canvas/Client:
```
base64(payload) "." base64(HMAC-SHA256(payload, secret))
↑                ↑
└─ "userId:username:issuedAtUnix"
                 └─ 32 bytes
```

```csharp
// Issue
long issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var payload = Encoding.UTF8.GetBytes($"{userId}:{username}:{issuedAt}");
byte[] sig;
using (var hmac = new HMACSHA256(secret))
    sig = hmac.ComputeHash(payload);
string token = Convert.ToBase64String(payload) + "." + Convert.ToBase64String(sig);

// Verify
var expectedSig = HMAC-SHA256(payloadBytes, secret);
if (!FixedTimeEquals(providedSig, expectedSig)) return -1;
if (UtcNow - issuedAt > 24h) return -1;
```

- **Secret** lấy từ env `CANVASAPP_JWT_SECRET` (fallback hardcoded dev). Auth issue, Canvas verify — **share cùng secret**.
- TTL 24h (`TokenTtlSeconds`).
- `FixedTimeEquals` cần thiết: nếu so sánh bằng `==`/`SequenceEqual`, attacker có thể đoán dần từng byte signature qua timing (response time tăng dần khi đoán đúng byte đầu).
- Format **không phải JWT chuẩn** (không có header/JSON) — gọn nhẹ hơn nhưng cùng nguyên lý: payload public + MAC signature.

### 8.3. AES-256-CBC + HMAC-SHA256 — mã hoá payload (opt-in)

> Mặc định **TẮT** trên dây để demo dễ thấy JSON qua Wireshark. Bật bằng cách set 32-byte base64 key vào env `CANVASAPP_AES_KEY`.

**Output format** (`AesHelper.EncryptString`):
```
base64( IV (16B) ‖ ciphertext (n B) ‖ HMAC (32B) )
                                       └─ MAC trên (IV ‖ ciphertext)
```

**Encrypt-then-MAC** order (chuẩn industry):
```csharp
// 1. Sinh IV random
var iv = new byte[16];
new RNGCryptoServiceProvider().GetBytes(iv);

// 2. AES-256-CBC encrypt với PKCS7 padding
aes.Mode = CipherMode.CBC;
aes.Padding = PaddingMode.PKCS7;
aes.Key = key32; aes.IV = iv;
byte[] ct = aes.CreateEncryptor().Encrypt(plaintext);

// 3. MAC covers IV || CT (tamper với IV cũng bị detect)
byte[] mac = HMAC-SHA256(key32, iv ‖ ct);

// 4. Wire format
return base64(iv ‖ ct ‖ mac);
```

**Decrypt**: verify MAC **TRƯỚC**, chỉ decrypt nếu MAC pass → chặn padding-oracle attack.

```csharp
// PadOracle attack (cái tránh được):
// - Attacker gửi ciphertext sửa, server cố decrypt → PKCS7 padding sai → exception
// - Exception/timing leak việc padding hợp lệ hay không → attacker decrypt từng block
// Encrypt-then-MAC: MAC sai → reject NGAY, không chạy đến AES decrypt
if (!FixedTimeEquals(receivedMac, computedMac))
    throw new CryptographicException("HMAC verification failed");
// chỉ tới đây mới AES decrypt → padding oracle không tồn tại
```

**Envelope wire** (`MessageCrypto.EncryptInPlace`):
```json
{
  "type": "CHAT_MESSAGE",      ← plaintext (LB cần peek route)
  "token": "MTpu...",          ← plaintext (Server verify HMAC)
  "data": {
    "_enc": "base64(IV‖CT‖MAC)",
    "_v": 1                    ← version: cho phép format evolve
  }
}
```

- `type` + `token` luôn plaintext để LB peek route và verify ngay tại edge.
- `data` (payload nội dung) được mã hoá → ngay cả MitM bypass TLS cũng không đọc được nội dung tin nhắn/draw.

### 8.4. RNGCryptoServiceProvider — randomness

| Dùng cho | Lý do |
|---|---|
| OTP code 6 số | Predictable code = ai cũng đoán được → mất an toàn |
| AES IV (16 bytes) | Reuse IV trong CBC = leak plaintext relationship |
| BCrypt salt | (Tự sinh trong thư viện, không cần code thêm) |

> **Không bao giờ** dùng `System.Random` cho mục đích bảo mật — seed mặc định là `Environment.TickCount` → predictable trong vòng vài ms.

### 8.5. FixedTimeEquals — constant-time compare

```csharp
private static bool FixedTimeEquals(byte[] a, byte[] b)
{
    if (a == null || b == null || a.Length != b.Length) return false;
    int diff = 0;
    for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];   // XOR tất cả byte
    return diff == 0;
}
```

- `==`, `SequenceEqual`, `memcmp` — đều **return early** khi gặp byte khác đầu tiên → timing leak.
- XOR tất cả byte (không break sớm) → thời gian compare **không phụ thuộc** content → attacker đoán byte không được.
- .NET 5+ có sẵn `CryptographicOperations.FixedTimeEquals`. .NET Framework 4.8 phải tự viết.

### 8.6. Timing-side-channel ở login

```csharp
// Pre-computed BCrypt hash ngay khi class load
private static readonly string _dummyBcryptHash =
    BCrypt.Net.BCrypt.HashPassword("dummy-timing-equaliser", 12);

// Khi login:
if (!reader.Read())  // username KHÔNG tồn tại
{
    // ❌ Không có dòng này: return ngay → response time = ~5ms
    //   → attacker biết username không tồn tại (user enumeration)
    // ✅ Có dòng này: response time = ~100ms (= BCrypt time với password thật)
    BCrypt.Net.BCrypt.Verify(password, _dummyBcryptHash);
    return "Sai tài khoản hoặc mật khẩu";
}
```

Đảm bảo "username sai" và "password sai" trả về cùng thời gian → attacker không enumerate user database được qua timing.

### Tóm tắt threat model

| Threat | Defense |
|---|---|
| DB dump → đọc password | BCrypt(workFactor=12) — không reverse, work factor cao |
| Sniff token forge user khác | HMAC-SHA256 signature, secret share Auth↔Canvas |
| Replay expired token | Embed `issuedAt` trong payload, check TTL 24h |
| Timing leak token signature | `FixedTimeEquals` constant-time |
| User enumeration qua login timing | Dummy BCrypt.Verify ở nhánh user-not-found |
| MitM đọc payload (giả sử AES bật) | AES-256-CBC + HMAC-SHA256 Encrypt-then-MAC |
| Padding oracle attack | Encrypt-then-MAC: verify MAC trước decrypt |
| Tamper với IV / ciphertext | MAC bao luôn IV‖CT |
| IV reuse trong CBC | Random IV mỗi lần encrypt (CSPRNG) |
| Predictable OTP/salt/IV | RNGCryptoServiceProvider (CSPRNG) |

---

## 📊 Tổng kết flow chính

### Login → vào room → chat
```
1. Client → LB → Auth: AUTH_LOGIN          → AUTH_LOGIN_RESULT (+token)
2. Client → LB → Canvas: ROOM_LIST          → ROOM_LIST_RESULT
3. Client → LB → Canvas: ROOM_RESOLVE       → ROOM_RESOLVE_RESULT (host+port)
4. Client → Canvas (direct): ROOM_JOIN      → ROOM_JOIN_RESULT (canvas+chat+members)
5. Client → Canvas: CHAT_MESSAGE            → broadcast tới all members
                                            ↓
                                            PEER_RELAY → peers → broadcast local
```

### Multi-server sync (xóa phòng)
```
1. OwnerClient → LB → Canvas A: ROOM_DELETE
2. Canvas A: TryDeleteRoomIfEmpty (atomic) → DB.SetActive(false)
3. Canvas A → all peers (B, C): PEER_ROOM_DELETE
4. Peers B, C: drop room khỏi _rooms + BroadcastLobbyRoomListAsync
5. LB sniff ROOM_DELETE_RESULT → UnregisterRoom (giảm RoomCount)
6. Mọi lobby client (qua polling 3s) → ROOM_LIST → thấy room đã biến mất
```

### Vẽ trong room nhiều server
```
1. ClientA (server 1) → DRAW_END (stroke)
2. Server 1: persist DB → broadcast to local room → PEER_RELAY to peers
3. Server 2 nhận PEER_RELAY → ApplyFromPeerAsync → broadcast local
   (self-loop guard: skip nếu OriginServerId == SelfServerId)
4. ClientB (server 2) thấy stroke
```

---

## 📖 Tham khảo

- Định nghĩa: [Message.cs](../../CanvasApp.Common/Models/Message.cs)
- Payload classes: [Models.cs](../../CanvasApp.Common/Models/Models.cs)
- LoadBalancer routing: [LoadBalancer.cs](../../CanvasApp.LoadBalancer/LoadBalancer.cs)
- Client-side handlers: [CanvasForm.cs](../../CanvasApp.Client/Forms/CanvasForm.cs)
- Server-side handlers: [Program.cs](../../CanvasApp.Server/Program.cs)
- Peer handlers: [PeerHandler.cs](../../CanvasApp.Server/PeerHandler.cs)

### OTP & Auth
- OTP business logic: [OtpService.cs](../../CanvasApp.AuthServer/Services/OtpService.cs)
- OTP DB persistence: [OtpStore.cs](../../CanvasApp.AuthServer/OtpStore.cs)
- SMTP sender: [SmtpEmailSender.cs](../../CanvasApp.AuthServer/Services/SmtpEmailSender.cs)
- User store + password hash + token: [UserStore.cs](../../CanvasApp.AuthServer/UserStore.cs)
- Auth handler: [AuthHandler.cs](../../CanvasApp.AuthServer/AuthHandler.cs)
- Token facade: [TokenService.cs](../../CanvasApp.AuthServer/Services/TokenService.cs)

### Cryptography
- AES-256-CBC + HMAC-SHA256: [AesHelper.cs](../../CanvasApp.Common/Utils/AesHelper.cs)
- Payload encrypt envelope: [MessageCrypto.cs](../../CanvasApp.Common/Utils/MessageCrypto.cs)
- HMAC token issue/verify: [UserStore.cs:398-446](../../CanvasApp.AuthServer/UserStore.cs#L398-L446)
