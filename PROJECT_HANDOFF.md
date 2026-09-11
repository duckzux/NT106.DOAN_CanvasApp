# CanvasApp — Collaborative Drawing over Network (Project Handoff)

> **Course:** NT106.Q23.ANTT — Lập trình mạng căn bản (Basic Network Programming)
> **Group:** Nhóm 14 (24520453 / 24521187 / 24521501)
> **Difficulty rating:** ★★★★ (4 stars)
> **Status:** Foundation phase complete — feature implementation pending
> **Language:** Vietnamese context, C# codebase

---

## 1. Project Overview

**CanvasApp** is a real-time **collaborative drawing application** (online whiteboard) that lets multiple users draw together on a shared canvas over a TCP network. Think Miro / Microsoft Whiteboard, built from scratch in C# WinForms for a university networking course.

### 1.1 Core Goals

- Multiple users join a "room" and draw simultaneously in real time.
- Strokes broadcast over **TCP sockets** (low-level `System.Net.Sockets`, no SignalR / WebSockets).
- Persist users, rooms, canvas snapshots, and draw history in **MySQL**.
- Secure: BCrypt for passwords, JWT for sessions, AES-256 for traffic.
- Demonstrate multi-server architecture with a Load Balancer.

### 1.2 Grading Criteria (the project must hit these)

| Category | Points | Notes |
|---|---|---|
| App Logic + Socket Logic | 5.0 | Core drawing + TCP |
| I/O | 0.5 | Export PNG, import background, NetworkStream |
| Database | 0.5 | 5 MySQL tables, CRUD |
| Thread | 0.5 | Task.Run, ConcurrentDictionary, Invoke() |
| Sign up / Sign in | 0.5 | BCrypt + JWT + 3 roles |
| Multi Client | 0.5 | 3+ users, cursor display |
| Multi Server | 0.5 | Auth.exe + Canvas.exe separate processes |
| Cryptography | 0.5 | BCrypt + AES-256 + JWT HMAC-SHA256 |
| Demo LAN | 0.5 | 3-4 Windows machines on same WiFi |
| Demo Internet | 0.5 | ngrok TCP tunnel |
| Load Balancing | 1.0 | 2 Canvas Servers + 1 LoadBalancer |
| **UI/Visual (separate 20pt block)** | 20 | FlatStyle, ToolStrip, layout |
| **Creative features (separate 10pt block)** | 10 | Layer system, replay, templates |

---

## 2. Tech Stack

| Layer | Tech | Why |
|---|---|---|
| Language | **C# / .NET Framework 4.8** | Required by course |
| GUI | **Windows Forms (WinForms)** | GDI+ is strong for canvas drawing |
| Drawing | **GDI+** (`Graphics`, `Bitmap`, `Pen`) | Built into .NET, no extra deps |
| Network | **TcpClient / TcpListener** (`System.Net.Sockets`) | TCP guarantees stroke ordering |
| Database | **MySQL 8.0** + `MySql.Data` NuGet | Free, good .NET connector |
| Serialization | **Newtonsoft.Json** (Json.NET) | Most common JSON lib |
| Password hashing | **BCrypt.Net-Next** | Industry standard |
| Token | **System.IdentityModel.Tokens.Jwt** | JWT HMAC-SHA256 |
| Traffic encryption | **System.Security.Cryptography.Aes** (AES-256) | .NET built-in |
| UI Polish | MaterialSkin.2, MetroForm | Optional WinForms theming |
| IDE | Visual Studio Community | |

### NuGet packages to install (in every relevant project)

```
Install-Package Newtonsoft.Json
Install-Package MySql.Data
Install-Package BCrypt.Net-Next
Install-Package System.IdentityModel.Tokens.Jwt
```

---

## 3. Solution Structure

The Visual Studio solution contains **4 main projects** (sometimes referenced as 5 in the mid-term report when counting LoadBalancer separately):

