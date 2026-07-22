using System;
using System.Text;

namespace PhantomCatWorks.RealtimeP2PKit
{
    /// <summary>
    /// Builds formatted strings describing HTTP/WebSocket/WebRTC traffic (request/response
    /// pairing, RTT, size, color-coded errors - modeled after a typical Unity SDK's
    /// HTTPLogger). None of these methods call Debug.Log themselves; the caller is
    /// responsible for guarding with `if (P2PNetworkLog.IsEnabled)` and logging the
    /// returned string directly, so that Unity's Console double-click navigation points
    /// at the real call site instead of a shared wrapper.
    /// </summary>
    public static class P2PNetworkLogFormat
    {
        private const string Tag = "[RealtimeP2PKit.Net]";

        public static string HttpRequest(string method, string url, string body)
        {
            var sb = new StringBuilder();
            sb.Append(Tag).Append(" -> ").Append(method.ToUpperInvariant()).Append(' ').Append(url);
            if (!string.IsNullOrEmpty(body)) sb.Append('\n').Append(body);
            return sb.ToString();
        }

        public static string HttpResponse(string method, string url, long statusCode, bool isError,
            string body, TimeSpan elapsed)
        {
            var sb = new StringBuilder();
            var openColor = isError ? "<color=red>" : "";
            var closeColor = isError ? "</color>" : "";
            sb.Append(Tag).Append(' ').Append(openColor);
            sb.Append("<- ").Append(method.ToUpperInvariant()).Append(' ').Append(url).Append('\n');
            sb.Append(statusCode).Append("  (RTT: ").Append((int)elapsed.TotalMilliseconds).Append("ms");
            if (!string.IsNullOrEmpty(body)) sb.Append(", Size: ").Append(FormatSize(Encoding.UTF8.GetByteCount(body)));
            sb.Append(')').Append(closeColor);
            if (!string.IsNullOrEmpty(body)) sb.Append('\n').Append(body);
            return sb.ToString();
        }

        public static string WebSocketOpen(string context, string url) => $"{Tag} [{context}] websocket OPEN {url}";

        public static string WebSocketClose(string context, string url, string reason) =>
            $"{Tag} [{context}] websocket CLOSED {url} ({reason})";

        public static string WebSocketSend(string context, string message) => $"{Tag} [{context}] -> ws {message}";

        public static string WebSocketReceive(string context, string message) => $"{Tag} [{context}] <- ws {message}";

        public static string WebRtcSend(byte[] payload) => $"{Tag} [DataChannel] -> {P2PHexFormat.Preview(payload)}";

        public static string WebRtcReceive(byte[] payload) => $"{Tag} [DataChannel] <- {P2PHexFormat.Preview(payload)}";

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double len = bytes;
            var order = 0;
            while (len >= 1024 && order + 1 < units.Length)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.#} {units[order]}";
        }
    }
}
