using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NativeWebSocket;
using UnityEngine;

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
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][Signaling] connecting to room websocket: {_url}");

            _ws = new WebSocket(_url);

            _ws.OnOpen += () =>
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][Signaling] websocket OPEN");
                if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketOpen("Room", _url));
                Connected?.Invoke();
            };

            _ws.OnError += err =>
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Error)) Debug.LogError($"[RealtimeP2PKit][Signaling] websocket error: {err}");
            };

            _ws.OnClose += code =>
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Warn)) Debug.LogWarning($"[RealtimeP2PKit][Signaling] websocket CLOSED code={code}");
                if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketClose("Room", _url, code.ToString()));
                Disconnected?.Invoke(code.ToString());
            };

            _ws.OnMessage += bytes =>
            {
                var json = Encoding.UTF8.GetString(bytes);
                if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketReceive("Room", json));
                try
                {
                    var envelope = JsonConvert.DeserializeObject<RoomSignalEnvelope>(json);
                    MessageReceived?.Invoke(envelope);
                }
                catch (Exception ex)
                {
                    if (P2PLog.ShouldLog(P2PLogLevel.Error)) Debug.LogError($"[RealtimeP2PKit][Signaling.OnMessage.Parse] Exception: {ex}");
                }
            };

            await _ws.Connect();
        }

        public void Send(RoomSignalEnvelope message)
        {
            if (_ws == null || _ws.State != WebSocketState.Open)
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Warn)) Debug.LogWarning($"[RealtimeP2PKit][Signaling] cannot send, socket not open (type={message.type})");
                return;
            }
            var json = JsonConvert.SerializeObject(message);
            if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketSend("Room", json));
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
