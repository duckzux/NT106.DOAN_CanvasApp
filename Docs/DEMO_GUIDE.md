# Test ứng dụng client-server:
### 1. Phải ở chung một mạng (LAN hoặc VPN)
- **Nếu ở chung :** Kết nối các máy tính vào cùng một mạng Wi-Fi hoặc dùng chung một switch mạng.
- **Nếu ở xa nhau (khác mạng):** Bạn sử dụng các phần mềm tạo mạng LAN ảo như **Radmin VPN**, **Hamachi**, hoặc **ZeroTier**. Tất cả các máy cài vào và join chung một network.

### 2. Lấy địa chỉ IP của máy ảo (Máy chạy Server & LoadBalancer)
Trên máy tính sẽ đóng vai trò là **Server**, bạn mở Command Prompt (cmd) và gõ lệnh:
```bash
ipconfig
```
Tìm dòng **IPv4 Address** (Ví dụ: `10.232.200.197` nếu dùng mạng LAN wifi, hoặc IP của mạng Radmin/Hamachi như `26.x.x.x`). 
Ghi nhớ IP này.

### 3. Sửa cấu hình kết nối (Endpoint/IP) trên source code
Mặc định ứng dụng thường đang chạy ở `localhost` hoặc `127.0.0.1`. Bạn cần sửa cấu hình bằng IP vừa lấy ở bước 2.

* **Trên máy chạy Server (AuthServer, Server, LoadBalancer):**
  - Mở các file cấu hình như `App.config`, `appsettings.json` hoặc trong mã nguồn (`Program.cs`, `CanvasServer.cs`, v.v.).
  - Đảm bảo các listener đangắng nghe trên địa chỉ `0.0.0.0` (nghe trên tất cả các IP của máy) hoặc điền đích danh IP LAN (`10.232.200.197`). Tránh việc chỉ listen trên `127.0.0.1`.

* **Trên máy chạy Client (CanvasApp.Client):**
  - Trỏ các địa chỉ kết nối tới IP của Server.
  - Ví dụ trong file App.config hoặc các hằng số ở `CanvasClient.cs`, bạn đổi `127.0.0.1` thành `10.232.200.197` (IP của máy chủ LoadBalancer/AuthServer tương ứng).

* **Database (MySQL):** 
  - Đảm bảo connection string trong các server (`AuthServer`, `CanvasServer`) có thể kết nối được CSDL (nếu DB cũng nằm trên máy chủ thì dùng `localhost/127.0.0.1` đều được, nhưng nếu DB ở máy thứ 3 thì phải trỏ IP tương ứng).

### 4. Bật quyền (Allow) cổng trên Tường lửa (Windows Firewall) của máy Server
Đây là lỗi phổ biến nhất khiến Client ở máy khác không thể kết nối dù ping thấy nhau:
- Nhấn phím Windows, tìm **Windows Defender Firewall with Advanced Security**.
- Chọn **Inbound Rules** > Chọn **New Rule...** 
- Chọn **Port** > Lần lượt thêm các Port mà hệ thống Server của bạn đang chạy (Ví dụ Port của `LoadBalancer`, `AuthServer`, `CanvasServer` như `9000, 9001, 8080`, v.v...).
- Chọn **Allow the connection** và hoàn tất.
*(Cách nhanh để test tạm thời là tắt tạm tường lửa của máy Server, nhưng nhớ bật lại sau khi test xong).*

### 5. Build và chạy
- Mở máy Server, chạy các dịch vụ (LoadBalancer, AuthServer, Server, DB).
- Copy bộ thư mục đã build của Debug ném sang máy tính khác.
- Mở file `.exe` của Client trên máy tính khác, điền các thông tin và test thử.
#### Client:

**Chạy trực tiếp bằng code trong Visual Studio **
1. Mở file **CanvasApp.sln** bằng Visual Studio.
2. Trong cửa sổ **Solution Explorer** (thường nằm bên tay phải), tìm đến dự án **CanvasApp.Client**.
3. Nhấp **chuột phải** vào CanvasApp.Client và chọn **"Set as Startup Project"** (để báo cho Visual Studio biết bạn muốn chạy project này lên đầu tiên). Khi đó tên project sẽ được in đậm.
4. Nhấn phím **F5** trên bàn phím (hoặc bấm nút **Start** màu xanh lá cây ở thanh công cụ phía trên). Project sẽ tự động build và chạy lên giao diện Client cho bạn.


