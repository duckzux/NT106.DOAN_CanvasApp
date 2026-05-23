# CanvasApp — System Architecture

> **Last updated:** 2026-05-23
> **Stack:** C# / .NET Framework 4.7.2/4.8, WinForms (client), Newtonsoft.Json, MySQL (persistence), raw TCP sockets, HMAC-SHA256–signed auth tokens (custom JWT-like format), BCrypt (passwords + OTP), AES-256-CBC + HMAC-SHA256 (built but opt-in on the wire).
> **Topology:** Distributed multi-server canvas with Load Balancer routing-table for per-room stickiness, peer-mesh synchronization between Canvas Servers, short-lived lobby connections, and a routing-only `ROOM_RESOLVE` pre-join that eliminates the historical double-join cost.

---

## 1. High-Level Topology

```
                          ┌─────────────────┐
                          │  WinForms       │
                          │  Client(s)      │
                          └────────┬────────┘
                                   │ TCP (JSON line-delimited)
                                   │
              ┌────────────────────┼────────────────────┐
              │                    │                    │
              ▼ :9001              ▼ :9000              ▼ :9002/9003
       ┌─────────────┐      ┌─────────────┐      ┌─────────────────────┐
       │ AuthServer  │      │ LoadBalancer│      │ CanvasServer × N    │
       │ (JWT issue) │      │ (TCP proxy) │      │ (per-room state)    │
       └──────┬──────┘      └──────┬──────┘      └────────┬────────────┘
              │                    │                      │ peer mesh
              │                    │ forwards             │ :9102/9103
              │                    └──────────────────────┤
              │                                           │
              ▼                                           ▼
       ┌─────────────────────────────────────────────────────────┐
       │ MySQL (canvasapp)                                       │
       │ users, rooms, room_members, draw_actions,               │
       │ chat_messages, canvas_snapshots                         │
       └─────────────────────────────────────────────────────────┘
```

**Five executables, one shared library:**

| Project | Port | Role |
|---|---|---|
| `CanvasApp.AuthServer` | 9001 | JWT issuer, password hashing, user CRUD |
| `CanvasApp.LoadBalancer` | 9000 | Stateless TCP proxy, routes by message type + RoomId hash |
| `CanvasApp.Server` (Canvas) | 9002, 9003, … | Real-time room state, draw/chat broadcast, peer sync |
| `CanvasApp.Client` | — | WinForms UI: Login → Lobby → Canvas |
| `CanvasApp.Common` | — | Shared models, DAOs, JWT/JSON/AES/Snapshot helpers |

---

## 2. Projects in Detail

### 2.1 `CanvasApp.Common` (shared library)

The wire protocol and persistence contracts live here. **No I/O loops or UI** — only models, helpers, and DAOs.

```
Models/
  Message.cs             ← envelope: { type, data, token } + canonical `MessageType` constants
  Models.cs              ← Room, User, JoinRoomResult (carries ServerHost/Port + ChatHistory), ChatMessage, etc.
  DrawAction.cs          ← stroke / shape / fill / text / image action payload (ActionId, SeqNo, IsUndone)
  Room.cs, User.cs       ← entity records
  RoomRole.cs            ← OWNER / MEMBER
  Layer.cs, CursorInfo.cs← (reserved for future cursor-presence + layered canvas)
  MessageType.cs         ← legacy stub (empty, kept to avoid breaking external refs — see §13)
Utils/
  JwtHelper.cs           ← HS256 helper (signature, expiry, payload extract)
  JsonHelper.cs          ← Newtonsoft.Json wrapper with Camel/PascalCase settings
  AesHelper.cs           ← AES-256-CBC + HMAC-SHA256 (Encrypt-then-MAC, IV‖CT‖MAC base64)
  MessageCrypto.cs       ← Wrap/unwrap `Message.Data` into `{ _enc, _v: 1 }` envelope
  CryptoConfig.cs        ← Process-wide key + `EncryptionEnabled` toggle (env `CANVASAPP_AES_KEY`)
  SnapshotHelper.cs      ← GZip + Base64 compress/decompress for CanvasSnapshot
DataAccess/
  DatabaseManager.cs     ← MySQL connection factory (singleton). Stores connection string only;
                          schema bootstrap lives in `Database/schema.sql` and is run externally.
  UserDAO.cs             ← users table CRUD
  RoomDAO.cs             ← rooms table CRUD + invite code lookup
  RoomMemberDAO.cs       ← room_members table (join/leave history)
  DrawActionDAO.cs       ← draw_actions table (per-stroke persistence)
  ChatMessageDAO.cs      ← chat_messages table
  CanvasSnapshotDAO.cs   ← canvas_snapshots table (baseline + version)
  PersistenceQueue.cs    ← background write-queue (avoids blocking hot path)
```

### 2.2 `CanvasApp.AuthServer` (:9001)

Standalone TCP server. Accepts only `AUTH_LOGIN` / `AUTH_REGISTER`; returns JWT-signed token on success.

