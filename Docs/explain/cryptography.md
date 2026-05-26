## 8. 🔐 Cryptography — toàn bộ primitive đang dùng

> Thư viện: .NET Framework 4.8 `System.Security.Cryptography` + `BCrypt.Net-Next` (NuGet). Tất cả crypto là **standard-library**, không tự cài (homemade crypto = nguy hiểm).

### Bản đồ: chỗ nào dùng cái gì

| Mục đích | Primitive | Code |
|---|---|---|
| Hash password user | `BCrypt(password, workFactor=12)` | [UserStore.cs:220, 379](../../CanvasApp.AuthServer/UserStore.cs#L220) |
| Hash OTP code | `BCrypt(code, workFactor=10)` | [OtpService.cs:94](../../CanvasApp.AuthServer/Services/OtpService.cs#L94) |
| Hash password phòng vẽ | `BCrypt(roomPwd, workFactor=10)` | `RoomManager.cs` |
| Auth token (JWT-lite) | `HMAC-SHA256(payload, secret)` + base64 | [UserStore.cs:398-446](../../CanvasApp.AuthServer/UserStore.cs#L398-L446) |
| Random OTP code 6 số | `RNGCryptoServiceProvider` (CSPRNG) | [OtpService.cs:278](../../CanvasApp.AuthServer/Services/OtpService.cs#L278) |
| Token GUID | `Guid.NewGuid()` (v4 random) | [OtpService.cs:92](../../CanvasApp.AuthServer/Services/OtpService.cs#L92) |
| Mã hoá payload (opt-in) | `AES-256-CBC` + `HMAC-SHA256` (Encrypt-then-MAC) | [AesHelper.cs](../../CanvasApp.Common/Utils/AesHelper.cs), [MessageCrypto.cs](../../CanvasApp.Common/Utils/MessageCrypto.cs) |
| So sánh MAC / token | `FixedTimeEquals` (constant-time XOR) | [UserStore.cs:440](../../CanvasApp.AuthServer/UserStore.cs#L440), [AesHelper.cs:129](../../CanvasApp.Common/Utils/AesHelper.cs#L129) |
| Chống timing-side-channel login | `BCrypt.Verify(dummyHash)` khi user không tồn tại | [UserStore.cs:272](../../CanvasApp.AuthServer/UserStore.cs#L272) |

### 8.1. BCrypt — hash password & OTP

```csharp
// Lưu
string hash = BCrypt.Net.BCrypt.HashPassword(plaintext, workFactor: 12);
// Verify
bool ok = BCrypt.Net.BCrypt.Verify(plaintext, hash);
```

- **Adaptive hash** với salt sinh trong hash (lưu chung trong chuỗi `$2a$12$<salt><hash>`).
- `workFactor=12` (password user): ≈100ms/hash → brute-force 100M password mất ~115 ngày trên 1 GPU.
- `workFactor=10` (OTP code): nhanh hơn (~25ms) vì OTP entropy thấp (1M), expiry 5 phút, mỗi token chỉ verify được 5 lần — không cần 12.
- BCrypt **không reversible** → server bị hack DB, attacker không lấy được plaintext.
- Salt random per-hash → 2 user cùng password sẽ có hash khác nhau, không rainbow-table được.

### 8.2. HMAC-SHA256 — auth token (JWT-lite)

Format token gửi giữa Auth/Canvas/Client:
```
base64(payload) "." base64(HMAC-SHA256(payload, secret))
↑                ↑
└─ "userId:username:issuedAtUnix"
                 └─ 32 bytes
```

```csharp
// Issue
long issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var payload = Encoding.UTF8.GetBytes($"{userId}:{username}:{issuedAt}");
byte[] sig;
using (var hmac = new HMACSHA256(secret))
    sig = hmac.ComputeHash(payload);
string token = Convert.ToBase64String(payload) + "." + Convert.ToBase64String(sig);

// Verify
var expectedSig = HMAC-SHA256(payloadBytes, secret);
if (!FixedTimeEquals(providedSig, expectedSig)) return -1;
if (UtcNow - issuedAt > 24h) return -1;
```

- **Secret** lấy từ env `CANVASAPP_JWT_SECRET` (fallback hardcoded dev). Auth issue, Canvas verify — **share cùng secret**.
- TTL 24h (`TokenTtlSeconds`).
- `FixedTimeEquals` cần thiết: nếu so sánh bằng `==`/`SequenceEqual`, attacker có thể đoán dần từng byte signature qua timing (response time tăng dần khi đoán đúng byte đầu).
- Format **không phải JWT chuẩn** (không có header/JSON) — gọn nhẹ hơn nhưng cùng nguyên lý: payload public + MAC signature.

### 8.3. AES-256-CBC + HMAC-SHA256 — mã hoá payload (opt-in)

> Mặc định **TẮT** trên dây để demo dễ thấy JSON qua Wireshark. Bật bằng cách set 32-byte base64 key vào env `CANVASAPP_AES_KEY`.

**Output format** (`AesHelper.EncryptString`):
```
base64( IV (16B) ‖ ciphertext (n B) ‖ HMAC (32B) )
                                       └─ MAC trên (IV ‖ ciphertext)
```

**Encrypt-then-MAC** order (chuẩn industry):
```csharp
// 1. Sinh IV random
var iv = new byte[16];
new RNGCryptoServiceProvider().GetBytes(iv);

// 2. AES-256-CBC encrypt với PKCS7 padding
aes.Mode = CipherMode.CBC;
aes.Padding = PaddingMode.PKCS7;
aes.Key = key32; aes.IV = iv;
byte[] ct = aes.CreateEncryptor().Encrypt(plaintext);

// 3. MAC covers IV || CT (tamper với IV cũng bị detect)
byte[] mac = HMAC-SHA256(key32, iv ‖ ct);

// 4. Wire format
return base64(iv ‖ ct ‖ mac);
```

**Decrypt**: verify MAC **TRƯỚC**, chỉ decrypt nếu MAC pass → chặn padding-oracle attack.

```csharp
// PadOracle attack (cái tránh được):
// - Attacker gửi ciphertext sửa, server cố decrypt → PKCS7 padding sai → exception
// - Exception/timing leak việc padding hợp lệ hay không → attacker decrypt từng block
// Encrypt-then-MAC: MAC sai → reject NGAY, không chạy đến AES decrypt
if (!FixedTimeEquals(receivedMac, computedMac))
    throw new CryptographicException("HMAC verification failed");
// chỉ tới đây mới AES decrypt → padding oracle không tồn tại
```

**Envelope wire** (`MessageCrypto.EncryptInPlace`):
```json
{
  "type": "CHAT_MESSAGE",      ← plaintext (LB cần peek route)
  "token": "MTpu...",          ← plaintext (Server verify HMAC)
  "data": {
    "_enc": "base64(IV‖CT‖MAC)",
    "_v": 1                    ← version: cho phép format evolve
  }
}
```

- `type` + `token` luôn plaintext để LB peek route và verify ngay tại edge.
- `data` (payload nội dung) được mã hoá → ngay cả MitM bypass TLS cũng không đọc được nội dung tin nhắn/draw.

### 8.4. RNGCryptoServiceProvider — randomness

| Dùng cho | Lý do |
|---|---|
| OTP code 6 số | Predictable code = ai cũng đoán được → mất an toàn |
| AES IV (16 bytes) | Reuse IV trong CBC = leak plaintext relationship |
| BCrypt salt | (Tự sinh trong thư viện, không cần code thêm) |

> **Không bao giờ** dùng `System.Random` cho mục đích bảo mật — seed mặc định là `Environment.TickCount` → predictable trong vòng vài ms.

### 8.5. FixedTimeEquals — constant-time compare

```csharp
private static bool FixedTimeEquals(byte[] a, byte[] b)
{
    if (a == null || b == null || a.Length != b.Length) return false;
    int diff = 0;
    for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];   // XOR tất cả byte
    return diff == 0;
}
```

- `==`, `SequenceEqual`, `memcmp` — đều **return early** khi gặp byte khác đầu tiên → timing leak.
- XOR tất cả byte (không break sớm) → thời gian compare **không phụ thuộc** content → attacker đoán byte không được.
- .NET 5+ có sẵn `CryptographicOperations.FixedTimeEquals`. .NET Framework 4.8 phải tự viết.

### 8.6. Timing-side-channel ở login

```csharp
// Pre-computed BCrypt hash ngay khi class load
private static readonly string _dummyBcryptHash =
    BCrypt.Net.BCrypt.HashPassword("dummy-timing-equaliser", 12);

// Khi login:
if (!reader.Read())  // username KHÔNG tồn tại
{
    // ❌ Không có dòng này: return ngay → response time = ~5ms
    //   → attacker biết username không tồn tại (user enumeration)
    // ✅ Có dòng này: response time = ~100ms (= BCrypt time với password thật)
    BCrypt.Net.BCrypt.Verify(password, _dummyBcryptHash);
    return "Sai tài khoản hoặc mật khẩu";
}
```

Đảm bảo "username sai" và "password sai" trả về cùng thời gian → attacker không enumerate user database được qua timing.

### Tóm tắt threat model

| Threat | Defense |
|---|---|
| DB dump → đọc password | BCrypt(workFactor=12) — không reverse, work factor cao |
| Sniff token forge user khác | HMAC-SHA256 signature, secret share Auth↔Canvas |
| Replay expired token | Embed `issuedAt` trong payload, check TTL 24h |
| Timing leak token signature | `FixedTimeEquals` constant-time |
| User enumeration qua login timing | Dummy BCrypt.Verify ở nhánh user-not-found |
| MitM đọc payload (giả sử AES bật) | AES-256-CBC + HMAC-SHA256 Encrypt-then-MAC |
| Padding oracle attack | Encrypt-then-MAC: verify MAC trước decrypt |
| Tamper với IV / ciphertext | MAC bao luôn IV‖CT |
| IV reuse trong CBC | Random IV mỗi lần encrypt (CSPRNG) |
| Predictable OTP/salt/IV | RNGCryptoServiceProvider (CSPRNG) |

---