| Project | Type | Description |
|---|---|---|
| **CanvasApp.Server** | Console App | Canvas Server: room management, broadcast strokes, persist canvas state. Listens on **port 9002**. |
| **CanvasApp.AuthServer** | Console App | Auth Server: register/login, issue JWT, BCrypt password hashing. Listens on **port 9001**. |
| **CanvasApp.LoadBalancer** | Console App | Routing table, health check, redirect clients to least-loaded Canvas Server. Listens on **port 9000**. |
| **CanvasApp.Client** | Windows Forms App | All client GUI: Login, Lobby, Canvas. |
| **CanvasApp.Common** | Class Library | Shared models: `Message`, `DrawAction`, `User`, `Room`, enums. Referenced by all other projects. |

### 3.1 Detailed Folder Layout (from `csharp_solution_folder_structure.pdf`)

```
CanvasApp.sln
├── CanvasApp.Common/              (Class Library — shared models)
│   ├── Models/
│   │   ├── Message.cs             (Type, RoomId, UserId, Timestamp, Data:JObject)
│   │   ├── DrawAction.cs
│   │   ├── User.cs
│   │   ├── Room.cs
│   │   └── Enums.cs               (MessageType, RoomRole, Tool)
│   └── Utilities/
│       ├── JsonHelper.cs
│       └── AesHelper.cs           (AES-256 encrypt/decrypt)
│
├── CanvasApp.Server/              (Canvas Server — port 9002)
│   ├── Program.cs                 (Main: TcpListener, AcceptLoop)
│   ├── ClientHandler.cs           (per-client Task)
│   ├── RoomManager.cs             (ConcurrentDictionary<roomId, Room>)
│   ├── CanvasState.cs             (List<DrawAction> per room)
│   ├── AutoSaveService.cs         (Timer 60s → snapshot to DB)
│   └── Config/appsettings.json
│
├── CanvasApp.AuthServer/          (Auth Server — port 9001)
│   ├── Program.cs
│   ├── AuthHandler.cs             (login / register)
│   ├── PasswordService.cs         (BCrypt hash + verify)
│   ├── TokenService.cs            (JWT create + validate)
│   └── Config/appsettings.json
│
├── CanvasApp.LoadBalancer/        (Load Balancer — port 9000)
│   ├── Program.cs
│   ├── LoadBalancer.cs            (routing table, FindBestServer(roomId))
│   ├── ServerInfo.cs              (IP, Port, RoomCount, Status)
│   ├── HealthChecker.cs           (Timer 5s, ping Canvas Servers, mark down after 3 misses)
│   └── appsettings.json
│
├── CanvasApp.Client/              (WinForms GUI)
│   ├── Program.cs                 (Application.Run(new LoginForm()))
│   ├── Forms/
│   │   ├── LoginForm.cs           (TabControl Login/Register)
│   │   ├── LobbyForm.cs           (FlowLayoutPanel of room cards)
│   │   ├── CanvasForm.cs          (MAIN: canvas + toolbar + user panel)
│   │   ├── CreateRoomDialog.cs
│   │   └── ExportDialog.cs
│   ├── Controls/
│   │   ├── CanvasPanel.cs         (Custom DoubleBuffered Panel)
│   │   ├── RoomCard.cs
│   │   ├── UserListItem.cs
│   │   └── ColorPaletteControl.cs
│   ├── Drawing/
│   │   ├── DrawingEngine.cs       (GDI+ core)
│   │   ├── ToolManager.cs         (enum Tool: Pen, Eraser, Rect, Circle, Line, Arrow, Text)
│   │   ├── UndoRedoManager.cs     (Stack<DrawAction>)
│   │   ├── LayerManager.cs        (per-user Bitmap, toggle visible)
│   │   └── ReplayEngine.cs        (Timer playback by timestamp)
│   ├── Network/
│   │   ├── NetworkClient.cs       (TcpClient wrapper)
│   │   ├── ReceiverTask.cs        (StreamReader.ReadLineAsync loop + Invoke())
│   │   ├── SenderTask.cs          (BlockingCollection consumer)
│   │   └── ConnectionManager.cs   (Reconnect with exponential backoff)
│   └── Resources/Icons/           (pen.png, eraser.png, rectangle.png, ... 16x16)
│
├── Database/
│   ├── schema.sql                 (CREATE TABLE statements)
│   ├── seed_data.sql              (demo1/demo2/demo3 users)
│   └── DataAccess/
│       ├── DatabaseManager.cs     (Singleton MySqlConnection pool)
│       ├── UserDAO.cs
│       ├── RoomDAO.cs
│       ├── RoomMemberDAO.cs
│       ├── CanvasSnapshotDAO.cs
│       └── DrawActionDAO.cs
│
└── Tools/                         (Helper scripts)
    ├── stress_test.py             (Python: spawn 5 TCP clients, continuous DRAW)
    └── generate_test_data.py      (random DrawActions JSON)
```

