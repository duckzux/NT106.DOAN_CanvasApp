using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CanvasApp.Common.Utils
{
    /// <summary>
    /// AES-256-CBC + HMAC-SHA256 (Encrypt-then-MAC) helper. Output format is base64 of
    /// <c>IV (16 bytes) || ciphertext || HMAC (32 bytes)</c>. The 32-byte master key is used
    /// directly for both AES and HMAC — fine for a school project but in production each
    /// primitive should get its own subkey via HKDF.
    /// </summary>
    public static class AesHelper
    {
        public const int KeySizeBytes = 32; // AES-256 + HMAC-SHA256
        public const int IvSizeBytes = 16;  // AES block size
        public const int MacSizeBytes = 32; // HMAC-SHA256 output

        public static byte[] KeyFromBase64(string base64Key)
        {
            if (string.IsNullOrWhiteSpace(base64Key))
                throw new ArgumentException("Key is empty", nameof(base64Key));
            var key = Convert.FromBase64String(base64Key.Trim());
            if (key.Length != KeySizeBytes)
                throw new ArgumentException(
                    $"Key must be exactly {KeySizeBytes} bytes (got {key.Length}). Generate one via:" +
                    " openssl rand -base64 32", nameof(base64Key));
            return key;
        }

        public static string EncryptString(string plaintext, byte[] key)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            ValidateKey(key);

            var iv = new byte[IvSizeBytes];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(iv);

            byte[] ct;
            using (var aes = new AesCryptoServiceProvider())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;

                using (var encryptor = aes.CreateEncryptor())
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    using (var sw = new StreamWriter(cs, new UTF8Encoding(false)))
                        sw.Write(plaintext);
                    ct = ms.ToArray();
                }
            }

            // MAC covers IV || CT so tampering with either is detected on Verify.
            var ivAndCt = new byte[iv.Length + ct.Length];
            Buffer.BlockCopy(iv, 0, ivAndCt, 0, iv.Length);
            Buffer.BlockCopy(ct, 0, ivAndCt, iv.Length, ct.Length);
            byte[] mac;
            using (var hmac = new HMACSHA256(key))
                mac = hmac.ComputeHash(ivAndCt);

            var packed = new byte[ivAndCt.Length + mac.Length];
            Buffer.BlockCopy(ivAndCt, 0, packed, 0, ivAndCt.Length);
            Buffer.BlockCopy(mac, 0, packed, ivAndCt.Length, mac.Length);
            return Convert.ToBase64String(packed);
        }

        public static string DecryptString(string base64Cipher, byte[] key)
        {
            if (string.IsNullOrEmpty(base64Cipher)) throw new ArgumentException("Cipher is empty", nameof(base64Cipher));
            ValidateKey(key);

            var packed = Convert.FromBase64String(base64Cipher);
            if (packed.Length < IvSizeBytes + MacSizeBytes)
                throw new CryptographicException("Cipher payload too short");

            int ctLen = packed.Length - IvSizeBytes - MacSizeBytes;
            var iv = new byte[IvSizeBytes];
            var ct = new byte[ctLen];
            var mac = new byte[MacSizeBytes];
            Buffer.BlockCopy(packed, 0, iv, 0, IvSizeBytes);
            Buffer.BlockCopy(packed, IvSizeBytes, ct, 0, ctLen);
            Buffer.BlockCopy(packed, IvSizeBytes + ctLen, mac, 0, MacSizeBytes);

            // Verify MAC FIRST (Encrypt-then-MAC). Refuse to decrypt tampered ciphertext.
            byte[] expectedMac;
            using (var hmac = new HMACSHA256(key))
            {
                var ivAndCt = new byte[IvSizeBytes + ctLen];
                Buffer.BlockCopy(iv, 0, ivAndCt, 0, IvSizeBytes);
                Buffer.BlockCopy(ct, 0, ivAndCt, IvSizeBytes, ctLen);
                expectedMac = hmac.ComputeHash(ivAndCt);
            }
            if (!FixedTimeEquals(mac, expectedMac))
                throw new CryptographicException("HMAC verification failed (tampered or wrong key)");

            using (var aes = new AesCryptoServiceProvider())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor())
                using (var ms = new MemoryStream(ct))
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (var sr = new StreamReader(cs, new UTF8Encoding(false)))
                    return sr.ReadToEnd();
            }
        }

        private static void ValidateKey(byte[] key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length != KeySizeBytes)
                throw new ArgumentException($"Key must be {KeySizeBytes} bytes (got {key.Length})", nameof(key));
        }

        // Constant-time comparison so MAC verification doesn't leak length/content via timing.
        // .NET Framework 4.8 lacks CryptographicOperations.FixedTimeEquals, so we roll our own.
        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