#### Server:
Để chạy hệ thống Server, vì dự án  có nhiều thành phần (AuthServer, Server chính, và Client), bạn cần chạy đồng thời các project Server lên trước khi chạy Client. 

### Bước 1: Mở Database (MySQL)
Trước khi chạy code, hãy đảm bảo bạn đã bật MySQL (ví dụ: bật MySQL trong bảng điều khiển **XAMPP** hoặc phần mềm tương tự mà bạn đang dùng) vì Server cần kết nối đến Database ngay khi khởi động.

### Bước 2: Chạy các Project Server
**Cách 1: Chạy nhiều Project cùng lúc trong Visual Studio (Khuyên dùng)**
Tính năng này giúp bạn ấn 1 nút là chạy cả `AuthServer`, `Server` và `Client` lên.
1. Nhấp **chuột phải vào Solution `CanvasApp`** (dòng trên cùng nhất trong Solution Explorer) -> Chọn **Properties** (Thuộc tính) hoặc **Configure Startup Projects...**
2. Chọn mục **Startup Project** ở menu bên trái.
3. Chọn tùy chọn **Multiple startup projects**.
4. Trong danh sách hiện ra, bạn chỉnh cột **Action** của các project sau thành **Start**:
   - CanvasApp.AuthServer
   - CanvasApp.Server
   - CanvasApp.Client (nếu bạn muốn nó tự mở luôn)
   *(Lưu ý thứ tự: Nên dùng nút mũi tên lên/xuống để đẩy AuthServer và Server lên đầu danh sách).*
5. Nhấn **OK**. Bây giờ mỗi khi bạn nhấn **Start (F5)**, Visual Studio sẽ tự động mở lần lượt các Server lên trước (sẽ hiện các cửa sổ Console màn hình đen) rồi mới mở Client.

**Cách 2: Chạy Server bằng file `.exe`, chỉ code/chạy Client trên Visual Studio**
Nếu cấu hình máy yếu, chạy nhiều project cùng lúc trên Visual Studio có thể hơi nặng. Bạn có thể làm cách tách rời:
1. Mở thư mục code của bạn trong File Explorer.
2. Vào Debug (hoặc Release) chạy file `CanvasApp.AuthServer.exe`.
3. Vào Debug chạy file `CanvasApp.Server.exe`.
4. (Lúc này bạn sẽ có 2 màn hình đen Console đang lắng nghe ở port 9001 và 9002).
5. Quay lại Visual Studio, đặt CanvasApp.Client làm Startup Project (như hướng dẫn ở câu trước) và nhấn **F5** để code/chạy riêng Client. 

> Luôn đảm bảo **AuthServer** và **Server** (các cửa sổ Console) đang hiển thị trạng thái "Listening on target port..." thì Client mới có thể kết nối vào mạng thành công.

---

# Demo Load Balancer trên Visual Studio 2022 (chi tiết step-by-step)

> Phần này demo riêng tính năng Load Balancer — tương ứng **1đ** trong rubric đồ án. Toàn bộ chạy trên **1 máy** (localhost), không cần LAN/Radmin. Yêu cầu: Visual Studio 2022, .NET Framework 4.8, MySQL đang chạy.

## Tổng quan kiến trúc demo

```
              ┌──────────────────────────┐
              │   Client.exe  (port ?)   │  Session.cs trỏ về :9000
              └────────────┬─────────────┘
                           │
                           ▼
              ┌──────────────────────────┐
              │  LoadBalancer  :9000     │  peek msg đầu → chọn pool
              └─────┬─────────────┬──────┘
            AUTH_*  │             │  ROOM_*
                    ▼             ▼
        ┌───────────┴─────┐  ┌────┴──────────────┬───────────────┐
        ▼                 ▼  ▼                   ▼               ▼
   AuthServer :9001  AuthServer :9011   CanvasServer :9002  CanvasServer :9003
   (round-robin)                        (room-affinity + least-loaded)
```

Tổng cộng **6 console** sẽ chạy đồng thời: 1 LB + 2 AuthServer + 2 CanvasServer + 1 Client (ít nhất).

