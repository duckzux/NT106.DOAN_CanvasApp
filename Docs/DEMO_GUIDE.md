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

**Cách 1: Chạy trực tiếp bằng code trong Visual Studio (Khuyên dùng khi đang Code/Test)**
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