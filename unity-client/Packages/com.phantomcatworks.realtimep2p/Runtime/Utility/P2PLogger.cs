using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit
{
    public enum P2PLogLevel
    {
        None = 0,
        Error = 1,
        Warn = 2,
        Info = 3,
        Verbose = 4,
    }

    /// <summary>
    /// Centralized logging for RealtimeP2PKit.
    /// Info level logs every step of matchmaking / signaling / WebRTC negotiation
    /// (state transitions, connect/disconnect, offer/answer/ICE exchange).
    /// Verbose level additionally logs internal state detail intended for
    /// debugging the connection flow during development.
    ///
    /// Every call site's class name, method name and line number are captured
    /// automatically (via [CallerMemberName]/[CallerFilePath]/[CallerLineNumber])
    /// and printed as part of the log line, e.g.
    ///   [RealtimeP2PKit][P2PManager.OnSignalMessage:210][10:23:45.123][Info] ...
    /// This is because every log physically goes through this one class, so
    /// double-clicking a log line in the Unity Console would otherwise always
    /// jump to P2PLogger.cs instead of the real caller - the text prefix makes
    /// the real source obvious even though the console's jump-to-source doesn't.
    /// Error() additionally appends a full stack trace of the real caller chain
    /// to the message body for the same reason.
    ///
    /// Turn Verbose off (use Info or lower) for production builds.
    /// For raw HTTP/WebSocket/WebRTC payload tracing, see P2PNetworkLogger instead -
    /// that is a separate, Editor-only toggle (see P2PConnectionSettingsWindow).
    /// </summary>
    public static class P2PLogger
    {
        public static P2PLogLevel Level = P2PLogLevel.Info;
        private const string Tag = "[RealtimeP2PKit]";

        public static void Error(string message,
            [CallerMemberName] string member = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            if (Level < P2PLogLevel.Error) return;
            // skipFrames:2 skips this method and the Log() helper, landing on the real caller.
            var stack = new System.Diagnostics.StackTrace(1, true);
            var line = BuildLine(P2PLogLevel.Error, message, member, filePath, lineNumber);
            Debug.LogError($"{line}\n{stack}");
        }

        public static void Warn(string message,
            [CallerMemberName] string member = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            if (Level < P2PLogLevel.Warn) return;
            Debug.LogWarning(BuildLine(P2PLogLevel.Warn, message, member, filePath, lineNumber));
        }

        public static void Info(string message,
            [CallerMemberName] string member = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            if (Level < P2PLogLevel.Info) return;
            Debug.Log(BuildLine(P2PLogLevel.Info, message, member, filePath, lineNumber));
        }

        public static void Verbose(string message,
            [CallerMemberName] string member = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            if (Level < P2PLogLevel.Verbose) return;
            Debug.Log(BuildLine(P2PLogLevel.Verbose, message, member, filePath, lineNumber));
        }

        public static void Exception(Exception ex, string context = null,
            [CallerMemberName] string member = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            if (Level < P2PLogLevel.Error) return;
            var loc = BuildLocationTag(filePath, member, lineNumber);
            Debug.LogError($"{Tag}{loc} {(context != null ? $"[{context}] " : "")}Exception: {ex}");
        }

        private static string BuildLine(P2PLogLevel level, string message, string member, string filePath, int lineNumber)
        {
            var loc = BuildLocationTag(filePath, member, lineNumber);
            return $"{Tag}{loc}[{DateTime.Now:HH:mm:ss.fff}][{level}] {message}";
        }

        private static string BuildLocationTag(string filePath, string member, int lineNumber)
        {
            if (string.IsNullOrEmpty(filePath)) return "";
            var className = Path.GetFileNameWithoutExtension(filePath);
            return $"[{className}.{member}:{lineNumber}]";
        }

        /// <summary>Hex preview of a byte payload, truncated, for debug tracing.</summary>
        public static string ToHexPreview(byte[] bytes, int maxLen = 64)
        {
            if (bytes == null) return "null";
            var len = Math.Min(bytes.Length, maxLen);
            var hex = BitConverter.ToString(bytes, 0, len).Replace("-", " ");
            return bytes.Length > maxLen ? $"{hex} ...({bytes.Length} bytes total)" : $"{hex} ({bytes.Length} bytes)";
        }
    }
}