## Bước 0 — Chuẩn bị (làm 1 lần)

### 0.1. Bật MySQL
- Mở **XAMPP Control Panel** → Start **MySQL**. Nếu chưa có DB `canvasapp`, import từ `Database/canvasapp.sql` qua phpMyAdmin (`http://localhost/phpmyadmin`).

### 0.2. Trỏ Client về Load Balancer
- Trong VS, mở file [CanvasApp.Client/Network/Session.cs](../CanvasApp.Client/Network/Session.cs).
- Đổi 2 hằng số:
  ```csharp
  public const int AUTH_PORT   = 9000;   // ← từ 9001 đổi thành 9000
  public const int CANVAS_PORT = 9000;   // ← từ 9002 đổi thành 9000
  ```
- Lưu (Ctrl+S). Bây giờ mọi connection của Client đều đi qua LB.

### 0.3. Build solution 1 lần
- Trong VS: menu **Build → Build Solution** (hoặc `Ctrl+Shift+B`).
- Kiểm tra cửa sổ **Output**: phải thấy `Build succeeded. 0 Error(s)`. Sau bước này, các file `.exe` đã sẵn sàng trong `bin\Debug\` của từng project.

### 0.4. Mở Firewall (chỉ khi demo qua mạng, bỏ qua nếu chỉ localhost)
- Nếu chạy hoàn toàn trên 1 máy, **không cần** làm bước này.
- Nếu demo qua LAN, mở Inbound Rule cho các port `9000, 9001, 9002, 9003, 9011` như hướng dẫn ở phần trên.

---

## Bước 1 — Cấu hình "Multiple Startup Projects" trong Visual Studio

> Mục tiêu: 1 cú **F5** mở luôn 1 AuthServer + 1 CanvasServer + 1 LoadBalancer. Các instance phụ (AuthServer #2, CanvasServer #2) sẽ chạy bằng `.exe` thủ công ở Bước 3.

1. Trong **Solution Explorer**, **chuột phải** vào dòng trên cùng `Solution 'CanvasApp'` → chọn **Configure Startup Projects...** (hoặc **Properties**).
2. Ở khung trái chọn **Common Properties → Startup Project**.
3. Tick **Multiple startup projects**.
4. Trong bảng, set **Action** cho từng project (các project còn lại để **None**):
   | Project | Action |
   |---|---|
   | `CanvasApp.AuthServer` | **Start** |
   | `CanvasApp.Server` | **Start** |
   | `CanvasApp.LoadBalancer` | **Start** |
   | `CanvasApp.Client` | **None** (mở thủ công sau) |
5. Dùng mũi tên ▲/▼ bên phải để **đẩy LoadBalancer xuống cuối** (start sau cùng để khi LB lên thì 2 backend đã listening). Thứ tự khuyến nghị:
   1. CanvasApp.AuthServer
   2. CanvasApp.Server
   3. CanvasApp.LoadBalancer
6. Nhấn **OK**.

---

## Bước 2 — F5 lần đầu: kiểm tra 3 console mở đúng

Nhấn **F5**. Visual Studio sẽ build (nếu cần) rồi mở **3 cửa sổ Console**:

### Console 1 — `AuthServer :9001`
Expect:
```
[DB] DatabaseManager initialized (hoặc tương đương)
╔══════════════════════════════════════╗
║  AUTH SERVER listening on :9001      ║
╚══════════════════════════════════════╝
```

### Console 2 — `CanvasServer :9002`
Expect:
```
[DB] DatabaseManager initialized
[DB] PersistenceQueue, AutoSaveService, and room state ready
╔══════════════════════════════════════╗
║   CANVAS SERVER listening on :9002    ║
╚══════════════════════════════════════╝
```

### Console 3 — `CanvasApp Load Balancer` (title bar)
Expect:
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

Sau **~10 giây** (3 strike × ~5s mỗi probe), LB sẽ log:
```
[HEALTH] Auth 127.0.0.1:9011 probe 1/3 failed (...)
[HEALTH] Auth 127.0.0.1:9011 probe 2/3 failed (...)
[HEALTH] Auth 127.0.0.1:9011 -> DOWN (3 fails: ...)
[HEALTH] Canvas 127.0.0.1:9003 -> DOWN (3 fails: ...)
```

> 💡 **Đây là demo Health Check 3-strike đầu tiên** — chụp màn hình đoạn này.

Cứ **15 giây**, LB in 1 dòng `[POOL]` snapshot. Nhìn vào dòng đó để theo dõi trạng thái pool live:
```
[POOL]
   Auth   127.0.0.1:9001 [UP]   conns=0/100 rooms=0 fails=0
   Auth   127.0.0.1:9011 [DOWN] conns=0/100 rooms=0 fails=3
   Canvas 127.0.0.1:9002 [UP]   conns=0/100 rooms=0 fails=0
   Canvas 127.0.0.1:9003 [DOWN] conns=0/100 rooms=0 fails=3
