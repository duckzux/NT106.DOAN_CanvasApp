# 1.1 📧 Chi tiết flow gửi OTP qua email

> Áp dụng cho cả **đăng ký** (`AUTH_SEND_OTP`) lẫn **quên mật khẩu** (`AUTH_FORGOT_SEND_OTP`). Code: [OtpService.cs](../../CanvasApp.AuthServer/Services/OtpService.cs), [OtpStore.cs](../../CanvasApp.AuthServer/OtpStore.cs), [SmtpEmailSender.cs](../../CanvasApp.AuthServer/Services/SmtpEmailSender.cs).

### Sơ đồ flow (đăng ký — quên mật khẩu tương tự)

```
┌────────┐                ┌────────┐               ┌──────────┐         ┌────────┐
│ Client │                │  Auth  │               │  MySQL   │         │ Gmail  │
│        │                │ Server │               │email_otp │         │  SMTP  │
└───┬────┘                └───┬────┘               └─────┬────┘         └────┬───┘
    │  AUTH_SEND_OTP          │                          │                   │
    │ {Username, Email}       │                          │                   │
    ├────────────────────────▶│                          │                   │
    │                         │ CheckRateLimit(email)    │                   │
    │                         ├─SELECT COUNT(*)─────────▶│                   │
    │                         │◀─count───────────────────┤                   │
    │                         │                          │                   │
    │                         │ UsernameExists?          │                   │
    │                         ├──SELECT 1 FROM users────▶│                   │
    │                         │                          │                   │
    │                         │ code = RNG.6digits()     │                   │
    │                         │ token = GUID             │                   │
    │                         │ codeHash = BCrypt(code,  │                   │
    │                         │   workFactor=10)         │                   │
    │                         │                          │                   │
    │                         │ Save(token, username,    │                   │
    │                         │   email, codeHash,       │                   │
    │                         │   expires=now+5m,        │                   │
    │                         │   created=UtcNow)        │                   │
    │                         ├──INSERT─────────────────▶│                   │
    │                         │                          │                   │
    │                         │ SmtpClient.Send(         │                   │
    │                         │   to=email,              │                   │
    │                         │   body=HTML{code})       │                   │
    │                         ├──────────────────────────────────────────────▶│
    │                         │                          │           SMTPS:587│
    │                         │                          │           STARTTLS │
    │                         │                          │           AUTH PLAIN│
    │                         │                          │                   │
    │ AUTH_SEND_OTP_RESULT    │                          │                   │
    │ {Success, OtpToken,     │                          │                   │
    │  ExpiresInSeconds:300}  │                          │                   │
    │◀────────────────────────┤                          │                   │
    │                         │                          │                   │
    │   (user mở mail, đọc code 6 số)                    │                   │
    │                         │                          │                   │
    │  AUTH_REGISTER          │                          │                   │
    │ {Username, Password,    │                          │                   │
    │  Email, OtpToken,       │                          │                   │
    │  OtpCode:"482917"}      │                          │                   │
    ├────────────────────────▶│                          │                   │
    │                         │ Verify(token, code, ...) │                   │
    │                         ├─SELECT...WHERE token────▶│                   │
    │                         │◀─OtpRecord───────────────┤                   │
    │                         │ BCrypt.Verify(code,      │                   │
    │                         │   record.CodeHash)       │                   │
    │                         │ MarkUsed(token) (atomic) │                   │
    │                         ├─UPDATE used=1───────────▶│                   │
    │                         │                          │                   │
    │                         │ UserStore.Register(...)  │                   │
    │                         ├─INSERT users────────────▶│                   │
    │ AUTH_REGISTER_RESULT    │                          │                   │
    │◀────────────────────────┤                          │                   │
```

### Tại sao tách `OtpToken` và `OtpCode`?

- `OtpCode` = 6 số gửi qua **email** (kênh phụ — out-of-band).
- `OtpToken` = GUID gửi qua **TCP** (kênh chính), client nhớ và gửi lại khi verify.
- Khi `AUTH_REGISTER` đi, server tra DB bằng `OtpToken` để lấy `codeHash`, rồi `BCrypt.Verify(OtpCode, codeHash)`. **Server không bao giờ lưu code plaintext.**
- Lợi ích: kẻ tấn công sniff được token cũng phải có mail mới biết code; sniff được code cũng phải có token mới biết bind với username nào.

### Schema bảng `email_otp_codes`

```sql
CREATE TABLE email_otp_codes (
    token       CHAR(36)     PRIMARY KEY,    -- GUID, gửi cho client
    username    VARCHAR(50)  NOT NULL,       -- bind code → username
    email       VARCHAR(100) NOT NULL,       -- bind code → email
    code_hash   VARCHAR(255) NOT NULL,       -- BCrypt(code, workFactor=10)
    expires_at  DATETIME     NOT NULL,       -- UTC, mặc định +5 phút
    attempts    INT          NOT NULL DEFAULT 0,  -- guard brute-force, max 5
    used        TINYINT(1)   NOT NULL DEFAULT 0,  -- single-shot
    created_at  DATETIME,                    -- UTC (set tường minh, KHÔNG dùng MySQL default)
    INDEX idx_otp_email (email)
);
```