```
Program.cs              ← Main: bind port, accept loop, dispatch AuthHandler;
                          background task runs `OtpStore.DeleteExpired(24h)` every 30 min.
AuthServer.cs           ← TcpListener wrapper, per-connection task (binds IPAddress.Any)
AuthHandler.cs          ← one connection: read message, route to Services, write reply, close
UserStore.cs            ← MySQL-backed user store. Owns: BCrypt hashing (cost 12),
                          HMAC-SHA256 token IssueToken/VerifyToken, dummy-bcrypt timing
                          decoy, `_dummyBcryptHash`, `MigrateSchema` (adds UNIQUE email).
OtpStore.cs             ← email_otp_codes table CRUD + rate-limit counters
Services/
  AuthService.cs        ← orchestrator: login / register / send-OTP / reset-password
  TokenService.cs       ← thin facade over UserStore IssueToken/VerifyToken
  UserService.cs        ← validation (username≥3, password≥6, regex email ≤100 chars),
                          register/reset-password flow (OTP verify gates DB insert)
  OtpService.cs         ← OTP generation (6-digit, BCrypt-hashed at rest, TTL 5 min,
                          single-use). Rate limit: ≤1/min, ≤5/hour per email.
                          `MarkUsed(token)` separate from `Verify` so a DB hiccup
                          between verify and UpdatePassword doesn't burn the code.
  SmtpEmailSender.cs    ← System.Net.Mail; HTML body, configurable host/port via App.config
```

**Connection lifetime:** short-lived. Client opens TCP, sends one auth message, reads one reply, both sides close. No persistent connection to AuthServer.

### 2.3 `CanvasApp.LoadBalancer` (:9000)

TCP proxy with an in-memory **room routing table** for stickiness.

```
Program.cs              ← reads appsettings.json (server pools), starts LoadBalancer + HealthChecker
LoadBalancer.cs         ← accept loop; per-connection:
                          1) peek first JSON line (timeout 5s), strip UTF-8 BOM if present
                          2) route by Message.Type:
                             AUTH_*                          → Auth pool (round-robin)
                                                               (AUTH_LOGIN/REGISTER/SEND_OTP/
                                                                FORGOT_SEND_OTP/RESET_PASSWORD)
                             ROOM_JOIN                       → RouteForRoom(RoomId)
                             ROOM_RESOLVE                    → RouteForRoom(RoomId)  (sticky pre-join)
                             ROOM_DELETE                     → RouteForRoomReadOnly(RoomId)
                             ROOM_UPDATE_PASSWORD            → RouteForRoomReadOnly(RoomId)
                             ROOM_JOIN_BY_CODE w/ RoomId     → RouteForRoom(RoomId)
                             ROOM_JOIN_BY_CODE w/o RoomId    → PickCanvasLeastLoaded()
                             default (ROOM_LIST, etc.)       → PickCanvasLeastLoaded()
                          3) connect backend, forward peeked line
                          4) for ROOM_CREATE: intercept first response line, extract Room.Id
                             from ROOM_CREATE_RESULT, register table mapping immediately.
                             for ROOM_DELETE: intercept response, on Success=true call
                             UnregisterRoom(roomId) to drop mapping + decrement RoomCount.
                          5) bidirectional pump until either side closes
ServerInfo.cs           ← Host, Port, Type, MaxConnections, ActiveConnections, RoomCount,
                          IsHealthy. FailThreshold = 3 (3-strike DOWN).
HealthChecker.cs        ← periodic TCP probe every ~5s; marks DOWN on 3 consecutive fails,
                          back to UP on first success. Emits `[POOL]` snapshot ~15s.
```

**`RouteForRoom` vs `RouteForRoomReadOnly`:**
- `RouteForRoom` binds the room to the chosen server on a routing-table miss (so a future `ROOM_JOIN` is sticky). Used by `ROOM_JOIN`, `ROOM_RESOLVE`, `ROOM_JOIN_BY_CODE`.
- `RouteForRoomReadOnly` returns the bound server **without** creating a binding when none exists — instead falls back to least-loaded. Used by `ROOM_DELETE` and `ROOM_UPDATE_PASSWORD`, where binding shouldn't be created by the metadata operation itself.

**Why a table, not CRC32?** CRC32 hashing is sticky only while the canvas server *count* is constant. Removing a server (or rebooting the LB with a different config) re-keys every room — users mid-session would get steered to a different canvas. The routing table is explicit: a room stays bound to its server until that server is unhealthy, at which point the next join transparently rebinds.

**Why each lobby request is a fresh TCP**: the LB only peeks the first message on a connection. If the client sent `ROOM_LIST` then `ROOM_JOIN` on the same socket, the `ROOM_JOIN` would skip routing and just ride whatever pipe the LB opened for `ROOM_LIST` (least-loaded). The `LobbyClient` helper in the WinForms client opens a fresh TCP for each lobby query, so every message gets correctly routed as its own first message.

### 2.4 `CanvasApp.Server` (Canvas Server — :9002, :9003, …)

The stateful real-time layer. Multiple instances run in parallel; each owns a subset of rooms (routing decided by LB). Instances cross-publish via the **peer mesh** so that even though room state is sharded, broadcasts reach every client.

