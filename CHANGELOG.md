
# [2026-05-19] Fix — Canvas Không Giới Hạn Vùng Vẽ + Sửa Nút Chat

## CanvasApp.Client

### Vùng vẽ không giới hạn (Dynamic Canvas Expansion)

| File | Thay đổi |
|------|----------|
| `Forms/CanvasForm.cs` | Thêm method `EnsureCanvasCovers(float cx, float cy)` — trước mỗi thao tác vẽ, kiểm tra nếu tọa độ canvas ánh xạ ra ngoài biên bitmap (khoảng đệm 200 px) thì tự động mở rộng bitmap thêm **2000 px** về phía cần thiết: tạo `newBitmap` mới, copy nội dung cũ vào đúng vị trí, cập nhật `_canvasOffsetX`/`_canvasOffsetY`, tái tạo `_graphics` với `TranslateTransform` mới; canvas trở nên **không giới hạn kích thước** |
| `Forms/CanvasForm.cs` | Gọi `EnsureCanvasCovers` trước tất cả thao tác ghi lên bitmap: `DrawLineLocal` (pen freehand), `EraseLineLocal` (eraser), `DrawActionLocal` nhánh shape (rectangle/circle/line/arrow), nhánh text, nhánh fill; `Canvas_MouseDown` nhánh fill tool (tính `bx`/`by` sau khi đã expand) |

### Fix hiển thị nút chat

| File | Thay đổi |
|------|----------|
| `Forms/CanvasForm.Designer.cs` | `btnSendMessage.Text`: `" ➤"` → `">"` — ký tự Dingbat `U+27A4` không render được trên `Guna2Button` (hiện trống); thay bằng ASCII `>` luôn hiển thị đúng |
| `Forms/CanvasForm.Designer.cs` | `btnSendMessage.Font`: 9F Regular → **13F Bold** — font nhỏ Regular quá mờ, đồng bộ cỡ chữ với nút đính kèm |
| `Forms/CanvasForm.Designer.cs` | `btnAttachFile.Font`: 15F Bold → **13F Bold** — giảm xuống cho đồng đều với nút gửi |

## Bug Fixes

| Bug | Fix |
|-----|-----|
| Không vẽ được ở nửa trái / vùng xung quanh khi zoom out hoặc pan | Bitmap cố định (`panelWidth × 2`) — tọa độ canvas vượt biên bị GDI+ clip ngầm. Fix: `EnsureCanvasCovers` tự expand bitmap mỗi khi cần, không có giới hạn vùng vẽ |
| Khi export ảnh nội dung bị giới hạn, không xuất được vùng đã vẽ ngoài bitmap ban đầu | Cùng nguyên nhân trên; sau fix, bitmap luôn đủ lớn chứa toàn bộ nét vẽ nên `GetContentBounds` + export hoạt động đúng |
| Nút gửi tin nhắn hiển thị trống (không thấy mũi tên) | `" ➤"` (Unicode Dingbat) không có glyph trong font Segoe UI 9pt trên Guna2Button. Fix: đổi sang `">"` ASCII |
| Hai nút chat kích thước font không đồng đều | `btnAttachFile` 15F vs `btnSendMessage` 9F. Fix: cả hai thống nhất 13F Bold |

---

# [2026-05-19] Chat — Gửi & Tải File Đính Kèm + Fix Layout Input Area

## CanvasApp.Common

| File | Thay đổi |
|------|----------|
| `Models/Models.cs` | `ChatMessage`: thêm 3 field `FileName` (`string`), `FileData` (`string`, Base64), `FileSizeBytes` (`long`) để mang dữ liệu file đính kèm; field null với tin nhắn text thường |
| `Models/Message.cs` | Thêm hằng `CHAT_FILE = "CHAT_FILE"` vào `MessageType` |

## CanvasApp.Server

| File | Thay đổi |
|------|----------|
| `Program.cs` | Tăng `MaxPayloadBytes` từ 64 KB lên **4 MB** để chứa file Base64 tối đa 2 MB raw (~2.7 MB sau encode); thêm `case MessageType.CHAT_FILE` — kiểm tra `FileName`/`FileData` không rỗng, gán `UserId`/`Username`/`Timestamp` từ token, broadcast tới room; file **không** được persist vào DB |

