using UnityEngine;

namespace net.taptappun.RealtimeP2PKit
{
    /// <summary>DataChannel, optional negotiation deadline, and logging settings.
    /// Signaling protocol and authentication are supplied separately through ISignalingClient.</summary>
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

        [Header("Connection")]
        [Min(0)]
        [Tooltip("Optional negotiation deadline in seconds. 0 leaves failure detection to WebRTC.")]
        public float PeerConnectionTimeoutSeconds = 0;

        [Header("Logging")]
        [Tooltip("General connection-flow logging (signaling/WebRTC state). " +
                 "For raw WebSocket/WebRTC payload tracing, see the Editor-only " +
                 "'Network Logging' toggle in RealtimeP2PKit > Connection Settings instead.")]
        public P2PLogLevel LogLevel = P2PLogLevel.Info;
    }
}
