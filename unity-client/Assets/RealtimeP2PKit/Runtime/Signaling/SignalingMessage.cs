namespace net.taptappun.RealtimeP2PKit
{
    public enum SignalingMessageType { Offer, Answer, IceCandidate }

    /// <summary>WebRTC negotiation data. The signaling adapter determines its wire representation.</summary>
    public sealed class SignalingMessage
    {
        public SignalingMessageType Type;
        public string Sdp;
        public string Candidate;
        public string SdpMid;
        public int? SdpMLineIndex;
    }
}
