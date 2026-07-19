using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit
{
    /// <summary>
    /// All external endpoints and tunables for a RealtimeP2PKit session, as a
    /// ScriptableObject asset so the same library can point at different
    /// backend deployments (dev/stg/prod, or an entirely different game) without
    /// touching code.
    /// </summary>
    [CreateAssetMenu(menuName = "RealtimeP2PKit/P2P Config", fileName = "P2PConfig")]
    public class P2PConfig : ScriptableObject
    {
        [Header("Server (Hono + partyserver, single Cloudflare Worker)")]
        [Tooltip("Host only, no scheme, e.g. realtime-p2p-server.<your-account>.workers.dev " +
                 "(or 127.0.0.1:8787 for `wrangler dev` / `pnpm dev` locally). " +
                 "Matchmaking (REST) and signaling (WebSocket) both live on this one host - " +
                 "see /server in the repo, a single wrangler deploy.")]
        public string ServerHost = "realtime-p2p-server.example.workers.dev";
        [Tooltip("On: https/wss (production). Off: http/ws (local `wrangler dev`).")]
        public bool UseSecureConnection = true;

        [Header("ICE / STUN servers")]
        [Tooltip("Public STUN servers used for NAT traversal. No TURN server is configured, " +
                 "so pairs where both peers are behind a symmetric NAT will fail to connect " +
                 "by design (a known limitation of STUN-only P2P).")]
        public string[] StunServerUrls =
        {
            "stun:stun.l.google.com:19302",
            "stun:stun1.l.google.com:19302",
            "stun:stun.cloudflare.com:3478",
        };

        [Header("Data channel")]
        public string DataChannelLabel = "gameplay";
        [Tooltip("Reliable = ordered, retransmitted delivery (higher latency under loss). " +
                 "Unreliable (recommended for position sync) = fire-and-forget, low latency.")]
        public bool Reliable = false;
        [Tooltip("Only used when Reliable is false. 0 = no retransmits at all.")]
        public int MaxRetransmits = 0;

        [Header("Logging")]
        public P2PLogLevel LogLevel = P2PLogLevel.Info;
    }
}
