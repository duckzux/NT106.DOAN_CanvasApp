using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CanvasApp.Common.Utils
{
    /// <summary>
    /// Wraps / unwraps the <see cref="Message.Data"/> payload with AES-256 encryption while
    /// leaving <c>Type</c> and <c>Token</c> plaintext so the LoadBalancer can still peek and
    /// route. Envelope shape on the wire (inside <c>data</c>):
    /// <code>
    /// { "_enc": "&lt;base64 IV‖CT‖HMAC&gt;", "_v": 1 }
    /// </code>
    /// Versioned so the format can evolve (e.g. add per-connection KDF) without breaking
    /// older clients.
    /// </summary>
    public static class MessageCrypto
    {
        public const int CurrentVersion = 1;
        private const string EncField = "_enc";
        private const string VersionField = "_v";

        /// <summary>Returns true if <paramref name="data"/> is an encrypted envelope.</summary>
        public static bool IsEncrypted(object data)
        {
            if (data == null) return false;
            if (data is JObject jo) return jo[EncField] != null;
            // Plain anonymous/POCO with the right shape — serialise once to check.
            try
            {
                var token = JToken.FromObject(data);
                return token is JObject obj && obj[EncField] != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// Replaces <c>msg.Data</c> with an encrypted envelope. Idempotent — if already encrypted,
        /// no-op. <c>msg.Type</c> and <c>msg.Token</c> are untouched.
        /// </summary>
        public static void EncryptInPlace(Message msg, byte[] key)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (msg.Data == null) return;
            if (IsEncrypted(msg.Data)) return;

            var payloadJson = JsonConvert.SerializeObject(msg.Data);
            var ct = AesHelper.EncryptString(payloadJson, key);
            msg.Data = new JObject
            {
                [EncField] = ct,
                [VersionField] = CurrentVersion,
            };
        }

        /// <summary>
        /// If <c>msg.Data</c> is an encrypted envelope, decrypts it back into a plain JObject
        /// (matching what would have arrived if encryption was off). No-op otherwise.
        /// Throws <see cref="System.Security.Cryptography.CryptographicException"/> on tamper.
        /// </summary>
        public static void DecryptInPlace(Message msg, byte[] key)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (msg.Data == null) return;

            JObject envelope = msg.Data as JObject;
            if (envelope == null)
            {
                try { envelope = JToken.FromObject(msg.Data) as JObject; }
                catch { return; }
            }
            if (envelope == null) return;

            var encToken = envelope[EncField];
            if (encToken == null) return;

            int version = envelope[VersionField]?.Value<int>() ?? 0;
            if (version != CurrentVersion)
                throw new InvalidOperationException(
                    $"Unsupported MessageCrypto envelope version: {version} (expected {CurrentVersion}).");

            var ct = encToken.Value<string>();
            var json = AesHelper.DecryptString(ct, key);
            msg.Data = JsonConvert.DeserializeObject<JToken>(json);
        }

        /// <summary>Convenience: encrypt a payload object into an envelope (without mutating a Message).</summary>
        public static object EncryptPayload(object payload, byte[] key)
        {
            if (payload == null) return null;
            var json = JsonConvert.SerializeObject(payload);
            return new JObject
            {
                [EncField] = AesHelper.EncryptString(json, key),
                [VersionField] = CurrentVersion,
            };
        }

        /// <summary>
        /// Convenience: decrypt an envelope into a typed payload. If <paramref name="data"/> is
        /// not encrypted, falls back to a plain deserialisation so callers can stay agnostic.
        /// </summary>
        public static T DecryptPayload<T>(object data, byte[] key)
        {
            if (data == null) return default(T);
            if (!IsEncrypted(data))
                return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(data));

            JObject envelope = data as JObject ?? JToken.FromObject(data) as JObject;
            var ct = envelope[EncField].Value<string>();
            var json = AesHelper.DecryptString(ct, key);
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
}
