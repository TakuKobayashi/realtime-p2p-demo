using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit
{
    /// <summary>
    /// Non-endpoint tunables for a RealtimeP2PKit session, as a ScriptableObject asset.
    /// Connection endpoints (matchmaking API URL / signaling WebSocket URL / STUN servers)
    /// are NOT here anymore - they live in P2PEndpoints and are switched between
    /// "Local"/"Remote" via the Unity Editor window "RealtimeP2PKit &gt; Connection Settings"
    /// (see P2PConnectionSettingsWindow). That split exists specifically so a developer can
    /// flip between a local `wrangler dev` server and the deployed one without touching this
    /// asset or committing a change.
    /// </summary>
    [CreateAssetMenu(menuName = "RealtimeP2PKit/P2P Config", fileName = "P2PConfig")]
    public class P2PConfig : ScriptableObject
    {
        [Header("Data channel")]
        public string DataChannelLabel = "gameplay";
        [Tooltip("Reliable = ordered, retransmitted delivery (higher latency under loss). " +
                 "Unreliable (recommended for position sync) = fire-and-forget, low latency.")]
        public bool Reliable = false;
        [Tooltip("Only used when Reliable is false. 0 = no retransmits at all.")]
        public int MaxRetransmits = 0;

        [Header("Logging")]
        [Tooltip("General connection-flow logging (matchmaking/signaling/WebRTC state). " +
                 "For raw HTTP/WebSocket/WebRTC payload tracing, see the Editor-only " +
                 "'Network Logging' toggle in RealtimeP2PKit > Connection Settings instead.")]
        public P2PLogLevel LogLevel = P2PLogLevel.Info;
    }
}