---

## 4. System Architecture

## 4. System Architecture (revised)

### 4.1 The Nodes (revised — addresses 3 design flaws from supervisor review)

```
                          ┌──────────────────────┐
                          │     Load Balancer    │  Port 9000
                          │ (routing + health    │
                          │  check, no DB calls) │
                          └──────────┬───────────┘
                                     │
                ┌────────────────────┼────────────────────┐
                │                    │                    │
                ▼                    ▼                    ▼
        ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
        │ Auth Server 1│     │ Auth Server 2│     │ Canvas Server│  9002
        │  port 9001   │     │  port 9011   │     │              │
        └──────┬───────┘     └──────┬───────┘     └──────┬───────┘
               │                    │                    │      ┌──────────────┐
               │  Verify BCrypt     │                    │      │ Canvas Server│  9003
               │  hash (users tbl)  │                    │      └──────┬───────┘
               │                    │                    │             │
               └────────┬───────────┘                    │             │
                        ▼                                ▼             ▼
                  ┌──────────┐                     ┌──────────────────────┐
                  │  MySQL   │ ◄────────────────── │  Redis (session      │
                  │  users / │   AutoSave snapshots│  store + blacklist)  │
                  │  rooms / │                     │  session:{jti} TTL   │
                  │  ...     │                     └──────────────────────┘
                  └──────────┘                          ▲       ▲
                                                        │       │
                                  Write session on login│       │Check jti on
                                  Remove on logout      │       │every connect
                                                        │       │
                                                  (Auth Servers) (Canvas Servers)


      ┌──────────┐
      │  Client  │ ── 1. Auth via LB ──▶ Auth Server (1 or 2)
      │ WinForms │ ◀── JWT + room list ─
      │          │ ── 2. ROOM_JOIN via LB ──▶ Canvas Server
      │          │ ◄── CANVAS_STATE / BROADCAST_DRAW ──
      └──────────┘
```

| Node | Role | Port |
|---|---|---|
| **Auth Server 1** | Register, login, BCrypt verify against MySQL `users`, issue JWT, write session to Redis. | 9001 |
| **Auth Server 2** | Identical replica of Auth Server 1 (failover + scale). Same JWT signing secret, same Redis store. | 9011 |
| **Canvas Server** | Manage rooms, receive strokes, broadcast to room members, save canvas. Validates JWT signature **and** introspects `jti` against Redis on every connection. | 9002 (+9003 for 2nd instance) |
| **Load Balancer** | Pure routing: health-checks Auth replicas (failover) and Canvas Servers (room-based routing). **Never** talks to MySQL or Redis directly. | 9000 |
| **MySQL** | Persistent store for `users`, `rooms`, `room_members`, `canvas_snapshots`, `draw_actions`. Accessed only by Auth Servers (for users) and Canvas Servers (for rooms/snapshots). | 3306 |
| **Redis** | In-memory store for active sessions (`session:{jti}` with TTL = token expiry) and revocation blacklist. Written by Auth Servers, read by Canvas Servers. | 6379 |
| **Client** | WinForms GUI: canvas + toolbar + lobby. Always reaches Auth and Canvas through the Load Balancer. | — |

### 4.1.1 Three fixes baked into this design

**Fix 1 — Explicit MySQL access path for Auth Server.**
Both Auth Server replicas have a clearly drawn line to MySQL labeled *"Verify BCrypt hash (users table)"*. On login, the Auth Server runs `SELECT password_hash FROM users WHERE username = @u`, then `BCrypt.Verify(input, hash)` before issuing a JWT. The Load Balancer **does not** talk to MySQL — it only routes TCP traffic, which matches how real load balancers operate.