```
Program.cs              ← Main: bind client port + peer port (client+100),
                          start RoomManager (with `_serverId = "canvas-{port}"`),
                          PersistenceQueue, AutoSaveService, PeerManager + PeerHandler
                          listener, then per-client receive loop dispatches every
                          MessageType to handlers. On peer reconnect, publishes
                          PEER_CANVAS_SYNC for dirty rooms via OnPeerReconnected.
CanvasServer.cs         ← TCP listener wrapper + accept loop
ClientHandler.cs        ← extracted per-client reader → dispatcher hand-off helpers
RoomManager.cs          ← in-memory room state: rooms, members, draw actions, locks.
                          Methods: CreateRoom, Join, Leave, BroadcastAsync,
                          BroadcastToLobbyAsync, Resolve (routing-only, no admit),
                          TryDeleteRoomIfEmpty (atomic under `lock(list)`),
                          UpdateRoomPassword (atomic write of HasPassword+Hash),
                          EnsureCanvasLoaded (per-room load lock),
                          RecordDrawAction (assigns SeqNo + ActionId, enqueues to
                          PersistenceQueue), ApplyFromPeerAsync (self-loop guard),
                          ApplyPeerCanvasSync (HashSet dedupe by ActionId+SeqNo).
RoomState.cs            ← single-room container: List<DrawAction>, List<ConnectedClient>,
                          peer member cache, lastActivity timestamp
BroadcastService.cs     ← per-client send serialization (SemaphoreSlim) so concurrent
                          DRAW/CHAT writes can't interleave bytes mid-line.
AutoSaveService.cs      ← timer: every N min, snapshot dirty rooms via CanvasSnapshotDAO
HealthCheckService.cs   ← server-side liveness ping for the lobby (separate from LB probes)
PeerManager.cs          ← outbound peer connections: dial, TCP keepalive (idle 10s,
                          retry 5s × 3), heartbeat (PEER_PING/PONG every 15s),
                          exponential reconnect (500ms→30s), OnPeerReconnected event,
                          publishes PEER_ROOM_CREATE/DELETE/PASSWORD_UPDATED on changes.
PeerHandler.cs          ← inbound peer connections: read PEER_HELLO (self-loop guard via
                          ServerId match), PEER_RELAY, PEER_MEMBER_SYNC, PEER_CANVAS_SYNC,
                          PEER_ROOM_CREATE/DELETE/PASSWORD_UPDATED, PEER_PING; applies
                          via RoomManager.ApplyFromPeerAsync.
```

**Two ports per Canvas Server instance:**
- Client port (e.g. 9002) — clients connect here, dispatched by LB
- Peer port = client port + 100 (e.g. 9102) — sister servers connect here for peer relay

`_serverId = "canvas-{port}"` uniquely identifies each instance in peer envelopes.

### 2.5 `CanvasApp.Client` (WinForms)

```
Program.cs              ← App entry, starts LoginForm
Forms/
  LoginForm.cs          ← login form, calls AuthClient (short-lived TCP)
  RegisterForm.cs       ← register form (sends AUTH_SEND_OTP, then AUTH_REGISTER w/ OTP)
  ForgotPasswordForm.cs ← AUTH_FORGOT_SEND_OTP then AUTH_RESET_PASSWORD
  OtpVerifyForm.cs      ← shared OTP entry modal for both flows above
  LobbyForm.cs          ← room list (3-s polling timer + Shown/post-action refresh),
                          create room, owner Edit/Delete buttons, invite code join
  CreateRoom.cs         ← in-lobby modal: name / password / template / maxUsers
  RequirePassword.cs    ← in-lobby modal: prompt for room password before Resolve
  CanvasForm.cs         ← drawing surface, tools, chat panel, member list,
                          undo/redo, snapshot + delta apply, file attachment,
                          image-overlay (drag/scale via DRAW_IMAGE_TRANSFORM)
  CanvasForm.Images.cs  ← partial: image overlay layer (import via OpenFileDialog,
                          send DRAW_IMAGE, receive + render, transform handles)
  CanvasForm.Icons.cs   ← partial: icon bitmaps for toolbar
Controls/
  RoomCard.cs           ← list item; owner-only "Đổi mật khẩu" + "Xóa phòng" buttons
                          when Session.CurrentUser.Id == room.OwnerId
  UserListItem.cs       ← member row with role + avatar color
  GradientPanel.cs      ← background panel painter
  loginTextbox.cs       ← composite input with label + password toggle
Network/
  Session.cs            ← static state: CurrentUser, Token, LB_HOST/PORT, AUTH_HOST/PORT,
                          runtime CanvasHost/CanvasPort (set after ROOM_RESOLVE_RESULT)
  AuthClient.cs         ← short-lived TCP to AuthServer for login/register/send-OTP/
                          forgot-send-OTP/reset-password
  LobbyClient.cs        ← short-lived TCP to LB for ROOM_LIST / ROOM_CREATE /
                          ROOM_RESOLVE / RESOLVE_INVITE_CODE / ROOM_DELETE /
                          ROOM_UPDATE_PASSWORD. Each query opens its own socket so
                          the LB peeks that message type as the first line and routes
                          it correctly (avoids "first-message-pins-route" bug).
  CanvasClient.cs       ← persistent TCP singleton with ConnectToServerAsync(host, port),
                          send/receive loops, heartbeat (PING 10s),
                          exponential-backoff reconnect, generation counter
                          to suppress OnDisconnected during intentional switches.
                          Opened ONLY after ROOM_RESOLVE_RESULT returns
                          ServerHost/ServerPort. ROOM_JOIN is then sent ONCE on the
                          direct socket — Resolve doesn't admit the user, so no
                          duplicate join side-effect.
Drawing/
  StrokeCollector.cs    ← gathers raw points into normalized polyline
  ShapeSuggestionController.cs ← orchestrator: detect → render preview → commit on accept
  SuggestionOverlay.cs  ← floating UI for accept/reject of detected shape
  Shapes/               ← ShapeType enum, RecognizedShape record
  Recognition/          ← IShapeDetector + Line/Circle/Ellipse/Rectangle detectors,
                          StrokeAnalysis, RecognitionConfig, RecognitionResult, ShapeRecognizer
  Rendering/ShapeRenderer.cs ← turn RecognizedShape → DrawAction
```

---

## 3. Wire Protocol

**Transport:** raw TCP, newline-delimited JSON. One `Message` per line.

```json
{ "type": "ROOM_JOIN", "data": { "RoomId": "abc", "Password": "" }, "token": "eyJhbGc..." }
```

**Categories** (`MessageType` constants in [`Common/Models/Message.cs`](../CanvasApp.Common/Models/Message.cs) — see [Docs/explain/all_message.md](explain/all_message.md) for full payload examples):

