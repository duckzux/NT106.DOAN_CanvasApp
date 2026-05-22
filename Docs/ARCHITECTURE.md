# CanvasApp — System Architecture

> **Last updated:** 2026-05-21
> **Stack:** C# / .NET Framework 4.7.2/4.8, WinForms (client), Newtonsoft.Json, MySQL (persistence), raw TCP sockets, JWT (auth tokens)
> **Topology:** Distributed multi-server canvas with Load Balancer routing-table for per-room stickiness, peer-mesh synchronization between Canvas Servers, and short-lived lobby connections.

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
  Message.cs             ← envelope: { type, data, token }, + MessageType constants
  Models.cs              ← Room, User, JoinRoomResult, ChatMessage, DrawAction, etc.
  DrawAction.cs          ← stroke / shape / fill action payload
  Room.cs, User.cs       ← entity records
  MessageType.cs         ← extra wire constants (peer, auth, draw)
Utils/
  JwtHelper.cs           ← HS256 sign/verify, expiry, payload extract
  JsonHelper.cs          ← Newtonsoft.Json wrapper with Camel/PascalCase settings
  AesHelper.cs           ← (future) message body encryption
  SnapshotHelper.cs      ← GZip + Base64 compress/decompress for CanvasSnapshot
DataAccess/
  DatabaseManager.cs     ← MySQL connection factory (singleton)
  UserDAO.cs             ← users table CRUD
  RoomDAO.cs             ← rooms table CRUD + invite code lookup
  RoomMemberDAO.cs       ← room_members table (history)
  DrawActionDAO.cs       ← draw_actions table (per-stroke persistence)
  ChatMessageDAO.cs      ← chat_messages table
  CanvasSnapshotDAO.cs   ← canvas_snapshots table (baseline + version)
  PersistenceQueue.cs    ← background write-queue (avoids blocking hot path)
```

### 2.2 `CanvasApp.AuthServer` (:9001)

Standalone TCP server. Accepts only `AUTH_LOGIN` / `AUTH_REGISTER`; returns JWT-signed token on success.

```
Program.cs              ← Main: bind port, accept loop, dispatch AuthHandler
AuthServer.cs           ← TcpListener wrapper, per-connection task
AuthHandler.cs          ← one connection: read message, route to Services, write reply, close
UserStore.cs            ← MySQL-backed user store (adapter over UserDAO)
Services/
  AuthService.cs        ← orchestrator: register/login/verify
  TokenService.cs       ← JWT issue + validate (delegates to JwtHelper)
  UserService.cs        ← user lookup, password hashing (BCrypt)
```

**Connection lifetime:** short-lived. Client opens TCP, sends one auth message, reads one reply, both sides close. No persistent connection to AuthServer.

### 2.3 `CanvasApp.LoadBalancer` (:9000)

TCP proxy with an in-memory **room routing table** for stickiness.

```
Program.cs              ← reads appsettings.json (server pools), starts LoadBalancer + HealthChecker
LoadBalancer.cs         ← accept loop; per-connection:
                          1) peek first JSON line (timeout 5s)
                          2) route by Message.Type:
                             AUTH_LOGIN/REGISTER       → Auth pool (round-robin)
                             ROOM_JOIN / *_BY_CODE     → RouteForRoom(RoomId)
                                                          • table hit & healthy → that server
                                                          • else pick least-loaded, store mapping
                             everything else           → Canvas pool (least-loaded by RoomCount)
                          3) connect backend, forward peeked line
                          4) for ROOM_CREATE: intercept first response line, extract Room.Id
                             from ROOM_CREATE_RESULT, register table mapping immediately
                          5) bidirectional pump until either side closes
