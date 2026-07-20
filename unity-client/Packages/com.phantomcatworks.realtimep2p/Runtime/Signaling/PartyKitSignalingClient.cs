using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NativeWebSocket;

namespace PhantomCatWorks.RealtimeP2PKit
{
    /// <summary>
    /// Signaling client for the "Room" party (a partyserver Durable Object, see
    /// /server/src/party/room.ts). Relays WebRTC offer/answer/ICE candidates
    /// between exactly two peers in a 1:1 room.
    /// Uses NativeWebSocket (github.com/endel/NativeWebSocket) for a cross-platform
    /// (Editor/Standalone/Mobile/WebGL) WebSocket implementation.
    /// </summary>
    public class PartyKitSignalingClient : ISignalingClient
    {
        public event Action Connected;
        public event Action<string> Disconnected;
        public event Action<RoomSignalEnvelope> MessageReceived;

        private readonly string _baseWsUrl;
        private string _url;
        private WebSocket _ws;

        /// <param name="baseWsUrl">e.g. "ws://localhost:8787" or "wss://realtime-p2p-server.example.workers.dev"
        /// (see P2PEndpoints.GetSignalingWebSocketUrl()). "/parties/room/{roomId}" is appended.</param>
        public PartyKitSignalingClient(string baseWsUrl)
        {
            _baseWsUrl = baseWsUrl.TrimEnd('/');
        }

        public async Task ConnectAsync(string roomId)
        {
            _url = $"{_baseWsUrl}/parties/room/{roomId}";
            P2PLogger.Info($"[Signaling] connecting to room websocket: {_url}");

            _ws = new WebSocket(_url);

            _ws.OnOpen += () =>
            {
                P2PLogger.Info("[Signaling] websocket OPEN");
                P2PNetworkLogger.LogWebSocketOpen("Room", _url);
                Connected?.Invoke();
            };

            _ws.OnError += err => P2PLogger.Error($"[Signaling] websocket error: {err}");

            _ws.OnClose += code =>
            {
                P2PLogger.Warn($"[Signaling] websocket CLOSED code={code}");
                P2PNetworkLogger.LogWebSocketClose("Room", _url, code.ToString());
                Disconnected?.Invoke(code.ToString());
            };

            _ws.OnMessage += bytes =>
            {
                var json = Encoding.UTF8.GetString(bytes);
                P2PNetworkLogger.LogWebSocketReceive("Room", json);
                try
                {
                    var envelope = JsonConvert.DeserializeObject<RoomSignalEnvelope>(json);
                    MessageReceived?.Invoke(envelope);
                }
                catch (Exception ex)
                {
                    P2PLogger.Exception(ex, "Signaling.OnMessage.Parse");
                }
            };

            await _ws.Connect();
        }

        public void Send(RoomSignalEnvelope message)
        {
            if (_ws == null || _ws.State != WebSocketState.Open)
            {
                P2PLogger.Warn($"[Signaling] cannot send, socket not open (type={message.type})");
                return;
            }
            var json = JsonConvert.SerializeObject(message);
            P2PNetworkLogger.LogWebSocketSend("Room", json);
            _ = _ws.SendText(json);
        }

        /// <summary>Must be pumped every frame from a MonoBehaviour Update() on non-WebGL platforms.</summary>
        public void DispatchMessageQueue()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            _ws?.DispatchMessageQueue();
#endif
        }

        public void Dispose() => _ws?.Close();
    }
}
