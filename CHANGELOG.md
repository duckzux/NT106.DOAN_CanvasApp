
#  DONE:
1. **Kiến trúc & Network:**
   - Hoàn thiện mô hình Multi Server với CanvasApp.AuthServer (Xử lý JWT, Register/Login), CanvasApp.Server (Quản lý phòng vẽ, Broadcast), và CanvasApp.LoadBalancer (chuyển hướng TCP và health check).
2. **Thao tác Database & Bảo mật:**
   - Các lớp truy cập DB (DAOs), sử dụng `MySql.Data` đã ở CanvasApp.Common.
   - Phân quyền (BCrypt), JWT, và mã hóa luồng bằng AES (`AesHelper.cs`) đã được tích hợp.
3. **Cơ bản màn hình Client (Winforms):**
   - Đã có Form cơ bản: Đăng nhập/Đăng ký (`LoginForm`, `RegisterForm`), sảnh tạo và tìm phòng (`LobbyForm`, xử lý được password phòng), Form Vẽ (`CanvasForm`).
   - Khung giao tiếp TCP Socket Client-Server. 
   - Đã có tính năng vẽ sơ cấp: **Pen**, **Eraser**, Clear, chọn màu, khung chat trong phòng và danh sách người dùng.
4. AuthServer - đã được hoàn thiện:

| File                       | Chức năng                                                                                                                                             |
| -------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AuthServer.cs`            | Tạo `TcpListener` trên port `9001`, vòng lặp accept connection, tạo một `AuthHandler` riêng cho mỗi client                                            |
| `AuthHandler.cs`           | Đọc JSON phân tách theo từng dòng (`line-delimited JSON`), định tuyến: `AUTH_LOGIN` → `AuthService`, `AUTH_REGISTER` → `UserService`, `PING` → `PONG` |
| `Services/AuthService.cs`  | Chức năng đăng nhập: gọi `UserStore.Login()` (xác thực BCrypt + cấp token), sau đó đóng gói kết quả thành `AUTH_LOGIN_RESULT`                         |
| `Services/UserService.cs`  | Chức năng đăng ký: kiểm tra username ≥ 3 ký tự, password ≥ 6 ký tự, gọi `UserStore.Register()`, sau đó đóng gói kết quả thành `AUTH_REGISTER_RESULT`  |
| `Services/TokenService.cs` | `CreateToken(user)` → tạo token dạng `Base64(id:username:unixTs)`; `ValidateToken(token)` → trả về `userId` hoặc `-1` (có kiểm tra TTL 24 giờ)        |
| `Program.cs`               | đọc config → tạo `UserStore` → tạo `AuthServer` → `await server.StartAsync()`                                                      |

1. CanvasClient:

### Heartbeat

`HeartbeatLoop()` gửi `PING` mỗi 10 giây; phản hồi `PONG` sẽ được xử lý âm thầm trong receive loop để các form giao diện không nhìn thấy chúng.

---

### Reconnect với exponential backoff

`ReconnectLoop()` thử kết nối lại tối đa 10 lần với khoảng delay:

```text
1s → 2s → 4s → ...
```

giới hạn tối đa là 30 giây.

* Nếu reconnect thành công → gọi `OnReconnected`
* Chỉ gọi `OnDisconnected` sau khi toàn bộ lần thử đều thất bại


### Đồng bộ toàn bộ trạng thái sau reconnect

Sau khi reconnect thành công:

1. Client gửi lại `PING`
   (đính kèm token để server xác thực lại)

2. Sau đó gửi `CANVAS_STATE` với `_currentRoomId` đã lưu
   nếu client trước đó đang ở trong room

### Serialize khi gửi dữ liệu

Sử dụng khóa `SemaphoreSlim` trên `_writer` để ngăn nhiều luồng ghi dữ liệu cùng lúc làm các dòng bị trộn (`interleaving`) trong quá trình sử dụng bình thường hoặc khi retry reconnect.


# REMAINING TASKS:
Done Foundation, giờ cần logic đồ họa và các tính năng sáng tạo:

1. **Nhóm công cụ vẽ mở rộng (Drawing Tools):**
   - Hiện chỉ cọ vẽ và tẩy hoạt động. Cần bổ sung các hình khối (Rectangle, Ellipse/Circle, Line, Arrow), Chèn Text.
   - Tính năng Tùy chỉnh độ dày nét vẽ (Line width) và cờ bật/tắt Đổ màu hình khối (Fill/Outline).\
   - Zoom + Pan bằng Matrix transform
2. **Cơ chế Hoàn tác (Undo/Redo):** 
   - Cần một `Stack<DrawAction>` (ví dụ: Lưu canvas state trong ConcurrentDictionary<roomId, List<DrawAction>>) để lưu lại các bút vẽ và khôi phục khi nhấn phím tắt như Ctrl+Z.
3. **Đồng bộ con trỏ chuột theo thời gian thực (Cursor Sync):**
   - Sự di chuyển chuột của mọi người cần được truyền qua mạng dựa trên Event `MouseMove` để hiển thị trên thiết bị khác.
4. **Export / Import Ảnh nền:** 
   - Tính năng xuất `Canvas` ra file PNG/JPEG (`ExportDialog`) qua hàm `DrawToBitmap`.
   - Tính năng chèn ảnh làm hình nền vào trong `Paint event`.
5. **Tính năng sáng tạo:**
   - Thêm hệ thống Layers (tắt bật, thay đổi layer của từng người).
   - Replay hệ thống (tua lại quá trình vẽ dự theo log Timestamp lưu trong CSDL MySQL).
   - Quản lý giao diện phông nền theo dạng Template (Dotted, Lined, Grid).
6. **Share link room**
7. **Load Balancer**
8. **Gợi ý hoàn thiện nét vẽ** (Kiểu vẽ hơi méo tự động gợi ý fix lại tròn,...)
9. Xác thực email (kiểu check email real hay fake hoặc thêm cái dạng xác thực OTP qua mail càng tốt)
