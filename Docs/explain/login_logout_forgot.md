# 1.2 🔑 Login / Logout / Forgot password — multi-server & multi-client

> Bối cảnh: **2 AuthServer** (port 9001 + 9011) đứng sau **1 LoadBalancer** (port 9000), nhiều **Client** kết nối song song, dùng chung **1 MySQL**. Code: [AuthHandler.cs](../../CanvasApp.AuthServer/AuthHandler.cs), [AuthService.cs](../../CanvasApp.AuthServer/Services/AuthService.cs), [UserService.cs](../../CanvasApp.AuthServer/Services/UserService.cs), [UserStore.cs](../../CanvasApp.AuthServer/UserStore.cs), [LoadBalancer.cs](../../CanvasApp.LoadBalancer/LoadBalancer.cs).

---

## 1. Topology

```
                          ┌────────────────────────────┐
                          │       MySQL (shared)       │
                          │  users · email_otp_codes   │
                          └─────────────┬──────────────┘
                                        │
                  ┌─────────────────────┼─────────────────────┐
                  │                                           │
            ┌─────▼──────┐                              ┌─────▼──────┐
            │ AuthServer │                              │ AuthServer │
            │   :9001    │                              │   :9011    │
            └─────▲──────┘                              └─────▲──────┘
                  │                                           │
                  └──────────────┐         ┌──────────────────┘
                                 │         │
                            ┌────▼─────────▼────┐
                            │  LoadBalancer     │   peek `type` → pickAuth() (round-robin)
                            │      :9000        │
                            └─┬──────┬──────┬───┘
                              │      │      │
                        ┌─────▼─┐  ┌─▼──┐  ┌▼────┐
                        │Client │  │... │  │ ... │   N client song song
                        └───────┘  └────┘  └─────┘
```

