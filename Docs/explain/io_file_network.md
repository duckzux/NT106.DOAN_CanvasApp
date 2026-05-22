# I/O File / Network (CanvasApp)

Tài liệu này giải thích chi tiết phần I/O File và I/O Network trong CanvasApp: code liên quan, luồng hoạt động, và logic xử lý. Tập trung vào những phần được chấm điểm trong rubric (import/export, đọc config, gửi file chat, và TCP NetworkStream).

---

## 1. Tổng quan I/O trong CanvasApp

CanvasApp có 2 nhóm I/O chính:

* **I/O File**: đọc/ghi file trên máy người dùng (config, import ảnh, export canvas, gửi file chat).
* **I/O Network**: TCP socket trường hợp chính, data truyền bằng JSON + newline qua NetworkStream.

---

## 2. I/O File

### 2.1. Đọc config JSON (Load Balancer)

**Mục đích:** load thông số server và pooling từ `appsettings.json` mà không cần build lại.

* Dùng `File.ReadAllText(...)` để đọc JSON.
* Parse bằng `JObject.Parse`/`JsonConvert`.
* Cho phép thay đổi `MaxConnections`, `Peers`, port... ngay trước demo.

**Logic:**

1. Mở file JSON.
2. Parse ra `ServerSettings` và danh sách backend.
3. Áp dụng cấu hình vào pool + health checker.

### 2.2. Import ảnh nền (Client)

**Mục đích:** người dùng chọn ảnh JPG/PNG làm background của canvas.

**Luồng hoạt động:**

1. `OpenFileDialog` cho người dùng chọn file.
2. `Image.FromFile(path)` đọc ảnh vào memory.
3. Vẽ lại lên panel/bitmap (làm background).
4. Canvas vẽ nét trên lớp trên.

**Note:** File đọc trên máy local, không qua network.

### 2.3. Export canvas ra PNG/JPEG (Client)

**Mục đích:** lưu canvas hiện tại ra file để nộp bài hoặc chia sẻ.

**Luồng hoạt động:**

1. `SaveFileDialog` cho người dùng chọn đường dẫn + định dạng.
2. `Bitmap.Save(path, ImageFormat.Png/Jpeg)` ghi file.
3. Thông báo thành công trên UI.

**Note:** Export là thao tác local, không cần server.

### 2.4. Gửi file qua chat (Client -> Server -> Client)

**Mục đích:** demo I/O file qua mạng (attach file). Rubric yêu cầu có I/O file + network.

**Luồng hoạt động:**

1. Client chọn file bằng `OpenFileDialog`.
2. Đọc file thành byte[] (giới hạn 2 MB).
3. Encode Base64 (`Convert.ToBase64String`).
4. Gửi message `CHAT_FILE` (JSON).
5. Server broadcast đến các client trong phòng.
6. Client nhận, hiện link tải file.

**Note:** File lưu/tải trên client, server chỉ relay, không persist.

---

## 3. I/O Network (TCP)

### 3.1. NetworkStream + line-delimited JSON

CanvasApp dùng TCP thường, mỗi message là 1 dòng JSON (newline-delimited). Quy ước:

* **Send:** `StreamWriter.WriteLine(...)` + `AutoFlush = true`.
* **Receive:** `StreamReader.ReadLineAsync()`.

Ưu điểm:

* Đơn giản, dễ debug bằng Wireshark.
* Mỗi line là 1 message hoàn chỉnh, dễ parse.

Cần lưu ý:

* `StreamWriter` không thread-safe -> cần lock để tránh interleaving.
* Payload quá lớn bị reject (server có giới hạn).

### 3.2. Lobby socket (short-lived)

**Mục đích:** query 1 lần từ client -> LB -> Canvas server.

Đối tượng: `ROOM_LIST`, `ROOM_CREATE`, `ROOM_RESOLVE`, `ROOM_DELETE`.

**Luồng hoạt động:**

1. Mở TCP đến LB.
2. Gửi 1 message.
3. Đọc response (terminal type).
4. Đóng socket.

Lý do: LB cần route theo room-affinity, nên mỗi request sử dụng socket riêng.

### 3.3. Canvas socket (persistent)

**Mục đích:** kết nối dài hạn để nhận realtime draw/chat.

Tính năng chính:

* **Send lock**: serialize send.
* **Heartbeat**: PING/PONG mỗi 10s.
* **Reconnect**: exponential backoff nếu drop.

**Luồng hoạt động:**

1. Connect đến Canvas Server.
2. Tạo `StreamReader/Writer`.
3. Loop nhận line JSON -> `OnMessageReceived` -> UI update.
4. Khi drop, tự động reconnect.

### 3.4. Threading + I/O flow

```mermaid
flowchart LR
	UI[UI thread] -->|OpenFileDialog/SaveFileDialog| Disk[Local file I/O]
	UI -->|SendFileAsync| Net[NetworkStream]
	Net --> Srv[Canvas Server]
	Srv --> Recv[Client ReceiveLoop]
```

---

## 4. Demo runtime (rubric)

### 4.1. I/O File

* Export canvas ra PNG, mở file để thấy đúng hình vẽ.
* Import ảnh nền, thấy background thay đổi.
* Gửi file chat, client khác nhận được và tải về.

### 4.2. I/O Network

* Mở Wireshark, filter `tcp.port == 9000`.
* Vẽ 1 nét, follow TCP stream, thấy JSON `{"Type":"DRAW_END",...}`.

---

## 5. Mapping log liên quan I/O

* `[CHAT_FILE]`/`[CHAT_MESSAGE]`: broadcast từ server.
* `[AutoSave] Snapshot ...`: DB I/O (snapshot) sau khi draw.
* `Unable to read data ...`: socket đóng đột ngột khi client đóng app.

---

## 6. File cần mở khi demo

* Load config: [CanvasApp.LoadBalancer/Program.cs](CanvasApp.LoadBalancer/Program.cs#L31-L55) và [LoadConfig](CanvasApp.LoadBalancer/Program.cs#L90-L101)
* Import ảnh nền: [CanvasApp.Client/Forms/CanvasForm.cs](CanvasApp.Client/Forms/CanvasForm.cs#L133-L142)
* Export PNG/JPEG: [CanvasApp.Client/Forms/CanvasForm.cs](CanvasApp.Client/Forms/CanvasForm.cs#L147-L188)
* Gửi file chat: [CanvasApp.Client/Forms/CanvasForm.cs](CanvasApp.Client/Forms/CanvasForm.cs#L1225-L1239)
* Nhận file chat: [CanvasApp.Client/Forms/CanvasForm.cs](CanvasApp.Client/Forms/CanvasForm.cs#L1143-L1156)
* TCP send/recv: [CanvasApp.Client/Network/CanvasClient.cs](CanvasApp.Client/Network/CanvasClient.cs#L124-L218)
* Lobby request ngắn hạn: [CanvasApp.Client/Network/LobbyClient.cs](CanvasApp.Client/Network/LobbyClient.cs#L73-L114)

---

## 7. Kết luận

Phần I/O File/Network thể hiện đủ cả thao tác đọc/ghi file local (import/export) và truyền dữ liệu qua TCP (draw/chat/file). Sử dụng line-delimited JSON để dễ debug, và có cơ chế giới hạn payload + send-lock để tránh lỗi interleaving.