# App Logic + Socket Logic (CanvasApp)

Tài liệu này giải thích chi tiết luồng xử lý App Logic + Socket Logic trong CanvasApp, từ khi client mở kết nối TCP đến khi thông điệp được xử lý, lưu vào DB, và broadcast đến các client khác. Tập trung vào các thành phần chính ở Server, Client, và Load Balancer.

---

## 1. Tổng quan kiến trúc kết nối

CanvasApp sử dụng TCP thuần, không dùng SignalR/WebSocket. Mỗi thông điệp được gửi theo định dạng JSON + newline (line-delimited JSON). Mỗi dòng JSON là 1 thông điệp hoàn chỉnh. Đọc bằng StreamReader.ReadLineAsync() và ghi bằng StreamWriter.WriteLine().

**Ba lớp kết nối chính:**

* **Load Balancer (LB)**: nhận kết nối từ client, peek message đầu tiên để route đến Auth pool hoặc Canvas pool.
* **Canvas Server**: xử lý toàn bộ luồng vẽ, chat, room, auto-save, broadcast.
* **Client (WinForms)**: mở TCP kết nối đến LB/Canvas, send/receive message, vẽ UI, và reconnection.

---

## 2. Socket Logic trên Server (CanvasApp.Server)

### 2.1. TCP Listener và accept loop

Canvas Server mở TcpListener trên port (mặc định 9002) và chấp nhận client liên tục.

* Mỗi client tạo ra 1 Task xử lý riêng (HandleClient).
* Mỗi kết nối TCP có 1 StreamReader/StreamWriter riêng.

**Lý do:** 1 task/client giúp không block trong khi một client bị trễ, và tạo concurrency cho nhiều user.

### 2.2. HandleClient: vòng đọc line và dispatcher

Server đọc từng dòng JSON, parse thành Message, sau đó dispatcher vào ProcessAsync.

Quản lý kết nối:

* Chỉ log connected khi đã nhận được 1 dòng data (tránh log health-check connect/close).
* Khi ReadLineAsync() trả về null hoặc exception, server gọi Leave() và broadcast danh sách thành viên mới.

### 2.3. ProcessAsync: auth + switch case

ProcessAsync là trung tâm xử lý App Logic. Trình tự:

1. **Kiểm tra payload size** (anti-abuse, ~4MB cap).
2. **Verify token HMAC-SHA256** (trừ PING). Token format `base64(payload).base64(HMAC(payload, secret))`. So sánh MAC bằng `FixedTimeEquals` để không leak timing.
3. **Switch msg.Type** xử lý từng loại message.

Các nhóm chính:

* **ROOM_***: list, create, resolve (routing-only pre-join), join, leave, delete, update password.
* **DRAW_***: start, move, end, shape, text, fill, undo, clear, image, image_transform.
* **CHAT_***: message, file. (CHAT_HISTORY giờ embedded trong ROOM_JOIN_RESULT).
* **CANVAS_STATE**: re-sync full snapshot + deltas on demand.
* **PING/PONG**: keepalive.

Mọi event sau khi xử lý local còn được `PEER_RELAY` sang các canvas server khác để đồng bộ multi-server.

### 2.4. RoomManager: logic domain

RoomManager chịu trách nhiệm:

* Quản lý danh sách phòng (_rooms).
* Danh sách user trong phòng (_roomClients).
* Trạng thái canvas theo room (_canvasState).
* Undo stack theo user/room.
* Snapshot cache và load from DB khi join.

Nhóm chức năng quan trọng:

* **CreateRoom**: tạo room, lưu DB, tạo invite code.
* **Join**: kiểm tra password, load snapshot, add vào room, cập nhật member list.
* **Leave**: remove client, đánh dấu room empty, thông báo lobby.
* **BroadcastAsync**: gửi message đến tất cả client trong phòng (trừ sender nếu cần).
* **RecordDrawAction**: gắn SeqNo, ActionId, enqueue vào persistence.
* **AutoSave**: định kỳ gom snapshot lưu DB.