## CanvasApp.Client — Network

| File | Thay đổi |
|------|----------|
| `Network/CanvasClient.cs` | Thêm method `SendFileAsync(string fileName, byte[] data)` — encode Base64 và gửi `CHAT_FILE` message |

## CanvasApp.Client — UI

| File | Thay đổi |
|------|----------|
| `Forms/CanvasForm.Designer.cs` | Thêm `btnAttachFile` (`Guna2Button`, `"+"`, 36×42, purple, `BorderRadius=20`, `Font 15F Bold`); điều chỉnh `txtMessageInput` từ `(6,693) 182×48` → `(48,693) 136×48`; điều chỉnh `btnSendMessage` từ `(194,696) 49×45` → `(192,696) 50×42` — căn giữa dọc 3 controls so với nhau |
| `Forms/CanvasForm.cs` | Thêm field `_chatFiles` (`Dictionary<string,(FileName,Data)>`); wire `btnAttachFile.Click` → `AttachFile()`; bật `rtbChatHistory.DetectUrls = true`; `LinkClicked` handler: intercept `https://canvas-file/{id}` → mở `SaveFileDialog` để tải file từ `_chatFiles`; thêm `AttachFile()` (mở `OpenFileDialog`, giới hạn 2 MB, gọi `SendFileAsync`); thêm `AppendFileMessage()` hiển thị `"📎 filename (size)"` kèm link download; thêm `FormatFileSize()` helper (B/KB/MB); thêm `case MessageType.CHAT_FILE` trong message handler — decode Base64, lưu vào `_chatFiles`, gọi `AppendFileMessage` |

## Bug Fixes / UX

| Vấn đề | Fix |
|--------|-----|
| Emoji `📎` không render được trên `Guna2Button` (hiển thị hình thoi) | Đổi text nút đính kèm thành `"+"` (ASCII), tăng font 15F Bold |
| Nút gửi và nút đính kèm không căn giữa dọc với ô nhập | Đồng bộ lại `Location.Y` và `Height` của 3 controls (`btnAttachFile`, `txtMessageInput`, `btnSendMessage`) |

---

# [2026-05-19] Canvas Drawing & Export — Bug Fixes & Virtual Canvas Expansion

## CanvasApp.Client

| File | Thay đổi |
|------|----------|
| `Forms/CanvasForm.cs` | Thêm fields `_canvasOffsetX`, `_canvasOffsetY`; `InitCanvas()` tạo bitmap **4× kích thước panel** và apply `TranslateTransform(_canvasOffsetX, _canvasOffsetY)` lên `_graphics` để gốc tọa độ canvas `(0,0)` nằm ở giữa bitmap; `CanvasPanel_Paint` render bitmap tại `(-_canvasOffsetX, -_canvasOffsetY)` thay vì `(0,0)`; zoom tối thiểu tăng từ `0.1` lên `0.25` |
| `Forms/CanvasForm.cs` | `Canvas_MouseUp` refactor async: capture `_currentStroke` vào biến local `stroke` và gán `_currentStroke = null` **trước** `await SendDrawAsync` — loại bỏ race condition khi user vẽ nhanh; push undo stack và `Invalidate()` cũng thực hiện trước await |
| `Forms/CanvasForm.cs` | `DrawActionLocal()`: thêm null/bounds guard cho shape actions — kiểm tra `action == null`, `action.Points == null`, `_graphics == null`, `Points.Count < 2` trước khi truy cập `Points[0]`/`Points[1]` |
| `Forms/CanvasForm.cs` | Export: thêm `GetContentBounds()` scan trực tiếp pixel non-transparent trong `_bitmap` qua `LockBits` + `Marshal.Copy` để tính bounding box thực tế của toàn bộ nội dung (kể cả nét vẽ remote users không có trong `_undoStack`); export bitmap có kích thước khớp đúng vùng content |
| `Forms/CanvasForm.cs` | `DrawBackgroundTemplateForExport()` thêm tham số `canvasOriginX/Y` để align dots/lines/grid đúng với canvas coordinates khi export vùng ngoài origin |
| `Forms/CanvasForm.cs` | Thêm `DrawBackgroundTemplateForExport()` vào export handler — background template (dotted/grid/lined) được vẽ kèm theo ảnh xuất |