| Category | Examples |
|---|---|
| Auth | `AUTH_LOGIN`, `AUTH_REGISTER`, `AUTH_SEND_OTP`, `AUTH_FORGOT_SEND_OTP`, `AUTH_RESET_PASSWORD`, `*_RESULT` |
| Room lifecycle | `ROOM_LIST`, `ROOM_CREATE`, `ROOM_RESOLVE`, `ROOM_JOIN`, `ROOM_LEAVE`, `ROOM_UPDATE`, `ROOM_DELETE`, `ROOM_UPDATE_PASSWORD`, `ROOM_JOIN_BY_CODE`, `RESOLVE_INVITE_CODE`, `*_RESULT` |
| Drawing | `DRAW_START`, `DRAW_MOVE`, `DRAW_END`, `DRAW_SHAPE`, `DRAW_FILL`, `DRAW_TEXT`, `DRAW_CLEAR`, `DRAW_UNDO`, `DRAW_IMAGE`, `DRAW_IMAGE_TRANSFORM` |
| Canvas sync | `CANVAS_STATE` (full re-sync on demand) |
| Chat | `CHAT_MESSAGE`, `CHAT_FILE` (`CHAT_HISTORY` deprecated — now embedded in `ROOM_JOIN_RESULT.ChatHistory`) |
| Liveness | `PING`, `PONG` |
| Errors | `ERROR` |
| Peer mesh | `PEER_HELLO`, `PEER_RELAY`, `PEER_MEMBER_SYNC`, `PEER_CANVAS_SYNC`, `PEER_ROOM_CREATE`, `PEER_ROOM_DELETE`, `PEER_ROOM_PASSWORD_UPDATED`, `PEER_PING`, `PEER_PONG` |

**Token format:** `base64(payload).base64(HMAC-SHA256(payload, secret))` where `payload = "userId:username:issuedAt"`. Secret loaded from env `CANVASAPP_JWT_SECRET` (falls back to a hardcoded dev secret). Tokens TTL 24h. Verified with `FixedTimeEquals` (constant-time MAC compare) on each Canvas Server message dispatch (see `ProcessAsync` in `Server/Program.cs`). Old unsigned tokens (pre-2026-05-22) are rejected outright.

---

## 4. Connect-on-Join Flow (current — single-join via `ROOM_RESOLVE`)

The lobby holds **no** persistent socket. Every lobby query is a fresh short-lived TCP to the LB, so the LB's first-message routing sees the right message type on every request. The persistent connection only opens after `ROOM_RESOLVE_RESULT` returns the canvas's `ServerHost/ServerPort`; `ROOM_JOIN` is then sent exactly once on that direct socket.

```
LoginForm
  └─ AuthClient.LoginAsync()        ← short-lived TCP → :9001 → stores HMAC-signed token
  └─ open LobbyForm

LobbyForm.Load + 3s polling timer
  └─ LobbyClient.GetRoomListAsync   ← short-lived TCP → LB → forwards to any healthy Canvas
                                       (LB peeks ROOM_LIST as first message → least-loaded)

User clicks Create Room
  └─ LobbyClient.CreateRoomAsync    ← short-lived TCP → LB → least-loaded Canvas
                                       LB sniffs ROOM_CREATE_RESULT → registers Room.Id → that Canvas
                                       Originating Canvas publishes PEER_ROOM_CREATE → peers add
                                       to their in-memory _rooms + invite-code map
  └─ LobbyClient.ResolveRoomAsync (auto-resolve the new room — sticky to same server)

User clicks Join Room X
  └─ LobbyClient.ResolveRoomAsync   ← short-lived TCP → LB (sticky route by RoomId X)
                                       Server verifies room exists + password matches.
                                       Returns ROOM_RESOLVE_RESULT{ ServerHost, ServerPort,
                                       RequiresPassword }. DOES NOT admit the user, DOES NOT
                                       broadcast — LB socket can close cleanly.
  └─ LB connection closes after the result

LobbyForm.HandleResolveResultAsync
  ├─ CanvasClient.ConnectToServerAsync(ServerHost, ServerPort)
  │   └─ persistent direct TCP to Canvas Server
  ├─ new CanvasForm()                ← constructor subscribes OnMessageReceived
  ├─ Show CanvasForm, Hide LobbyForm
  └─ CanvasClient.JoinRoomAsync(X)   ← single ROOM_JOIN on direct connection
                                       Server adds to _roomClients, replies with
                                       ROOM_JOIN_RESULT{ Room, SnapshotData, CanvasState,
                                       Members, ChatHistory } — atomic snapshot, no race
                                       between history fetch and live broadcast (see CHANGELOG
                                       2026-05-22 §3.5).

CanvasForm closes
  └─ ROOM_LEAVE → server Leave() → BroadcastAsync(ROOM_UPDATE) to other clients
  └─ LobbyForm.Shown event → RefreshRoomListAsync (fresh LB query)
```

**Why `ROOM_RESOLVE` instead of an LB-routed `ROOM_JOIN`?** The previous design sent `ROOM_JOIN` twice: once via the LB to discover `ServerHost/ServerPort`, then again on the direct socket to re-register on the new connection. The first join briefly admitted the user (broadcast `ROOM_UPDATE`, sent `CHAT_HISTORY`, fired `PEER_RELAY(ROOM_UPDATE)`), and the immediate disconnect undid it — every room entrance was visible to other clients as a phantom join → leave → join. `ROOM_RESOLVE` cleanly separates routing from admission: the server returns the address without touching `_roomClients`, so the LB socket can close silently and the single direct `ROOM_JOIN` produces exactly one set of side effects.

