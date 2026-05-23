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