### Sinh OTP code (cryptographically random)

```csharp
private static string GenerateSixDigitCode()
{
    var bytes = new byte[4];
    using (var rng = new RNGCryptoServiceProvider())
        rng.GetBytes(bytes);
    uint v = BitConverter.ToUInt32(bytes, 0) % 1000000u;
    return v.ToString("D6");   // "000000" → "999999"
}
```

- Dùng `RNGCryptoServiceProvider` (CSPRNG), **không** dùng `Random` (predictable seed = current time).
- 4 bytes → uint → mod 1_000_000 → có **modulo bias rất nhỏ** (chấp nhận được cho 6-digit OTP, không phải crypto key).

### Rate-limit (chống spam mail / DoS)

```csharp
private const int RateLimitPerMinute = 1;
private const int RateLimitPerHour   = 5;

private string CheckRateLimit(string email)
{
    if (_store.CountRecentByEmail(email, DateTime.UtcNow.AddMinutes(-1)) >= 1)
        return "Vui lòng đợi 1 phút trước khi yêu cầu mã mới";
    if (_store.CountRecentByEmail(email, DateTime.UtcNow.AddHours(-1)) >= 5)
        return "Đã yêu cầu quá nhiều mã trong 1 giờ — thử lại sau";
    return null;
}
```

- Per-email không per-IP → 1 user spam mail dù đổi IP vẫn bị chặn.
- Bị bypass nếu attacker đổi email mỗi lần — không phòng được user enumeration. Có thể thêm per-IP rate-limit sau.

### Verify (single-shot, brute-force guard)

```csharp
public (bool ok, string message, OtpRecord record) Verify(token, code, expectedUser, expectedEmail, ...)
{
    var rec = _store.Find(token);
    if (rec == null)                               return "Mã không tồn tại";
    if (rec.Used)                                  return "Mã đã được sử dụng";
    if (DateTime.UtcNow > rec.ExpiresUtc)          return "Mã hết hạn";
    if (rec.Attempts >= 5)                         return "Mã bị khoá do nhập sai 5 lần";

    // Bind: code phải sinh cho ĐÚNG (username, email) — chống code-swap
    if (rec.Username != expectedUser ||
        !string.Equals(rec.Email, expectedEmail, OrdinalIgnoreCase))
        return "Không khớp tài khoản/email";

    if (!BCrypt.Net.BCrypt.Verify(code, rec.CodeHash))
    {
        _store.IncrementAttempts(token);   // 5 lần sai → khoá
        return "Mã không đúng";
    }
    if (markUsedOnSuccess) _store.MarkUsed(token);  // single-shot
    return ok;
}
```

- `markUsedOnSuccess=false` dùng cho `AUTH_RESET_PASSWORD`: chỉ burn token **sau khi** `UpdatePassword` thành công — tránh case DB lỗi giữa chừng, OTP bị burn nhưng password chưa đổi → user phải xin mã mới.

### SMTP (Gmail App Password)

```
SmtpHost:    smtp.gmail.com
SmtpPort:    587   (submission, STARTTLS)
SmtpEnableSsl: true  → SmtpClient nâng cấp TLS sau khi greet
Credentials: NetworkCredential(user, AppPassword)
                ↑ App Password 16 ký tự, KHÔNG dùng password Gmail thật
Timeout:     15s
```

> Setup Gmail: bật 2FA → tạo App Password tại myaccount.google.com/apppasswords → lưu vào `App.config` (`SmtpPassword`). Production: chuyển qua env var hoặc Key Vault, không commit vào git.

### 

| Tấn công | Phòng chống |
|---|---|
| Sniff TCP để đoán code | Code chỉ ở mail (out-of-band), payload TCP chỉ có `OtpToken` GUID |
| Brute-force 6 số (1M permutations) | `attempts >= 5` khoá token; expiry 5 phút |
| Spam mail (DoS user inbox / SMTP cost) | Rate-limit 1/phút, 5/giờ per email |
| Replay code đã dùng | `used = 1` sau lần verify thành công đầu |
| Sniff DB dump | `code_hash = BCrypt(code, 10)` — không có plaintext |
| Code-swap (verify code của email A cho email B) | Bind `(username, email)` check trong `Verify()` |
| Race: DB lỗi giữa chừng đổi password → OTP đã burn | `markUsedOnSuccess=false` + burn sau khi commit |
| MitM đọc nội dung mail | TLS giữa AuthServer ↔ Gmail (STARTTLS:587); end-to-mailbox vẫn dựa Gmail TLS |