## Bug Fixes

| Bug | Fix |
|-----|-----|
| Vẽ ở vùng xung quanh khi thu nhỏ (zoom out) không lưu nét vẽ | Bitmap trước đây chỉ bằng đúng kích thước panel — tọa độ canvas vượt giới hạn bitmap bị GDI+ clip ngầm. Fix: bitmap 4× panel với origin offset ở giữa, bao phủ zoom đến 0.25× |
| Shape cũ bị mất nét vẽ sau khi vẽ shape mới (async race condition) | `Canvas_MouseUp` là `async void` — sau `await`, `_currentStroke` có thể đã bị thay bởi stroke mới khiến undo stack push sai và stroke đang vẽ bị null hoá. Fix: capture local variable trước await |
| Undo/Redo crash sau khi có shape không hợp lệ trong stack | `RedrawCanvas` gọi `_graphics.Clear()` rồi replay, nếu `DrawActionLocal` throw `IndexOutOfRangeException` (shape chỉ có 1 điểm do race condition) thì bitmap bị xóa trắng vĩnh viễn. Fix: guard `Points.Count < 2` |
| Export chỉ capture vùng cố định ban đầu, bỏ sót nội dung zoom-out và remote users | Export dùng `_undoStack` để tính bounds — bỏ sót toàn bộ nét vẽ remote. Fix: scan pixel bitmap thực tế |
| Background template (dotted/grid/lined) không xuất hiện trong ảnh export | Export không gọi `DrawBackgroundTemplateForExport`. Fix: thêm vào export handler |

---

# [2026-05-19] UI Fix & Feature — Room Header Labels + Copy Invite Code Button

## CanvasApp.Client

| File | Thay đổi |
|------|----------|
| `Forms/CanvasForm.Designer.cs` | `lblRoomName`: `AutoSize = false`, `Size = (230, 28)`, `TextAlign = MiddleCenter`; `lblRoomCode`: `AutoSize = false`, `Size = (198, 24)`, `Location = (10, 43)`, `TextAlign = MiddleCenter`; thêm `btnCopyCode` (Button, 24×24, FlatStyle, no border, hand cursor, image từ `CanvasForm.resx`) vào `pnlRight`; khai báo field `private System.Windows.Forms.Button btnCopyCode` |
| `Forms/CanvasForm.resx` | Nhúng inline binary PNG (`btnCopyCode.Image`) của icon copy 64×64 dưới dạng `mimetype="application/x-microsoft.net.object.bytearray.base64"` thay vì `ResXFileRef` để tương thích MSBuild .NET Framework 4.8 |
| `Forms/CanvasForm.cs` | Thêm field `private string _roomPassword`; đổi `SetRoom(JoinRoomResult)` → `SetRoom(JoinRoomResult, string password = "")`, gán `_roomPassword = password`; `lblRoomName.Text` hiện hiển thị `"Phòng vẽ: {name}"`; sau `InitializeComponent` scale icon copy xuống 15×15 (`new Bitmap(btnCopyCode.Image, 15, 15)`); handler `btnCopyCode.Click` copy clipboard theo định dạng `"Mã mời: {code}\nMật khẩu: {password}"` (dòng mật khẩu chỉ xuất hiện nếu phòng có mật khẩu); hiệu ứng flash xanh lá 700 ms sau khi copy |
| `Forms/LobbyForm.cs` | Thêm field `private string _pendingPassword = ""`; gán `_pendingPassword` ở tất cả các nhánh join: `JoinRoom(card, password)`, `PromptJoinByCode()` (sau khi dialog OK), `ShowRequirePasswordForCode.OnSubmit`, `createRoom.OnRoomCreated` (cho creator auto-join); truyền `_pendingPassword` vào `canvas.SetRoom(res, _pendingPassword)` rồi reset về `""` |
| `Properties/Resources.resx` | Thêm `ResXFileRef` entry cho `copy.png` và `copy1` (cả hai trỏ về `Resources\copy.png`) |
| `Properties/Resources.Designer.cs` | VS tự sinh lại: thêm property `copy` và `copy1` kiểu `System.Drawing.Bitmap` |

