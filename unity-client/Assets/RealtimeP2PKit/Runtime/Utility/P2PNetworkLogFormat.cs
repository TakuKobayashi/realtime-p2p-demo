using System;
using Newtonsoft.Json;

namespace net.taptappun.RealtimeP2PKit
{
    /// <summary>
    /// Builds formatted strings describing WebSocket and WebRTC traffic.
    /// None of these methods call Debug.Log themselves; the caller is
    /// responsible for guarding with `if (P2PNetworkLog.IsEnabled)` and logging the
    /// returned string directly, so that Unity's Console double-click navigation points
    /// at the real call site instead of a shared wrapper.
    /// </summary>
    public static class P2PNetworkLogFormat
    {
        private const string Tag = "[RealtimeP2PKit.Net]";

        public static string WebSocketOpen(string context, string url) => $"{Tag} [{context}] websocket OPEN {url}";

        public static string WebSocketClose(string context, string url, string reason) =>
            $"{Tag} [{context}] websocket CLOSED {url} ({reason})";

        public static string WebSocketSend(string context, string message) => $"{Tag} [{context}] -> ws {message}";

        public static string WebSocketReceive(string context, string message) => $"{Tag} [{context}] <- ws {message}";

        /// <summary>
        /// Formats a MessagePack data-channel packet as its original JSON-friendly
        /// object. The packet id is intentionally included because it is part of
        /// the wire format but not the MessagePack body.
        /// </summary>
        public static string WebRtcSend<T>(byte packetId, T value, int byteLength) =>
            WebRtcMessage("->", packetId, typeof(T).Name, value, byteLength);

        public static string WebRtcReceive<T>(byte packetId, T value, int byteLength) =>
            WebRtcMessage("<-", packetId, typeof(T).Name, value, byteLength);

        // Kept as a safe fallback for packets whose application type is unknown.
        public static string WebRtcSend(byte[] payload) => $"{Tag} [DataChannel] -> unknown {P2PHexFormat.Preview(payload)}";

        public static string WebRtcReceive(byte[] payload) => $"{Tag} [DataChannel] <- unknown {P2PHexFormat.Preview(payload)}";

        private static string WebRtcMessage(string direction, byte packetId, string typeName, object value, int byteLength)
        {
            string json;
            try
            {
                json = JsonConvert.SerializeObject(value, Formatting.Indented);
            }
            catch (Exception ex)
            {
                json = $"<JSON serialization failed: {ex.Message}>";
            }

            return $"{Tag} [DataChannel] {direction} packetId={packetId} type={typeName} size={byteLength}B\n{json}";
        }

    }
}