**Fix 2 — Stateful token validation via Redis stops forged/replayed token attacks.**
The original design had Canvas Server validate the JWT signature locally and accept it. That meant any attacker holding a still-signature-valid token (leaked secret, stolen token after logout, replayed old session) could connect directly to Canvas Server and bypass authentication. Fix:

- On successful login, Auth Server writes `session:{jti}` to Redis with TTL = token expiry.
- On every Canvas Server connection, **before** processing any draw operation, Canvas Server checks Redis: is this `jti` still active and not blacklisted? If not, drop the connection.
- On logout or password change, Auth Server deletes / blacklists `session:{jti}` in Redis. The token becomes useless everywhere immediately, even if its signature still verifies.

This is called **stateful token validation / token introspection** and is the standard solution for the exact attack the supervisor described.

**Fix 3 — Auth Server is no longer a single point of failure.**
Two Auth Server replicas (ports 9001 and 9011) sit behind the Load Balancer, sharing the same MySQL database and the same Redis store. Both load the **same JWT signing secret** from a shared config file, so a token issued by one is accepted by either Canvas Server. The Load Balancer health-checks both Auth nodes; if Auth Server 1 crashes, traffic automatically routes to Auth Server 2 and the system stays available. Scaling to 3+ Auth replicas later is a config change, nothing more.

### 4.2 Connection Flow (revised)

1. Client → **Load Balancer (TCP 9000)** with `AUTH_LOGIN {username, password}`.
2. LB picks a healthy Auth Server (1 or 2) and forwards.
3. Auth Server queries MySQL `users`, `BCrypt.Verify` → on success, generates JWT with a unique `jti` claim.
4. Auth Server writes `session:{jti}` to Redis with TTL = token expiry.
5. Auth Server returns `AUTH_RESPONSE {Success, Token, Message}` + room list to the client.
6. Client → LB with `ROOM_JOIN {RoomId, Token}` → LB routes to the correct Canvas Server (same room → same server).
7. Canvas Server: **(a)** validate JWT signature locally with shared secret, **(b)** check Redis `session:{jti}` is active and not blacklisted. If either check fails → reject.
8. Canvas Server sends full canvas state (`CANVAS_STATE`) to the new client.
9. Client renders via GDI+ → ready to draw.
10. Every stroke goes through LB → Canvas Server broadcasts to the rest of the room.
11. On logout (or password change), Auth Server removes `session:{jti}` from Redis → token invalidated everywhere instantly.
### 4.3 Thread Model — Server

| Thread / Task | Job | C# |
|---|---|---|
| Main | `TcpListener.AcceptTcpClientAsync()` accept loop | `async Task ListenAsync()` |
| ClientHandler | One Task per client: read NetworkStream, parse JSON, route to RoomManager | `Task.Run(() => HandleClient(client))` |
| BroadcastTask | Dequeue messages → send to all clients in room | `ConcurrentQueue<Message>` |
| AutoSaveTimer | Every 60s → save canvas snapshot to DB | `System.Timers.Timer` |
| HealthCheckTimer | Ping clients every 10s, detect disconnect | `Timer + async ping` |

### 4.4 Thread Model — Client (WinForms)

| Thread / Task | Job | WinForms note |
|---|---|---|
| UI Thread (main) | Render WinForms, mouse events, GDI+ on Panel | Use `Invoke()` from background threads |
| ReceiverTask | `Task.Run`: `StreamReader.ReadLineAsync` loop → `Invoke()` to UI | `this.Invoke((Action)(() => ...))` |
| SenderTask | Consumer of `BlockingCollection<Message>` → write to stream | |
| CursorTimer | `System.Windows.Forms.Timer` every 100ms: send cursor pos | UI thread Timer |

---

## 5. Protocol & Message Format

### 5.1 Message Class (in `CanvasApp.Common`)

```csharp
public class Message {
    public string Type { get; set; }        // "DRAW_MOVE", "ROOM_JOIN", ...
    public string RoomId { get; set; }
    public string UserId { get; set; }
    public long   Timestamp { get; set; }
    public JObject Data { get; set; }       // payload (Newtonsoft.Json.Linq.JObject)

    public string ToJson() => JsonConvert.SerializeObject(this);
    public static Message FromJson(string json)
        => JsonConvert.DeserializeObject<Message>(json);
}
```

