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
    /// Holds only the current log verbosity for RealtimeP2PKit. This is intentionally
    /// NOT a logging facade / wrapper around UnityEngine.Debug.
    ///
    /// An earlier version of this library funneled every log line through a shared
    /// P2PLogger.Info()/Warn()/Error() method that called Debug.Log internally. That
    /// broke Unity's "double-click a Console log line to jump to the source" feature:
    /// Unity always jumps to wherever Debug.Log() was physically called, which was
    /// always inside that one wrapper method, never the real call site.
    ///
    /// Every log call site in this library now calls UnityEngine.Debug.Log /
    /// LogWarning / LogError DIRECTLY, guarded inline by
    /// `if (P2PLog.ShouldLog(P2PLogLevel.X)) Debug.Log(...)`. That keeps the same
    /// verbosity control (driven by P2PConfig.LogLevel, applied in
    /// P2PManager.Initialize()) while keeping every log's Console double-click
    /// pointing at the actual line that logged it.
    /// </summary>
    public static class P2PLog
    {
        public static P2PLogLevel Level = P2PLogLevel.Info;

        public static bool ShouldLog(P2PLogLevel level) => Level >= level;
    }
}