## Bug Fixes

| Bug | Fix |
|-----|-----|
| `lblRoomName` chỉ hiển thị tên ngắn ("1", "2") thay vì "Phòng vẽ: 1" | Thêm prefix `"Phòng vẽ: "` khi gán `lblRoomName.Text` trong `SetRoom` |
| Tên phòng và mã mời không căn giữa | `AutoSize = false` + `TextAlign = MiddleCenter` + kích thước cố định cho cả hai label |
| Icon `btnCopyCode` không hiện (dùng `ResXFileRef` không hợp lệ với MSBuild .NET FW 4.8) | Nhúng PNG thành binary inline trong `CanvasForm.resx` với `mimetype` base64 |
| Icon hiện nhưng trống (64×64 button nhỏ 24×24 clip phần giữa rỗng) | Scale ảnh xuống 15×15 sau `InitializeComponent` bằng `new Bitmap(img, 15, 15)` |
| Mật khẩu không xuất hiện khi copy mã mời | `_pendingPassword` chưa được gán ở nhánh creator — thêm `_pendingPassword = pwd ?? ""` trong `createRoom.OnRoomCreated` |

---

# [2026-05-18] Bug Fixes & Feature Additions — Room Sync, Invite Codes, Chat Persistence, Architecture Hardening

## CanvasApp.Common — Models & Protocol

| File | Thay đổi |
|------|----------|
| `Models/Message.cs` | Thêm hằng `ROOM_JOIN_BY_CODE`, `CHAT_HISTORY` vào `MessageType` |
| `Models/Models.cs` | Thêm `Room.InviteCode` (`[JsonProperty("inviteCode")]`); thêm `JoinRoomResult.SnapshotData` (compressed baseline), `JoinRoomResult.RequiresPassword`; thêm class mới `InviteCodeRequest`, `ChatHistoryResult` |

## CanvasApp.Common — DataAccess & Utils

| File | Thay đổi |
|------|----------|
| `DataAccess/ChatMessageDAO.cs` | **File mới** — `Insert(roomId, userId, message)` lưu vào `chat_messages`; `GetByRoom(roomId, limit=50)` trả về lịch sử chat có join với `users` để lấy `username`, kết quả theo thứ tự tăng dần thời gian |
| `DataAccess/RoomDAO.cs` | Thêm cột `invite_code` vào `Insert()`, `FindById()`, `GetAllActive()` và `MapRow()`; schema SQL cập nhật phản ánh column mới |
| `Utils/SnapshotHelper.cs` | **File mới** — `static Compress(json)` (GZip + Base64) và `static Decompress(base64)` → `List<DrawAction>`; dùng chung bởi cả server lẫn client để tránh duplicate code và tránh circular dependency |
| `CanvasApp.Common.csproj` | Thêm `<Compile>` entry cho `ChatMessageDAO.cs` và `SnapshotHelper.cs` |

## CanvasApp.AuthServer

| File | Thay đổi |
|------|----------|
| `UserStore.cs` | **Fix bảo mật**: `VerifyToken()` bây giờ parse `issuedAt` từ token và từ chối nếu `now − issuedAt > 86400s` (24h TTL) — trước đây token không bao giờ hết hạn. Thêm `CREATE TABLE chat_messages` vào `InitializeDatabase()`. Thêm migration `ALTER TABLE rooms ADD COLUMN invite_code VARCHAR(8) NULL` vào `MigrateSchema()` |

## CanvasApp.Server — RoomManager (kiến trúc thay đổi lớn)