### Connection generation counter (anti-spurious-disconnect)

`CanvasClient` keeps an `int _connectionGen`. Every `Disconnect()` and `TryConnectAsync()` calls `Interlocked.Increment` on it. The `ReceiveLoop` captures `myGen` at start; in `finally` it only fires `OnDisconnected` if `myGen == Volatile.Read(ref _connectionGen)`. Otherwise (a newer connection has been established), the event is suppressed. Without this, every server switch would log the user out.

---

## 5. Routing — LoadBalancer Decisions

`LoadBalancer.HandleClientAsync()` peeks the first JSON line and decides:

```
type ∈ { AUTH_LOGIN, AUTH_REGISTER, AUTH_SEND_OTP,
         AUTH_FORGOT_SEND_OTP, AUTH_RESET_PASSWORD }     → PickAuth() (round-robin)
type == ROOM_JOIN  with non-empty RoomId                 → RouteForRoom(RoomId)
type == ROOM_RESOLVE                                     → RouteForRoom(RoomId)
type == ROOM_DELETE                                      → RouteForRoomReadOnly(RoomId)
type == ROOM_UPDATE_PASSWORD                             → RouteForRoomReadOnly(RoomId)
type == ROOM_JOIN_BY_CODE with non-empty RoomId          → RouteForRoom(RoomId)
type == ROOM_JOIN_BY_CODE without RoomId                 → PickCanvasLeastLoaded()
                                                            (client should call RESOLVE_INVITE_CODE first)
everything else (ROOM_LIST, RESOLVE_INVITE_CODE, …)      → PickCanvasLeastLoaded()
```

**`RouteForRoom(roomId)`** — explicit routing table (`ConcurrentDictionary<string, ServerInfo>`) protected by a slow-path `_routingLock` for the bind step:

```
if _roomRouting[roomId] exists and is healthy → return that server  (sticky)
else  (under _routingLock, with re-check)     → PickCanvasLeastLoaded()
                                                 store mapping, return picked server
```

**`RouteForRoomReadOnly(roomId)`** — same lookup, but **does not bind** on miss:

```
if _roomRouting[roomId] exists and is healthy → return that server
else                                          → PickCanvasLeastLoaded()
                                                 (mapping NOT stored)
```

Used by `ROOM_DELETE` and `ROOM_UPDATE_PASSWORD` so a metadata mutation can't accidentally claim a room for a server that doesn't actually host it.

If the previously-bound server is unhealthy, the mapping is dropped so the next join rebinds. Two pre-registration paths:
- `ROOM_CREATE_RESULT` sniff — LB extracts `Room.Id` from the reply and **registers** the mapping for the creating server, so the very first remote join lands on the same server (no race window).
- `ROOM_DELETE_RESULT` sniff — on `Success=true`, LB calls `UnregisterRoom(roomId)` to drop the mapping and decrement `RoomCount` on the bound server. Without this, deleted rooms stayed mapped and least-loaded picks skewed over time.

**`PickCanvasLeastLoaded`** sorts healthy servers by `RoomCount`, breaks ties by `ActiveConnections`, then round-robin.

**UTF-8 BOM tolerance:** `ReadOneLineAsync` strips a leading BOM before parsing. The .NET `StreamWriter` default settings emit a BOM on the first write, which used to break the JSON parser on the LB side.

---

## 6. Peer Mesh Synchronization

Every Canvas Server connects to every other Canvas Server over its **peer port** (client port + 100). The mesh exchanges:

| Wire message | Direction | Purpose |
|---|---|---|
| `PEER_HELLO` | on connect | declare `ServerId = "canvas-{port}"`; exchange known rooms + local member lists. Receiver drops connection if `ServerId == SelfServerId` (self-loop guard). |
| `PEER_RELAY` | streaming | rebroadcast any local room event (`ROOM_UPDATE`, `DRAW_*`, `CHAT_*`) wrapped in `{ RoomId, OriginServerId, Inner }`. Receiver applies and does **not** re-publish (cycle break). |
| `PEER_MEMBER_SYNC` | on member change | publish updated local-only member list; recipient merges and re-broadcasts to local room clients |
| `PEER_CANVAS_SYNC` | on peer reconnect | replay committed `DRAW_END`/`DRAW_SHAPE`/`DRAW_TEXT`/`DRAW_IMAGE` actions for active rooms; recipient dedupes by `ActionId` (primary) + `SeqNo` (fallback for legacy rows) under `lock(state)`. |
| `PEER_ROOM_CREATE` | on local `ROOM_CREATE` | announce a new room so peers add it to their `_rooms` + invite-code map without waiting for a DB reload |
| `PEER_ROOM_DELETE` | on owner delete | drop the room from `_rooms` and re-broadcast room list to lobby clients on every peer (previously deleted rooms lingered in remote lobbies until restart) |
| `PEER_ROOM_PASSWORD_UPDATED` | on owner password change | propagate new `Room.PasswordHash`; applied under the same `lock(room)` that `Resolve`/`Join` take when reading it (so subsequent verifies use the new hash on every server) |
| `PEER_PING` / `PEER_PONG` | every 15s | application-level heartbeat; on timeout, close socket and reconnect |

**Reliability layers (since 2026-05-20):**
1. Windows TCP keepalive (idle 10s, retry 5s × 3 = ~25s detection)
2. `PEER_PING` every 15s, PONG expected within 30s
3. Reconnect with exponential backoff (500ms → 30s cap)
4. On reconnect, `OnPeerReconnected` fires → `Program.cs` re-publishes canvas state for every dirty room via `PEER_CANVAS_SYNC`