**Wire format:** JSON + newline delimiter (`\n`). Read with `StreamReader.ReadLineAsync()`, write with `StreamWriter.WriteLine() + Flush()`.

### 5.2 Message Types

| Type | Direction | Description | Data fields |
|---|---|---|---|
| `AUTH_LOGIN` | Client → Auth | Login | `{Username, Password}` |
| `AUTH_REGISTER` | Client → Auth | Register | `{Username, Password, Email}` |
| `AUTH_RESPONSE` | Auth → Client | Auth result | `{Success, Token, Message}` |
| `ROOM_JOIN` | Client ↔ Server | Join room | `{RoomId, Token}` |
| `ROOM_LEAVE` | Client → Server | Leave room | `{RoomId}` |
| `ROOM_UPDATE` | Server → Clients | User list / room info update | `{Users:[], RoomInfo}` |
| `DRAW_START` | Client → Server | Begin a stroke | `{Tool, Color, Width, StartX, StartY}` |
| `DRAW_MOVE` | Client → Server | Next point in stroke | `{Points:[{X,Y},...]}` |
| `DRAW_END` | Client → Server | End stroke | `{EndX, EndY}` |
| `SHAPE` | Client → Server | Draw shape | `{ShapeType, X, Y, W, H, Color, Width, Fill}` |
| `TEXT` | Client → Server | Insert text | `{Text, X, Y, FontFamily, FontSize, Color}` |
| `UNDO` / `REDO` | Client → Server | Undo / redo | `{ActionId}` |
| `CLEAR` | Client → Server | Clear canvas (Owner only) | `{}` |
| `CURSOR_UPDATE` | Client → Server | Cursor position | `{X, Y, Username, Color}` |
| `CANVAS_STATE` | Server → Client | Full sync to new joiner | `{Strokes:[], Shapes:[]}` |
| `BROADCAST_DRAW` | Server → Clients | Re-broadcast a stroke | `DRAW_* + UserId + Color` |
| `PING` / `PONG` | Both | Health check | `{Timestamp}` |

### 5.3 Send/Receive Code

```csharp
// SEND
var writer = new StreamWriter(tcpClient.GetStream());
writer.WriteLine(message.ToJson());
writer.Flush();

// RECEIVE (in a separate Task)
var reader = new StreamReader(tcpClient.GetStream());
string line;
while ((line = await reader.ReadLineAsync()) != null) {
    var msg = Message.FromJson(line);
    this.Invoke((Action)(() => HandleMessage(msg)));  // WinForms UI thread
}
```

---

## 6. Database Schema (MySQL)

5 tables, all in database `canvas_app`.

```sql
CREATE TABLE users (
  id INT AUTO_INCREMENT PRIMARY KEY,
  username VARCHAR(50) UNIQUE NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  email VARCHAR(100),
  avatar_color VARCHAR(7) DEFAULT '#3498db',
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE rooms (
  id VARCHAR(36) PRIMARY KEY,          -- GUID
  name VARCHAR(100) NOT NULL,
  owner_id INT REFERENCES users(id),
  max_users INT DEFAULT 8,
  is_active BOOLEAN DEFAULT TRUE,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE room_members (
  room_id VARCHAR(36) REFERENCES rooms(id),
  user_id INT REFERENCES users(id),
  role ENUM('OWNER','MEMBER','VIEWER') DEFAULT 'MEMBER',
  joined_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (room_id, user_id)
);

CREATE TABLE canvas_snapshots (
  id INT AUTO_INCREMENT PRIMARY KEY,
  room_id VARCHAR(36) REFERENCES rooms(id),
  snapshot_data LONGTEXT,              -- JSON of full canvas
  version INT NOT NULL,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE draw_actions (
  id BIGINT AUTO_INCREMENT PRIMARY KEY,
  room_id VARCHAR(36),
  user_id INT,
  action_type VARCHAR(20),
  action_data TEXT,                    -- JSON
  timestamp BIGINT NOT NULL            -- for Replay
);
```

