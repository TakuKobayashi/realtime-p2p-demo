using System;

namespace PhantomCatWorks.RealtimeP2PKit
{
    /// <summary>Pure string formatting helpers - none of these log anything themselves.</summary>
    public static class P2PHexFormat
    {
        /// <summary>Hex preview of a byte payload, truncated, for debug tracing.</summary>
        public static string Preview(byte[] bytes, int maxLen = 64)
        {
            if (bytes == null) return "null";
            var len = Math.Min(bytes.Length, maxLen);
            var hex = BitConverter.ToString(bytes, 0, len).Replace("-", " ");
            return bytes.Length > maxLen ? $"{hex} ...({bytes.Length} bytes total)" : $"{hex} ({bytes.Length} bytes)";
        }
    }
}