| Tính năng | Mô tả |
|-----------|-------|
| **All-client tracking** | `_allClients: ConcurrentDictionary<ConnectedClient, byte>` — track tất cả TCP client đang kết nối; `RegisterClient()` / `UnregisterClient()` gọi từ `HandleClient` |
| **Lobby broadcast** | `BroadcastToLobbyAsync(msg, except)` — gửi đến tất cả client có `CurrentRoomId == null` (đang ở lobby); dùng để đồng bộ danh sách phòng theo thời gian thực |
| **Invite code** | `_codeToRoomId: ConcurrentDictionary<string,string>`; `GenerateInviteCode()` sinh mã 6 ký tự (A-Z, 2-9, loại bỏ ký tự dễ nhầm); `GetRoomByInviteCode(code)`; code được lưu DB và load lại khi server restart |
| **Snapshot cache** | `_snapshotCache: ConcurrentDictionary<string,string>` lưu compressed snapshot mới nhất cho từng room; `SetSnapshotCache()` được gọi sau khi AutoSaveService persist DB xong |
| **Canvas trim** | `TrimCanvasState(roomId, upToSeq)` — xóa khỏi `_canvasState` tất cả action có `SeqNo ≤ upToSeq` sau mỗi snapshot; `_canvasState` chỉ giữ delta kể từ snapshot gần nhất thay vì toàn bộ lịch sử |
| **PrepareSnapshot mới** | Decompress `_snapshotCache` ngoài lock + append `_canvasState` delta bên trong lock → trả về full state cho AutoSaveService nén lại; tránh circular dependency qua `SnapshotHelper` |
| **EnsureCanvasLoaded mới** | Không còn add snapshot actions vào `_canvasState`; chỉ cache compressed data vào `_snapshotCache`, chỉ thêm delta vào `_canvasState` |
| **Join mới** | Trả về `JoinRoomResult.SnapshotData` (compressed) + `CanvasState` (delta only); khi room join lần đầu xóa `_lastEmptyTime` |
| **Idle room tracking** | `_lastEmptyTime` ghi timestamp khi room về 0 client; `GetIdleRoomIds(threshold)` + `RemoveIdleRoom(roomId)` dọn sạch RAM và soft-delete DB |
| **Leave** | Sau khi remove client, nếu room rỗng ghi `_lastEmptyTime[roomId] = UtcNow` |
| **ClearCanvas** | Cũng xóa `_snapshotCache[roomId]` để client join sau khi clear nhận canvas rỗng |

## CanvasApp.Server — AutoSaveService

| Thay đổi | Mô tả |
|----------|-------|
| **Fix GC race condition** | `DeleteOlderThanSeq(roomId, oldestSeq - GcSafetyMargin)` (margin = 200, khớp với batch size của `PersistenceQueue`); trước đây GC có thể xóa action chưa kịp flush xuống DB |
| **Snapshot cache + trim** | Sau khi persist snapshot xong gọi `_rooms.SetSnapshotCache()` rồi `_rooms.TrimCanvasState()`; giữ RAM bounded |
| **Idle room cleanup** | `CheckIdleRooms()` gọi mỗi 60s trong `Tick()`; room rỗng > 30 phút → `RemoveIdleRoom()` |
| **SnapshotHelper delegation** | `Compress`/`Decompress` ủy quyền cho `SnapshotHelper`; `AutoSaveService.Decompress()` giữ lại cho backward compat |

## CanvasApp.Server — Program.cs

| Thay đổi | Mô tả |
|----------|-------|
| **Client lifecycle** | `RegisterClient()` khi connect, `UnregisterClient()` trong finally block |
| **Input validation** | Từ chối payload > 64 KB trước khi parse JSON; tên phòng phải 1–100 ký tự; màu vẽ phải khớp regex `#RGB` hoặc `#RRGGBB` (auto-correct về `#000000`); chat text giới hạn 1000 ký tự |
| **ROOM_JOIN_BY_CODE** | Handler mới: lookup room bằng invite code → gọi `HandleJoin()` chung với `ROOM_JOIN` |
| **Shared HandleJoin()** | Private method dùng cho cả `ROOM_JOIN` và `ROOM_JOIN_BY_CODE`; gửi `CHAT_HISTORY` (50 tin gần nhất) ngay sau join thành công; broadcast `ROOM_UPDATE` cho client trong phòng; push `ROOM_LIST_RESULT` đến lobby |
| **Lobby sync** | Sau `ROOM_CREATE`, `ROOM_JOIN`, `ROOM_LEAVE`, `HandleClient.finally` đều push `ROOM_LIST_RESULT` đến tất cả lobby client |
| **Chat persistence** | Khởi tạo `ChatMessageDAO`; trong `CHAT_MESSAGE` handler gọi `_chatDao.Insert()` fire-and-forget |
| **Token error message** | Thông báo lỗi rõ hơn: `"Token không hợp lệ hoặc đã hết hạn"` |

