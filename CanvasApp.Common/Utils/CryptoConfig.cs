using System;
using System.IO;

namespace CanvasApp.Common.Utils
{
    /// <summary>
    /// Process-wide AES key + toggle. All servers and clients must agree on the same key
    /// for encrypted messages to round-trip. Loaded once at process startup and cached.
    /// </summary>
    /// <remarks>
    /// <para><b>Key sources (highest precedence first)</b>:</para>
    /// <list type="number">
    ///   <item><c>CANVASAPP_AES_KEY</c> environment variable (Base64 of 32 bytes).</item>
    ///   <item>Explicit <see cref="Initialize(string, bool)"/> call (from <c>Program.cs</c> /
    ///         <c>Session</c>).</item>
    ///   <item><see cref="DefaultDevKey"/> — a hardcoded dev key so the system runs out-of-the-box.
    ///         <b>Replace in production.</b></item>
    /// </list>
    /// <para><b>Toggle</b>: <see cref="EncryptionEnabled"/> defaults to <c>true</c>. Setting the
    /// <c>CANVASAPP_AES_DISABLED=1</c> env var (or calling <see cref="Initialize"/> with
    /// <c>enabled: false</c>) turns encryption off — useful for debugging with Wireshark or
    /// when troubleshooting a mismatched key across processes.</para>
    /// </remarks>
    public static class CryptoConfig
    {
        // Hardcoded fallback so a fresh checkout still works. SHA-256 of "CanvasApp-dev-key-do-not-use-in-prod"
        // — 32 bytes, exactly AES-256 key size. SAFE to publish: this is openly a dev fallback,
        // not a production secret. Real deployments MUST override via env var or Initialize().
        private const string DefaultDevKey = "z9ZvBnQfX2YlS3o4nUvE3pK9XHN0V3FwS+rJqcXyB3o=";

        private static readonly object _gate = new object();
        private static byte[] _key;
        private static bool _enabled = true;
        private static bool _initialized;
        private static string _source = "default-dev";

        /// <summary>The 32-byte AES key. Triggers <see cref="EnsureInitialized"/> on first access.</summary>
        public static byte[] Key
        {
            get { EnsureInitialized(); return _key; }
        }

        /// <summary>Whether sensitive payloads should be wrapped/unwrapped on this process.</summary>
        public static bool EncryptionEnabled
        {
            get { EnsureInitialized(); return _enabled; }
        }

        /// <summary>Human-readable description of where <see cref="Key"/> came from (for startup banners).</summary>
        public static string KeySource
        {
            get { EnsureInitialized(); return _source; }
        }

        /// <summary>
        /// Explicit init — call from <c>Program.cs</c> after reading <c>App.config</c> or
        /// <c>appsettings.json</c>. <paramref name="base64Key"/> may be null/empty to fall back to
        /// the env var or dev default. Subsequent calls are ignored.
        /// </summary>
        public static void Initialize(string base64Key, bool enabled = true)
        {
            lock (_gate)
            {
                if (_initialized) return;

                string envDisabled = SafeGetEnv("CANVASAPP_AES_DISABLED");
                _enabled = enabled && envDisabled != "1" && envDisabled != "true";

                string envKey = SafeGetEnv("CANVASAPP_AES_KEY");
                if (!string.IsNullOrWhiteSpace(envKey))
                {
                    _key = AesHelper.KeyFromBase64(envKey);
                    _source = "env CANVASAPP_AES_KEY";
                }
                else if (!string.IsNullOrWhiteSpace(base64Key))
                {
                    _key = AesHelper.KeyFromBase64(base64Key);
                    _source = "config";
                }
                else
                {
                    _key = AesHelper.KeyFromBase64(DefaultDevKey);
                    _source = "default-dev (override in production!)";
                }

                _initialized = true;
            }
        }

        /// <summary>For tests: reset internal state. NOT thread-safe with concurrent users.</summary>
        public static void ResetForTest()
        {
            lock (_gate)
            {
                _initialized = false;
                _key = null;
                _enabled = true;
                _source = "default-dev";
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            Initialize(null);
        }

        private static string SafeGetEnv(string name)
        {
            try { return Environment.GetEnvironmentVariable(name); }
            catch { return null; }
        }
    }
}