```

---

## Bước 3 — Chạy 2 instance phụ (AuthServer #2 + CanvasServer #2) bằng `.exe` + CLI args

Vì port đã được wired để đọc từ **CLI args đầu tiên** (`args[0]`), ta dùng `.exe` trực tiếp với arg:

### 3.1. AuthServer instance #2 (port 9011)
1. Mở **File Explorer**, vào `H:\[TL]KI4\T3_LTMCB\DOAN\CanvasApp\CanvasApp.AuthServer\bin\Debug\`.
2. **Shift + chuột phải** trên vùng trống → **Open PowerShell window here** (hoặc **Open in Terminal**).
3. Gõ:
   ```powershell
   .\CanvasApp.AuthServer.exe 9011
   ```
4. Console mới mở, title `AuthServer :9011`, log `listening on :9011`.

### 3.2. CanvasServer instance #2 (port 9003)
1. Mở `H:\[TL]KI4\T3_LTMCB\DOAN\CanvasApp\CanvasApp.Server\bin\Debug\` tương tự.
2. Gõ:
   ```powershell
   .\CanvasApp.Server.exe 9003
   ```
3. Console mới mở, title `CanvasServer :9003`, log `listening on :9003`.

### 3.3. Quan sát LB phát hiện server hồi sinh
Sau **≤5 giây** (1 chu kỳ health check), console LB sẽ log:
```
[HEALTH] Auth 127.0.0.1:9011 -> UP (recovered)
[HEALTH] Canvas 127.0.0.1:9003 -> UP (recovered)
```
→ **Demo Health Check recovery** — chụp màn hình đoạn này.

Dòng `[POOL]` tiếp theo:
```
[POOL]
   Auth   127.0.0.1:9001 [UP] ...
   Auth   127.0.0.1:9011 [UP] ...
   Canvas 127.0.0.1:9002 [UP] ...
   Canvas 127.0.0.1:9003 [UP] ...