### Connection example (MySql.Data)

```csharp
string connStr = "server=localhost;database=canvas_app;uid=root;pwd=pass";
using var conn = new MySqlConnection(connStr);
conn.Open();

using var cmd = new MySqlCommand(
    "INSERT INTO users(username, password_hash) VALUES(@u, @h)", conn);
cmd.Parameters.AddWithValue("@u", username);
cmd.Parameters.AddWithValue("@h", BCrypt.Net.BCrypt.HashPassword(password));
cmd.ExecuteNonQuery();
```

**Never use** Entity Framework — the course wants raw `MySql.Data` to show SQL knowledge.

---

## 7. Security

| Concern | Implementation |
|---|---|
| **Password storage** | `BCrypt.Net.BCrypt.HashPassword(pwd, workFactor: 12)` → DB. Verify with `BCrypt.Verify(input, hash)`. |
| **Session token** | JWT HMAC-SHA256. Shared secret in `appsettings.json` between Auth and Canvas servers. Expiry 24h. |
| **Traffic encryption** | AES-256. Use `Aes.Create()`, encrypt JSON payload → Base64 → send. Demo with Wireshark to show encrypted bytes. |
| **3 roles** | `enum RoomRole { Owner, Member, Viewer }`. Server checks role **before** processing message. Owner: create/kick/clear. Member: draw + undo self. Viewer: read-only. |

---

## 8. UI Design

### 8.1 Forms

| Form | Controls | Function |
|---|---|---|
| `LoginForm` | TabControl (Login/Register), TextBox×2, Button, Label error, PictureBox logo | Authenticate → open LobbyForm |
| `LobbyForm` | FlowLayoutPanel of room cards, Buttons (Create / Refresh), profile label | List rooms → open CanvasForm |
| `CanvasForm` | Panel (canvas, DoubleBuffered), ToolStrip (tools), users+chat side panel, StatusStrip | Main drawing surface |
| `ExportDialog` | ComboBox (format), TrackBar (quality), preview PictureBox, Save button | Export canvas to PNG/JPEG |
| `CreateRoomDialog` | TextBox (name), ComboBox (max users), Button Create | Create new room |

### 8.2 Visual Theme

A reference HTML mockup exists in `UIproject.html` — translate its style into WinForms theming. Key tokens:

- Primary: `#6c5ce7` (purple)
- Primary dark: `#534ab7`
- Secondary: `#a29bfe`
- Background: `#f5f6fa`
- Danger: `#ff7675`
- Success: `#2ecc71`
- Warning: `#f1c40f`
- Logo: "CANVASAPP" wordmark with hexagonal "C" mark (gradient purple)

Layout convention: **toolbar on left, canvas in center, side panel on right** (users list + chat).

### 8.3 Canvas Drawing (GDI+ core pattern)

```csharp
private Panel canvasPanel;
private Bitmap canvasBitmap;
private Graphics canvasGraphics;

// Init
canvasPanel.DoubleBuffered = true;      // anti-flicker
canvasBitmap = new Bitmap(canvasPanel.Width, canvasPanel.Height);
canvasGraphics = Graphics.FromImage(canvasBitmap);
canvasGraphics.SmoothingMode = SmoothingMode.AntiAlias;

// Paint event
private void canvasPanel_Paint(object sender, PaintEventArgs e) {
    e.Graphics.DrawImage(canvasBitmap, 0, 0);
    DrawRemoteCursors(e.Graphics);
}

// Mouse pen
private void canvasPanel_MouseMove(object sender, MouseEventArgs e) {
    if (!isDrawing) return;
    using var pen = new Pen(currentColor, currentWidth);
    canvasGraphics.DrawLine(pen, lastPoint, e.Location);
    lastPoint = e.Location;
    canvasPanel.Invalidate();
    SendMessage(new Message { Type = "DRAW_MOVE", /* ... */ });
}
```

---

## 9. Drawing Tools to Implement