ServerInfo.cs           ← Host, Port, Type, MaxConnections, ActiveConnections, RoomCount, IsHealthy
HealthChecker.cs        ← periodic TCP probe, marks server unhealthy on N consecutive fails
```

**Why a table, not CRC32?** CRC32 hashing is sticky only while the canvas server *count* is constant. Removing a server (or rebooting the LB with a different config) re-keys every room — users mid-session would get steered to a different canvas. The routing table is explicit: a room stays bound to its server until that server is unhealthy, at which point the next join transparently rebinds.

**Why each lobby request is a fresh TCP**: the LB only peeks the first message on a connection. If the client sent `ROOM_LIST` then `ROOM_JOIN` on the same socket, the `ROOM_JOIN` would skip routing and just ride whatever pipe the LB opened for `ROOM_LIST` (least-loaded). The `LobbyClient` helper in the WinForms client opens a fresh TCP for each lobby query, so every message gets correctly routed as its own first message.

### 2.4 `CanvasApp.Server` (Canvas Server — :9002, :9003, …)

The stateful real-time layer. Multiple instances run in parallel; each owns a subset of rooms (routing decided by LB). Instances cross-publish via the **peer mesh** so that even though room state is sharded, broadcasts reach every client.

```
Program.cs              ← Main: bind client port + peer port (client+100),
                          start RoomManager, PersistenceQueue, AutoSaveService,
                          PeerManager + PeerHandler listener; per-client receive loop
                          dispatches every MessageType to handlers
RoomManager.cs          ← in-memory room state: rooms, members, draw actions, locks;
                          Join/Leave, Broadcast, BroadcastToLobby, peer-merged GetMembers
RoomState.cs            ← single-room container: List<DrawAction>, List<ConnectedClient>,
                          peer member cache, lastActivity timestamp
AutoSaveService.cs      ← timer: every N min, snapshot dirty rooms via CanvasSnapshotDAO
PeerManager.cs          ← outbound peer connections: dial, TCP keepalive, heartbeat
                          (PEER_PING/PONG every 15s), exponential reconnect, OnPeerReconnected event
PeerHandler.cs          ← inbound peer connections: read PEER_RELAY / PEER_HELLO / PEER_MEMBER_SYNC /
                          PEER_CANVAS_SYNC / PEER_PING, apply via RoomManager.ApplyFromPeerAsync
```

**Two ports per Canvas Server instance:**
- Client port (e.g. 9002) — clients connect here, dispatched by LB
- Peer port = client port + 100 (e.g. 9102) — sister servers connect here for peer relay

`_serverId = "canvas-{port}"` uniquely identifies each instance in peer envelopes.

### 2.5 `CanvasApp.Client` (WinForms)

```
Program.cs              ← App entry, starts LoginForm
Forms/
  LoginForm.cs          ← login / register form, calls AuthClient
  RegisterForm.cs       ← register form
  LobbyForm.cs          ← room list, create room, invite code join
  CreateRoom.cs         ← in-lobby modal: name/password/template/maxUsers
  RequirePassword.cs    ← in-lobby modal: prompt for room password
  CanvasForm.cs         ← drawing surface, tools, chat panel, member list,
                          undo/redo, snapshot + delta apply, file attachment
  CanvasForm.Icons.cs   ← partial: icon bitmaps for toolbar
Controls/
  RoomCard.cs           ← list item with name / users / join button
  UserListItem.cs       ← member row with role + avatar color
  GradientPanel.cs      ← background panel painter
  loginTextbox.cs       ← composite input with label + password toggle
Network/
  Session.cs            ← static state: CurrentUser, Token, LB_HOST/PORT, AUTH_HOST/PORT,
                          runtime CanvasHost/CanvasPort (set after LB redirect)
  AuthClient.cs         ← short-lived TCP to AuthServer for login/register
  LobbyClient.cs        ← short-lived TCP to LB for ROOM_LIST / ROOM_CREATE / ROOM_JOIN /
                          RESOLVE_INVITE_CODE / ROOM_JOIN_BY_CODE. Each query opens its own
                          socket so the LB peeks that message type as the first line and
                          routes it correctly (avoids "first-message-pins-route" bug).
  CanvasClient.cs       ← persistent TCP client w/ ConnectAsync / ConnectToServerAsync,
                          send/receive loops, heartbeat (PING 10s),
                          exponential-backoff reconnect, generation counter
                          to suppress OnDisconnected during intentional switches.
                          Opened ONLY after ROOM_JOIN_RESULT returns ServerHost/ServerPort.
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