- Mỗi request AUTH là **1 connection ngắn hạn**: client → LB → 1 trong các AuthServer → đóng.
- LB chọn AuthServer bằng **round-robin** trên các backend healthy ([LoadBalancer.cs:102-108](../../CanvasApp.LoadBalancer/LoadBalancer.cs#L102-L108)). Không sticky theo user → 2 request liên tiếp của cùng 1 user có thể rơi vào 2 AuthServer khác nhau.
- Cấu hình pool: [appsettings.json](../../CanvasApp.LoadBalancer/appsettings.json).

>  Auth không lưu session in-memory: token là **HMAC-signed payload** (xem [cryptography.md §8.2](cryptography.md#82-hmac-sha256--auth-token-jwt-lite)). Server nào nhận cũng verify được bằng cùng `CANVASAPP_JWT_SECRET`. Đây là điều cho phép pool scale horizontal mà không cần sticky session / Redis.

---

## 2. LOGIN — Client → LB → AuthServer

### Sơ đồ flow

```
┌──────┐                ┌────┐               ┌────────────┐         ┌───────┐
│Client│                │ LB │               │ AuthServer │         │ MySQL │
└──┬───┘                └─┬──┘               └─────┬──────┘         └───┬───┘
   │ TCP connect :9000    │                        │                    │
   ├─────────────────────▶│                        │                    │
   │ AUTH_LOGIN (json+\n) │                        │                    │
   ├─ MessageCrypto───────▶│ peek type=AUTH_LOGIN  │                    │
   │  .Serialize           │ → PickAuth() (RR)     │                    │
   │  (AES enc payload)    │                       │                    │
   │                       │ open backend socket   │                    │
   │                       ├──forward firstLine───▶│                    │
   │                       │                        │ Deserialize        │
   │                       │                        │ (AES decrypt)      │
   │                       │                        │ SELECT users       │
   │                       │                        │   WHERE username  ─▶
   │                       │                        │◀── row + hash ─────┤
   │                       │                        │ BCrypt.Verify(pwd) │
   │                       │                        │ IssueToken(uid,nm) │
   │                       │                        │ UPDATE last_login ▶│
   │                       │ AUTH_LOGIN_RESULT      │                    │
   │                       │◀──(AES enc)────────────┤                    │
   │◀──forward─────────────┤                        │                    │
   │ close                 │ close backend          │                    │
```

### Bên server:

```csharp
// 1. Bind theo username (đã trim) — tránh trailing space gây "Sai tài khoản"
SELECT id, username, email, password_hash, avatar_color
FROM users WHERE username = @Username;

// 2. Username không tồn tại → vẫn chạy BCrypt với dummy hash để equalise timing
//    (chống user-enumeration via timing side-channel)
BCrypt.Verify(password, _dummyBcryptHash);  // ~100ms như verify thật
return "Sai tài khoản hoặc mật khẩu";

// 3. Verify mật khẩu (workFactor=12 → ~100ms — đắt, cố ý)
if (!BCrypt.Verify(password, passwordHash))
    return "Sai tài khoản hoặc mật khẩu";

// 4. Issue token = base64(payload) "." base64(HMAC-SHA256(payload, secret))
//    payload = "userId:username:issuedAt(unix)"
//    Canvas server verify cùng secret → biết user là ai → không cần Auth tra DB lần nữa.
```

Code: [UserStore.cs:245-321](../../CanvasApp.AuthServer/UserStore.cs#L245-L321).

### Bên LB: round-robin với health check

```csharp
private ServerInfo PickAuth() {
    var healthy = _authPool.Where(s => s.IsHealthy).ToList();
    if (healthy.Count == 0) return null;        // ⚠ trả null → reject client
    int idx = Interlocked.Increment(ref _authRR) % healthy.Count;
    return healthy[idx];
}
```

- `IsHealthy` cập nhật từ background `HealthCheck` task (TCP probe 5s/lần).
- Mỗi connect-fail tăng `FailCount`; đạt threshold → server đánh Down, bị loại khỏi pool.
- Server back online → reset, lại nhận traffic.

### Bên client

```csharp
using (var tcp = new TcpClient()) {
    await tcp.ConnectAsync(LB_HOST, LB_PORT);   // không direct connect AuthServer
    // Send AUTH_LOGIN (đã AES-encrypt) → read AUTH_LOGIN_RESULT → decrypt → parse
    // Timeout 10s; nếu nghẽn (LB chậm hoặc Auth backend treo) → hiện "timeout 10s"
}
// Connection đóng ngay sau khi nhận response — KHÔNG giữ kết nối Auth lâu dài.
Session.CurrentUser = result.User;
Session.Token = result.Token;
```

Code: [AuthClient.LoginAsync](../../CanvasApp.Client/Network/AuthClient.cs#L29).

---

## 3. LOGOUT — Hoàn toàn client-side

### Flow

```csharp
private void btnLogout_Click(object sender, EventArgs e) {
    CanvasClient.Instance.Disconnect();   // đóng socket persistent (nếu có)
    Session.Clear();                       // wipe CurrentUser, Token, CanvasHost/Port
    new LoginForm().Show();
    this.Hide();
}
```

Code: [LobbyForm.cs:525-533](../../CanvasApp.Client/Forms/LobbyForm.cs#L525-L533).

**Không có `AUTH_LOGOUT` message**. Vì sao?

| Lý do | Diễn giải |
|---|---|
| Token là **stateless** (HMAC) | Không có "session table" để xóa — server không lưu state về phiên đăng nhập. |
| Multi-server: revocation phải broadcast | Nếu muốn invalidate, phải báo cho **mọi AuthServer + mọi CanvasServer**. Cần thêm Redis/blocklist → overhead. |
| Hết hạn tự nhiên | TTL token = **24h** (`TokenTtlSeconds`); sau đó verify tự fail. |
| Đủ cho mô hình demo | Logout = user xóa token khỏi máy mình. Attacker phải có sẵn token mới forge được. |

> **Cảnh báo bảo mật**: nếu token bị leak (ví dụ máy bị compromise), logout phía client **không đủ** — kẻ giữ token vẫn dùng được tới khi hết 24h. Production sẽ cần thêm token blocklist (Redis) hoặc rút TTL xuống ngắn + refresh-token, nhưng dự án demo chấp nhận trade-off này.

### Multi-client cùng tài khoản

```
PC1: login → token_A (issuedAt=T0)
PC2: login → token_B (issuedAt=T1)
PC1: logout → wipe Session (token_A bị "quên" ở local PC1)
PC2: vẫn dùng token_B bình thường → KHÔNG ảnh hưởng
```

→ Một user có thể đăng nhập đồng thời nhiều máy. Mỗi token độc lập. Logout máy nào chỉ ảnh hưởng máy đó.

---

## 4. FORGOT PASSWORD — 2 bước, dùng email-OTP

### Bước 1: Yêu cầu mã (`AUTH_FORGOT_SEND_OTP`)

```
Client → LB → AuthServer A (RR)
   ├ FindByEmail(email) → user tồn tại?
   ├ CheckRateLimit(email) → ≤ 1/phút, 5/giờ
   ├ code = RNG.6digits()
   ├ token = GUID()
   ├ codeHash = BCrypt(code, 10)
   ├ INSERT email_otp_codes (token, username, email, codeHash, expires=+5m)
   └ SMTP.Send(email, code)            ← code chỉ gửi qua mail, KHÔNG về client
Client ← AUTH_FORGOT_SEND_OTP_RESULT { OtpToken: GUID, ExpiresInSeconds: 300 }
```

Code: [OtpService.SendForgotPasswordOtp:70-138](../../CanvasApp.AuthServer/Services/OtpService.cs#L70-L138).

### Bước 2: Đặt lại mật khẩu (`AUTH_RESET_PASSWORD`)

```
User mở mail → đọc 6 số → nhập vào client
Client → LB → AuthServer B (có thể KHÁC server đã gửi mã)
   ├ FindByEmail(email)
   ├ Verify(otpToken, otpCode, user.Username, email, markUsedOnSuccess=FALSE)
   │     └ SELECT WHERE token=@token (bất kỳ AuthServer nào cũng tìm thấy — chung DB)
   │     └ check: !used && !expired && attempts<5 && (username,email) khớp
   │     └ BCrypt.Verify(code, codeHash)
   ├ UPDATE users SET password_hash = BCrypt(NewPassword, 12) WHERE id = user.Id
   └ MarkUsed(otpToken)                ← burn SAU update thành công
Client ← AUTH_RESET_PASSWORD_RESULT { Success: true }
```

Code: [UserService.ResetPassword:66-107](../../CanvasApp.AuthServer/Services/UserService.cs#L66-L107).

### có thể rơi vào AuthServer khác nhau?

```
   B1: AUTH_FORGOT_SEND_OTP   → LB picks AuthServer :9001
       AuthServer :9001 ─INSERT─▶ MySQL.email_otp_codes (token=GUID1)

   (user mở mail, gõ code, vài chục giây sau)

   B2: AUTH_RESET_PASSWORD    → LB picks AuthServer :9011  ← KHÁC server!
       AuthServer :9011 ─SELECT WHERE token=GUID1─▶ MySQL  ← vẫn tìm được
       AuthServer :9011 ─UPDATE users.password_hash────▶ MySQL
```

→ **State chia sẻ qua MySQL**, không qua RAM server. Đây là lý do thiết kế stateless: client không cần biết, không cần sticky route, chỉ cần token GUID đúng và DB còn row.

### Vì sao `markUsedOnSuccess: false` ở bước Verify?

```csharp
// Tránh case:
//   1. Verify OTP → MarkUsed(token)    ← token đã burn
//   2. UpdatePassword → DB lỗi          ← password CHƯA đổi
//   3. User retry → OTP đã used → bị từ chối → phải xin code mới
//   4. Nhưng rate-limit chặn 1 phút → user bực
//
// Fix: burn token CHỈ KHI UpdatePassword thành công
var verify = _otpService.Verify(req.OtpToken, req.OtpCode, ..., markUsedOnSuccess: false);
if (!verify.ok) return error;

var ok = _store.UpdatePassword(user.Id, req.NewPassword);
if (ok) _otpService.MarkUsed(req.OtpToken);   // ← chỉ burn ở đây
```

Code: [UserService.cs:90-100](../../CanvasApp.AuthServer/Services/UserService.cs#L90-L100).

---

## 5. Token sau khi đổi mật khẩu — race window

```
T0: Client A: login → token_A (issuedAt=T0, hash_v1)
T1: Client B (cùng user): forgot-password → đổi password → hash_v2
T2: Client A: gửi ROOM_JOIN với token_A
    → Canvas server verify HMAC → ✅ pass (token vẫn signed đúng, chưa hết TTL)
    → Canvas server CHẤP NHẬN, user vẫn vào được room
```

**Token cũ vẫn dùng được** tới khi hết 24h, vì:
- Canvas server **chỉ verify chữ ký HMAC** (HMAC còn đúng vì secret không đổi).
- KHÔNG tra `password_hash` từ DB mỗi request (sẽ giết performance — login chính là điểm exchange password → token).
- KHÔNG có version trong token để invalidate hàng loạt.

Trade-off chấp nhận được trong demo. Production: thêm `password_version` vào token payload + so với `users.password_version` mỗi request quan trọng.

---

## 6. Concurrent multi-client scenarios

### 6.1 Cùng user, login đồng thời 2 máy

```
PC1 ─AUTH_LOGIN─▶ LB ─▶ AuthServer 9001 ─SELECT─▶ MySQL
PC2 ─AUTH_LOGIN─▶ LB ─▶ AuthServer 9011 ─SELECT─▶ MySQL
```

- 2 transaction `SELECT` chạy song song — không conflict (read-only).
- `UPDATE users SET last_login_at = NOW()` — không lock dài, ok.
- 2 token issued ra → 2 phiên độc lập. OK.

### 6.2 Cùng email, spam forgot-password từ nhiều máy

```
PC1 (T=0):   AUTH_FORGOT_SEND_OTP(email=X) → AuthServer 9001 → INSERT (rate=ok) → mail
PC2 (T=2s):  AUTH_FORGOT_SEND_OTP(email=X) → AuthServer 9011
              ├ CountRecentByEmail(email, -1min) → 1 ≥ RateLimitPerMinute
              └ return "Vui lòng đợi 1 phút"
```

Rate-limit query đếm **trên DB chung** → áp dụng cross-server. 1 user spam dù nhảy server qua LB cũng vẫn bị chặn.

> Limit: dựa **email** không phải IP → attacker đổi email mỗi lần vẫn bypass (chỉ chống abuse 1 user, không chống enumeration scale lớn).

### 6.3 Race: verify OTP rồi user reset password 2 lần liên tiếp

```
T0: Client → AUTH_RESET_PASSWORD (otp_X) → AuthServer 9001
    ├ Verify(otp_X, markUsedOnSuccess=false) → ok
    ├ UpdatePassword → ok
    └ MarkUsed(otp_X)              ← row updated: used=1
T1 (50ms sau): Client retry vì timeout phía UI → AUTH_RESET_PASSWORD (otp_X) → AuthServer 9011
    ├ Verify(otp_X) → SELECT thấy used=1 → return "Mã đã được sử dụng"
```

`MarkUsed` là `UPDATE ... SET used = 1`, atomic single-row. Mọi AuthServer SELECT sau đó đều thấy used → reject. Idempotent ở DB level.

### 6.4 1 AuthServer chết giữa chừng

```
PC1: AUTH_LOGIN ─▶ LB ─▶ AuthServer 9001 ─▶ ✓ ok (token issued)
                                  ↓
                            (9001 crash ngay sau)
PC2: AUTH_LOGIN ─▶ LB ─▶ health check thấy 9001 Down ─▶ PickAuth pick 9011 ─▶ ✓ ok
PC3: dùng token_PC1 join room ─▶ Canvas server verify HMAC ─▶ ✓ (chỉ cần secret, không cần AuthServer)
```

→ AuthServer rớt **không ảnh hưởng** token đã issued. Đây là lợi ích chính của stateless HMAC: tách quyết định "verify identity" khỏi quyết định "phục vụ request thông thường".

---

## 7. Bảo mật multi-server 

| Thứ phải share giữa các instance | Tại sao | Nếu lệch sẽ ra sao |
|---|---|---|
| `CANVASAPP_JWT_SECRET` (mọi AuthServer + mọi CanvasServer) | Auth issue token, Canvas verify | Token issued bởi Auth_X bị Canvas reject ("Token không hợp lệ") |
| `CANVASAPP_AES_KEY` (mọi AuthServer + mọi Client) | AES-256 envelope cho `data` field | Decrypt fail → exception → "Lỗi kết nối" phía client; hoặc data hiển thị `{_enc,_v}` raw → `Success=false, Message=null` → "Đăng nhập thất bại" (xem [io_file_network.md](io_file_network.md) cho chi tiết wire format) |
| MySQL connection string (cùng DB, cùng schema) | Users + OTP table | Phân mảnh state: login ở Auth A nhưng forgot-password ở Auth B không thấy OTP record |
| SMTP credentials | Gửi mail OTP | Một server gửi được, server còn lại không → user hên xui có nhận được mail |

> Trong dự án này tất cả 4 mục trên đều resolve theo cùng cơ chế (env var > config > hardcoded fallback). Build & deploy cùng commit → mặc định đồng bộ. Vấn đề thực tế hay gặp nhất là **deploy không đồng bộ binary** giữa các máy (ví dụ máy client copy bản cũ thiếu `MessageCrypto`) — xem [all_message.md](all_message.md) phần ghi chú AES.

---

## 8. Tóm tắt threat model

| Tình huống | Cơ chế phòng | Code |
|---|---|---|
| User enumeration qua timing login | Dummy `BCrypt.Verify` ở nhánh user-not-found | [UserStore.cs:272](../../CanvasApp.AuthServer/UserStore.cs#L272) |
| Brute-force password | `BCrypt(workFactor=12)` ~100ms/hash | [UserStore.cs:220](../../CanvasApp.AuthServer/UserStore.cs#L220) |
| Forge token user khác | `HMAC-SHA256` + secret server-only | [UserStore.cs:398](../../CanvasApp.AuthServer/UserStore.cs#L398) |
| Replay token cũ | `issuedAt` trong payload + TTL 24h | [UserStore.cs:431-433](../../CanvasApp.AuthServer/UserStore.cs#L431-L433) |
| Timing leak token signature | `FixedTimeEquals` constant-time compare | [UserStore.cs:440](../../CanvasApp.AuthServer/UserStore.cs#L440) |
| Brute-force OTP (1M permutations) | `attempts >= 5` khoá token + expiry 5 phút | [OtpService.cs:248](../../CanvasApp.AuthServer/Services/OtpService.cs#L248) |
| Spam OTP mail | Rate-limit per-email (1/phút, 5/giờ) — cross-server vì query DB chung | [OtpService.cs:47-62](../../CanvasApp.AuthServer/Services/OtpService.cs#L47-L62) |
| Replay OTP đã dùng | `used=1` sau verify thành công | [OtpService.cs:265](../../CanvasApp.AuthServer/Services/OtpService.cs#L265) |
| Code-swap (OTP của email A dùng cho B) | Bind `(username, email)` check | [OtpService.cs:252-257](../../CanvasApp.AuthServer/Services/OtpService.cs#L252-L257) |
| DB hiccup giữa verify OTP và update password | `markUsedOnSuccess=false` + burn sau khi commit | [UserService.cs:90-100](../../CanvasApp.AuthServer/Services/UserService.cs#L90-L100) |
| Sniff payload trên LAN | AES-256-CBC + HMAC Encrypt-then-MAC | [AesHelper.cs](../../CanvasApp.Common/Utils/AesHelper.cs) |

---

## 9. Code 

| Layer | File |
|---|---|
| Routing AUTH_* qua LB | [LoadBalancer.cs:267-276](../../CanvasApp.LoadBalancer/LoadBalancer.cs#L267-L276) |
| AuthServer dispatch | [AuthHandler.cs:97-110](../../CanvasApp.AuthServer/AuthHandler.cs#L97-L110) |
| Login (BCrypt + dummy timing) | [UserStore.Login](../../CanvasApp.AuthServer/UserStore.cs#L245) |
| Issue/Verify token (HMAC) | [UserStore.IssueToken / VerifyToken](../../CanvasApp.AuthServer/UserStore.cs#L398) |
| Register (validate + verify OTP + insert) | [UserService.Register](../../CanvasApp.AuthServer/Services/UserService.cs#L21) |
| OTP send (cho cả register + forgot) | [OtpService](../../CanvasApp.AuthServer/Services/OtpService.cs) |
| Reset password | [UserService.ResetPassword](../../CanvasApp.AuthServer/Services/UserService.cs#L66) |
| Client login | [AuthClient.LoginAsync](../../CanvasApp.Client/Network/AuthClient.cs#L29) |
| Client logout | [LobbyForm.btnLogout_Click](../../CanvasApp.Client/Forms/LobbyForm.cs#L525) |
| Forgot password UI flow | [ForgotPasswordForm.cs](../../CanvasApp.Client/Forms/ForgotPasswordForm.cs) |
