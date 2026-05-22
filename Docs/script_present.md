
# Script trình bày theo Rubric (CanvasApp)

> Mục tiêu: giúp trình bày mạch lạc, có sẵn “cơ sở giải thích” khi giảng viên hỏi từng phần.
> Cấu trúc mỗi mục: **Nói gì** → **Bằng chứng** → **Câu hỏi thường gặp**.

---

## 0) Mở đầu (30–45s)

**Nói gì**
- Nhóm em xây dựng CanvasApp: bảng trắng thời gian thực, có đăng nhập/OTP, chat, lưu vết và hỗ trợ đa server + cân bằng tải.
- Kiến trúc gồm 4 thành phần: Client (WinForms), AuthServer, CanvasServer, LoadBalancer; dữ liệu lưu MySQL.

**Bằng chứng**
- Mở sẵn solution và chạy 3 console server, client mở thủ công.

**Câu hỏi thường gặp**
- “Vì sao cần 3 server?” → Tách xác thực và nghiệp vụ vẽ để dễ scale, LB điều phối kết nối.

---

## 1) App Logic + Socket Logic (5.0)

**Nói gì**
- Logic cốt lõi nằm ở luồng xử lý message qua TCP: Client gửi JSON theo dòng, LB định tuyến, Server xử lý và broadcast lại.
- Mỗi client có 1 task xử lý riêng, nên nhiều người vẽ không nghẽn.

**Bằng chứng**
- Cho thấy 1 vòng đời nét vẽ: Client A vẽ → server nhận `DRAW_END` → gán seq → broadcast → Client B thấy ngay.
- Console server in log nhận message, client B hiển thị nét vẽ.

**Câu hỏi thường gặp**
- “Tại sao dùng TCP?” → Realtime ổn định, đảm bảo thứ tự message, dễ implement line-delimited JSON.
- “Trễ bao nhiêu?” → Trong LAN thường < 100ms, tuỳ mạng.

---

## 2) I/O File + Network (0.5)

**Nói gì**
- I/O file: import ảnh nền, export canvas, đính kèm file chat.
- I/O network: toàn bộ message đi qua TCP NetworkStream.

**Bằng chứng**
- Demo export ảnh PNG và mở file.
- Demo import ảnh nền.
- Demo đính kèm file chat và tải từ client khác.

**Câu hỏi thường gặp**
- “Giới hạn file chat?” → Có giới hạn size (ví dụ 2MB) để tránh nghẽn.

---

## 3) Database (0.5)

**Nói gì**
- MySQL lưu users, rooms, actions, snapshots, chat, OTP.
- Snapshot lưu theo batch giúp phục hồi nhanh.

**Bằng chứng**
- Mở phpMyAdmin: bảng `users`, `rooms`, `draw_actions`, `canvas_snapshots`.
- Tắt server rồi bật lại, join phòng cũ thấy canvas còn nguyên.

**Câu hỏi thường gặp**
- “Vì sao không lưu từng nét ngay lập tức?” → Có queue ghi theo batch để giảm I/O, tăng hiệu năng.

---

## 4) Thread / Đa luồng (0.5)

**Nói gì**
- Mỗi client = 1 task async.
- Ghi DB chạy background queue.
- Timer định kỳ để autosave và health check.

**Bằng chứng**
- 3 client vẽ cùng lúc, không crash.
- Console log có batch ghi DB và health check.

**Câu hỏi thường gặp**
- “Đua dữ liệu?” → Dùng lock / concurrent collections cho phòng và sequence.

---

## 5) Sign up / Sign in + Quên mật khẩu (0.5)

**Nói gì**
- Đăng ký: gửi OTP email, xác thực xong mới tạo account.
- Quên mật khẩu: gửi OTP, xác thực, đổi mật khẩu.
- Session có token TTL.

**Bằng chứng**
- Đăng ký user mới → nhận OTP (email/console) → nhập OTP → có record trong bảng `users`.
- Đổi mật khẩu thành công, login bằng mật khẩu mới.

**Câu hỏi thường gặp**
- “OTP bảo mật thế nào?” → OTP được băm bằng BCrypt, có TTL 5 phút và single-use.

---

## 6) Multi Client (0.5)

**Nói gì**
- Nhiều client join cùng phòng; server broadcast trạng thái vẽ và chat.

**Bằng chứng**
- Mở 3 client, 1 người vẽ → 2 người còn lại thấy realtime.
- Chat hiển thị đồng bộ và danh sách thành viên cập nhật.

**Câu hỏi thường gặp**
- “Ai là host phòng?” → Server quản lý phòng trung tâm, client chỉ nhận trạng thái.

---

## 7) Multi Server (0.5)

**Nói gì**
- Có 3 loại server: AuthServer, CanvasServer, LoadBalancer.
- Có thể mở thêm instance để scale.

**Bằng chứng**
- Chạy 2 instance Auth + 2 instance Canvas → LB in số backend.
- `netstat` thấy nhiều port LISTENING.

**Câu hỏi thường gặp**
- “LB chia traffic thế nào?” → Auth theo round-robin; canvas theo room-affinity.

---

## 8) Cryptography (BCrypt + JWT TTL) (0.5)

**Nói gì**
- Password/OTP đều băm bằng BCrypt.
- Token có TTL 24h; quá hạn thì bị reject.
- (Nếu có) payload AUTH_* được AES + HMAC.

**Bằng chứng**
- DB `users.password_hash` dạng `$2b$12$...`.
- `email_otp_codes.code_hash` dạng `$2b$10$...`.

**Câu hỏi thường gặp**
- “Có thể khôi phục mật khẩu từ hash?” → Không, BCrypt là one-way.

---

## 9) Demo LAN (0.5)

**Nói gì**
- Server bind `IPAddress.Any` nên máy trong LAN kết nối được.

**Bằng chứng**
- Máy A chạy server, máy B chỉnh IP và login thành công; vẽ thấy realtime.

**Câu hỏi thường gặp**
- “Vấn đề firewall?” → Cần mở inbound port 9000/9001/9002.

---

## 10) Demo Internet (VPN) (0.5)

**Nói gì**
- Dùng Radmin/Hamachi VPN, 2 máy ở 2 mạng khác nhau vẫn kết nối.

**Bằng chứng**
- Ping IP VPN thành công, login/vẽ realtime.

**Câu hỏi thường gặp**
- “Vì sao không dùng port-forward?” → VPN dễ demo, không cần cấu hình router.

---

## 11) Load Balancing (1.0)

**Nói gì**
- LoadBalancer đọc message đầu để phân tuyến: auth vs canvas.
- Có health check; backend chết thì route sang server còn sống.

**Bằng chứng**
- Console LB hiển thị pool backend.
- Tắt 1 server → LB phát hiện DOWN và failover.
- 2 client login đi 2 AuthServer khác nhau (round-robin).

**Câu hỏi thường gặp**
- “Room-affinity để làm gì?” → Đảm bảo 1 phòng luôn vào cùng 1 canvas server, tránh lệch trạng thái.

---

## 12) Kết luận (20–30s)

**Nói gì**
- Hệ thống đáp ứng đủ rubric: realtime, lưu DB, đa client/đa server, bảo mật, demo LAN/VPN, và cân bằng tải.
- Sẵn sàng trả lời sâu từng mục bằng code evidence và demo runtime.