**Categories** (`MessageType` constants in `Common/Models/Message.cs`):

| Category | Examples |
|---|---|
| Auth | `AUTH_LOGIN`, `AUTH_REGISTER`, `*_RESULT` |
| Room lifecycle | `ROOM_LIST`, `ROOM_CREATE`, `ROOM_JOIN`, `ROOM_LEAVE`, `ROOM_UPDATE`, `ROOM_JOIN_BY_CODE`, `RESOLVE_INVITE_CODE(_RESULT)` |
| Drawing | `DRAW_START`, `DRAW_MOVE`, `DRAW_END`, `DRAW_SHAPE`, `DRAW_FILL`, `DRAW_TEXT`, `DRAW_CLEAR`, `DRAW_UNDO` |
| Canvas sync | `CANVAS_STATE` (full re-sync after reconnect) |
| Chat | `CHAT_MESSAGE`, `CHAT_HISTORY`, `CHAT_FILE` |
| Liveness | `PING`, `PONG` |
| Peer mesh | `PEER_HELLO`, `PEER_RELAY`, `PEER_MEMBER_SYNC`, `PEER_CANVAS_SYNC`, `PEER_PING`, `PEER_PONG` |

Tokens are validated server-side on each message (see `AuthService` in AuthServer, and the dispatch in `Server/Program.cs`).

---

## 4. Connect-on-Join Flow (current)

The lobby holds **no** persistent socket. Every lobby query is a fresh short-lived TCP to the LB, so the LB's first-message routing sees the right message type on every request. The persistent connection only opens after `ROOM_JOIN_RESULT` returns the canvas's actual `ServerHost/ServerPort`.

```
LoginForm
  └─ AuthClient.LoginAsync()        ← short-lived TCP → :9001 → stores JWT in Session
  └─ open LobbyForm

LobbyForm.Load
  └─ LobbyClient.GetRoomListAsync   ← short-lived TCP → LB → forwards to any healthy Canvas
                                       (LB peeks ROOM_LIST as first message → least-loaded)

User clicks Create Room
  └─ LobbyClient.CreateRoomAsync    ← short-lived TCP → LB → least-loaded Canvas
                                       LB sniffs ROOM_CREATE_RESULT → registers Room.Id → that Canvas
  └─ LobbyClient.JoinRoomAsync (auto-join the new room — sticky to same server)

User clicks Join Room X
  └─ LobbyClient.JoinRoomAsync      ← short-lived TCP → LB
                                       LB looks up routing table for X → same Canvas every time
                                       Canvas processes Join, replies with ROOM_JOIN_RESULT
                                       including ServerHost & ServerPort
  └─ LB connection closes after the result

LobbyForm.HandleJoinResultAsync
  ├─ CanvasClient.ConnectToServerAsync(canvasHost, canvasPort)
  │   └─ persistent direct TCP to Canvas Server
  ├─ new CanvasForm()                ← constructor subscribes OnMessageReceived
  ├─ SetRoom(joinRes)                ← applies snapshot + delta from LB-routed result
  ├─ Show CanvasForm, Hide LobbyForm
  └─ CanvasClient.JoinRoomAsync(X)   ← second ROOM_JOIN on direct connection
                                       so server registers user on THIS socket
                                       (LB-side socket was closed → server cleaned up)

CanvasForm closes
  └─ LobbyForm.Show() → Shown event fires → RefreshRoomListAsync (fresh LB query)
```