---

## 3. Socket Logic trên Client (CanvasApp.Client)

### 3.1. LobbyClient: socket ngắn hạn

LobbyClient mở socket đến LB cho các request 1 lần, sau đó đóng ngay.

* Dùng cho ROOM_LIST, ROOM_CREATE, ROOM_RESOLVE, RESOLVE_INVITE_CODE, ROOM_DELETE, ROOM_UPDATE_PASSWORD.
* Mục tiêu: LB peek message **đầu tiên** trên mỗi socket để route. Nếu dùng 1 socket cho nhiều request thì chỉ message đầu được route đúng, các message sau ride pipe sai.
* `ROOM_RESOLVE` chỉ trả về `ServerHost`/`ServerPort` (không admit user) → sau khi đóng socket LB, client trực tiếp connect tới canvas server đúng và gửi `ROOM_JOIN` **một lần duy nhất** trên socket persistent.

### 3.2. CanvasClient: socket persistent

CanvasClient duy trì 1 kết nối dài hạn đến Canvas Server để:

* Nhận realtime draw/chat/broadcast.
* Gửi DRAW_* và CHAT_*.
* PING/PONG keepalive.
* Tự động reconnect nếu bị drop.

Các điểm kỹ thuật:

* **Send lock**: serialize StreamWriter.WriteLineAsync để tránh interleaving JSON.
* **Heartbeat**: PING mỗi 10s, server trả PONG.
* **Reconnect loop**: exponential backoff, tối đa 10 lần.
* **Connection generation**: tránh trigger OnDisconnected khi có switch server (LB -> Canvas).

### 3.3. Threading flow (server)

```mermaid
flowchart LR
	A[Accept loop] --> B[Task per client]
	B --> C[ProcessAsync]
	C --> D[RoomManager]
	D --> E[BroadcastAsync]
```

---

## 4. Luồng vẽ nét (DRAW_*) end-to-end

Đây là luồng "1 nét vẽ" từ client A sang client B:

1. **Client A** vẽ trên CanvasForm -> tạo DrawAction.
2. **CanvasClient.SendDrawAsync** gửi Message(DRAW_END, action).
3. **LB** nhận message, nhận diện Type = DRAW_END -> route vào Canvas Server.
4. **Canvas Server** ProcessAsync nhận DRAW_END.
5. **RoomManager.RecordDrawAction** gắn SeqNo, ActionId, enqueue DB.
6. **BroadcastAsync** gửi cho tất cả client trong phòng (trừ sender).
7. **Client B** nhận trong ReceiveLoop -> UI vẽ lại nét.

Dấu hiệu log cho demo:

* Server log [Room] ... joined và [AutoSave] Snapshot ....
* Client B vẽ nét ngay sau khi client A vẽ.

---

## 5. Luồng tạo phòng và join phòng

### 5.1. Tạo phòng (ROOM_CREATE)

1. Client gọi LobbyClient.CreateRoomAsync.
2. Socket ngắn hạn đến LB, LB route vào Canvas Server.
3. Server tạo room, lưu DB, log [Room] Created ....
4. Client nhận ROOM_CREATE_RESULT và đóng socket.

### 5.2. Join phòng (ROOM_RESOLVE → connect → ROOM_JOIN)

1. Client đã có token.
2. Client gọi LobbyClient.ResolveRoomAsync (socket ngắn hạn qua LB).
3. LB route sticky theo RoomId → canvas server đúng. Server check password (under `lock(room)`), trả `ROOM_RESOLVE_RESULT { ServerHost, ServerPort, RequiresPassword }`. **KHÔNG** add user vào `_roomClients`.
4. LB socket đóng.
5. Client mở socket persistent đến `ServerHost:ServerPort` (CanvasClient.ConnectToServerAsync).
6. Client gửi `ROOM_JOIN` **một lần** trên socket persistent.
7. Server fetch chat history snapshot **trước** khi admit (tránh race history vs live), add member, load canvas state, trả `ROOM_JOIN_RESULT { Room, Members, SnapshotData, CanvasState, ChatHistory }`.
8. Server broadcast `ROOM_UPDATE` cho các user khác, publish `PEER_RELAY(ROOM_UPDATE)` sang các peer.