| Tool | Implementation |
|---|---|
| **Pen** | `Graphics.DrawLine` on MouseMove |
| **Eraser** | Draw with background color, or remove from stroke list |
| **Rectangle** | `DrawRectangle` / `FillRectangle` |
| **Ellipse / Circle** | `DrawEllipse` / `FillEllipse` |
| **Line** | `DrawLine` (start → end) |
| **Arrow** | Line + arrowhead polygon |
| **Text** | Click → overlay TextBox → on commit, `DrawString` onto bitmap |
| **Color picker** | `ColorDialog` |
| **Line width** | `TrackBar` |
| **Fill toggle** | `CheckBox` |
| **Undo / Redo** | `Stack<DrawAction>` (≥ 50 deep), clear+redraw bitmap |
| **Zoom / Pan** | `Graphics.Transform = new Matrix(); matrix.Scale(...)` |
| **Export PNG/JPG** | `canvas.DrawToBitmap(...)` → `bmp.Save(path, ImageFormat.Png)` |
| **Import background** | `Image.FromFile()` → draw in Paint event |

---

## 10. Creative Features (the 10-point block)

1. **Layer system** — Each user gets their own `Bitmap` layer. Toggle visibility, adjust opacity, render all layers stacked.
2. **Replay** — Persist all `DrawAction` rows in `draw_actions` table with timestamps. Replay via Timer that re-emits strokes in order with speed control.
3. **Templates** — Background style switcher: Blank, Grid, Dotted, Lined.
4. **File transfer** — Send images/documents to room (progress bar, save on receive).
5. Others to consider: Vote, room password, sticker palette.

---

## 11. Team Roles (3 members)

| Member | Role | Modules |
|---|---|---|
| **A (Hạnh)** | Lead + Server | `CanvasApp.Server` (RoomManager, broadcast, canvas state), `CanvasApp.LoadBalancer` (routing, health check), auto-save to DB, reconnect handling, Git merge owner. Criteria: server App Logic, Multi Server, Load Balancing, server threads. |
| **B (Nguyên)** | Client + Auth | `CanvasApp.Client` (CanvasForm GDI+, all drawing tools), `CanvasApp.AuthServer` (BCrypt + JWT), AES, Socket comm layer, Export PNG, cursor sync, keyboard shortcuts. Criteria: client App Logic, Sign up/Sign in, Cryptography, I/O. |
| **C (Quyên)** | UI + DB + Creative | LoginForm, LobbyForm, Room panels, StatusStrip, MySQL schema + DAO layer, Layer system, Replay feature, Templates, slides + report. Criteria: Database, UI, Creative, Multi Client (UI). |

---

## 12. 10-Week Plan & Current Status

| Week | Phase | Deliverable | Status |
|---|---|---|---|
| 1-2 | Setup & Core Network | Solution + NuGet + TCP basics + Message class + Auth Server (BCrypt + JWT) + MySQL schema + 2 clients can register/login | **Done** (foundation phase complete per mid-term report) |
| 3-4 | Canvas & Drawing Tools | All drawing tools, full sync, Undo/Redo | **Next** |
| 5 | Room System & Sync | RoomManager, LobbyForm, cursor display, PING/PONG, reconnect | Pending |
| 6 | Multi Server & Crypto | Split AuthServer to separate .exe, AES-256, auto-save | Pending |
| 7 | Load Balance + Creative | LoadBalancer.exe, Layer system, Replay | Pending |
| 8 | Integration & Bug Fix | End-to-end test, code review, optimization | Pending |
| 9 | Testing | LAN test, Internet test (ngrok), edge cases | Pending |
| 10 | Demo Prep | Slides, 15-20 page report, rehearsal | Pending |

### Current State (per mid-term report — `Báo_cáo_giữa_kì`)

The team has completed the **Foundation phase**:
- ✅ Solution with 5 sub-projects set up
- ✅ Initial project plan drafted
- ✅ MySQL schema designed
- ✅ Basic UI mockup (see `UIproject.html`)

**What still needs to be built:** the actual drawing engine, the real TCP protocol implementation, room management, sync logic, encryption, load balancer, and all creative features. Essentially everything from Week 3 onwards is code yet to be written.

---

## 13. Demo Plan (15-17 min)