**Why two `ROOM_JOIN`s?** The first (via LB short-lived) is what learns `ServerHost/ServerPort`. The LB connection closes immediately after the reply, so the server-side `ConnectedClient` is destroyed. The second join, on the now-open direct socket, re-registers the client on that socket so subsequent draw/chat traffic is associated with the right room. Acceptable for the demo; could be avoided by adding a `ROOM_DISCOVER` message that returns the address without joining.

### Connection generation counter (anti-spurious-disconnect)

`CanvasClient` keeps an `int _connectionGen`. Every `Disconnect()` and `TryConnectAsync()` calls `Interlocked.Increment` on it. The `ReceiveLoop` captures `myGen` at start; in `finally` it only fires `OnDisconnected` if `myGen == _connectionGen`. Otherwise (a newer connection has been established), the event is suppressed. Without this, every server switch would log the user out.

---

## 5. Routing — LoadBalancer Decisions

`LoadBalancer.HandleClientAsync()` peeks the first JSON line and decides:

```
type ∈ { AUTH_LOGIN, AUTH_REGISTER }       → PickAuth() (round-robin)
type == ROOM_JOIN with non-empty RoomId    → RouteForRoom(RoomId)
type == ROOM_JOIN_BY_CODE with RoomId      → RouteForRoom(RoomId)
type == ROOM_JOIN_BY_CODE without RoomId   → PickCanvasLeastLoaded()
                                              (client should call RESOLVE_INVITE_CODE first)
everything else                            → PickCanvasLeastLoaded()
```

**`RouteForRoom(roomId)`** — explicit routing table (`ConcurrentDictionary<string, ServerInfo>`):

```
if _roomRouting[roomId] exists and is healthy → return that server  (sticky)
else                                          → PickCanvasLeastLoaded()
                                                 store mapping, return picked server
```

If the previously-bound server is unhealthy, the mapping is dropped so the next join rebinds. After `ROOM_CREATE_RESULT` is forwarded, the LB extracts `Room.Id` from the reply and pre-registers the mapping for the creating server — so the very first remote join lands on the same server (no race window).

**`PickCanvasLeastLoaded`** sorts healthy servers by `RoomCount`, breaks ties by `ActiveConnections`, then round-robin.

---

## 6. Peer Mesh Synchronization

Every Canvas Server connects to every other Canvas Server over its **peer port** (client port + 100). The mesh exchanges:

| Wire message | Direction | Purpose |
|---|---|---|
| `PEER_HELLO` | on connect | exchange known rooms + local member lists; primes both ends |
| `PEER_RELAY` | streaming | rebroadcast any local room event (ROOM_UPDATE, DRAW_*, CHAT_*) |
| `PEER_MEMBER_SYNC` | on member change | publish updated local-only member list; recipient merges and re-broadcasts to local room clients |
| `PEER_CANVAS_SYNC` | on peer reconnect | replay committed `DrawAction`s for active rooms; recipient deduplicates by `ActionId` |
| `PEER_PING` / `PEER_PONG` | every 15s | application-level heartbeat; on timeout, close socket and reconnect |

**Reliability layers (added 2026-05-20):**
1. Windows TCP keepalive (idle 10s, retry 5s × 3 = ~25s detection)
2. `PEER_PING` every 15s, PONG expected within 30s
3. Reconnect with exponential backoff (500ms → 30s cap)
4. On reconnect, `OnPeerReconnected` fires → `Program.cs` re-publishes canvas state for every dirty room

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
                  Canvas Server
                  └─ RoomManager records DrawAction
                  └─ PersistenceQueue (async DB write)
                  └─ BroadcastAsync to other local clients (except sender)
                  └─ PeerManager.PublishAsync(PEER_RELAY(DRAW_*)) to other servers
                                      │
                                      ▼
                              Peer Canvas Server
                              └─ ApplyFromPeerAsync re-broadcasts to its local clients