**Self-loop guard (2026-05-22).** Two Canvas servers sharing the same `Config/appsettings.json` peer list could each try to dial their own peer port, causing every event to be applied locally twice (chat doubled, joined-room notifications doubled). The guard runs in two places: `PeerHandler` drops inbound peers whose `PEER_HELLO.ServerId` matches `SelfServerId`, and `ApplyFromPeerAsync` drops envelopes whose `OriginServerId` matches `SelfServerId`.

**Member-list merge:** `RoomManager.GetMembers(roomId)` returns local members ∪ peer-cached members. `ApplyFromPeerAsync` intercepts `PEER_RELAY(ROOM_UPDATE)`, rebuilds the member list from the merged view, preserves the original join/leave-username notification, and broadcasts to local clients.

---

## 7. Drawing Pipeline

```
Mouse events on CanvasForm.canvasPanel
  └─ StrokeCollector accumulates points
  └─ on DRAW_START/MOVE/END:
      ├─ local draw on Bitmap via Graphics
      └─ CanvasClient.SendDrawAsync(DRAW_*, action)
                       │
                       ▼
                  Canvas Server (ProcessAsync dispatch)
                  └─ RoomManager.RecordDrawAction (assigns SeqNo + ActionId)
                  └─ PersistenceQueue (async DB write — batched)
                  └─ BroadcastAsync to other local clients (chat broadcasts also include sender)
                  └─ PeerManager.PublishAsync(PEER_RELAY(DRAW_*)) to other servers
                                      │
                                      ▼
                              Peer Canvas Server
                              └─ ApplyFromPeerAsync (self-loop guard) re-broadcasts to its local clients
```

**Live vs. committed:** `DRAW_START` / `DRAW_MOVE` are **live signals only** — broadcast but not persisted. `DRAW_END`, `DRAW_SHAPE`, `DRAW_TEXT`, `DRAW_FILL`, `DRAW_IMAGE`, `DRAW_IMAGE_TRANSFORM` go through `RecordDrawAction` and are persisted to `draw_actions`.

**Image overlay (2026-05-22).** `DRAW_IMAGE` places a bitmap on the canvas: payload `{ type="image", ActionId, Points=[topLeft, bottomRight], ImageData=base64 }`. After placement, drag/scale handles emit `DRAW_IMAGE_TRANSFORM` with the same `ActionId` and updated `Points` (no `ImageData`) — the server mutates the existing action in place rather than appending a new one. Client renders via the `CanvasForm.Images.cs` partial.

**Shape recognition (client-only post-stroke):** when a stroke ends, `ShapeSuggestionController` runs the polyline through `ShapeRecognizer` (Line/Circle/Ellipse/Rectangle detectors). On a confident match, `SuggestionOverlay` floats above the stroke offering Accept/Reject; on Accept, the raw stroke is replaced with a clean `DRAW_SHAPE` action and re-published.

**Undo:** server tracks `IsUndone` per `DrawAction.ActionId`. `DRAW_UNDO` flips the flag and broadcasts. Each client filters undone actions out of `_history` on redraw.

**Snapshot:** `AutoSaveService` periodically GZip-compresses each dirty room's `DrawAction` list (after a high-water `SeqNo`) and writes a `CanvasSnapshot`. On `ROOM_JOIN_RESULT`, the server attaches `SnapshotData` (base64) + delta actions; client decompresses baseline, then layers deltas. Keeps cold-join payload bounded. The atomic snapshot now also includes `ChatHistory` so a joiner can't see a chat line twice (live + history overlap).

---

## 8. Persistence

MySQL schema (defined in [`Database/schema.sql`](../Database/schema.sql); applied externally via phpMyAdmin / MySQL CLI — `DatabaseManager` only opens connections, it does **not** bootstrap tables. `UserStore.MigrateSchema` runs lightweight idempotent ALTERs at AuthServer startup, e.g. adding `UNIQUE KEY uk_users_email`):

| Table | Owner | Notes |
|---|---|---|
| `users` | AuthServer | id, username, password_hash (BCrypt cost 12), email (UNIQUE), avatar_color, created_at, last_login_at |
| `email_otp_codes` | AuthServer | id, email, token, code_hash (BCrypt cost 10), purpose, created_at, expires_at, used_at. Cleaned every 30 min by background task. |
| `rooms` | Canvas | id, name, owner_id, has_password, password_hash (BCrypt cost 10), template, max_users, invite_code, is_active, created_at |
| `room_members` | Canvas | room_id, user_id, role, joined_at, left_at (history) |
| `draw_actions` | Canvas | room_id, seq_no, action_id, type, payload (JSON), user_id, is_undone, timestamp |
| `chat_messages` | Canvas | room_id, user_id, username, text, file_name, file_data (LONGBLOB), file_size, timestamp |
| `canvas_snapshots` | Canvas | room_id, version, snapshot_data (LONGTEXT GZip+base64), action_seq_at, byte_size, created_at |

**Write path:** `PersistenceQueue` is a single background consumer over a `BlockingCollection<Action>`. Hot-path message handlers enqueue `() => dao.Insert(...)` and return immediately; the queue drains in order, isolating socket loops from DB latency.

**Soft-delete:** `ROOM_DELETE` sets `rooms.is_active = FALSE`; the row is preserved so `room_members` / `chat_messages` / `canvas_snapshots` foreign-key references stay valid for audit. `LoadActiveRooms` filters on `is_active = TRUE` at startup.

---

## 9. Security Model