| Time | Person | Content |
|---|---|---|
| 0-2' | A | Architecture slide: 4 nodes, TCP flow, thread model |
| 2-4' | B | Auth demo: open 3 Client.exe, register 3 accounts, login → receive token |
| 4-5' | C | Lobby demo: list rooms, create "Demo Room", 2 people join |
| 5-10' | B | Drawing demo: 3 people draw simultaneously — pen, shapes, text, color, undo/redo, see each other's cursors |
| 10-12' | C | Creative: Layer toggle, Replay, Export PNG |
| 12-13' | B | Security: Wireshark shows AES encrypted; MySQL shows BCrypt hash |
| 13-14' | A | Multi-server: run 2 Canvas.exe (9002, 9003) + LoadBalancer.exe. 2 rooms → different servers |
| 14-17' | All | Internet via ngrok. Q&A. |

### Common professor questions

- **Why TCP not UDP?** → TCP guarantees ordered delivery; losing a stroke would desync the canvas.
- **Why WinForms not WPF?** → Simpler, GDI+ is enough, .NET Framework supports it well.
- **Why not Entity Framework?** → Raw MySql.Data demonstrates SQL + connection management knowledge.

---

## 14. Source Files in This Project

Files an agent picking up this work can read for context:

| File | What's in it |
|---|---|
| `huong_dan_csharp.docx` | **Primary spec** — full 10-part Vietnamese guide: tech, architecture, protocol, criteria, DB, UI, schedule, team split, checklist, demo plan |
| `Báo_cáo_giữa_kì_Nhóm_14_...pdf` | Mid-term presentation slides (current progress summary) |
| `csharp_solution_folder_structure.pdf` | Exact intended folder/file layout for the Visual Studio solution, with task-owner assignments per file |
| `UIproject.html` | Interactive HTML mockup of the full UI (auth screen, lobby, canvas + floating toolbar). Use as the visual target when building WinForms. |

---

## 15. Next Concrete Actions for the Continuing Agent

If you're picking this up, here's the order to make progress:

1. **Verify the Solution skeleton compiles.** Open `CanvasApp.sln` in Visual Studio, install the 4 NuGet packages on the relevant projects, ensure all 5 projects reference `CanvasApp.Common` correctly.
2. **Implement `CanvasApp.Common`** first — `Message`, `DrawAction`, `User`, `Room` classes, the `MessageType` and `RoomRole` enums. Everything else depends on these.
3. **Implement Auth Server end-to-end** (port 9001): `TcpListener`, register (BCrypt hash → INSERT users), login (verify → JWT), respond with `AUTH_RESPONSE`. Test with a tiny TCP client.
4. **Implement minimum Canvas Server** (port 9002): accept TCP, parse `ROOM_JOIN`, `RoomManager` with `ConcurrentDictionary<string, Room>`, broadcast `DRAW_*` to room members except sender.
5. **Implement basic Client `CanvasForm`** with a Pen tool only — DoubleBuffered Panel + Bitmap + Graphics + Mouse events. Get one user drawing to a canvas locally, then wire it to send `DRAW_START`/`DRAW_MOVE`/`DRAW_END`.
6. **Get 2 clients drawing together** on the same room. That's the minimum viable thing. Everything else (shapes, undo, layers, replay, LB, AES) layers on top.
7. **Then** tackle: shapes/text → undo/redo → cursor sync → AES → AutoSave → LoadBalancer → creative features.

### Critical gotchas

- Every UI update from a background Task **must** go through `this.Invoke()` or the WinForms control will throw.
- Use `ConcurrentDictionary` / `BlockingCollection`, **never** `List<>` for shared server state.
- JSON is **newline-delimited**, so always `WriteLine` + `Flush` on send, `ReadLineAsync` on receive.
- BCrypt `workFactor: 12` is recommended — lower for tests if it slows things down.
- JWT shared secret must be **identical** in both Auth Server and Canvas Server `appsettings.json`, otherwise Canvas can't verify tokens.
- Disable Windows Firewall (or add inbound rules) when demoing LAN, or connections silently drop.

---

*End of handoff. Good luck — the foundation is in place, the spec is detailed, and the path from here to a complete project is well marked. The biggest remaining unknown is just execution time on the drawing + sync logic in weeks 3-5.*