```

**Shape recognition (client-only post-stroke):** when a stroke ends, `ShapeSuggestionController` runs the polyline through `ShapeRecognizer` (Line/Circle/Ellipse/Rectangle detectors). On a confident match, `SuggestionOverlay` floats above the stroke offering Accept/Reject; on Accept, the raw stroke is replaced with a clean `DRAW_SHAPE` action and re-published.

**Undo:** server tracks `IsUndone` per `DrawAction.ActionId`. `DRAW_UNDO` flips the flag and broadcasts an `UndoNotification`. Each client filters undone actions out of `_history` on redraw.

**Snapshot:** `AutoSaveService` periodically GZip-compresses each dirty room's `DrawAction` list (after a high-water `SeqNo`) and writes a `CanvasSnapshot`. On `ROOM_JOIN_RESULT`, the server attaches `SnapshotData` (base64) + delta actions; client decompresses baseline, then layers deltas. Keeps cold-join payload bounded.

---

## 8. Persistence

MySQL schema (auto-created by `DatabaseManager` if absent — see SQL in `Common/DataAccess/DatabaseManager.cs`):

| Table | Owner | Notes |
|---|---|---|
| `users` | AuthServer | id, username, password_hash, email, avatar_color, created_at |
| `rooms` | Canvas | id, name, owner_id, has_password, password_hash, template, max_users, invite_code, created_at |
| `room_members` | Canvas | room_id, user_id, role, joined_at, left_at (history) |
| `draw_actions` | Canvas | room_id, seq_no, action_id, type, payload (JSON), user_id, is_undone, timestamp |
| `chat_messages` | Canvas | room_id, user_id, username, text, file_name, file_data (LONGBLOB), file_size, timestamp |
| `canvas_snapshots` | Canvas | room_id, version, snapshot_data (LONGTEXT GZip+base64), action_seq_at, byte_size, created_at |

**Write path:** `PersistenceQueue` is a single background consumer over a `BlockingCollection<Action>`. Hot-path message handlers enqueue `() => dao.Insert(...)` and return immediately; the queue drains in order, isolating socket loops from DB latency.

---

## 9. Security Model

- **Authentication:** AuthServer issues HS256 JWT containing `{ sub: userId, username, exp }`. Secret lives in `App.config`. Tokens validated by `JwtHelper.TryVerify` on each Canvas Server message.
- **Authorization:** room-level only (member of room ⇒ may draw/chat); no per-action ACL. Room password is BCrypt-hashed.
- **Transport:** plain TCP. No TLS. (Acceptable for LAN demo; production would terminate TLS at the LB.)
- **Input validation:** server-side `MaxPayloadBytes = 4MB`, `MaxRoomNameLength = 100`, `ColorRegex` (`#RRGGBB(AA)?`). Oversized or malformed messages drop the connection.

---

## 10. Configuration

| Component | Config | Keys |
|---|---|---|
| AuthServer | `App.config` | `ConnectionStrings.CanvasDb`, `AppSettings.AuthPort`, `AppSettings.JwtSecret`, `AppSettings.JwtExpiryMinutes` |
| LoadBalancer | `appsettings.json` | `ListenPort`, `HealthCheckIntervalSeconds`, `AuthServers[]`, `CanvasServers[]` (each with Host/Port/MaxConnections) |
| Canvas Server | `Config/appsettings.json` + CLI args | CLI `args[0]` = client port (e.g. 9003) overrides config; CLI `args[1]` = comma-sep peers ("host:peerPort"); config keys: `ServerPort`, `Peers[]`, `AutoSave.IntervalMinutes`, `Snapshot.RetentionVersions` |
| Client | `Session.cs` constants | `LB_HOST`, `LB_PORT`, `AUTH_HOST`, `AUTH_PORT` (only LB & Auth are compile-time; Canvas is runtime via LB redirect) |

**Running multiple Canvas instances locally:**
```
CanvasApp.Server.exe 9002 127.0.0.1:9103
CanvasApp.Server.exe 9003 127.0.0.1:9102
CanvasApp.LoadBalancer.exe   # picks up 9002 + 9003 from appsettings.json
CanvasApp.AuthServer.exe
```