- **Authentication:**
  - AuthServer issues custom HMAC-signed tokens `base64(payload).base64(HMAC-SHA256(payload, secret))` where `payload = "userId:username:issuedAt"`. TTL 24h, enforced by `UserStore.VerifyToken`.
  - HMAC secret loaded from env `CANVASAPP_JWT_SECRET`; falls back to a labelled hardcoded dev secret. Production deployments must override.
  - `FixedTimeEquals` (constant-time MAC compare) defends against MAC timing attacks.
  - Constant-time login: `UserStore.Login` runs a dummy `BCrypt.Verify(password, _dummyBcryptHash)` when the username SELECT returns 0 rows, so attackers can't enumerate users by response time.
  - Token validated by `ProcessAsync` in `Server/Program.cs` on **every** Canvas message except `PING`.

- **OTP (email):** Generated random 6 digits, BCrypt-hashed (cost 10) at rest, TTL 5 min, single-use. Rate limit: ≤1/email/minute, ≤5/email/hour. The forgot-password flow only marks the OTP used **after** `UpdatePassword` succeeds (so a DB hiccup mid-flow doesn't burn the code).

- **Authorization:** room-level only (member of room ⇒ may draw/chat); no per-action ACL. Room password is BCrypt-hashed (cost 10). Owner-only actions (delete, password change) gated by `Session.CurrentUser.Id == room.OwnerId`.

- **Transport:** plain TCP. No TLS. (Acceptable for LAN demo; production would terminate TLS at the LB.) AES-256-CBC + HMAC-SHA256 (Encrypt-then-MAC) module exists in `Common/Utils/AesHelper.cs` + `MessageCrypto.cs` and is testable, but **not wired into the live wire protocol** by default — to enable, uncomment the `EncryptInPlace`/`DecryptInPlace` calls in `AuthClient.cs`, `LobbyClient.cs`, `CanvasClient.cs`, `AuthHandler.cs`, and `Server/Program.cs`. Only `Message.Data` is encrypted; `type` + `token` remain plaintext so the LB can route and AuthServer can verify the token.

- **Input validation:** server-side `MaxPayloadBytes ≈ 4 MB`, `MaxRoomNameLength = 100`, email regex `^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$` (≤100 chars), password ≥6, username ≥3 (trimmed). Oversized or malformed messages drop the connection.

---

## 10. Configuration

| Component | Config | Keys |
|---|---|---|
| AuthServer | `App.config` + env | `ConnectionStrings.CanvasDb`, `AppSettings.AuthPort`, `AppSettings.Smtp*`. Env: `CANVASAPP_JWT_SECRET` (HMAC secret). |
| LoadBalancer | `appsettings.json` | `ListenPort`, `HealthCheckIntervalSeconds`, `AuthServers[]`, `CanvasServers[]` (each with Host/Port/MaxConnections) |
| Canvas Server | `Config/appsettings.json` + CLI args | CLI `args[0]` = client port (e.g. 9003) overrides config; CLI `args[1]` = comma-sep peers ("host:peerPort"); config keys: `ServerPort`, `Peers[]`, `AutoSave.IntervalMinutes`, `Snapshot.RetentionVersions` |
| Client | `Session.cs` constants | `LB_HOST`, `LB_PORT`, `AUTH_HOST`, `AUTH_PORT` (only LB & Auth are compile-time; Canvas is runtime via LB redirect). Env: `CANVASAPP_AES_KEY` (optional Base64 key for AES module). |

**Running multiple Canvas instances locally:**
```
CanvasApp.Server.exe 9002 127.0.0.1:9103
CanvasApp.Server.exe 9003 127.0.0.1:9102
CanvasApp.LoadBalancer.exe   # picks up 9002 + 9003 from appsettings.json
CanvasApp.AuthServer.exe
```

---

## 11. Lifecycle Diagrams

### Sign-up + first join (current `ROOM_RESOLVE` flow)

```
Client       AuthServer      LoadBalancer      Canvas 9002      Canvas 9003
  │              │                 │                │                │
  │── SEND_OTP ─▶│  (BCrypts the 6-digit, emails it, returns OtpToken)
  │◀── ok ──────│                 │                │                │
  │── REGISTER(otp) ▶│            │                │                │
  │◀── token ───│  (HMAC-signed, TTL 24h)         │                │
  │              │                 │                │                │
  │── ROOM_LIST ───────────────────▶│ (least-loaded)│                │
  │              │                 │── forward ────▶│                │
  │◀── list ─────────────────────────────────────────│               │
  │              │                 │                │                │
  │── ROOM_RESOLVE(R) ─────────────▶│ (table[R] → 9003, sticky)
  │              │                 │── forward ──────────────────────▶│
  │              │                 │           (verify exists + pwd, NO admit)
  │◀── RESOLVE_RESULT { ServerHost=127.0.0.1, ServerPort=9003 } ─────│
  │              │   (LB socket closes silently — server cleanup nop)
  │              │                 │                │                │
  │── direct TCP to 9003 ─────────────────────────────────────────────▶│
  │── ROOM_JOIN(R) ───────────────────────────────────────────────────▶│
  │◀── JOIN_RESULT { Room, Members, SnapshotData, CanvasState, ChatHistory } ─│
```

### Cross-server draw propagation

```
Client A (on 9002)         Canvas 9002             Canvas 9003           Client B (on 9003)
  │                            │                       │                       │
  │── DRAW_MOVE ────────────────▶                       │                       │
  │                            │── persist (async)     │                       │
  │                            │── broadcast local    │                       │
  │                            │── PEER_RELAY ─────────▶                       │
  │                            │                       │── ApplyFromPeer       │
  │                            │                       │── broadcast local ───▶│
```

---

## 12. Known Trade-offs

- **Peer mesh is O(N²)** — every Canvas Server holds a connection to every other one. Fine for N ≤ ~8. Beyond that, switch to a pub/sub broker (Redis, NATS) and remove peer mesh entirely.
- **Lobby polling.** Lobby clients use short-lived TCP — there's no persistent socket the server can push updates over. `LobbyForm` runs a 3-second polling timer (semaphore-guarded so a slow LB doesn't stack requests). Acceptable for school demo scale; a long-poll or WebSocket-style push is the obvious upgrade.
- **No transport encryption by default.** AES-256-CBC + HMAC-SHA256 module is implemented and tested, but disabled on the wire so JSON traffic is visible to Wireshark for the demo. TLS is the obvious next step for non-LAN deployment.
- **Snapshot vs delta growth.** Without snapshot pruning, `canvas_snapshots` grows unbounded. `AutoSaveService` only writes; a retention sweep is on the roadmap.
- **Single AuthServer instance** is acceptable because connections are short-lived, but the LB still proxies AUTH through a pool to support future horizontal scaling.
- **Pre-2026-05-22 tokens are rejected.** After the HMAC signing rollout, any persisted token without a signature is treated as invalid. Operators must trigger a re-login pass after deploying the security update.
- **Legacy `MessageType.cs` stub** lives at `CanvasApp.Common/Models/MessageType.cs` (empty internal class). It is unused and kept only so older externally-built tooling doesn't trip on the missing type name. New readers should look at `Message.cs` for the canonical enum-like static class.

