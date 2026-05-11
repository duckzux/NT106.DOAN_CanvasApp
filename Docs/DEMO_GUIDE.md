# Test ứng dụng client-server:
### 1. Phải ở chung một mạng (LAN hoặc VPN)
- **Nếu ở chung :** Kết nối các máy tính vào cùng một mạng Wi-Fi hoặc dùng chung một switch mạng.
- **Nếu ở xa nhau (khác mạng):** Bạn sử dụng các phần mềm tạo mạng LAN ảo như **Radmin VPN**, **Hamachi**, hoặc **ZeroTier**. Tất cả các máy cài vào và join chung một network.

### 2. Lấy địa chỉ IP của máy ảo (Máy chạy Server & LoadBalancer)
Trên máy tính sẽ đóng vai trò là **Server**, bạn mở Command Prompt (cmd) và gõ lệnh:
```bash
ipconfig
```
Tìm dòng **IPv4 Address** (Ví dụ: `192.168.1.150` nếu dùng mạng LAN wifi, hoặc IP của mạng Radmin/Hamachi như `26.x.x.x`). 
Ghi nhớ IP này.

### 3. Sửa cấu hình kết nối (Endpoint/IP) trên source code
Mặc định ứng dụng thường đang chạy ở `localhost` hoặc `127.0.0.1`. Bạn cần sửa cấu hình bằng IP vừa lấy ở bước 2.

* **Trên máy chạy Server (AuthServer, Server, LoadBalancer):**
  - Mở các file cấu hình như `App.config`, `appsettings.json` hoặc trong mã nguồn (`Program.cs`, `CanvasServer.cs`, v.v.).
  - Đảm bảo các listener đangắng nghe trên địa chỉ `0.0.0.0` (nghe trên tất cả các IP của máy) hoặc điền đích danh IP LAN (`192.168.1.150`). Tránh việc chỉ listen trên `127.0.0.1`.

* **Trên máy chạy Client (CanvasApp.Client):**
  - Trỏ các địa chỉ kết nối tới IP của Server.
  - Ví dụ trong file App.config hoặc các hằng số ở `CanvasClient.cs`, bạn đổi `127.0.0.1` thành `192.168.1.150` (IP của máy chủ LoadBalancer/AuthServer tương ứng).

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
