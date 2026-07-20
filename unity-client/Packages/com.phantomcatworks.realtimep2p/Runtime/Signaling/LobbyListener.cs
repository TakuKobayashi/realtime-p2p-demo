using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NativeWebSocket;

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
            P2PLogger.Info($"[Lobby] connecting: {_url}");

            _ws = new WebSocket(_url);
            _ws.OnOpen += () =>
            {
                P2PLogger.Info("[Lobby] websocket OPEN, waiting for match...");
                P2PNetworkLogger.LogWebSocketOpen("Lobby", _url);
            };
            _ws.OnError += err => P2PLogger.Error($"[Lobby] websocket error: {err}");
            _ws.OnClose += code =>
            {
                P2PLogger.Info($"[Lobby] websocket closed code={code}");
                P2PNetworkLogger.LogWebSocketClose("Lobby", _url, code.ToString());
            };
            _ws.OnMessage += bytes =>
            {
                var json = Encoding.UTF8.GetString(bytes);
                P2PNetworkLogger.LogWebSocketReceive("Lobby", json);
                try
                {
                    var msg = JsonConvert.DeserializeObject<LobbyMatchedMessage>(json);
                    if (msg?.type == "matched")
                    {
                        P2PLogger.Info($"[Lobby] matched! roomId={msg.roomId} opponentId={msg.opponentId} isInitiator={msg.isInitiator}");
                        Matched?.Invoke(msg);
                    }
                }
                catch (Exception ex)
                {
                    P2PLogger.Exception(ex, "Lobby.OnMessage.Parse");
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