## CanvasApp.Client

| File | Thay đổi |
|------|----------|
| `Network/CanvasClient.cs` | Thêm `JoinRoomByCodeAsync(inviteCode, password)` — gửi `ROOM_JOIN_BY_CODE` |
| `Forms/LobbyForm.cs` | Thêm `using System.Threading.Tasks`; thêm button "Nhập mã mời" programmatically bên cạnh `btnCreateShow`; `PromptJoinByCode()` hiển thị dialog nhập code + password tùy chọn; `HandleJoinResult()` xử lý `RequiresPassword = true` bằng cách hiện `RequirePassword` dialog và retry; bỏ handler `ROOM_UPDATE` (server giờ push `ROOM_LIST_RESULT` trực tiếp); thêm case `CHAT_HISTORY` (ignore trong lobby) |
| `Forms/CanvasForm.cs` | `SetRoom(JoinRoomResult)` thay thế `SetRoom(Room, List<DrawAction>, List<RoomMember>)`; decompresses `SnapshotData` qua `SnapshotHelper.Decompress()` rồi apply baseline trước, sau đó apply delta; `lblRoomCode` hiển thị `InviteCode`; thêm handler `CHAT_HISTORY` để populate chat log khi join |

## Database

| File | Thay đổi |
|------|----------|
| `Database/schema.sql` | Thêm column `invite_code VARCHAR(8) NULL` + `UNIQUE KEY uk_invite_code` vào bảng `rooms`; thêm bảng mới `chat_messages (id, room_id, user_id, message TEXT, sent_at)` với index `idx_chat_room (room_id, id)` |

## Bug Fixes

| Bug | Fix |
|-----|-----|
| Danh sách phòng không sync giữa các user sau login | Server push `ROOM_LIST_RESULT` đến tất cả lobby client sau mỗi sự kiện tạo/join/rời phòng |
| Lịch sử chat mất khi restart server hoặc join muộn | `ChatMessageDAO.Insert()` persist mọi tin; `GetByRoom()` + `CHAT_HISTORY` message gửi lại 50 tin gần nhất khi join |
| Race condition GC xóa action chưa flush | `DeleteOlderThanSeq(oldestSeq - 200)` — safety margin khớp batch size |
| `_canvasState` tăng vô hạn → RAM leak | Trim sau mỗi snapshot; `_canvasState` chỉ chứa delta; snapshot cache compressed |
| Room rỗng tích luỹ mãi trong RAM | `CheckIdleRooms()` mỗi 60s; room rỗng > 30 phút bị dọn |
| Token không hết hạn — token 6 tháng vẫn hợp lệ | `VerifyToken()` kiểm tra `issuedAt + 86400 > now` |
| Không validate đầu vào → injection / crash | Validate tên phòng, màu vẽ, kích thước payload, độ dài chat |

---

# [2026-05-18] Database Persistence Layer — Full Implementation & Bug Fixes

## CanvasApp.Common — DataAccess layer (toàn bộ từ stub trống → hoàn chỉnh)