---

## 11. Lifecycle Diagrams

### Sign-up + first join

```
Client       AuthServer      LoadBalancer      Canvas 9002      Canvas 9003
  │              │                 │                │                │
  │── REGISTER ─▶│                 │                │                │
  │◀── token ───│                 │                │                │
  │              │                 │                │                │
  │── ROOM_LIST ───────────────────▶│ (least-loaded)│                │
  │              │                 │── forward ────▶│                │
  │◀── list ─────────────────────────────────────────│               │
  │              │                 │                │                │
  │── ROOM_JOIN(R) ────────────────▶│ (hash R → 9003)│                │
  │              │                 │── forward ──────────────────────▶│
  │◀── JOIN_RESULT { ServerHost=127.0.0.1, ServerPort=9003 } ─────────│
  │              │                 │                │                │
  │── direct TCP to 9003 ─────────────────────────────────────────────▶│
  │── ROOM_JOIN(R) again ─────────────────────────────────────────────▶│
  │◀── JOIN_RESULT + CHAT_HISTORY + snapshot ─────────────────────────│
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

- **Double-join cost.** Connect-on-Join requires sending `ROOM_JOIN` twice (LB-discover + direct-register). Server re-emits `CHAT_HISTORY` and `ROOM_UPDATE` on the second join. Other clients see the joining user as briefly leaving then rejoining. See CHANGELOG 2026-05-20 entry for the deferred `ROOM_DISCOVER` design.
- **Peer mesh is O(N²)** — every Canvas Server holds a connection to every other one. Fine for N ≤ ~8. Beyond that, switch to a pub/sub broker (Redis, NATS) and remove peer mesh entirely.
- **No transport encryption.** TLS is the obvious next step for non-LAN deployment.
- **Snapshot vs delta growth.** Without snapshot pruning, `canvas_snapshots` grows unbounded. `AutoSaveService` only writes; a retention sweep is on the roadmap.
- **Single AuthServer instance** is acceptable because connections are short-lived, but the LB still proxies AUTH through a pool to support future horizontal scaling.

---

## 13. Index of Recent Architectural Changes

(See `CHANGELOG.md` for details.)

| Date | Change | Files touched |
|---|---|---|
| 2026-05-21 | Explicit room routing table in LB; ROOM_CREATE_RESULT sniff to pre-register binding; short-lived TCP for every lobby query (new `LobbyClient`); lobby no longer holds a persistent LB socket | `LoadBalancer.cs`, `LobbyClient.cs` (new), `LobbyForm.cs` |
| 2026-05-20 | Connect-on-Join pattern; generation counter to suppress spurious disconnects; ROOM_JOIN_RESULT carries ServerHost/Port | `Session.cs`, `CanvasClient.cs`, `LoginForm.cs`, `LobbyForm.cs`, `Models.cs` (JoinRoomResult), `Server/Program.cs` |
| 2026-05-20 | Peer mesh hardening: TCP keepalive, PEER_PING heartbeat, PEER_CANVAS_SYNC replay on reconnect, merged member-list rebroadcast | `PeerManager.cs`, `PeerHandler.cs`, `RoomManager.cs`, `Message.cs`, `Models.cs` |
| 2026-05-20 | Room affinity via CRC32(RoomId), invite-code resolve before routing | `LoadBalancer.cs`, `Server/Program.cs`, `CanvasClient.cs` |
| 2026-05-15 | Smart shape recognition subsystem (Line/Circle/Ellipse/Rectangle detectors) | `Drawing/Recognition/*`, `CanvasForm.cs` |
| earlier | Canvas snapshot + delta sync; AutoSaveService; PersistenceQueue | `CanvasSnapshotDAO.cs`, `SnapshotHelper.cs`, `AutoSaveService.cs`, `PersistenceQueue.cs` |
