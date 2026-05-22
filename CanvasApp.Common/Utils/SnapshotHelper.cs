using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;

namespace CanvasApp.Common
{
    public static class SnapshotHelper
    {
        public static string Compress(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
                    gzip.Write(bytes, 0, bytes.Length);
                return Convert.ToBase64String(output.ToArray());
            }
        }

        public static List<DrawAction> Decompress(string base64Data)
        {
            var compressed = Convert.FromBase64String(base64Data);
            using (var input = new MemoryStream(compressed))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gzip.CopyTo(output);
                var json = Encoding.UTF8.GetString(output.ToArray());
                return JsonConvert.DeserializeObject<List<DrawAction>>(json) ?? new List<DrawAction>();
            }
        }
    }
}
