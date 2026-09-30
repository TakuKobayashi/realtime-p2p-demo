using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit
{
    /// <summary>
    /// Holds only the on/off state for raw network traffic tracing (HTTP/WebSocket/WebRTC
    /// payload content). This is NOT a logging facade - see P2PLog for why. Pair with
    /// P2PNetworkLogFormat, which builds the message strings, and call
    /// UnityEngine.Debug.Log directly at each real call site:
    /// <code>
    ///   if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketSend(...));
    /// </code>
    ///
    /// Controllable ONLY from the Unity Editor (via P2PConnectionSettingsWindow,
    /// "Network Logging" section), persisted per-machine in PlayerPrefs. Outside the
    /// Editor (i.e. in any Player build) this is always disabled and cannot be turned
    /// on - there is no UI for it and IsEnabled always returns false, so none of this
    /// traffic is ever logged in a shipped build.
    /// </summary>
    public static class P2PNetworkLog
    {
        public const string PrefKeyEnabled = "RealtimeP2PKit.NetworkLoggingEnabled";

        public static bool IsEnabled
        {
#if UNITY_EDITOR
            get => PlayerPrefs.GetInt(PrefKeyEnabled, 0) == 1;
            set => PlayerPrefs.SetInt(PrefKeyEnabled, value ? 1 : 0);
#else
            get => false;
            // No-op outside the Editor: network logging can never be turned on in a build.
            set { }
#endif
        }
    }
}