---

## 6. Socket Logic và Load Balancer

LB đọc thông điệp đầu tiên để route:

* **AUTH_*** (LOGIN/REGISTER/SEND_OTP/FORGOT_SEND_OTP/RESET_PASSWORD) -> Auth pool, round-robin.
* **ROOM_JOIN**, **ROOM_RESOLVE**, **ROOM_JOIN_BY_CODE (w/ RoomId)** -> Canvas pool, sticky route theo RoomId (`RouteForRoom`). Bind vào table nếu chưa có.
* **ROOM_DELETE**, **ROOM_UPDATE_PASSWORD** -> Canvas pool, `RouteForRoomReadOnly` (sticky nếu đã bind, nếu không thì least-loaded; **không** tạo binding mới — metadata operation).
* **default** (ROOM_LIST, RESOLVE_INVITE_CODE, DRAW_*, CHAT_*) -> Canvas pool, least-loaded.

LB còn intercept `ROOM_CREATE_RESULT` để pre-register binding cho server vừa tạo room, và intercept `ROOM_DELETE_RESULT` (Success=true) để `UnregisterRoom` (giảm RoomCount).

---

## 7. Lưu ý

* **Line-delimited JSON**: mỗi message là 1 dòng. Interleaving bytes sẽ làm JSON bị lỗi.
* **StreamWriter không thread-safe**: server và client đều dùng lock để serialize send.
* **Token check**: mỗi message (trừ PING) đều verify token.
* **Reconnect**: client tự động reconnect khi server down, giúp demo không bị crash.
* **AutoSave + Snapshot**: tránh load full action list, chỉ load snapshot + delta.

---

## 8. Mapping log thông thường

* [+] Client ... connected -> server nhận dòng data đầu tiên.
* [Room] Created ... -> ROOM_CREATE.
* [Room] X joined ... -> ROOM_JOIN.
* [Room] X left ... -> disconnect/ROOM_LEAVE.
* [AutoSave] Snapshot ... -> auto save timer.
* Unable to read data ... aborted -> client đóng socket đột ngột.

---

## 9. File


### 9.1. Protocol chung (định dạng wire)

* `Message` envelope (Type + Data + Token) + danh sách `MessageType.*`: [CanvasApp.Common/Models/Message.cs](../../CanvasApp.Common/Models/Message.cs)
  → mọi component (Client, LB, Canvas Server, Auth Server, Peer) đều serialize/deserialize qua class này, nên đây là "schema" thống nhất của toàn bộ giao tiếp.

### 9.2. Client → LB (outbound từ Client)

* **Auth ngắn hạn** (LOGIN / REGISTER / SEND_OTP / FORGOT / RESET): [CanvasApp.Client/Network/AuthClient.cs](../../CanvasApp.Client/Network/AuthClient.cs)
  → mỗi method mở 1 TCP mới đến `Session.LB_HOST:LB_PORT`, gửi 1 Message (mã hóa qua `MessageCrypto`), đọc 1 dòng response, đóng socket.
