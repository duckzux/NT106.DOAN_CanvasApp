# CanvasApp — Demo Guide 

> 1. **Code evidence** — file + dòng cần mở sẵn trong Visual Studio 2022
> 2. **Demo runtime** — thao tác chạy thật
> 3. **Bằng chứng quan sát** — log console / record DB / packet Wireshark giảng viên có thể thấy
>
> **Mục lục**
> - [A. Chuẩn bị (làm 1 lần trước buổi demo)](#a-chuẩn-bị-làm-1-lần-trước-buổi-demo)
> - [B. Khởi động hệ thống](#b-khởi-động-hệ-thống)
> - [C. Demo từng mục rubric (10đ)](#c-demo-từng-mục-rubric-10đ)
> - [D. Phụ lục: Wireshark / AES / netstat / Troubleshooting / Checklist](#d-phụ-lục)

---

# A. Chuẩn bị 

## A.1. Bộ công cụ cần mở sẵn

| Công cụ | Mục đích | Mở bằng |
|---|---|---|
| **Visual Studio 2022** | Mở solution, build, debug, show code | Mở `CanvasApp.sln` |
| **XAMPP Control Panel** | Bật **MySQL** + (tùy chọn) **Apache** cho phpMyAdmin | Start menu → XAMPP |
| **phpMyAdmin** hoặc **MySQL Workbench** | Show database table + record | `http://localhost/phpmyadmin` |
| **PowerShell / cmd** | Chạy `ipconfig`, `netstat`, khởi `.exe` thủ công | `Win + R` → `powershell` |
| **Wireshark** (tùy chọn — extra credit I/O Network) | Bắt gói TCP để chứng minh truyền JSON qua mạng | [wireshark.org](https://www.wireshark.org/) (cài kèm **Npcap**, tick **"WinPcap API-compatible Mode"**) |

## A.2. Database

1. Mở **XAMPP** → Start **MySQL** (port 3306).
2. Vào phpMyAdmin → nếu chưa có DB `canvasapp`:
   - Import [Database/schema.sql](../Database/schema.sql)
   - Import [Database/seed_data.sql](../Database/seed_data.sql) (tùy chọn — tạo 3 user demo + 2 room mẫu)
3. Kiểm tra có **7 bảng**: `users`, `rooms`, `room_members`, `draw_actions`, `canvas_snapshots`, `chat_messages`, `email_otp_codes`.

> Cần reset dữ liệu giữa buổi demo? Import [Database/reset.sql](../Database/reset.sql).

## A.3. Build solution

- Trong Visual Studio: `Ctrl + Shift + B`. Output phải hiện `Build succeeded. 0 Error(s)`.
- Build xong các `.exe` nằm trong `bin/Debug/` của từng project — sẽ dùng ở phần B.2.

## A.4. Cấu hình "Multiple Startup Projects"

> Mục tiêu: 1 cú **F5** mở luôn 3 console (AuthServer + CanvasServer + LoadBalancer).

1. **Solution Explorer** → chuột phải `Solution 'CanvasApp'` → **Configure Startup Projects…**
2. Tick **Multiple startup projects** và set Action:

   | Project | Action |
   |---|---|
   | `CanvasApp.AuthServer` | **Start** |
   | `CanvasApp.Server` | **Start** |
   | `CanvasApp.LoadBalancer` | **Start** |
   | `CanvasApp.Client` | **None** (mở thủ công sau) |

3. Dùng mũi tên ▲/▼ để **đẩy LoadBalancer xuống cuối** (start sau cùng → khi LB lên thì 2 backend đã listening).
4. Trỏ Client về Load Balancer: mở [CanvasApp.Client/Network/Session.cs](../CanvasApp.Client/Network/Session.cs):
   ```csharp
   public const int AUTH_PORT   = 9000;   // đi qua LB
   public const int CANVAS_PORT = 9000;   // đi qua LB
   ```

## A.5. Cấu hình network cho demo qua LAN/VPN (chỉ khi demo cross-machine)

### A.5.1. Cùng mạng LAN/Wi-Fi
- Trên **máy Server**: `ipconfig` → ghi nhớ **IPv4 Address** (ví dụ `192.168.1.42`).
- Đảm bảo các listener bind `IPAddress.Any` (đã code sẵn, không cần sửa) — xem [Server/Program.cs](../CanvasApp.Server/Program.cs), [AuthServer/AuthServer.cs](../CanvasApp.AuthServer/AuthServer.cs), [LoadBalancer/LoadBalancer.cs](../CanvasApp.LoadBalancer/LoadBalancer.cs).
- Trên **máy Client**: sửa `Session.cs` → `LB_HOST = "192.168.1.42"` → rebuild.

### A.5.2. Khác mạng — qua VPN ảo
- Cài **Radmin VPN** / **Hamachi** / **ZeroTier** trên cả 2 máy → join cùng network ảo.
- Trên máy Server: `ipconfig` → tìm interface VPN → ghi nhớ IP dạng `26.x.x.x` (Radmin) / `25.x.x.x` (Hamachi).
- Client sửa `Session.cs` → trỏ về IP đó → rebuild.

### A.5.3. Mở Firewall (Windows)
- Search **"Windows Defender Firewall with Advanced Security"** → **Inbound Rules** → **New Rule…**
- Chọn **Port** → thêm: `9000, 9001, 9002, 9003, 9011, 9102, 9103` → **Allow the connection**.
- *(Cách nhanh test tạm: tắt firewall, nhớ bật lại sau).*

---

# B. Khởi động hệ thống

## B.1. F5 lần đầu — 3 console mở đúng

Nhấn **F5**. Visual Studio sẽ build (nếu cần) rồi mở **3 cửa sổ Console**:

### Console 1 — `AuthServer :9001`
```
[DB] DatabaseManager initialized
╔══════════════════════════════════════╗
║  AUTH SERVER listening on :9001      ║
╚══════════════════════════════════════╝
```

### Console 2 — `CanvasServer :9002`
```
[DB] DatabaseManager initialized
[DB] PersistenceQueue, AutoSaveService, and room state ready
╔══════════════════════════════════════╗
║   CANVAS SERVER listening on :9002    ║
╚══════════════════════════════════════╝
```

### Console 3 — `LoadBalancer :9000`
```
╔══════════════════════════════════════════════╗
║   LOAD BALANCER listening on :9000           ║
╠══════════════════════════════════════════════╣
║   Auth pool:   1 backend(s)                  ║
║   Canvas pool: 1 backend(s)                  ║
╚══════════════════════════════════════════════╝
   AUTH   • 127.0.0.1:9001
   CANVAS • 127.0.0.1:9002
   AUTH   • 127.0.0.1:9011        ← sẽ DOWN vì chưa start
   CANVAS • 127.0.0.1:9003        ← sẽ DOWN vì chưa start
```

Sau **~15 giây** (3 strike × ~5s mỗi probe), LB sẽ log:
```
[HEALTH] Auth 127.0.0.1:9011 probe 1/3 failed (...)
[HEALTH] Auth 127.0.0.1:9011 probe 2/3 failed (...)
[HEALTH] Auth 127.0.0.1:9011 -> DOWN (3 fails: ...)
```

> 💡 **Đây là demo Health Check 3-strike** — chụp màn hình đoạn này (sẽ dùng ở C.11).

Mỗi **15 giây**, LB in `[POOL]` snapshot trạng thái pool live:
```
[POOL]
   Auth   127.0.0.1:9001 [UP]   conns=0/100 rooms=0 fails=0
   Auth   127.0.0.1:9011 [DOWN] conns=0/100 rooms=0 fails=3
   Canvas 127.0.0.1:9002 [UP]   conns=0/100 rooms=0 fails=0
   Canvas 127.0.0.1:9003 [DOWN] conns=0/100 rooms=0 fails=3
```

## B.2. Chạy 2 instance phụ (AuthServer #2 + CanvasServer #2) qua `.exe` + CLI args

Port đọc từ **CLI args đầu tiên** (`args[0]`).

### AuthServer instance #2 (port 9011)
1. Mở **File Explorer** → `CanvasApp.AuthServer\bin\Debug\`.
2. **Shift + chuột phải** vùng trống → **Open PowerShell window here**.
3. Gõ: `.\CanvasApp.AuthServer.exe 9011`

### CanvasServer instance #2 (port 9003)
1. `CanvasApp.Server\bin\Debug\` tương tự.
2. Gõ: `.\CanvasApp.Server.exe 9003` (peer port sẽ tự = 9103).

Sau **≤5s** (1 chu kỳ health check), LB log:
```
[HEALTH] Auth 127.0.0.1:9011 -> UP (recovered)
[HEALTH] Canvas 127.0.0.1:9003 -> UP (recovered)
```

→ **Demo Health Check recovery** — chụp màn hình (sẽ dùng ở C.11).

## B.3. Script khởi động nhanh — `start-demo.ps1`

Tạo file `start-demo.ps1` ở root project:
```powershell
$root = $PSScriptRoot
$auth = "$root\CanvasApp.AuthServer\bin\Debug\CanvasApp.AuthServer.exe"
$canv = "$root\CanvasApp.Server\bin\Debug\CanvasApp.Server.exe"
$lb   = "$root\CanvasApp.LoadBalancer\bin\Debug\CanvasApp.LoadBalancer.exe"

Start-Process $auth -ArgumentList "9001"
Start-Process $auth -ArgumentList "9011"
Start-Sleep -Seconds 2
Start-Process $canv -ArgumentList "9002"
Start-Process $canv -ArgumentList "9003"
Start-Sleep -Seconds 2
Start-Process $lb

Write-Host "Pool started. Mở Client tay từ bin\Debug." -ForegroundColor Green
```

Chạy: **chuột phải file → Run with PowerShell** → toàn bộ pool 5 console bật lên trong ~5 giây.

---

# C. Demo từng mục rubric (10đ)

| # | Mục | Điểm | Section |
|---|---|---|---|
| 1 | App Logic + Socket Logic | 5.0 | [C.1](#c1-app-logic--socket-logic-50đ) |
| 2 | I/O File + Network | 0.5 | [C.2](#c2-io-file--network-05đ) |
| 3 | Database | 0.5 | [C.3](#c3-database-05đ) |
| 4 | Thread / Đa luồng | 0.5 | [C.4](#c4-thread--đa-luồng-05đ) |
| 5 | Sign up / Sign in / OTP / Forgot Password | 0.5 | [C.5](#c5-sign-up--sign-in--otp--forgot-password-05đ) |
| 6 | Multi Client | 0.5 | [C.6](#c6-multi-client-05đ) |
| 7 | Multi Server | 0.5 | [C.7](#c7-multi-server-05đ) |
| 8 | Cryptography | 0.5 | [C.8](#c8-cryptography-05đ) |
| 9 | Demo LAN | 0.5 | [C.9](#c9-demo-lan-05đ) |
| 10 | Demo Internet (VPN) | 0.5 | [C.10](#c10-demo-internet-vpn-05đ) |
| 11 | Load Balancing | 1.0 | [C.11](#c11-load-balancing-10đ) |
| **Tổng** | | **10.0** | |

---

## C.1. App Logic + Socket Logic (5.0đ)

### Code evidence — 4 layer

| Layer | File | Điểm nhấn |
|---|---|---|
| **TCP Listener** | [CanvasApp.Server/Program.cs:88](../CanvasApp.Server/Program.cs#L88) | `new TcpListener(IPAddress.Any, port)` + vòng `AcceptTcpClientAsync()` |
| **Per-client handler** | [CanvasApp.Server/Program.cs:285-352](../CanvasApp.Server/Program.cs#L285-L352) | `HandleClient` — 1 Task/client, không block |
| **Message dispatcher** | [CanvasApp.Server/Program.cs:354-728](../CanvasApp.Server/Program.cs#L354-L728) | `ProcessAsync` — switch case cho ROOM_*, DRAW_*, CHAT_*, AUTH_*, PING (12+ types) |
| **Client socket** | [CanvasApp.Client/Network/CanvasClient.cs](../CanvasApp.Client/Network/CanvasClient.cs) | `TcpClient`, `StreamReader/Writer`, line-delimited JSON, heartbeat 10s, reconnect exponential backoff, generation counter |
| **Domain logic** | [CanvasApp.Server/RoomManager.cs](../CanvasApp.Server/RoomManager.cs) | Quản lý phòng, broadcast, undo stack, snapshot delta, atomic delete |

### Demo runtime — "1 vòng đời của 1 nét vẽ"

1. **Client #1**: login `demo1` → **Tạo phòng** → vẽ 1 nét bằng Pen.
2. **Client #2**: login `demo2` → Join cùng phòng → thấy nét vẽ realtime.
3. **Chỉ tay vào code** cho giảng viên thấy flow:
   - Client #1 [CanvasClient.SendDrawAsync] → JSON `DRAW_END` lên LB :9000
   - LB peek `MessageType` → forward về Canvas Server [Program.cs ProcessAsync case DRAW_END]
   - Server gọi [RoomManager.RecordDrawAction] gắn SeqNo + ActionId → enqueue vào [PersistenceQueue]
   - Server [BroadcastAsync] → tất cả client trong phòng (kèm `PEER_RELAY` sang server khác)
   - Client #2 [CanvasClient.ReceiveLoop] nhận → vẽ lên bitmap

### Bằng chứng quan sát
- Console `CANVAS SERVER` log `[ROOM] DRAW_END from demo1 seq=N`
- Client #2 thấy nét vẽ trong < 100ms
- Wireshark filter `tcp contains "DRAW_"` → thấy chuỗi `DRAW_START → DRAW_MOVE × N → DRAW_END`

> 💡 **5đ này chiếm trọng số lớn nhất** — show kỹ flow + giải thích thiết kế single-join (`ROOM_RESOLVE` → direct TCP → một `ROOM_JOIN` duy nhất) thay vì double-join cũ.

---

## C.2. I/O File + Network (0.5đ)

### Code evidence

| Tính năng | File |
|---|---|
| **Đọc config JSON** | [LoadBalancer/Program.cs](../CanvasApp.LoadBalancer/Program.cs) — `File.ReadAllText("appsettings.json")` + `JsonConvert.DeserializeObject` |
| **Import ảnh nền** | [CanvasForm.cs](../CanvasApp.Client/Forms/CanvasForm.cs) — `OpenFileDialog` → `Image.FromFile()` |
| **Export Canvas → PNG/JPEG** | [CanvasForm.cs `btnExport`](../CanvasApp.Client/Forms/CanvasForm.cs) — `SaveFileDialog` + `bitmap.Save(path, ImageFormat.Png/Jpeg)` |
| **Image overlay sync (DRAW_IMAGE)** | [CanvasForm.Images.cs](../CanvasApp.Client/Forms/CanvasForm.Images.cs) — OpenFileDialog → base64 → broadcast |
| **Gửi file qua chat** | [CanvasForm.cs `SendChat`](../CanvasApp.Client/Forms/CanvasForm.cs) — `OpenFileDialog` (≤2MB) → Base64 → `CHAT_FILE` |
| **TCP NetworkStream** | [CanvasClient.cs](../CanvasApp.Client/Network/CanvasClient.cs) — `_client.GetStream()` → `StreamReader` (line-delimited JSON) + `StreamWriter` |

### Demo runtime

1. **Export ảnh**: Trong phòng → menu **Tệp → Xuất ảnh** → `.png` → mở file vừa lưu, đúng nội dung canvas.
2. **Import ảnh nền**: Menu **Tệp → Đặt ảnh nền** → chọn ảnh JPG → background canvas thay đổi.
3. **Image overlay**: Toolbar → nút Image → chọn ảnh → ảnh xuất hiện trên canvas, **kéo/scale** → Client #2 thấy ảnh di chuyển realtime (`DRAW_IMAGE_TRANSFORM`).
4. **Gửi file chat**: Ô chat → nút **+** → chọn PDF nhỏ → Client #2 thấy `📎 filename.pdf (12 KB)` kèm link tải → click → tải về.
5. **(Tùy chọn) Wireshark**: Filter `tcp.port == 9000` → vẽ 1 nét → thấy gói TCP có payload JSON `{"type":"DRAW_END",...}`. Chi tiết ở [phần D.1](#d1-wireshark--show-traffic-thật).

### Bằng chứng quan sát
- File PNG/PDF mở được bằng Windows Photo / Adobe.
- Console server log `[CHAT_FILE] demo1 sent design.png (102400 bytes)`.

---

## C.3. Database (0.5đ)

### Code evidence

| Layer | File |
|---|---|
| **Connection factory** | [DatabaseManager.cs](../CanvasApp.Common/DataAccess/DatabaseManager.cs) — `OpenConnection()`. **Lưu ý**: schema bootstrap nằm ở [Database/schema.sql](../Database/schema.sql), không phải trong code. |
| **DAOs (7 file)** | [DataAccess/](../CanvasApp.Common/DataAccess/) — UserDAO, RoomDAO, RoomMemberDAO, DrawActionDAO, CanvasSnapshotDAO, ChatMessageDAO, plus AuthServer's OtpStore |
| **Write-behind queue** | [PersistenceQueue.cs](../CanvasApp.Common/DataAccess/PersistenceQueue.cs) — `BlockingCollection<Action>` + 1 background consumer |
| **Schema** | [Database/schema.sql](../Database/schema.sql) — 7 bảng + index + `UNIQUE(email)` |
| **Migration** | [UserStore.MigrateSchema](../CanvasApp.AuthServer/UserStore.cs) — idempotent ALTER (vd add UNIQUE email) khi AuthServer start |

### Demo runtime

1. Mở **phpMyAdmin** → DB `canvasapp` → bảng `users` → có record từ register trước (`password_hash` dạng `$2b$12$...`).
2. **Login lại** Client #1 → refresh bảng `users` → cột `last_login_at` cập nhật timestamp mới.
3. **Vẽ vài nét** → đợi ≤60s → bảng `canvas_snapshots` có row mới (`version`, `snapshot_data` LONGTEXT GZip-base64, `byte_size`).
4. **Tắt cả server và Client**, **F5 lại** → join lại phòng cũ → canvas vẫn còn nguyên (`RoomManager.EnsureCanvasLoaded` đọc snapshot + delta từ DB).
5. Show bảng `email_otp_codes` → cột `code_hash` dạng `$2b$10$...` → OTP **không** lưu plaintext.

### Bằng chứng quan sát
- phpMyAdmin show đủ 7 bảng + record cụ thể.
- Restart pipeline vẫn giữ data (canvas, room, history, snapshot).

---

## C.4. Thread / Đa luồng (0.5đ)

### Code evidence — 4 pattern

| Pattern | File | Điểm nhấn |
|---|---|---|
| **`async/await` + Task per client** | [Server/Program.cs](../CanvasApp.Server/Program.cs) | `AcceptTcpClientAsync`, `HandleClient` mỗi connection = 1 task |
| **`Task.Run` + `BlockingCollection`** | [PersistenceQueue.cs](../CanvasApp.Common/DataAccess/PersistenceQueue.cs) | Producer-consumer write-behind queue, batch insert |
| **`System.Timers.Timer` định kỳ** | [AutoSaveService.cs](../CanvasApp.Server/AutoSaveService.cs) (snapshot mỗi 60s); [HealthChecker.cs](../CanvasApp.LoadBalancer/HealthChecker.cs) (probe mỗi 5s); [PeerManager.cs](../CanvasApp.Server/PeerManager.cs) (PEER_PING 15s) | Multiple timers chạy độc lập |
| **Thread-safe collections + lock** | [RoomManager.cs](../CanvasApp.Server/RoomManager.cs) | `ConcurrentDictionary`, `lock(list)` cho atomic delete, `SemaphoreSlim` (`_sendLock`) cho per-client serialize, `Interlocked` cho SeqNo |

### Demo runtime
1. Mở **3 Client** cùng lúc → cả 3 cùng vẽ → không lag/crash → mỗi client = 1 task riêng.
2. Console Canvas Server in `[PERSISTENCE] batched N actions in X ms` → write-behind queue chạy thread riêng.
3. Console LB in `[POOL]` snapshot mỗi 15s và `[HEALTH] probe` mỗi 5s → timer chạy độc lập với accept loop.
4. Console Canvas Server in `[PEER] PING -> canvas-9003 ... PONG received` → peer heartbeat task chạy riêng.

### Bằng chứng quan sát
- 3 client vẽ đồng thời mượt mà.
- Multiple timer log cùng xuất hiện trong console.
- Atomic counters cho SeqNo: vẽ liên tục, mỗi action có SeqNo duy nhất, tăng đơn điệu.

---

## C.5. Sign up / Sign in / OTP / Forgot Password (0.5đ)

### Code evidence

| Tính năng | File |
|---|---|
| **UI Register** | [RegisterForm.cs](../CanvasApp.Client/Forms/RegisterForm.cs) — gửi `AUTH_SEND_OTP` rồi `AUTH_REGISTER` với OTP |
| **UI Login** | [LoginForm.cs](../CanvasApp.Client/Forms/LoginForm.cs) |
| **UI Forgot Password** | [ForgotPasswordForm.cs](../CanvasApp.Client/Forms/ForgotPasswordForm.cs) — gửi `AUTH_FORGOT_SEND_OTP` rồi `AUTH_RESET_PASSWORD` |
| **UI OTP entry** | [OtpVerifyForm.cs](../CanvasApp.Client/Forms/OtpVerifyForm.cs) — shared modal cho cả 2 flow |
| **Validate input** | [UserService.cs](../CanvasApp.AuthServer/Services/UserService.cs) — username≥3, password≥6, email regex ≤100 chars |
| **BCrypt + DB** | [UserStore.cs](../CanvasApp.AuthServer/UserStore.cs) `HashPassword(req.Password, 12)`; `Verify()` |
| **OTP service** | [OtpService.cs](../CanvasApp.AuthServer/Services/OtpService.cs) — generate 6-digit, BCrypt cost 10, TTL 5min, single-use, rate-limit (≤1/min, ≤5/hour per email) |
| **SMTP gửi email** | [SmtpEmailSender.cs](../CanvasApp.AuthServer/Services/SmtpEmailSender.cs) |
| **Token issue/verify** | [UserStore.cs IssueToken/VerifyToken](../CanvasApp.AuthServer/UserStore.cs) — HMAC-SHA256 signed, TTL 24h, FixedTimeEquals |
| **Session client** | [Session.cs](../CanvasApp.Client/Network/Session.cs) — singleton `CurrentUser`, `Token` |

### Demo runtime

1. **Register**: Client → Register form → nhập `demo3 / 123456 / demo3@gm.uit.edu.vn` → bấm "Gửi mã OTP".
2. Check email (hoặc console AuthServer in OTP nếu dev mode) → nhập OTP → submit → tài khoản tạo.
3. Mở phpMyAdmin → bảng `users` → row mới, `password_hash` dạng `$2b$12$...` (không plaintext).
4. **Login sai password**: nhập `demo3 / wrong` → báo `Sai tên đăng nhập hoặc mật khẩu` (response time tương đương login đúng — constant-time, xem [C.8](#c8-cryptography-05đ)).
5. **Login đúng**: → vào lobby → `last_login_at` update.
6. **Forgot Password**:
   - Click **"Quên mật khẩu"** trên LoginForm → nhập email `demo3@gm.uit.edu.vn` → bấm "Gửi mã".
   - Nhận OTP → nhập + new password → submit → thành công.
   - Login lại với password mới → vào lobby OK.
7. **OTP rate-limit demo**:
   - Bấm "Gửi mã OTP" lần 1 → success.
   - Bấm lần 2 ngay → reject `Vui lòng đợi 1 phút trước khi yêu cầu mã mới`.
   - (Test): 5 lần khác nhau trong 1h → lần 6 reject `Đã yêu cầu quá nhiều mã trong 1 giờ`.
8. **Persistent state**: vẽ trong phòng → close Client → mở lại → login → join phòng cũ → canvas vẫn còn.

### Bằng chứng quan sát
- DB `users.password_hash` dạng `$2b$12$XYZ...` (60 ký tự).
- DB `email_otp_codes.code_hash` dạng `$2b$10$...`.
- Rate-limit reject thấy ngay trên UI.

---

## C.6. Multi Client (0.5đ)

### Code evidence
- [RoomManager.BroadcastAsync](../CanvasApp.Server/RoomManager.cs) — gửi message đến tất cả client trong phòng.
- [Server/Program.cs](../CanvasApp.Server/Program.cs) — gọi `BroadcastAsync` sau mỗi `DRAW_*`, `CHAT_MESSAGE`, `CHAT_FILE`, `ROOM_UPDATE`.
- Per-client send lock: [BroadcastService.cs](../CanvasApp.Server/BroadcastService.cs) — `SemaphoreSlim` chống interleaving bytes giữa các broadcast đồng thời.

### Demo runtime — 3 client cùng phòng
1. Mở **3 instance** `CanvasApp.Client.exe` (chuột phải project → Debug → Start New Instance × 3).
2. Login 3 user khác nhau (`demo1`, `demo2`, `demo3`) → cùng join 1 phòng.
3. Client #1 vẽ → #2 và #3 thấy realtime.
4. Client #2 gửi chat → cả #1 và #3 thấy (kể cả sender — đã fix chat self-echo từ 2026-05-22).
5. Right-panel **Danh sách thành viên** hiện 3 user với avatar màu khác nhau.
6. **Client #1 thoát** → #2, #3 thấy member list giảm còn 2 (broadcast `ROOM_UPDATE`).

### Bằng chứng quan sát
- 3 client cùng vẽ — mượt, không miss stroke.
- Chat hiển thị đồng bộ ở mọi client.
- Member list cập nhật realtime.

---

## C.7. Multi Server (0.5đ)

### Code evidence — 3 service độc lập + peer mesh

| Server | File chính | Port mặc định |
|---|---|---|
| **AuthServer** | [AuthServer.cs](../CanvasApp.AuthServer/AuthServer.cs), [Program.cs](../CanvasApp.AuthServer/Program.cs) | 9001 |
| **CanvasServer** | [Server/Program.cs](../CanvasApp.Server/Program.cs), [CanvasServer.cs](../CanvasApp.Server/CanvasServer.cs) | 9002 (client), 9102 (peer) |
| **LoadBalancer** | [LoadBalancer.cs](../CanvasApp.LoadBalancer/LoadBalancer.cs) | 9000 |
| **Peer mesh** | [PeerManager.cs](../CanvasApp.Server/PeerManager.cs) (outbound) + [PeerHandler.cs](../CanvasApp.Server/PeerHandler.cs) (inbound) | port = client port + 100 |

**LB tách 2 pool** dựa trên peek JSON message đầu:
- [LoadBalancer.cs](../CanvasApp.LoadBalancer/LoadBalancer.cs) — `_authPool` (AUTH_*), `_canvasPool` (ROOM_*, DRAW_*, CHAT_*)

### Demo runtime — 5 server đồng thời
1. F5 → 3 console mở (Auth :9001, Canvas :9002, LB :9000).
2. Chạy thủ công 2 instance phụ (xem [B.2](#b2-chạy-2-instance-phụ-authserver-2--canvasserver-2-qua-exe--cli-args)):
   ```powershell
   .\CanvasApp.AuthServer\bin\Debug\CanvasApp.AuthServer.exe 9011
   .\CanvasApp.Server\bin\Debug\CanvasApp.Server.exe 9003
   ```
3. Console LB hiện `Auth pool: 2 backend(s)` + `Canvas pool: 2 backend(s)` → **tổng 5 server**.
4. PowerShell:
   ```powershell
   netstat -ano | Select-String "LISTENING" | Select-String "9000|9001|9002|9003|9011|9102|9103"
   ```
   → thấy 7 dòng `LISTENING` (3 client port + 2 peer port + Auth + LB).

### Bằng chứng quan sát
- Banner LB hiện đủ pool 2+2.
- `netstat` confirm OS-level 5+ processes listening.
- Peer mesh log: `[PEER] handshake with canvas-9003 OK` ở console CanvasServer.

---

## C.8. Cryptography (0.5đ)

### Code evidence

| Cơ chế | File | Chú thích |
|---|---|---|
| **BCrypt password** (cost 12) | [UserStore.cs Register/Login](../CanvasApp.AuthServer/UserStore.cs) | Hash 1 chiều + salt tự sinh |
| **BCrypt room password** (cost 10) | [RoomManager.cs CreateRoom/UpdatePassword](../CanvasApp.Server/RoomManager.cs) | Phòng riêng tư |
| **HMAC-SHA256 token** | [UserStore.cs IssueToken/VerifyToken](../CanvasApp.AuthServer/UserStore.cs) | `base64(payload).base64(HMAC(payload, secret))` — chống forge |
| **Constant-time MAC compare** | [UserStore.cs `FixedTimeEquals`](../CanvasApp.AuthServer/UserStore.cs) | Verify MAC không leak timing |
| **TTL enforcement 24h** | [UserStore.cs VerifyToken](../CanvasApp.AuthServer/UserStore.cs) | Reject token > 24h |
| **Constant-time login** | [UserStore.cs Login](../CanvasApp.AuthServer/UserStore.cs) | Chạy BCrypt dummy khi username không tồn tại → chống user-enumeration via timing |
| **OTP BCrypt** (cost 10) | [OtpService.cs](../CanvasApp.AuthServer/Services/OtpService.cs) | OTP 6 số hash trước khi lưu |
| **OTP rate-limit** | [OtpService.CheckRateLimit](../CanvasApp.AuthServer/Services/OtpService.cs) | ≤1/email/min, ≤5/email/hour |
| **AES-256-CBC + HMAC-SHA256** (opt-in) | [AesHelper.cs](../CanvasApp.Common/Utils/AesHelper.cs) | Encrypt-then-MAC, IV‖CT‖MAC base64 |
| **Message envelope** | [MessageCrypto.cs](../CanvasApp.Common/Utils/MessageCrypto.cs) | Wrap `Message.Data` thành `{_enc, _v}`, `type` + `token` plaintext cho LB peek |

### Demo runtime
1. **BCrypt hash trong DB**: phpMyAdmin → bảng `users.password_hash` = `$2b$12$XYZ...` (60 ký tự, có salt) — không thể đảo ngược.
2. **HMAC token tamper**: copy token từ Session.cs lúc runtime → chỉnh 1 byte trong base64 phần signature → restart Client với token chỉnh → server reject `Token không hợp lệ`.
3. **Constant-time login**: dùng Stopwatch test login với username không tồn tại vs sai password — cả 2 đều ~100ms (BCrypt). **Trước fix**: username không tồn tại < 1ms → leak (đã sửa 2026-05-22).
4. **OTP rate-limit demo**: spam button "Gửi OTP" 2 lần liên tiếp → lần 2 reject `Vui lòng đợi 1 phút...`.
5. **AES module demo** (LINQPad / Console temp project): xem [phần D.2](#d2-aes-module--demo-encrypt--tamper).

### Bằng chứng quan sát
- DB BCrypt hash dạng `$2b$12$...`.
- Token tamper → reject visible trên client UI.
- OTP rate-limit reject visible.
- AES tamper → `CryptographicException: HMAC verification failed`.

### Trạng thái mã hóa wire protocol
- ✅ **BCrypt password + OTP storage** — enabled
- ✅ **HMAC token signature** — enabled
- ⚠️ **AES message encryption** — module built, **chưa wire vào wire protocol** (traffic JSON vẫn plaintext). Để bật: uncomment `MessageCrypto.EncryptInPlace` ở 5 file (AuthClient, LobbyClient, CanvasClient, AuthHandler, Server.Program). Cố ý tắt để demo dễ thấy JSON qua Wireshark.

---

## C.9. Demo LAN (0.5đ)

### Code evidence — listener bind `IPAddress.Any` (0.0.0.0)
- [CanvasApp.Server/Program.cs:88](../CanvasApp.Server/Program.cs#L88) — `new TcpListener(IPAddress.Any, port)`
- [CanvasApp.AuthServer/AuthServer.cs:18](../CanvasApp.AuthServer/AuthServer.cs#L18) — `IPAddress.Any`
- [CanvasApp.LoadBalancer/LoadBalancer.cs:62](../CanvasApp.LoadBalancer/LoadBalancer.cs#L62) — `IPAddress.Any`

→ **Listen trên tất cả interface**, không chỉ loopback.

### Demo runtime
1. **Máy A (Server)**: PowerShell → `ipconfig` → đọc **IPv4 Address** (vd `192.168.1.42`).
2. Tắt firewall tạm hoặc add Inbound Rule (xem [A.5.3](#a53-mở-firewall-windows)).
3. Máy A chạy server (F5).
4. **Máy B (Client) — cùng Wi-Fi**: sửa [Session.cs](../CanvasApp.Client/Network/Session.cs) → `LB_HOST = "192.168.1.42"` → rebuild → chạy Client.
5. Login từ máy B → join phòng do máy A tạo → vẽ → máy A thấy nét vẽ realtime.

### Bằng chứng quan sát
- PowerShell máy A: `netstat -ano | findstr :9000` → thấy connection `ESTABLISHED` với IP máy B.
- Canvas đồng bộ giữa 2 máy.

---

## C.10. Demo Internet (VPN) (0.5đ)

### Code evidence
- Cùng như [C.9](#c9-demo-lan-05đ) — không cần đổi code.

### Demo runtime — qua VPN ảo (Radmin / Hamachi)
1. Cài **Radmin VPN** trên cả máy A và máy B → tạo network mới → cả 2 join.
2. Máy A: `ipconfig` → tìm interface `Radmin VPN` → IP dạng `26.x.x.x`.
3. Máy A chạy server (F5).
4. Máy B: sửa `Session.cs` → `LB_HOST = "26.x.x.x"` → rebuild → chạy Client.
5. Máy B ở **mạng Internet khác hoàn toàn** (4G / Wi-Fi nhà khác) → vẫn login + vẽ được.

### Bằng chứng quan sát
- Máy B ping `26.x.x.x` thành công.
- Canvas đồng bộ realtime qua Internet.

> 💡 Nếu giảng viên có 2 máy ở 2 mạng khác nhau → demo này gây ấn tượng nhất. Quay video sẵn dự phòng.

---

## C.11. Load Balancing (1.0đ)

### Code evidence

| Tính năng | File | Điểm nhấn |
|---|---|---|
| **Peek JSON đầu** | [LoadBalancer.cs `ReadOneLineAsync`](../CanvasApp.LoadBalancer/LoadBalancer.cs) | Đọc byte-by-byte có deadline, parse `Message.Type`, strip UTF-8 BOM |
| **Routing 2 pool** | [LoadBalancer.cs](../CanvasApp.LoadBalancer/LoadBalancer.cs) | `_authPool` round-robin; `_canvasPool` room-affinity + least-loaded; `RouteForRoom` (sticky bind) vs `RouteForRoomReadOnly` (no bind on miss) |
| **Sniff ROOM_CREATE_RESULT** | [LoadBalancer.cs `SniffAndForwardAsync`](../CanvasApp.LoadBalancer/LoadBalancer.cs) | Extract `Room.Id` → `RegisterRoom` mapping |
| **Sniff ROOM_DELETE_RESULT** | [LoadBalancer.cs `SniffAndForwardAsync`](../CanvasApp.LoadBalancer/LoadBalancer.cs) | Trên `Success=true` → `UnregisterRoom` (giảm RoomCount) |
| **Health check 3-strike** | [HealthChecker.cs](../CanvasApp.LoadBalancer/HealthChecker.cs) | Probe TCP-connect mỗi 5s, fail 3 lần liên tiếp mới mark DOWN |
| **Atomic counters** | [ServerInfo.cs](../CanvasApp.LoadBalancer/ServerInfo.cs) | `Interlocked` cho `ActiveConnections`, `RoomCount`, `FailCount` |
| **Config động** | [appsettings.json](../CanvasApp.LoadBalancer/appsettings.json) | Sửa `MaxConnections` không cần rebuild |

### Demo runtime — 6 bằng chứng cần show

> Mỗi cái 1 screenshot/video. Đủ 6 → tròn 1.0đ.

#### Bằng chứng #1 — Banner LB khởi động đúng pool 2+2
- Bước [B.1](#b1-f5-lần-đầu--3-console-mở-đúng) + [B.2](#b2-chạy-2-instance-phụ-authserver-2--canvasserver-2-qua-exe--cli-args).
- Kỳ vọng:
  ```
  Auth pool:   2 backend(s)
  Canvas pool: 2 backend(s)
  ```

#### Bằng chứng #2 — Health Check 3-strike DOWN
- Kill 1 backend (Ctrl+C console của nó), đợi ~15s.
- Console LB:
  ```
  [HEALTH] Canvas 127.0.0.1:9002 probe 1/3 failed (...)
  [HEALTH] Canvas 127.0.0.1:9002 probe 2/3 failed (...)
  [HEALTH] Canvas 127.0.0.1:9002 -> DOWN (3 fails: ...)
  ```
- → **3-strike chống flap** khi mạng tạm chập 1 lần.

#### Bằng chứng #3 — Health Check `UP (recovered)`
- Restart backend đã chết:
  ```powershell
  .\CanvasApp.Server\bin\Debug\CanvasApp.Server.exe 9002
  ```
- Sau ≤5s, LB:
  ```
  [HEALTH] Canvas 127.0.0.1:9002 -> UP (recovered)
  ```

#### Bằng chứng #4 — Auth round-robin
- Mở Client #1 → login user A → console LB:
  ```
  [LB] 127.0.0.1:5xxxx -> Auth 127.0.0.1:9001 [UP] (first=AUTH_LOGIN)
  ```
- Mở Client #2 → login user B:
  ```
  [LB] 127.0.0.1:5yyyy -> Auth 127.0.0.1:9011 [UP] (first=AUTH_LOGIN)
  ```
- → 2 login đi 2 AuthServer khác.

#### Bằng chứng #5 — Canvas room-affinity stickiness
> ⚠️ Cần dùng **Join by Invite Code** để LB peek được `ROOM_JOIN_BY_CODE` từ message đầu. Lobby gửi `ROOM_LIST` trước → LB không biết RoomId nếu join từ lobby card thông thường, sẽ dùng least-loaded.

1. Client #1: login → **Tạo bảng trắng mới** → tạo room `ROOM-DEMO`.
   ```
   [LB] ... -> Canvas 127.0.0.1:9002 (first=ROOM_LIST)
   [ROUTE] room <RoomId> claimed by 127.0.0.1:9002 (sniff ROOM_CREATE_RESULT)
   ```
2. Trong room → bấm **Sao chép link mời** → copy invite code.
3. Client #2 (instance hoàn toàn mới): login → ngay khi vào lobby, paste invite code vào ô **Tham gia bằng mã** (KHÔNG click card).
4. Console LB:
   ```
   [ROUTE] room <RoomId> -> 127.0.0.1:9002 (rooms=1, sticky)
   [LB] ... -> Canvas 127.0.0.1:9002 ... (first=ROOM_JOIN_BY_CODE)
   ```
   → Client #2 route về **cùng** Canvas Server `9002` với Client #1.
5. Trong room: vẽ ở Client #1 → Client #2 thấy ngay → **canvas đồng bộ** → chứng minh affinity.

#### Bằng chứng #6 — Failover khi 1 Canvas Server chết
1. Đảm bảo cả 2 CanvasServer đang chạy, có client đang vẽ.
2. Tắt CanvasServer :9002 → LB log 3-strike DOWN (như #2) → drop mapping cho rooms host bởi 9002.
3. Client trên 9002 disconnect → reconnect logic kích → tạo room mới → LB route về `9003`:
   ```
   [LB] ... -> Canvas 127.0.0.1:9003 ...
   ```
4. Bật lại `9002`: sau ≤5s, LB log `UP (recovered)` → pool đầy đủ trở lại.

### Bonus demos (nếu còn thời gian)

**Cross-server sync** (đã có trong khi demo #5):
- 2 client → cùng phòng → khác Canvas Server (qua round-robin trước room-affinity) → Wireshark filter `tcp.port == 9102 or tcp.port == 9103` → thấy `PEER_RELAY` chạy giữa 2 canvas khi vẽ/chat.

**Owner xóa room → sync sang server khác**:
- Client #1 (owner) xóa room → console mọi peer log `[PEER_ROOM_DELETE] room X removed from local state`.
- Client #2 (đang ở lobby trên Canvas khác) → lobby refresh polling 3s → room biến mất.

**Room cleanup ref-counting**:
- Client cuối cùng leave/close → LB log:
  ```
  [LB] ... closed (backend 127.0.0.1:9002 conns=0)
  [ROUTE] room <RoomId> freed from 127.0.0.1:9002 (rooms=0)
  ```

**MaxConnections cap**:
- Sửa `appsettings.json` → `MaxConnections: 1` → restart LB.
- 3 Client cùng lúc → Client thứ 3 bị reject:
  ```
  [LB] ... rejected — 127.0.0.1:9003 at MaxConnections
  ```

---

# D. Phụ lục

## D.1. Wireshark — show traffic thật

> Mục tiêu: chứng minh client/server trao đổi JSON thật qua TCP, và 1 message đi qua LB → Auth/Canvas → response như thế nào.

### Setup
1. Cài **Wireshark + Npcap** (tick **"Install Npcap in WinPcap API-compatible Mode"** khi cài) — bắt buộc cho loopback 127.0.0.1.
2. Mở Wireshark → chọn interface **"Adapter for loopback traffic capture"**.
3. Display Filter:
   ```
   tcp.port in {9000 9001 9002 9003 9011 9102 9103}
   ```

### Bảng filter chi tiết

| Mục đích | Filter |
|---|---|
| Tất cả traffic CanvasApp | `tcp.port in {9000 9001 9002 9003 9011 9102 9103}` |
| Chỉ LB ↔ Client | `tcp.port == 9000` |
| Chỉ Auth pool | `tcp.port == 9001 or tcp.port == 9011` |
| Chỉ Canvas pool | `tcp.port == 9002 or tcp.port == 9003` |
| Peer mesh server-to-server | `tcp.port == 9102 or tcp.port == 9103` |
| Search 1 loại message | `tcp contains "ROOM_JOIN"`, `tcp contains "AUTH_LOGIN"`, … |
| Chỉ DRAW events | `tcp contains "DRAW_"` |
| Chỉ chat | `tcp contains "CHAT_MESSAGE"` |

### Follow TCP Stream
1. Click bất kỳ packet → **Right-click → Follow → TCP Stream**.
2. Popup hiện toàn bộ JSON request/response như đoạn chat:
   - **Đỏ** = client gửi
   - **Xanh dương** = server trả lời
3. Export: **File → Export Specified Packets** → `.pcapng` để nộp kèm báo cáo.

### 4 scenario nên show

| # | Demo | Filter / thao tác | Bằng chứng |
|---|------|-------------------|------------|
| 1 | Login qua LB → Auth | `tcp.port == 9000 or tcp.port == 9001` → Follow TCP Stream | Cùng `AUTH_LOGIN` xuất hiện 2 lần (LB là proxy plaintext) |
| 2 | Room affinity stickiness | `tcp.port == 9002 or tcp.port == 9003` — 2 client cùng vào 1 room | Cả 2 connection persistent đều đến cùng port |
| 3 | Peer mesh `PEER_RELAY` | `tcp.port == 9102 or tcp.port == 9103` — chat từ client A trên server khác client B | Có packet `{"type":"PEER_RELAY","data":{...,"Inner":{"type":"CHAT_MESSAGE",...}}}` |
| 4 | Draw realtime | `tcp contains "DRAW_"` — vẽ 1 nét | Thấy chuỗi `DRAW_START` → `DRAW_MOVE × N` → `DRAW_END` + server-assigned `actionId` |

Ví dụ thực tế (Auth flow):
```
{"type":"AUTH_LOGIN","data":{"Username":"ndln","Password":"123456"},"token":null}
                                                                                ← client
{"type":"AUTH_LOGIN_RESULT","data":{"Success":true,"Token":"eyJ...","User":{"Id":1,...}}}
                                                                                ← server
```

### Lưu ý
- Loopback đôi khi miss packet đầu → stop & start capture lại.
- `tcp.port` hoạt động ở mức packet, không hỗ trợ regex nội dung → dùng `tcp contains "..."` để search text.
- Nếu thầy hỏi "sao đọc được rõ JSON vậy?" → trả lời: **plaintext JSON over TCP** (chưa bật AES — xem [D.2](#d2-aes-module--demo-encrypt--tamper)).

## D.2. AES module — demo encrypt + tamper

> Module mã hóa AES-256-CBC + HMAC-SHA256 (Encrypt-then-MAC) sẵn sàng wire vào wire protocol. **Chưa enable trên live traffic** — feature phòng khi cần.

### Vị trí code

| File | Vai trò |
|---|---|
| [AesHelper.cs](../CanvasApp.Common/Utils/AesHelper.cs) | AES-256-CBC + HMAC-SHA256 implementation |
| [MessageCrypto.cs](../CanvasApp.Common/Utils/MessageCrypto.cs) | Wrap/unwrap `Message.Data` thành `{ _enc: "...", _v: 1 }` |
| [CryptoConfig.cs](../CanvasApp.Common/Utils/CryptoConfig.cs) | Process-wide key + toggle (ENV `CANVASAPP_AES_KEY`) |

### Format ciphertext
Output `EncryptString` = Base64 của:
```
[ IV (16 bytes) ][ Ciphertext (PKCS7 padded) ][ HMAC-SHA256 (32 bytes) ]
```
- **IV**: random mỗi lần encrypt (CBC mode → semantic security)
- **HMAC** = SHA256(IV ‖ Ciphertext) — verify TRƯỚC decrypt (Encrypt-then-MAC)
- **FixedTimeEquals**: compare MAC không leak timing

### Demo 1 — Encrypt + Decrypt + Tamper (LINQPad/Console)

```csharp
using CanvasApp.Common.Utils;
using System;

var key = AesHelper.KeyFromBase64("z9ZvBnQfX2YlS3o4nUvE3pK9XHN0V3FwS+rJqcXyB3o=");

// Encrypt
var plaintext = "{\"type\":\"AUTH_LOGIN\",\"data\":{\"Username\":\"ndln\",\"Password\":\"secret\"}}";
var ciphertext = AesHelper.EncryptString(plaintext, key);
Console.WriteLine($"Encrypted ({ciphertext.Length} chars base64):\n{ciphertext}");

// Decrypt
var decrypted = AesHelper.DecryptString(ciphertext, key);
Console.WriteLine($"\nDecrypted:\n{decrypted}");

// Tamper test
var tampered = ciphertext.Substring(0, ciphertext.Length - 4) + "AAAA";
try { AesHelper.DecryptString(tampered, key); }
catch (System.Security.Cryptography.CryptographicException ex)
{
    Console.WriteLine($"\n✅ Tampering caught: {ex.Message}");
}
```

Output:
```
Encrypted (124 chars base64):
yIAjVxqxJK6Xq7M9...+8h2QYpZCk3W0eaPfg==

Decrypted:
{"type":"AUTH_LOGIN","data":{"Username":"ndln","Password":"secret"}}

✅ Tampering caught: HMAC verification failed (tampered or wrong key)
```

> **Đoạn này show cho thầy/cô**:
> - Mỗi lần encrypt cùng plaintext ra base64 khác → IV random (semantic security).
> - Sửa 1 ký tự ciphertext → HMAC fail → message integrity được bảo vệ.

### Demo 2 — Envelope trên Message

```csharp
var msg = new Message("CHAT_MESSAGE",
    new { text = "hello secret world", userId = 1 },
    token: "eyJ...");

Console.WriteLine("Plaintext:\n" + msg.ToJson());
// {"type":"CHAT_MESSAGE","data":{"text":"hello secret world","userId":1},"token":"..."}

MessageCrypto.EncryptInPlace(msg, CryptoConfig.Key);
Console.WriteLine("\nEncrypted:\n" + msg.ToJson());
// {"type":"CHAT_MESSAGE","data":{"_enc":"yIAjVx...","_v":1},"token":"..."}

MessageCrypto.DecryptInPlace(msg, CryptoConfig.Key);
```

> **Điểm mấu chốt**:
> - `type` và `token` **không** mã hóa → LB vẫn peek route được + AuthServer vẫn verify token được.
> - Chỉ `data` (chứa password, chat, draw actions) được mã hóa.
> - Versioned (`_v: 1`) để backward compatible khi nâng cấp format.

### Demo 3 — So sánh trên Wireshark
- **Chưa bật AES**:
  ```
  {"type":"AUTH_LOGIN","data":{"Username":"ndln","Password":"secret123"}}
  ```
- **Sau khi bật AES**:
  ```
  {"type":"AUTH_LOGIN","data":{"_enc":"yIAjVxqxJK6X...","_v":1}}
  ```
→ Username/Password **không đọc được trên dây** nữa. `type` còn plaintext để LB peek route.

### Wire AES vào prod (nếu cần demo full encrypted)
Chỉ cần sửa **AuthClient.cs** + **CanvasClient.cs** + **LobbyClient.cs** + **AuthHandler.cs** + **Server/Program.cs** — thêm 2 dòng mỗi chỗ:

**Outbound** (gửi):
```csharp
if (CryptoConfig.EncryptionEnabled)
    MessageCrypto.EncryptInPlace(msg, CryptoConfig.Key);
await writer.WriteLineAsync(msg.ToJson());
```

**Inbound** (nhận):
```csharp
var msg = Message.FromJson(line);
if (CryptoConfig.EncryptionEnabled && msg.Data != null)
    MessageCrypto.DecryptInPlace(msg, CryptoConfig.Key);
```

## D.3. netstat — show số connection trực quan

```powershell
# Đếm connection client đang giữ với từng canvas server
netstat -ano | findstr ":9002 :9003" | findstr ESTABLISHED

# Tổng connection theo port
netstat -ano | findstr ":900" | Measure-Object

# Xem các port đang LISTEN
netstat -ano | Select-String "LISTENING" | Select-String "9000|9001|9002|9003|9011"

# Xem connection đến LB :9000
netstat -ano | Select-String ":9000"
```

Dùng để show: client #1 → :9002, client #2 → :9003 (round-robin) hoặc cả 2 → cùng :9002 (room-affinity sau khi join cùng phòng).

## D.4. Troubleshooting

| Triệu chứng | Nguyên nhân | Cách fix |
|---|---|---|
| LB banner báo `Canvas pool: 0` rồi exit | Chưa start CanvasServer / port sai | Start CanvasServer trước, hoặc sửa `appsettings.json` của LB |
| Tất cả backend đều `DOWN` ngay khi LB lên | Firewall chặn loopback hoặc port đã bị process khác chiếm | `netstat -ano \| findstr :9002` để xem ai giữ port. Kill process |
| Client login bị treo, LB log `peek error: timeout` | Client không gửi message JSON nào trong 5s | Check `Session.cs` đã đổi sang `:9000` chưa; check Client connect đúng host |
| `dropped — no first message in 5000ms` | Client mở socket mà không gửi gì | Kiểm tra phía Client |
| Room-affinity không kick in (Client #2 về server khác) | Client gửi `ROOM_LIST` trước thay vì `ROOM_JOIN_BY_CODE` | Dùng đúng Join-by-Code flow; hoặc accept giới hạn và demo least-loaded |
| `CanvasApp.Common.dll` bị lock khi build | Có instance Client/Server còn chạy | `Get-Process CanvasApp.* \| Stop-Process -Force` |
| `bin\Debug\` không có `.exe` | Chưa build / build Release | `Ctrl+Shift+B` rebuild; check Configuration là `Debug` |
| Login lỗi `Token không hợp lệ hoặc đã hết hạn` | Pre-2026-05-22 token không có signature | Re-login (HMAC migration) |
| OTP không gửi đến email | SMTP config sai trong AuthServer `App.config` | Check log AuthServer; xem OTP trong console (dev mode in plaintext) |

## D.5. Self-grading checklist

| # | Mục | Điểm | Đã demo? | Bằng chứng chính |
|---|---|---|---|---|
| 1 | App Logic + Socket Logic | 5.0 | ☐ | 3 client cùng phòng vẽ realtime + Wireshark DRAW_* flow |
| 2 | I/O File + Network | 0.5 | ☐ | Export PNG + gửi file chat + image overlay + (Wireshark TCP) |
| 3 | Database | 0.5 | ☐ | phpMyAdmin 7 bảng + restart server vẫn còn data |
| 4 | Thread / Đa luồng | 0.5 | ☐ | 3 client vẽ + console batched persistence + timer logs |
| 5 | Sign up / Sign in / OTP / Forgot Password | 0.5 | ☐ | Register có OTP, DB hash BCrypt, OTP rate-limit reject |
| 6 | Multi Client | 0.5 | ☐ | 3 client cùng phòng, vẽ + chat + member list sync |
| 7 | Multi Server | 0.5 | ☐ | 5 server LISTENING (netstat) + peer mesh log |
| 8 | Cryptography | 0.5 | ☐ | DB `password_hash` `$2b$12$...` + token tamper reject + AES demo |
| 9 | Demo LAN | 0.5 | ☐ | 2 máy cùng Wi-Fi vẽ realtime |
| 10 | Demo Internet | 0.5 | ☐ | 2 máy qua Radmin VPN từ 2 mạng khác |
| 11 | Load Balancing | 1.0 | ☐ | 6 screenshot ở [C.11](#c11-load-balancing-10đ) |
| **Tổng** | | **10.0** | | |

## D.6. PowerShell quick commands

```powershell
# Xem IP máy hiện tại (LAN/VPN demo)
ipconfig | Select-String "IPv4"

# Xem các port đang LISTEN (chứng minh 5 server đang chạy)
netstat -ano | Select-String "LISTENING" | Select-String "9000|9001|9002|9003|9011"

# Đếm số connection đến LB :9000
netstat -ano | Select-String ":9000" | Measure-Object

# Kill mọi instance CanvasApp (giải lock DLL trước khi rebuild)
Get-Process CanvasApp.* -ErrorAction SilentlyContinue | Stop-Process -Force

# Tail log file (nếu redirect console ra file)
Get-Content lb.log -Wait | Select-String "ROUTE|HEALTH|POOL"
```

## D.7. Suggested presentation flow (~25 phút)

| Bước | Show gì | Tool | Mục rubric |
|---|---|---|---|
| 1 | Mở đầu — kiến trúc 5 component | Slides + Docs/README.md | — |
| 2 | Login flow real-time | Wireshark Follow TCP Stream (port 9000 + 9001) | C.1, C.5, C.8 |
| 3 | Register OTP + Forgot Password | UI + DB email_otp_codes + rate-limit reject | C.5 |
| 4 | Tạo room + LB sniff | LB console `[ROUTE] claim on create` | C.7, C.11 |
| 5 | Multi-client join cùng room | netstat (cả 2 connect cùng port 9002) + Wireshark | C.6, C.11 |
| 6 | Vẽ broadcast realtime | Wireshark filter `tcp contains "DRAW_"` + UI 3 client | C.1, C.6 |
| 7 | Image overlay drag/scale sync | UI 2 client (DRAW_IMAGE + DRAW_IMAGE_TRANSFORM) | C.2 |
| 8 | Chat duplicate fix (history vs live) | Code: ChatHistory embedded in ROOM_JOIN_RESULT | C.1 |
| 9 | Owner xóa room → sync sang server khác | Wireshark `tcp contains "PEER_ROOM_DELETE"` | C.7, C.11 |
| 10 | Failover khi kill Canvas Server | LB console 3-strike + reconnect | C.11 |
| 11 | DB persistence: restart server vẫn còn canvas | phpMyAdmin + UI | C.3 |
| 12 | AES module demo | LINQPad: encrypt/decrypt/tamper | C.8 |
| 13 | LAN/VPN demo (nếu có 2 máy) | 2 laptop + Radmin VPN | C.9, C.10 |
| 14 | Q&A — show source code | VS Code | — |