```

Bây giờ đủ pool 2+2 để demo balancing.

---

## Bước 4 — Demo Auth round-robin + failover

### 4.1. Mở Client #1 (giữ chuột phải VS để debug, hoặc chạy `.exe`)
- **Cách qua VS**: Solution Explorer → chuột phải `CanvasApp.Client` → **Debug → Start New Instance**.
- **Cách qua exe**: chạy `CanvasApp.Client\bin\Debug\CanvasApp.Client.exe`.

### 4.2. Đăng nhập từ Client #1
- Nhập user/pass → **Đăng nhập**.
- Quan sát **console LB**:
   ```
   [LB] 127.0.0.1:5xxxx -> Auth 127.0.0.1:9001 [UP] conns=1/100 rooms=0 fails=0 (first=AUTH_LOGIN)
   ```

### 4.3. Mở Client #2 và đăng nhập (user khác)
- Chạy thêm 1 instance `CanvasApp.Client.exe`.
- Đăng nhập user thứ 2.
- Console LB:
   ```
   [LB] 127.0.0.1:5yyyy -> Auth 127.0.0.1:9011 [UP] conns=1/100 rooms=0 fails=0 (first=AUTH_LOGIN)
   ```
   → **Round-robin** đẩy login thứ 2 sang AuthServer khác. Chụp screenshot.

### 4.4. Demo failover Auth
- **Tắt AuthServer :9001** (console đầu tiên — Ctrl+C hoặc đóng cửa sổ).
- Đợi ≤15s. Console LB:
   ```
   [HEALTH] Auth 127.0.0.1:9001 -> DOWN (3 fails: ...)
   ```
- Mở Client #3 → login. Console LB:
   ```
   [LB] ... -> Auth 127.0.0.1:9011 ... (first=AUTH_LOGIN)
   ```
   → **Toàn bộ login đẩy về AuthServer còn sống** mặc dù #1 đã chết. **Đây là failover.**

---

## Bước 5 — Demo Canvas room-affinity routing

> ⚠️ Để LB peek được `ROOM_JOIN` từ đầu, cần dùng **Join by Invite Code** thay vì lobby. Lobby gửi `ROOM_LIST` trước → LB không biết RoomId → chỉ route theo least-loaded thuần.

### 5.1. Tạo 1 room với invite code (từ Client #1)
- Client #1: login xong → **Tạo bảng trắng mới** → đặt tên `ROOM-DEMO` → tạo room.
- Console LB:
   ```
   [LB] ... -> Canvas 127.0.0.1:9002 [UP] conns=1 rooms=0 fails=0 (first=ROOM_LIST)
   ```
   *(first=ROOM_LIST vì lobby gửi list trước; chấp nhận giới hạn này)*
- Vào trong room → bấm **Sao chép link mời** (icon link) → copy invite code.

### 5.2. Client #2 dùng **Join by Code**
- Client #2: logout (hoặc dùng từ instance mới) → login → **trên màn hình lobby**, paste invite code vào ô **Tham gia bằng mã** (thay vì click vào card room).
- Quan trọng: nếu UI vẫn gửi `ROOM_LIST` trước, kết quả demo cho roomAffinity sẽ không thấy rõ. Để chắc, **mở Client #2 instance hoàn toàn mới** và bấm Join by Code ngay khi vào lobby.
- Console LB lý tưởng:
   ```
   [ROUTE] room <RoomId> -> 127.0.0.1:9002 (rooms=1)
   [LB] ... -> Canvas 127.0.0.1:9002 ... (first=ROOM_JOIN_BY_CODE)
   ```
   → Client #2 được route về cùng Canvas Server `9002` với Client #1.
- Trong room: vẽ ở Client #1 → Client #2 thấy ngay → **canvas đồng bộ** → chứng minh room-affinity hoạt động.

### 5.3. Tạo room thứ 2 từ Client #3 (test least-loaded)
- Client #3 (instance mới): login → tạo `ROOM-2`.
- Vì `Canvas 9002` đã có `rooms=1` (giả định LB đã record) và `Canvas 9003` có `rooms=0` → LB chọn `9003`:
   ```
   [LB] ... -> Canvas 127.0.0.1:9003 [UP] conns=1 rooms=0 fails=0 (first=ROOM_LIST)
   ```
   → Demo least-loaded routing (cân bằng).

---

## Bước 6 — Demo Health Check 3-strike khi kill Canvas Server giữa chừng

1. Đảm bảo cả 2 CanvasServer đang chạy và có client đang vẽ (để thấy reconnect behavior).
2. **Tắt CanvasServer :9002** (Ctrl+C cửa sổ console của nó).
3. Quan sát console LB:
   ```
   [HEALTH] Canvas 127.0.0.1:9002 probe 1/3 failed (...)
   [HEALTH] Canvas 127.0.0.1:9002 probe 2/3 failed (...)
   [HEALTH] Canvas 127.0.0.1:9002 -> DOWN (3 fails: ...)
   ```
   → Mỗi probe cách nhau ~5s, tổng ~15s mới mark DOWN. **Đây là demo 3-strike** — KHÔNG flap khi 1 lần fail tạm thời.
4. Client đang ở room trên 9002 sẽ disconnect → Client (có reconnect logic) sẽ thử lại; LB sẽ route về `9003`. **Lưu ý**: vì room state ở 9002 đã mất, room có thể load lại từ DB snapshot (nếu có `LoadActiveRooms`) trên 9003, hoặc client báo lỗi "Room not found". Đây là **giới hạn không-shared-state** — chấp nhận trong scope demo.
5. Tạo room mới → tất cả về `9003`:
   ```
   [LB] ... -> Canvas 127.0.0.1:9003 ...
   ```
6. **Bật lại** `CanvasServer :9003` bằng cách chạy lại `.exe 9002` từ File Explorer. Sau ≤5s:
   ```
   [HEALTH] Canvas 127.0.0.1:9002 -> UP (recovered)
   ```
   → Server tự khôi phục, pool đầy đủ trở lại.

---

## Bước 7 — Demo room cleanup (ref-counting)

- Khi client cuối cùng trong 1 room ngắt connection (đóng Client.exe hoặc rời room):
   ```
   [LB] ... closed (backend 127.0.0.1:9002 conns=0)
   [ROUTE] room <RoomId> freed from 127.0.0.1:9002 (rooms=0)
   ```
   → Routing table tự xóa mapping, `RoomCount` của Canvas Server giảm về 0. **Không leak**.

---

## Bước 8 — Demo `MaxConnections` cap (tùy chọn)

1. Tắt LB.
2. Mở `CanvasApp.LoadBalancer\bin\Debug\appsettings.json`, đổi `MaxConnections` từ `100` thành `1`:
   ```json
   "CanvasServers": [
     { "Host": "127.0.0.1", "Port": 9002, "MaxConnections": 1 },
     { "Host": "127.0.0.1", "Port": 9003, "MaxConnections": 1 }
   ]
   ```
3. Restart LB (F5 lại nếu đã dừng, hoặc chạy `.exe`).
4. Mở 3 Client cùng lúc → Client thứ 3 sẽ bị reject ngay:
   ```
   [LB] ... rejected — 127.0.0.1:9003 at MaxConnections
   ```
   → Demo cơ chế bảo vệ overload.

---

## Checklist screenshot/video cho buổi demo

Để được trọn **1đ** rubric Load Balancer, chuẩn bị 6 screenshot/video clip ngắn:

1. ✅ Banner LB khởi động — thấy `Auth pool: 2, Canvas pool: 2` (Bước 2 + 3)
2. ✅ Health Check 3-strike DOWN — 3 dòng `probe N/3 failed` rồi `-> DOWN` (Bước 2 hoặc 6)
3. ✅ Health Check `UP (recovered)` (Bước 3.3 hoặc 6)
4. ✅ Auth round-robin — 2 client login về 2 AuthServer khác nhau (Bước 4.3)
5. ✅ Canvas room-affinity — 2 client vào cùng room → cùng Canvas Server → vẽ đồng bộ (Bước 5.2)
6. ✅ Failover khi 1 Canvas Server chết → traffic đẩy về server còn lại (Bước 6)

## Phụ lục — Lệnh nhanh khởi động toàn bộ qua PowerShell

Tạo file `start-demo.ps1` ở root project, paste:
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

Sau đó **chuột phải `start-demo.ps1` → Run with PowerShell** → toàn bộ pool 5 console bật lên trong ~5 giây. Tiện cho buổi demo live, không phải set up từng cái.

---

## Troubleshooting

| Triệu chứng | Nguyên nhân | Cách fix |
|---|---|---|
| LB banner báo `Canvas pool: 0` rồi exit | Chưa start CanvasServer hoặc port sai | Start CanvasServer trước, hoặc sửa `appsettings.json` của LB |
| Tất cả backend đều `DOWN` ngay khi LB lên | Firewall chặn loopback, hoặc port đã bị process khác chiếm | `netstat -ano | findstr :9002` để xem ai đang giữ port. Tắt process đó |
| Client login bị treo, LB log `peek error: timeout` | Client không gửi message JSON nào trong 5s | Check `Session.cs` đã đổi sang `:9000` chưa; check Client connect đúng host |
| `dropped — no first message in 5000ms` | Client mở socket mà không gửi gì | Client gửi `PING` trước khi gửi auth/room — không bình thường, kiểm tra phía Client |
| Room-affinity không kick in (Client #2 về server khác) | Client gửi `ROOM_LIST` trước thay vì `ROOM_JOIN_BY_CODE` | Dùng đúng Join-by-Code flow; hoặc accept giới hạn và demo qua bước 5.3 (least-loaded thay vì affinity) |
| `bin\Debug\` không có `.exe` | Chưa build hoặc build Release | `Ctrl+Shift+B` rebuild; check Configuration là `Debug` |


