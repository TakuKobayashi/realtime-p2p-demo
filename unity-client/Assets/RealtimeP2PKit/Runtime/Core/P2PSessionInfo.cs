namespace net.taptappun.RealtimeP2PKit
{
    public enum P2PSessionState
    {
        Idle,
        SignalingConnecting,
        Negotiating,
        Connected,
        Disconnected,
        Failed,
        WaitingForPeers,
    }

    /// <summary>Mutable state for the current (or most recent) P2P session.</summary>
    public class P2PSessionInfo
    {
        public string LocalPeerId;
        public bool IsSignalingReady;
        public P2PSessionState State;
    }
}
