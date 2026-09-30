using System;

namespace PhantomCatWorks.RealtimeP2PKit
{
    /// <summary>Envelope for messages relayed inside a PartyKit game room (SDP/ICE).</summary>
    [Serializable]
    public class RoomSignalEnvelope
    {
        public string type; // "client-ready" | "offer" | "answer" | "ice-candidate" | "peer-ready" | "peer-left"
        public string sdp;
        public string candidate;
        public string sdpMid;
        public int? sdpMLineIndex;
        public int? count;
    }
}
