using System;
using System.Threading.Tasks;

namespace net.taptappun.RealtimeP2PKit
{
    /// <summary>
    /// Abstraction over the signaling transport used to exchange SDP offer/answer
    /// and ICE candidates before the WebRTC data channel is established. Swappable
    /// so a different signaling backend can be used without touching P2PManager.
    /// Raise events on Unity's main thread; DispatchMessageQueue can marshal transport callbacks.
    /// </summary>
    public interface ISignalingClient : IDisposable
    {
        event Action<string> Disconnected;
        event Action<string, bool> PeerJoined;
        event Action<string> PeerLeft;
        event Action<string, SignalingMessage> MessageReceived;

        /// <summary>Complete when signaling is ready. Report discovered peers through PeerJoined (ID, offerer role).
        /// The implementation owns authentication, wire format, keepalive, and any server session setup.</summary>
        Task ConnectAsync();
        void Send(string peerId, SignalingMessage message);
        void DispatchMessageQueue();
    }
}