---

## 13. Index of Recent Architectural Changes

(See `CHANGELOG.md` for details.)

| Date | Change | Files touched |
|---|---|---|
| 2026-05-22 | **Security hardening:** HMAC-SHA256-signed auth tokens, `FixedTimeEquals`, constant-time login via dummy BCrypt; OTP rate-limit (1/min, 5/hour) + cleanup task; OTP not burned on UpdatePassword DB failure; email regex validation; `UNIQUE(email)` migration | `UserStore.cs`, `Services/*`, `OtpStore.cs`, `schema.sql` |
| 2026-05-22 | **Peer mesh cross-server consistency:** self-loop guard (PEER_HELLO & ApplyFromPeerAsync), `PEER_ROOM_CREATE` / `PEER_ROOM_DELETE` / `PEER_ROOM_PASSWORD_UPDATED`; `PEER_CANVAS_SYNC` HashSet dedupe by ActionId + SeqNo | `Message.cs`, `RoomManager.cs`, `PeerHandler.cs`, `PeerManager.cs` |
| 2026-05-22 | **Race fixes:** per-client send `SemaphoreSlim`; `TryDeleteRoomIfEmpty` atomic under `lock(list)` + `_deleting` guard; atomic password write; `EnsureCanvasLoaded` per-room load lock; `ROOM_JOIN_RESULT.ChatHistory` embedded (replaces standalone `CHAT_HISTORY` send) | `RoomManager.cs`, `Models.cs`, `Server/Program.cs` |
| 2026-05-22 | **LB routing rules:** `ROOM_RESOLVE` sticky; `ROOM_DELETE` + `ROOM_UPDATE_PASSWORD` use `RouteForRoomReadOnly`; `ROOM_DELETE_RESULT` sniff calls `UnregisterRoom`; UTF-8 BOM tolerance in `ReadOneLineAsync` | `LoadBalancer.cs` |
| 2026-05-22 | **`ROOM_RESOLVE` pre-join** eliminates the historical double-`ROOM_JOIN` cost — server returns address without admitting the user | `Message.cs`, `Models.cs`, `RoomManager.cs`, `LobbyClient.cs`, `LobbyForm.cs` |
| 2026-05-22 | **AES module wired into csproj** (was stub before); demo flow documented | `AesHelper.cs`, `MessageCrypto.cs`, `CryptoConfig.cs`, `CanvasApp.Common.csproj` |
| 2026-05-22 | **Image overlay**: `DRAW_IMAGE` + `DRAW_IMAGE_TRANSFORM` for placing/moving/scaling bitmaps on the canvas | `Message.cs`, `CanvasForm.Images.cs` (new partial), `CanvasForm.cs` |
| 2026-05-21 | Explicit room routing table in LB; `ROOM_CREATE_RESULT` sniff to pre-register binding; short-lived TCP for every lobby query (new `LobbyClient`); lobby no longer holds a persistent LB socket | `LoadBalancer.cs`, `LobbyClient.cs` (new), `LobbyForm.cs` |
| 2026-05-20 | Connect-on-Join pattern; generation counter to suppress spurious disconnects; `ROOM_JOIN_RESULT` carries `ServerHost`/`ServerPort` | `Session.cs`, `CanvasClient.cs`, `LoginForm.cs`, `LobbyForm.cs`, `Models.cs` (`JoinRoomResult`), `Server/Program.cs` |
| 2026-05-20 | Peer mesh hardening: TCP keepalive, `PEER_PING` heartbeat, `PEER_CANVAS_SYNC` replay on reconnect, merged member-list rebroadcast | `PeerManager.cs`, `PeerHandler.cs`, `RoomManager.cs`, `Message.cs`, `Models.cs` |
| 2026-05-20 | Room affinity (originally CRC32, later routing-table); invite-code resolve before routing | `LoadBalancer.cs`, `Server/Program.cs`, `CanvasClient.cs` |
| 2026-05-15 | Smart shape recognition subsystem (Line/Circle/Ellipse/Rectangle detectors) | `Drawing/Recognition/*`, `CanvasForm.cs` |
| earlier | Canvas snapshot + delta sync; `AutoSaveService`; `PersistenceQueue` | `CanvasSnapshotDAO.cs`, `SnapshotHelper.cs`, `AutoSaveService.cs`, `PersistenceQueue.cs` |
