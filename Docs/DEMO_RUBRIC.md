# Hướng dẫn Demo theo Checklist Rubric (CanvasApp)


> 1. **Code evidence** — file + dòng cần mở sẵn trong Visual Studio 2022 (icon tím)
> 2. **Demo runtime** — thao tác chạy thật
> 3. **Bằng chứng quan sát** — log console / packet / record DB mà giảng viên thấy
>
> Các phần dùng chung (cấu hình LAN, build, demo Load Balancer chi tiết) đã ghi ở [DEMO_GUIDE.md](DEMO_GUIDE.md) — file này tham chiếu sang khi cần.

---

## 0. Chuẩn bị (làm 1 lần trước buổi demo)

### 0.1. Bộ công cụ cần mở sẵn
| Công cụ | Mục đích | Mở bằng |
|---|---|---|
| **Visual Studio 2022** (icon tím) | Mở solution, build, debug, show code | Mở file `CanvasApp.sln` |
| **XAMPP Control Panel** | Bật **MySQL** + (tùy chọn **Apache** cho phpMyAdmin) | Start menu → XAMPP |
| **phpMyAdmin** hoặc **MySQL Workbench** | Show database table + record | `http://localhost/phpmyadmin` |
| **PowerShell / cmd** | Chạy `ipconfig`, `netstat`, khởi `.exe` thủ công | `Win + R` → `powershell` |
| **Wireshark** (tùy chọn — mục I/O Network) | Bắt gói TCP để chứng minh truyền qua mạng | Cài từ [wireshark.org](https://www.wireshark.org/) |

### 0.2. Database
- Mở **XAMPP** → Start **MySQL** (port 3306).
- Vào phpMyAdmin → nếu chưa có DB `canvasapp` thì import [Database/schema.sql](../Database/schema.sql) + [Database/seed_data.sql](../Database/seed_data.sql).
- Kiểm tra có 6 bảng: `users`, `rooms`, `room_members`, `draw_actions`, `canvas_snapshots`, `chat_messages`.

### 0.3. Build solution
- Trong Visual Studio: `Ctrl + Shift + B`. Output phải hiện `Build succeeded. 0 Error(s)`.

### 0.4. Cấu hình startup
- Solution Explorer → chuột phải **Solution 'CanvasApp'** → **Configure Startup Projects…**
- Tick **Multiple startup projects** và set Action:
  - `CanvasApp.AuthServer` = **Start**
  - `CanvasApp.Server` = **Start**
  - `CanvasApp.LoadBalancer` = **Start**
  - `CanvasApp.Client` = **None** (mở thủ công sau)

> Khi đã hoàn thiện chuẩn bị, F5 sẽ mở 3 console server. Client sẽ mở 2–3 instance thủ công.

---

## 1. App Logic + Socket Logic (5đ)

### 1.1. Code evidence — show 4 layer rõ ràng
| Layer | File | Điểm nhấn |
|---|---|---|
| **TCP Listener** | [CanvasApp.Server/Program.cs:88](../CanvasApp.Server/Program.cs#L88) | `new TcpListener(IPAddress.Any, port)` + vòng `AcceptTcpClientAsync()` |
| **Per-client handler** | [CanvasApp.Server/Program.cs:127-415](../CanvasApp.Server/Program.cs#L127-L415) | `HandleClient()` — dispatcher 12+ message types (ROOM_*, DRAW_*, CHAT_*, AUTH_*, PING) |
| **Client socket** | [CanvasApp.Client/Network/CanvasClient.cs](../CanvasApp.Client/Network/CanvasClient.cs) | `TcpClient`, `StreamReader/Writer`, line-delimited JSON, heartbeat 10s, reconnect exponential backoff |
| **Domain logic** | [CanvasApp.Server/RoomManager.cs](../CanvasApp.Server/RoomManager.cs) | Quản lý phòng, broadcast, undo stack, snapshot delta, room-affinity |

### 1.2. Demo runtime — "1 vòng đời" của 1 nét vẽ
1. **Mở Client #1** (login user `demo1`) → **Tạo phòng** → vẽ 1 nét bằng Pen.
2. **Mở Client #2** (login user `demo2`) → Join cùng phòng → thấy nét vẽ realtime.
3. **Highlight cho giảng viên** flow này (chỉ tay vào code):
   - Client #1 [CanvasClient.cs SendDrawAsync] → JSON `DRAW_END` lên LB :9000
   - LB peek `MessageType` → forward về Canvas Server [Program.cs HandleClient case DRAW_END]
   - Server gọi [RoomManager.RecordDrawAction()] gắn SeqNo → enqueue vào [PersistenceQueue]
   - Server [BroadcastAsync] → tất cả client trong phòng (trừ sender)
   - Client #2 [CanvasClient.ReceiveLoop] nhận → `DrawActionLocal()` vẽ lên bitmap

### 1.3. Bằng chứng giảng viên thấy
- Console `CANVAS SERVER` in `[ROOM] ... DRAW_END from user demo1`
- Client #2 thấy nét vẽ trong < 100 ms.

---

## 2. I/O File / Network (0,5đ)

### 2.1. I/O File — 4 use-case sẵn có
| Tính năng | File | Dòng |
|---|---|---|
| **Đọc config JSON** | [CanvasApp.LoadBalancer/Program.cs](../CanvasApp.LoadBalancer/Program.cs) | `File.ReadAllText("appsettings.json")` + `JsonConvert.DeserializeObject` |
| **Import ảnh nền** | [CanvasApp.Client/Forms/CanvasForm.cs:115](../CanvasApp.Client/Forms/CanvasForm.cs#L115) | `OpenFileDialog` → `Image.FromFile()` |
| **Export Canvas → PNG/JPEG** | [CanvasApp.Client/Forms/CanvasForm.cs:129](../CanvasApp.Client/Forms/CanvasForm.cs#L129) | `SaveFileDialog` + `bitmap.Save(path, ImageFormat.Png/Jpeg)` |
| **Gửi file qua chat** | [CanvasApp.Client/Forms/CanvasForm.cs:1030](../CanvasApp.Client/Forms/CanvasForm.cs#L1030) | `OpenFileDialog` (giới hạn 2 MB) → Base64 → `CHAT_FILE` |

### 2.2. I/O Network — TCP NetworkStream
- [CanvasApp.Client/Network/CanvasClient.cs](../CanvasApp.Client/Network/CanvasClient.cs) — `_client.GetStream()` → `StreamReader` (đọc line-delimited JSON) + `StreamWriter` (gửi message).

### 2.3. Demo runtime
1. **Export ảnh**: Trong phòng → menu **Tệp → Xuất ảnh** → chọn `.png` → mở file vừa lưu, thấy đúng nội dung canvas.
2. **Import ảnh nền**: Menu **Tệp → Đặt ảnh nền** → chọn 1 ảnh JPG → ảnh hiển thị làm background canvas.
3. **Gửi file chat**: Ô chat → nút **+** (đính kèm) → chọn 1 file PDF nhỏ → Client #2 thấy `📎 filename.pdf (12 KB)` kèm link tải → click → tải về.
4. **(Tùy chọn) Wireshark**: Filter `tcp.port == 9000` → vẽ 1 nét trên Client → thấy gói TCP có payload JSON `{"Type":"DRAW_END",...}`.

---

## 3. Database (0,5đ)

### 3.1. Code evidence
| Layer | File |
|---|---|
| **Connection factory** | [CanvasApp.Common/DataAccess/DatabaseManager.cs](../CanvasApp.Common/DataAccess/DatabaseManager.cs) — `OpenConnection()` |
| **DAOs** (6 file) | [CanvasApp.Common/DataAccess/](../CanvasApp.Common/DataAccess/) — UserDAO, RoomDAO, RoomMemberDAO, DrawActionDAO, CanvasSnapshotDAO, ChatMessageDAO |
| **Write-behind queue** | [CanvasApp.Common/DataAccess/PersistenceQueue.cs](../CanvasApp.Common/DataAccess/PersistenceQueue.cs) — batch 200 actions/transaction |
| **Schema** | [Database/schema.sql](../Database/schema.sql) — 6 bảng + index |
| **DDL khởi tạo** | [CanvasApp.AuthServer/UserStore.cs](../CanvasApp.AuthServer/UserStore.cs) — `InitializeDatabase()` + `MigrateSchema()` |

### 3.2. Demo runtime
1. Mở **phpMyAdmin** → DB `canvasapp` → bảng `users` → có record từ lần register trước (cột `username`, `password_hash` BCrypt dạng `$2b$12$...`).
2. **Login lại** Client #1 → refresh bảng `users` → cột `last_login_at` cập nhật timestamp mới.
3. **Vẽ vài nét** → đợi ≤60 s → bảng `canvas_snapshots` xuất hiện row mới (cột `version`, `compressed_data` BLOB, `byte_size`).
4. **Tắt cả server và Client**, sau đó **F5 lại** → join lại phòng cũ → canvas vẫn còn nguyên (vì load từ DB qua [RoomManager.EnsureCanvasLoaded()](../CanvasApp.Server/RoomManager.cs)).

---

## 4. Thread / Đa luồng (0,5đ)

### 4.1. Code evidence — 4 pattern threading
| Pattern | File | Điểm nhấn |
|---|---|---|
| **`async/await` + Task** | [CanvasApp.Server/Program.cs](../CanvasApp.Server/Program.cs) | `AcceptTcpClientAsync`, `HandleClientAsync` — 1 task/client, không block |
| **`Task.Run` + `BlockingCollection`** | [CanvasApp.Common/DataAccess/PersistenceQueue.cs](../CanvasApp.Common/DataAccess/PersistenceQueue.cs) | Producer-consumer write-behind queue |
| **`Timer` định kỳ** | [CanvasApp.Server/AutoSaveService.cs](../CanvasApp.Server/AutoSaveService.cs) | Snapshot mỗi 60 s; [HealthChecker.cs](../CanvasApp.LoadBalancer/HealthChecker.cs) probe mỗi 5 s |
| **Thread-safe collections** | [CanvasApp.Server/RoomManager.cs](../CanvasApp.Server/RoomManager.cs) | `ConcurrentDictionary`, `lock`, `Interlocked.Increment` cho SeqNo |

### 4.2. Demo runtime
1. Mở **3 Client** cùng lúc → cả 3 cùng vẽ → không bị lag/crash → chứng tỏ mỗi client chạy 1 task riêng (`HandleClientAsync`).
2. Console Canvas Server in `[PERSISTENCE] batched N actions in X ms` → chứng minh PersistenceQueue chạy thread riêng.
3. Console LB in `[POOL]` snapshot mỗi 15 s và `[HEALTH]` probe mỗi 5 s → chứng minh HealthChecker timer task chạy độc lập.

---

## 5. Sign up / Sign in / Lưu trạng thái (0,5đ)

### 5.1. Code evidence
| Tính năng | File |
|---|---|
| **UI Register** | [CanvasApp.Client/Forms/RegisterForm.cs](../CanvasApp.Client/Forms/RegisterForm.cs) |
| **UI Login** | [CanvasApp.Client/Forms/LoginForm.cs](../CanvasApp.Client/Forms/LoginForm.cs) |
| **Validate input** | [CanvasApp.AuthServer/Services/UserService.cs](../CanvasApp.AuthServer/Services/UserService.cs) — username ≥3, password ≥6 |
| **BCrypt + DB** | [CanvasApp.AuthServer/UserStore.cs:164](../CanvasApp.AuthServer/UserStore.cs#L164) `HashPassword(req.Password, 12)`; [:202](../CanvasApp.AuthServer/UserStore.cs#L202) `Verify()` |
| **Cấp token** | [CanvasApp.AuthServer/Services/TokenService.cs](../CanvasApp.AuthServer/Services/TokenService.cs) — Base64(`id:username:unixTs`), TTL 24 h |
| **Lưu state phía client** | [CanvasApp.Client/Network/Session.cs](../CanvasApp.Client/Network/Session.cs) — singleton `CurrentUser`, `Token` |

### 5.2. Demo runtime
1. **Register**: Client → Register form → nhập user mới `demo3 / 123456` → submit.
2. Mở phpMyAdmin → bảng `users` → thấy row mới, cột `password_hash` đã hash dạng `$2b$12$...` (không lưu plaintext).
3. **Login sai password**: nhập `demo3 / wrong` → báo lỗi `Sai tên đăng nhập hoặc mật khẩu`.
4. **Login đúng**: → vào lobby → cột `last_login_at` được update.
5. **Persistent state**: vẽ trong phòng → close Client → mở lại → login lại → join cùng phòng → canvas vẫn còn (nhờ DB) + danh sách phòng vẫn còn (nhờ `rooms.is_active = TRUE`).

---

## 6. Multi Client (0,5đ)

### 6.1. Code evidence
- [CanvasApp.Server/RoomManager.cs](../CanvasApp.Server/RoomManager.cs) — `BroadcastAsync(roomId, msg, except=sender)` gửi đến tất cả client trong phòng.
- [CanvasApp.Server/Program.cs](../CanvasApp.Server/Program.cs) — gọi `BroadcastAsync` sau mỗi `DRAW_*`, `CHAT_MESSAGE`, `CHAT_FILE`, `ROOM_UPDATE`.

### 6.2. Demo runtime — **3 client cùng phòng**
1. Mở **3 instance** `CanvasApp.Client.exe` (chuột phải project Client → Debug → Start New Instance, làm 3 lần).
2. Login 3 user khác nhau (`demo1`, `demo2`, `demo3`) → cùng join 1 phòng.
3. Client #1 vẽ → cả #2 và #3 thấy realtime.
4. Client #2 gửi tin nhắn chat → cả #1 và #3 thấy.
5. Right-panel **Danh sách thành viên** hiện 3 user với avatar màu khác nhau.
6. **Client #1 thoát** → #2, #3 thấy member list giảm còn 2 (broadcast `ROOM_UPDATE`).

---

## 7. Multi Server (0,5đ)

### 7.1. Code evidence — 3 service độc lập
| Server | File chính | Port mặc định |
|---|---|---|
| **AuthServer** | [CanvasApp.AuthServer/AuthServer.cs](../CanvasApp.AuthServer/AuthServer.cs), [Program.cs](../CanvasApp.AuthServer/Program.cs) | 9001 |
| **CanvasServer** | [CanvasApp.Server/Program.cs](../CanvasApp.Server/Program.cs) | 9002 |
| **LoadBalancer** | [CanvasApp.LoadBalancer/LoadBalancer.cs](../CanvasApp.LoadBalancer/LoadBalancer.cs) | 9000 |

**LB tách 2 pool** dựa trên peek JSON message đầu:
- [CanvasApp.LoadBalancer/LoadBalancer.cs](../CanvasApp.LoadBalancer/LoadBalancer.cs) — `_authPool` (cho `AUTH_*`), `_canvasPool` (cho `ROOM_*`, `DRAW_*`)

### 7.2. Demo runtime — **5 server đồng thời**
1. F5 → 3 console mở: AuthServer :9001, CanvasServer :9002, LoadBalancer :9000.
2. Chạy thủ công 2 instance phụ (chi tiết tại [DEMO_GUIDE.md § Bước 3](DEMO_GUIDE.md)):
   ```powershell
   .\CanvasApp.AuthServer\bin\Debug\CanvasApp.AuthServer.exe 9011
   .\CanvasApp.Server\bin\Debug\CanvasApp.Server.exe 9003
   ```
3. Console LB hiện `Auth pool: 2 backend(s)` + `Canvas pool: 2 backend(s)` → **tổng 5 server**.
4. **Chạy `netstat -ano | findstr "9000 9001 9002 9003 9011"`** trong PowerShell → thấy 5 dòng `LISTENING` → bằng chứng OS-level.

---

## 8. Cryptography / Mã hóa (0,5đ)

### 8.1. Code evidence
| Cơ chế | File | Chú thích |
|---|---|---|
| **BCrypt** password người dùng (cost 12) | [UserStore.cs:164](../CanvasApp.AuthServer/UserStore.cs#L164), [:202](../CanvasApp.AuthServer/UserStore.cs#L202) | Hash 1 chiều + salt tự sinh |
| **BCrypt** password phòng vẽ (cost 10) | [RoomManager.cs:201](../CanvasApp.Server/RoomManager.cs#L201), [:272](../CanvasApp.Server/RoomManager.cs#L272) | Phòng riêng tư |
| **Token có TTL** | [TokenService.cs](../CanvasApp.AuthServer/Services/TokenService.cs) | Base64(`id:username:unixTs`), kiểm `now − issuedAt ≤ 86400` |
| **TTL enforcement** | [UserStore.cs `VerifyToken()`](../CanvasApp.AuthServer/UserStore.cs) | Reject token > 24 h |

### 8.2. Demo runtime
1. Mở phpMyAdmin → bảng `users` → cột `password_hash` cho user demo1 — copy ra Notepad → giá trị dạng `$2b$12$XYZ...` (60 ký tự, có salt) → **không thể đảo ngược về password gốc**.
2. Login với password đúng → vào được. Login với cùng user nhưng password sai → reject. Chứng minh `Verify()` hoạt động.
3. **(Tùy chọn) Token TTL**: Mở `Session.cs` → in `Token` ra Console.WriteLine để xem giá trị; decode Base64 → cấu trúc `id:username:timestamp`. Nếu chỉnh tay timestamp về 2 ngày trước rồi gửi → server reject `Token không hợp lệ hoặc đã hết hạn`.

> ⚠️ Lưu ý thật thà: [AesHelper.cs](../CanvasApp.Common/Utils/AesHelper.cs) hiện là stub trống — luồng TCP **không** mã hóa AES end-to-end. Phần mã hóa thực sự đang dùng là BCrypt + Token TTL như trên.

---

## 9. Demo via LAN (0,5đ)

### 9.1. Code evidence — listener bind `IPAddress.Any` (0.0.0.0)
- [CanvasApp.Server/Program.cs:88](../CanvasApp.Server/Program.cs#L88) — `new TcpListener(IPAddress.Any, port)`
- [CanvasApp.AuthServer/AuthServer.cs:18](../CanvasApp.AuthServer/AuthServer.cs#L18) — `IPAddress.Any`
- [CanvasApp.LoadBalancer/LoadBalancer.cs:62](../CanvasApp.LoadBalancer/LoadBalancer.cs#L62) — `IPAddress.Any`

→ **Listen trên tất cả interface**, không chỉ loopback.

### 9.2. Demo runtime
1. **Máy A (Server)**: PowerShell → `ipconfig` → đọc **IPv4 Address** (ví dụ `192.168.1.42`).
2. **Tắt Windows Firewall tạm thời** hoặc add Inbound Rule cho port 9000 (xem [DEMO_GUIDE.md § Bước 4](DEMO_GUIDE.md)).
3. Trên máy A: chạy server (`F5`).
4. **Máy B (Client) — cùng Wi-Fi**: sửa [Session.cs](../CanvasApp.Client/Network/Session.cs) → `AUTH_HOST = "192.168.1.42"`, `CANVAS_HOST = "192.168.1.42"` → build → chạy Client.
5. Login từ máy B → join phòng do máy A tạo → vẽ → máy A thấy nét vẽ.
6. **Bằng chứng**: PowerShell trên máy A → `netstat -ano | findstr :9000` thấy connection `ESTABLISHED` với IP máy B.

---

## 10. Demo via Internet (0,5đ)

### 10.1. Code evidence
- Cùng như mục 9 (`IPAddress.Any`) — không cần đổi code.

### 10.2. Demo runtime — qua VPN Radmin/Hamachi (khuyên dùng vì miễn phí + không port-forward)
1. Cài **Radmin VPN** trên cả máy A và máy B → tạo network mới → cả 2 join cùng network.
2. Trên máy A: PowerShell → `ipconfig` → tìm interface `Radmin VPN` → đọc IP dạng `26.x.x.x`.
3. Máy A chạy server (F5).
4. Máy B: sửa `Session.cs` → `AUTH_HOST = "26.x.x.x"` → build → chạy Client.
5. Máy B ở **mạng Internet khác hoàn toàn** (4G hoặc Wi-Fi nhà khác) → vẫn login + vẽ được.
6. **Bằng chứng**: trên máy B, ping `26.x.x.x` thành công; canvas đồng bộ realtime qua Internet.

> Nếu giảng viên có 2 máy ở 2 mạng khác nhau, đây là demo "wow" nhất. Có thể quay video sẵn để dự phòng.

---

## 11. Load Balancing (1đ)

> Mục này được hướng dẫn **chi tiết step-by-step** tại [DEMO_GUIDE.md § "Demo Load Balancer trên Visual Studio 2022"](DEMO_GUIDE.md) (Bước 0–8). Phần dưới là tóm tắt các "screenshot bắt buộc" để giảng viên thấy đủ 6 chứng cứ.

### 11.1. Code evidence
| Tính năng | File | Điểm nhấn |
|---|---|---|
| **Peek JSON đầu** | [LoadBalancer.cs `ReadOneLineAsync`](../CanvasApp.LoadBalancer/LoadBalancer.cs) | Đọc byte-by-byte có deadline, parse `Message.Type` |
| **Routing 2 pool** | [LoadBalancer.cs](../CanvasApp.LoadBalancer/LoadBalancer.cs) | `_authPool` round-robin, `_canvasPool` room-affinity + least-loaded |
| **Health check 3-strike** | [HealthChecker.cs](../CanvasApp.LoadBalancer/HealthChecker.cs) | Probe TCP-connect 5 s, fail 3 lần liên tiếp mới mark DOWN |
| **Atomic counters** | [ServerInfo.cs](../CanvasApp.LoadBalancer/ServerInfo.cs) | `Interlocked` cho `ActiveConnections`, `RoomCount`, `FailCount` |
| **Config động** | [appsettings.json](../CanvasApp.LoadBalancer/appsettings.json) | Sửa `MaxConnections` không cần build lại |

### 11.2. 6 bằng chứng cần show cho 1đ (mỗi cái 1 screenshot/video)
| # | Bằng chứng | Bước trong [DEMO_GUIDE.md](DEMO_GUIDE.md) |
|---|---|---|
| 1 | Banner LB `Auth pool: 2, Canvas pool: 2` | Bước 2 + 3 |
| 2 | Health Check 3-strike `probe 1/3 → 2/3 → DOWN` | Bước 2 hoặc 6 |
| 3 | `UP (recovered)` khi server hồi sinh | Bước 3.3 hoặc 6 |
| 4 | **Auth round-robin** — 2 login đi 2 server khác | Bước 4.3 |
| 5 | **Canvas room-affinity** — 2 client cùng phòng → cùng server → vẽ đồng bộ | Bước 5.2 |
| 6 | **Failover** — kill 1 server, traffic dồn về server còn lại | Bước 6 |

### 11.3. Demo nhanh nếu thiếu thời gian
Chạy script tự động ở [DEMO_GUIDE.md § Phụ lục](DEMO_GUIDE.md) (`start-demo.ps1`) → 5 console bật lên trong 5 giây → focus show **3 console**: 1 client + 1 LB + 1 server bị kill.

---

## 12. Tổng kết — bảng tự chấm

In ra bảng dưới, đánh dấu khi đã demo xong từng mục:

| # | Mục | Điểm | Đã demo? | Bằng chứng chính |
|---|---|---|---|---|
| 1 | App Logic + Socket Logic | 5,0 | ☐ | 3 client cùng phòng vẽ realtime |
| 2 | I/O File + Network | 0,5 | ☐ | Export PNG + gửi file chat + (Wireshark TCP) |
| 3 | Database | 0,5 | ☐ | phpMyAdmin show 6 bảng + restart server vẫn còn data |
| 4 | Thread / Đa luồng | 0,5 | ☐ | Multi-client + console log batched persistence |
| 5 | Sign up / Sign in | 0,5 | ☐ | Register → DB hash BCrypt; login đúng/sai |
| 6 | Multi Client | 0,5 | ☐ | 3 client cùng phòng, vẽ + chat + member list sync |
| 7 | Multi Server | 0,5 | ☐ | 5 server LISTENING (netstat) |
| 8 | Cryptography | 0,5 | ☐ | DB cột `password_hash` dạng `$2b$12$...` |
| 9 | Demo LAN | 0,5 | ☐ | 2 máy cùng Wi-Fi vẽ realtime |
| 10 | Demo Internet | 0,5 | ☐ | 2 máy qua Radmin VPN từ 2 mạng khác nhau |
| 11 | Load Balancing | 1,0 | ☐ | 6 screenshot ở § 11.2 |
| **Tổng** | | **10,0** | | |

---

## Phụ lục — Lệnh PowerShell nhanh trong buổi demo

```powershell
# Xem IP máy hiện tại (để show LAN/Radmin)
ipconfig | Select-String "IPv4"

# Xem các port đang LISTEN (chứng minh 5 server đang chạy)
netstat -ano | Select-String "LISTENING" | Select-String "9000|9001|9002|9003|9011"

# Xem connection thực tế đến port LB :9000 (khi có client kết nối)
netstat -ano | Select-String ":9000"

# Đếm số dòng log trong console LB (nếu redirect ra file)
Get-Content lb.log -Wait | Select-String "ROUTE|HEALTH|POOL"
```

## Phụ lục — Wireshark filter

| Mục đích | Filter |
|---|---|
| Xem traffic đến LB | `tcp.port == 9000` |
| Xem traffic Auth pool | `tcp.port == 9001 or tcp.port == 9011` |
| Xem traffic Canvas pool | `tcp.port == 9002 or tcp.port == 9003` |
| Decode payload JSON | Right-click 1 gói → **Follow → TCP Stream** → thấy nguyên dòng `{"Type":"DRAW_END",...}` |

> Mẹo: chọn interface **Loopback (lo)** trên Wireshark để bắt traffic 127.0.0.1; chọn Wi-Fi/Ethernet để bắt traffic LAN.