| File | Thay đổi |
|------|----------|
| `DataAccess/DatabaseManager.cs` | Rewrite thành `public static` connection factory: `Initialize(connStr)`, `OpenConnection()`, `IsInitialized` |
| `DataAccess/UserDAO.cs` | Thêm `FindByUsername()`, `Insert()`, `UpdateLastLogin()` |
| `DataAccess/RoomDAO.cs` | Thêm `Insert()`, `FindById()`, `GetAllActive()`, `SetActive()` |
| `DataAccess/RoomMemberDAO.cs` | Thêm `Insert()`, `Find()`, `UpdateLastSeen()`, `GetMembers()` |
| `DataAccess/DrawActionDAO.cs` | Thêm `InsertBatch()` (multi-row, 1 transaction), `GetSinceSeq()`, `MarkUndone()`, `DeleteOlderThanSeq()` |
| `DataAccess/CanvasSnapshotDAO.cs` | Thêm `Insert()`, `GetLatest()`, `PruneOlderThan()` (giữ 5 snapshot gần nhất), `GetOldestKeptSeq()` |
| `DataAccess/PersistenceQueue.cs` | **File mới** — `BlockingCollection<DrawAction>` write-behind queue, batch tối đa 200 actions/transaction |
| `CanvasApp.Common.csproj` | Thêm reference `MySql.Data 9.6.0`; thêm compile entry cho `PersistenceQueue.cs` |
| `packages.config` | Thêm `MySql.Data 9.6.0` |

## CanvasApp.Common — Models

| File | Thay đổi |
|------|----------|
| `Models/Models.cs` | Thêm class `CanvasSnapshot`; mở rộng `DrawAction` thêm 3 field DB-only (`[JsonIgnore]`): `RoomId`, `SeqNo`, `IsUndone` |

## CanvasApp.AuthServer

| File | Thay đổi |
|------|----------|
| `UserStore.cs` | Cập nhật toàn bộ `CREATE TABLE` với schema mới: thêm `last_login_at` (users), `password_hash`+`template` (rooms), `last_seen_at` (room_members), `seq_no`+`is_undone`+`client_ts` (draw_actions), `action_seq_at`+`byte_size` (canvas_snapshots). Thêm `MigrateSchema()` dùng `ALTER TABLE ADD COLUMN` (bỏ qua lỗi 1060 nếu cột đã tồn tại). Thêm cập nhật `last_login_at` trong `Login()` |

## CanvasApp.Server

| File | Thay đổi |
|------|----------|
| `RoomState.cs` | Rewrite: thêm `InitSeqNo()`, `SnapshotVersionFromDb()`, `NextSnapshotVersion()` (Interlocked — thread-safe), `_snapshotVersion` dùng `private int` thay vì `public int` |
| `RoomManager.cs` | **Thay đổi lớn** — xem chi tiết bên dưới |
| `AutoSaveService.cs` | **File mới** — timer 60s chụp snapshot, nén GZip+Base64, GC giữ 5 snapshot, `ForceSnapshot()` để gọi on-demand, `static Decompress()` để dùng lại khi recovery |
| `Program.cs` | Khởi tạo DB, inject DAOs vào RoomManager, start PersistenceQueue + AutoSaveService, load rooms từ DB lúc startup; thêm handler `DRAW_UNDO`; `DRAW_CLEAR` gọi `ForceSnapshot`; force snapshot khi user cuối rời phòng; `_autoSave` và `_drawActionDao` là static field |

### RoomManager.cs — chi tiết thay đổi

- **Constructor** nhận thêm `CanvasSnapshotDAO` và `DrawActionDAO` (optional, null-safe)
- **`LoadActiveRooms()`** — load tất cả room `is_active=TRUE` từ DB vào RAM khi server khởi động
- **`EnsureCanvasLoaded(roomId)`** — lazy load snapshot + delta actions từ DB lần đầu client join room; dùng `_canvasLoaded` dictionary để chạy đúng 1 lần mỗi room
- **`Join()`** — fix race condition MaxUsers: check `Count >= MaxUsers` chuyển vào trong `lock(_roomClients[roomId])`; gọi `EnsureCanvasLoaded()` trước khi trả canvas state
- **`RecordDrawAction()`** — gán `SeqNo` từ `rs.NextSeqNo()`, push vào `_undoStacks` để track undo per-user
- **`UndoLastAction(roomId, userId)`** — pop seqNo từ stack, xóa action khỏi `_canvasState`, trả seqNo cho caller mark DB
- **`ClearCanvas()`** — xóa RAM, reset DirtyCount, xóa toàn bộ undo stacks của room
- **`PrepareSnapshot()`** — dùng `rs.NextSnapshotVersion()` thay vì `++rs.SnapshotVersion` (fix atomicity bug)
- **`GetMembers()`** — dùng `userId % colors.Length` thay `username.GetHashCode()` (stable across restarts)
- **`GetDirtyRoomIds()`**, **`RoomExists()`**, **`IsRoomEmpty()`** — thêm mới để hỗ trợ AutoSaveService và Program.cs

