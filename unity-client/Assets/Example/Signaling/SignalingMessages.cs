using System;

namespace net.taptappun.RealtimeP2PKit.Example.Signaling
{
    /// <summary>Envelope for messages relayed inside a PartyKit game room (SDP/ICE).</summary>
    [Serializable]
    public class RoomSignalEnvelope
    {
        public string type;
        public string from;
        public string to;
        public string[] peers;
        public bool isInitiator;
        public string sdp;
        public string candidate;
        public string sdpMid;
        public int? sdpMLineIndex;
        public int? count;
    }
}
