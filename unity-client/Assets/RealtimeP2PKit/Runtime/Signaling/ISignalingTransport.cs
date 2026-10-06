using System;
using System.Threading.Tasks;

namespace net.taptappun.RealtimeP2PKit
{
    /// <summary>Text transport for a signaling adapter; it imposes no message schema or authentication.</summary>
    public interface ISignalingTransport : IDisposable
    {
        event Action<string> MessageReceived;
        event Action<string> Disconnected;
        Task ConnectAsync();
        Task SendAsync(string message);
        void DispatchMessageQueue();
    }
}