## Bug Fixes

| Bug | Fix |
|-----|-----|
| Server restart mất toàn bộ state (room + canvas) | `LoadActiveRooms()` + `EnsureCanvasLoaded()` |
| `DRAW_CLEAR` không xóa DB → restart replay lại canvas cũ | `ForceSnapshot()` sau khi clear (xóa draw_actions cũ) |
| `DRAW_UNDO` bị bỏ qua hoàn toàn | Handler đầy đủ: xóa RAM + `MarkUndone()` DB + broadcast |
| `autoSave` là local var → không gọi được từ `ProcessAsync` | Đổi thành `private static AutoSaveService _autoSave` |
| `PersistenceQueue` chết lặng khi `CancellationToken` bị cancel | Bắt `OperationCanceledException`, drain queue trước khi thoát |
| `++SnapshotVersion` không atomic | Thay bằng `Interlocked.Increment` qua `NextSnapshotVersion()` |
| Race condition MaxUsers: 2 client cùng join vượt giới hạn | Check `Count >= MaxUsers` đưa vào trong `lock` |
| Action bị drop âm thầm khi queue đầy | Log cảnh báo với drop counter |
| `last_login_at` không bao giờ được cập nhật | Thêm `UPDATE users SET last_login_at=NOW()` trong `Login()` |
| Avatar color thay đổi sau restart (GetHashCode không stable) | Dùng `userId % colors.Length` |
| Snapshot cuối không được chụp khi user cuối rời phòng | `ForceSnapshot()` trong `HandleClient` finally block |

---

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
   - Tính năng Tùy chỉnh độ dày nét vẽ (Line width) và cờ bật/tắt Đổ màu hình khối (Fill/Outline).
   - Zoom + Pan bằng Matrix transform
  > Dong Nguyen
2. **Cơ chế Hoàn tác (Undo/Redo):** 
   - Cần một `Stack<DrawAction>` (ví dụ: Lưu canvas state trong ConcurrentDictionary<roomId, List<DrawAction>>) để lưu lại các bút vẽ và khôi phục khi nhấn phím tắt như Ctrl+Z.
  > Dong Nguyen
3. **Đồng bộ con trỏ chuột theo thời gian thực (Cursor Sync):**
   - Sự di chuyển chuột của mọi người cần được truyền qua mạng dựa trên Event `MouseMove` để hiển thị trên thiết bị khác.
  > Kim Quyen
4. **Export / Import Ảnh nền:** 
   - Tính năng xuất `Canvas` ra file PNG/JPEG (`ExportDialog`) qua hàm `DrawToBitmap`.
   - Tính năng chèn ảnh làm hình nền vào trong `Paint event`.
  > Dong Nguyen
5. **Tính năng sáng tạo:**
   - Thêm hệ thống Layers (tắt bật, thay đổi layer của từng người).
   - Replay hệ thống (tua lại quá trình vẽ dự theo log Timestamp lưu trong CSDL MySQL).
   - Quản lý giao diện phông nền theo dạng Template (Dotted, Lined, Grid).
6. **Share link room**
     > Kim Quyen
7. **Load Balancer**
     > Kim Quyen
8. **Gợi ý hoàn thiện nét vẽ** (Kiểu vẽ hơi méo tự động gợi ý fix lại tròn,...)
9.  Xác thực email (kiểu check email real hay fake hoặc thêm cái dạng xác thực OTP qua mail càng tốt)
      > Kim Quyen
