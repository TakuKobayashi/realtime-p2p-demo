using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NativeWebSocket;
using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit
{
    /// <summary>
    /// Listens on this player's "Lobby" party (a partyserver Durable Object, one
    /// instance per playerId) for a push notification that a match was found
    /// while this player was waiting in the matchmaking queue.
    /// See /server/src/party/lobby.ts and
    /// /server/src/routes/matchmaking.ts (the Durable Object fetch() push).
    /// </summary>
    public class LobbyListener : IDisposable
    {
        public event Action<LobbyMatchedMessage> Matched;

        private readonly string _baseWsUrl;
        private string _url;
        private WebSocket _ws;

        /// <param name="baseWsUrl">e.g. "ws://localhost:8787" or "wss://realtime-p2p-server.example.workers.dev"
        /// (see P2PEndpoints.GetSignalingWebSocketUrl()). "/parties/lobby/{playerId}" is appended.</param>
        public LobbyListener(string baseWsUrl)
        {
            _baseWsUrl = baseWsUrl.TrimEnd('/');
        }

        public async Task ConnectAsync(string playerId)
        {
            _url = $"{_baseWsUrl}/parties/lobby/{playerId}";
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][Lobby] connecting: {_url}");

            _ws = new WebSocket(_url);
            _ws.OnOpen += () =>
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][Lobby] websocket OPEN, waiting for match...");
                if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketOpen("Lobby", _url));
            };
            _ws.OnError += err =>
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Error)) Debug.LogError($"[RealtimeP2PKit][Lobby] websocket error: {err}");
            };
            _ws.OnClose += code =>
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][Lobby] websocket closed code={code}");
                if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketClose("Lobby", _url, code.ToString()));
            };
            _ws.OnMessage += bytes =>
            {
                var json = Encoding.UTF8.GetString(bytes);
                if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketReceive("Lobby", json));
                try
                {
                    var msg = JsonConvert.DeserializeObject<LobbyMatchedMessage>(json);
                    if (msg?.type == "matched")
                    {
                        if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][Lobby] matched! roomId={msg.roomId} opponentId={msg.opponentId} isInitiator={msg.isInitiator}");
                        Matched?.Invoke(msg);
                    }
                }
                catch (Exception ex)
                {
                    if (P2PLog.ShouldLog(P2PLogLevel.Error)) Debug.LogError($"[RealtimeP2PKit][Lobby.OnMessage.Parse] Exception: {ex}");
                }
            };

            await _ws.Connect();
        }

        public void DispatchMessageQueue()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            _ws?.DispatchMessageQueue();
#endif
        }

        public void Dispose() => _ws?.Close();
    }
}