* **Lobby ngắn hạn** (ROOM_LIST / ROOM_CREATE / ROOM_RESOLVE / RESOLVE_INVITE_CODE / ROOM_DELETE / ROOM_UPDATE_PASSWORD): [CanvasApp.Client/Network/LobbyClient.cs:73-115](../../CanvasApp.Client/Network/LobbyClient.cs#L73-L115)
  → `QueryAsync<TResult>`: open → write 1 request → đọc đến khi thấy `terminalType` → close.
* **Canvas persistent** (DRAW_* / CHAT_* / ROOM_JOIN / PING…): [CanvasApp.Client/Network/CanvasClient.cs:83-141](../../CanvasApp.Client/Network/CanvasClient.cs#L83-L141)
  → `TryConnectAsync` mở socket dài hạn tới `_targetHost:_targetPort`; `SendAsync` serialize qua `_sendLock` để không interleave JSON.

### 9.3. LB peek + route + proxy (Load Balancer)

* Accept + per-connection state: [CanvasApp.LoadBalancer/LoadBalancer.cs:242-260](../../CanvasApp.LoadBalancer/LoadBalancer.cs#L242-L260)
* **Peek dòng JSON đầu** và **switch theo `msg.Type` để chọn pool**: [CanvasApp.LoadBalancer/LoadBalancer.cs:254-389](../../CanvasApp.LoadBalancer/LoadBalancer.cs#L254-L389)
  → đây là chỗ "router" — AUTH_* → Auth pool round-robin; ROOM_JOIN/ROOM_RESOLVE/ROOM_JOIN_BY_CODE → `RouteForRoom` sticky theo RoomId; ROOM_DELETE/ROOM_UPDATE_PASSWORD → `RouteForRoomReadOnly`; mặc định → least-loaded canvas.
* **Sticky table** (roomId → Canvas server): [CanvasApp.LoadBalancer/LoadBalancer.cs:136-238](../../CanvasApp.LoadBalancer/LoadBalancer.cs#L136-L238)
* **Forward dòng đầu rồi proxy 2 chiều** (`PumpAsync` c↔s): [CanvasApp.LoadBalancer/LoadBalancer.cs:419-502](../../CanvasApp.LoadBalancer/LoadBalancer.cs#L419-L502)
* **Sniff response để cập nhật routing table** (ROOM_CREATE_RESULT → claim, ROOM_DELETE_RESULT → unbind): [CanvasApp.LoadBalancer/LoadBalancer.cs:429-460](../../CanvasApp.LoadBalancer/LoadBalancer.cs#L429-L460) + [SniffAndForwardAsync:513-573](../../CanvasApp.LoadBalancer/LoadBalancer.cs#L513-L573)

### 9.4. Canvas Server nhận message từ Client

* Accept loop chính + listener client port: [CanvasApp.Server/Program.cs:171-182](../../CanvasApp.Server/Program.cs#L171-L182)
* **HandleClient** — vòng đọc `ReadLineAsync` + cleanup `Leave + ROOM_UPDATE broadcast + PEER publish` khi disconnect: [CanvasApp.Server/Program.cs:342-409](../../CanvasApp.Server/Program.cs#L342-L409)
* **ProcessAsync** — dispatcher trung tâm, switch theo `msg.Type` và gọi `BroadcastAsync` + `PublishToPeersAsync` cho từng nhóm message (ROOM_*, DRAW_*, CHAT_*, CURSOR_UPDATE, PING): [CanvasApp.Server/Program.cs:411-801](../../CanvasApp.Server/Program.cs#L411-L801)
* **HandleJoin** — thứ tự bắt buộc *ROOM_JOIN_RESULT → PEER_MEMBER_SYNC → ROOM_UPDATE → ROOM_LIST_RESULT*: [CanvasApp.Server/Program.cs:804-839](../../CanvasApp.Server/Program.cs#L804-L839)

### 9.5. Canvas Server gửi message ra Client (broadcast in-room + lobby)

* **`ConnectedClient.SendAsync`** — serialize WriteLine theo từng client (`_sendLock`): [CanvasApp.Server/RoomManager.cs:29-39](../../CanvasApp.Server/RoomManager.cs#L29-L39)
* **`BroadcastAsync(roomId, msg, sender)`** — fanout cho mọi client trong room (option loại sender): [CanvasApp.Server/RoomManager.cs:813-823](../../CanvasApp.Server/RoomManager.cs#L813-L823)
* **`BroadcastToLobbyAsync`** — gửi cho client đang ở lobby (chưa join room nào): [CanvasApp.Server/RoomManager.cs:138-147](../../CanvasApp.Server/RoomManager.cs#L138-L147)
* **`RecordDrawAction`** — gắn `SeqNo` + `ActionId` và enqueue persistence trước khi server gọi BroadcastAsync: [CanvasApp.Server/RoomManager.cs:899-916](../../CanvasApp.Server/RoomManager.cs#L899-L916)

### 9.6. Canvas Server ↔ Peer Canvas Server (mesh đồng bộ multi-server)

* **Envelope `PEER_RELAY` + `PEER_MEMBER_SYNC`** (publisher side): [CanvasApp.Server/Program.cs:187-211](../../CanvasApp.Server/Program.cs#L187-L211)
* **Peer mesh setup + listener cổng `port+100`**: [CanvasApp.Server/Program.cs:103-169](../../CanvasApp.Server/Program.cs#L103-L169)
* **`PeerManager.PublishAsync`** — fanout 1 message ra toàn bộ peer outbound connection: [CanvasApp.Server/PeerManager.cs:48-54](../../CanvasApp.Server/PeerManager.cs#L48-L54)
* **`PeerEndpoint.ConnectLoopAsync`** — outbound TCP persistent tới mỗi peer, gửi `PEER_HELLO` đầu tiên, heartbeat `PEER_PING`, auto-reconnect: [CanvasApp.Server/PeerManager.cs:75-201](../../CanvasApp.Server/PeerManager.cs#L75-L201)
* **`PeerHandler.HandleAsync`** — inbound: dispatch PEER_HELLO / PEER_RELAY / PEER_MEMBER_SYNC / PEER_ROOM_CREATE / PEER_ROOM_DELETE / PEER_ROOM_PASSWORD_UPDATED / PEER_CANVAS_SYNC / PEER_PING: [CanvasApp.Server/PeerHandler.cs:19-170](../../CanvasApp.Server/PeerHandler.cs#L19-L170)
* **`RoomManager.ApplyFromPeerAsync`** — apply inner message vào local state, rebuild member list, rồi `BroadcastAsync` xuống client local: [CanvasApp.Server/RoomManager.cs:1194-1273](../../CanvasApp.Server/RoomManager.cs#L1194-L1273)

### 9.7. Client nhận message từ Canvas Server

* **`ReceiveLoop`** — đọc từng dòng JSON, tách `RESOLVE_INVITE_CODE_RESULT` / `ROOM_JOIN_RESULT` / `PONG`, còn lại bắn ra `OnMessageReceived`: [CanvasApp.Client/Network/CanvasClient.cs:145-198](../../CanvasApp.Client/Network/CanvasClient.cs#L145-L198)
* **`HeartbeatLoop` PING 10s** + **`ReconnectLoop` exponential backoff + xin `CANVAS_STATE` sau reconnect**: [CanvasApp.Client/Network/CanvasClient.cs:202-254](../../CanvasApp.Client/Network/CanvasClient.cs#L202-L254)
* **Connection generation** (chặn `OnDisconnected` giả khi đổi server LB → Canvas): [CanvasApp.Client/Network/CanvasClient.cs:99-120](../../CanvasApp.Client/Network/CanvasClient.cs#L99-L120)

### 9.8. Auth Server nhận message (đầu kia của AuthClient)

* **`AuthHandler.HandleClientAsync`** — read line → `ProcessMessage` (switch theo Type → AuthService / UserService / OtpService) → write encrypted response: [CanvasApp.AuthServer/AuthHandler.cs:30-123](../../CanvasApp.AuthServer/AuthHandler.cs#L30-L123)


---
