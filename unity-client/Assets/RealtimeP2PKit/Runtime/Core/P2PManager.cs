using System;
using Unity.WebRTC;
using UnityEngine;
using System.Threading.Tasks;

namespace PhantomCatWorks.RealtimeP2PKit
{
    /// <summary>
    /// Singleton entry point for the RealtimeP2PKit library. Orchestrates:
    ///   1. Matchmaking      (IMatchmakingClient  - Hono REST API)
    ///   2. Signaling        (ISignalingClient / LobbyListener - PartyKit WebSocket)
    ///   3. WebRTC negotiation & data channel (WebRtcPeerConnection)
    ///   4. Packet (de)serialization + routing (PacketRouter / MessagePack)
    ///
    /// This is the ONLY class other game code should talk to. Everything else in
    /// this package is an implementation detail reachable through here, which is
    /// what makes the library safe to drop into a different Unity project as-is.
    ///
    /// Typical usage from any other script (see Assets/Scripts/Demo for a full example):
    /// <code>
    ///   P2PManager.Instance.Initialize(config);
    ///   P2PManager.Instance.RegisterPacketHandler&lt;PositionPacket&gt;(1, OnPosition);
    ///   P2PManager.Instance.Matched += info => ...;
    ///   P2PManager.Instance.DataChannelReady += () => ...;
    ///   P2PManager.Instance.StartMatchmaking(myPlayerId);
    ///   ...
    ///   P2PManager.Instance.Send(1, new PositionPacket { X = 1, Y = 0, Z = 3 });
    /// </code>
    /// </summary>
    [DisallowMultipleComponent]
    public class P2PManager : MonoBehaviour
    {
        private static P2PManager _instance;

        /// <summary>Lazily creates a persistent (DontDestroyOnLoad) singleton instance.</summary>
        public static P2PManager Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject(nameof(P2PManager));
                _instance = go.AddComponent<P2PManager>();
                DontDestroyOnLoad(go);
                if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][P2PManager] singleton instance created lazily");
                return _instance;
            }
        }

        /// <summary>
        /// Returns the persistent manager only when it was already created.
        /// This lets offline scenes avoid creating a networking object merely to
        /// determine whether an online match is active.
        /// </summary>
        public static bool TryGetExistingInstance(out P2PManager manager)
        {
            manager = _instance;
            return manager != null;
        }

        public event Action<P2PSessionState> StateChanged;
        public event Action<P2PSessionInfo> Matched;
        public event Action DataChannelReady;
        public event Action<string> ConnectionClosed;
        /// <summary>Raised once when the other peer leaves the signaling room.</summary>
        public event Action OpponentLeft;

        public P2PSessionInfo Session { get; private set; } = new() { State = P2PSessionState.Idle };
        /// <summary>True only for a session started through the online room flow.</summary>
        public bool IsOnlineMatch { get; private set; }

        private P2PConfig _config;
        private HttpMatchmakingClient _matchmakingClient;
        private LobbyListener _lobbyListener;
        private PartyKitSignalingClient _signalingClient;
        private WebRtcPeerConnection _peerConnection;
        private PacketRouter _packetRouter;
        private bool _webRtcUpdateStarted;
        private bool _peerConnectionStarted;
        private bool _dataChannelReadyRaised;
        private bool _opponentLeftRaised;
        private P2PEnvironment _endpointEnvironment;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Warn)) Debug.LogWarning("[RealtimeP2PKit][P2PManager] duplicate instance detected, destroying this one");
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Optionally supplies data-channel settings. Calling this is not required
        /// for matchmaking/room HTTP APIs; those APIs lazily use P2PConfig's
        /// built-in defaults when no explicit config has been supplied.
        /// </summary>
        public void Initialize(P2PConfig config)
        {
            Initialize(config, P2PEndpoints.GetCurrentEnvironment());
        }

        /// <summary>Initializes this session using explicitly selected endpoints.</summary>
        public void Initialize(P2PConfig config, P2PEnvironment environment)
        {
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<P2PConfig>();
                if (P2PLog.ShouldLog(P2PLogLevel.Warn))
                    Debug.LogWarning("[RealtimeP2PKit][P2PManager] no P2PConfig supplied; using built-in defaults");
            }

            _config = config;
            _endpointEnvironment = environment;
            P2PLog.Level = config.LogLevel;
            var matchmakingBaseUrl = P2PEndpoints.GetMatchmakingApiUrl(_endpointEnvironment);
            if (!Uri.TryCreate(matchmakingBaseUrl, UriKind.Absolute, out var apiUri) ||
                (apiUri.Scheme != Uri.UriSchemeHttp && apiUri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("Configure a valid matchmaking HTTP URL in RealtimeP2PKit/Connection Settings.");
            var signalingUrl = P2PEndpoints.GetSignalingWebSocketUrl(_endpointEnvironment);
            if (!Uri.TryCreate(signalingUrl, UriKind.Absolute, out var wsUri) ||
                (wsUri.Scheme != "ws" && wsUri.Scheme != "wss"))
                throw new ArgumentException("Configure a valid signaling WebSocket URL in RealtimeP2PKit/Connection Settings.");
            if (P2PLog.ShouldLog(P2PLogLevel.Info))
            {
                Debug.Log($"[RealtimeP2PKit][P2PManager] initializing. environment={_endpointEnvironment} " +
                          $"matchmakingApiUrl={matchmakingBaseUrl} " +
                          $"signalingWebSocketUrl={signalingUrl} logLevel={config.LogLevel}");
            }

            _matchmakingClient = new HttpMatchmakingClient(matchmakingBaseUrl);
            // Do not discard registered gameplay packet handlers when a scene
            // provides its config after the manager was lazily initialized.
            _packetRouter ??= new PacketRouter(new MessagePackPayloadCodec());

            if (!_webRtcUpdateStarted)
            {
                StartCoroutine(WebRTC.Update());
                _webRtcUpdateStarted = true;
                if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][P2PManager] Unity.WebRTC update loop started");
            }
        }

        /// <summary>Register a typed handler for an application-defined packet id (see PacketRouter).</summary>
        public void RegisterPacketHandler<T>(byte packetId, Action<T> handler)
        {
            EnsureInitialized();
            _packetRouter.Register(packetId, handler);
        }

        public void UnregisterPacketHandler(byte packetId)
        {
            EnsureInitialized();
            _packetRouter.Unregister(packetId);
        }

        /// <summary>Send a MessagePack-encoded packet over the open data channel.</summary>
        public void Send<T>(byte packetId, T value)
        {
            if (Session.State != P2PSessionState.Connected)
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Warn)) Debug.LogWarning($"[RealtimeP2PKit][P2PManager] Send<{typeof(T).Name}> ignored, session state={Session.State}");
                return;
            }
            var buffer = _packetRouter.Encode(packetId, value);
            if (P2PNetworkLog.IsEnabled)
                Debug.Log(P2PNetworkLogFormat.WebRtcSend(packetId, value, buffer.Length));
            _peerConnection.Send(buffer);
        }

        /// <summary>Joins the matchmaking queue and drives the connection through to Connected.</summary>
        public async void StartMatchmaking(string localPlayerId)
        {
            EnsureInitialized();
            IsOnlineMatch = true;
            _peerConnectionStarted = false;
            _dataChannelReadyRaised = false;
            _opponentLeftRaised = false;
            SetState(P2PSessionState.Matchmaking);
            Session = new P2PSessionInfo { LocalPlayerId = localPlayerId, State = P2PSessionState.Matchmaking };
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][P2PManager] starting matchmaking as playerId={localPlayerId}");

            // Listen on our own lobby room first, in case we end up waiting and get
            // matched later by another player's join request.
            _lobbyListener = new LobbyListener(P2PEndpoints.GetSignalingWebSocketUrl(_endpointEnvironment));
            _lobbyListener.Matched += OnLobbyMatched;
            await _lobbyListener.ConnectAsync(localPlayerId);

            var result = await _matchmakingClient.JoinQueueAsync(localPlayerId);
            if (result.status == "matched")
            {
                OnLobbyMatched(new LobbyMatchedMessage
                {
                    type = "matched",
                    roomId = result.roomId,
                    opponentId = result.opponentId,
                    isInitiator = result.isInitiator,
                });
            }
            else
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][P2PManager] queued, waiting for an opponent...");
            }
        }

        /// <summary>Creates a public room and waits there until another player joins it.</summary>
        public async Task CreateRoom(string localPlayerId)
        {
            try
            {
                PrepareNewSession(localPlayerId);
                var room = await _matchmakingClient.CreateRoomAsync(localPlayerId);
                await ConnectToRoomAsync(room.id, null, true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RealtimeP2PKit][P2PManager] room creation failed: {ex}");
                SetState(P2PSessionState.Idle);
                throw;
            }
        }

        /// <summary>Reserves an open room and joins its signaling room as the answerer.</summary>
        public async Task JoinRoom(string localPlayerId, MachingRoom room)
        {
            try
            {
                PrepareNewSession(localPlayerId);
                var joined = await _matchmakingClient.JoinRoomAsync(room.id, localPlayerId);
                await ConnectToRoomAsync(joined.id, joined.hostPlayerId, false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RealtimeP2PKit][P2PManager] room join failed: {ex.Message}");
                SetState(P2PSessionState.Idle);
                throw;
            }
        }

        private void PrepareNewSession(string localPlayerId)
        {
            EnsureInitialized();
            IsOnlineMatch = true;
            _peerConnectionStarted = false;
            _dataChannelReadyRaised = false;
            _opponentLeftRaised = false;
            SetState(P2PSessionState.Matchmaking);
            Session = new P2PSessionInfo { LocalPlayerId = localPlayerId, State = P2PSessionState.Matchmaking };
        }

        private void EnsureInitialized()
        {
            if (_matchmakingClient != null && _packetRouter != null && _config != null) return;
            Initialize(null);
        }

        private async void OnLobbyMatched(LobbyMatchedMessage msg)
        {
            if (Session.State is P2PSessionState.Negotiating or P2PSessionState.Connected)
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Warn)) Debug.LogWarning("[RealtimeP2PKit][P2PManager] OnLobbyMatched fired again, ignoring (already negotiating/connected)");
                return;
            }

            await ConnectToRoomAsync(msg.roomId, msg.opponentId, msg.isInitiator);
        }

        private async System.Threading.Tasks.Task ConnectToRoomAsync(string roomId, string opponentId, bool isInitiator)
        {
            Session.RoomId = roomId;
            Session.OpponentId = opponentId;
            Session.IsInitiator = isInitiator;
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][P2PManager] joining room. roomId={roomId} opponentId={opponentId} isInitiator={isInitiator}");
            Matched?.Invoke(Session);

            SetState(P2PSessionState.SignalingConnecting);
            _signalingClient = new PartyKitSignalingClient(P2PEndpoints.GetSignalingWebSocketUrl(_endpointEnvironment));
            _signalingClient.MessageReceived += OnSignalMessage;
            _signalingClient.Connected += OnSignalingConnected;
            _signalingClient.Disconnected += reason =>
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Warn)) Debug.LogWarning($"[RealtimeP2PKit][P2PManager] signaling disconnected: {reason}");
            };
            await _signalingClient.ConnectAsync(roomId);
        }

        private void OnSignalingConnected()
        {
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][P2PManager] signaling connected; sending client-ready");
            _signalingClient.Send(new RoomSignalEnvelope { type = "client-ready" });
        }

        private void StartWebRtcNegotiation()
        {
            if (_peerConnectionStarted) return;
            _peerConnectionStarted = true;
            SetState(P2PSessionState.Negotiating);
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][P2PManager] peer ready, starting WebRTC negotiation (isInitiator={Session.IsInitiator})");

            var stunServerUrls = P2PEndpoints.GetStunServerUrls(_endpointEnvironment);
            _peerConnection = new WebRtcPeerConnection(this, _config, stunServerUrls);
            _peerConnection.Initialize(Session.IsInitiator);

            _peerConnection.LocalIceCandidateGathered += candidate => _signalingClient.Send(new RoomSignalEnvelope
            {
                type = "ice-candidate",
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex,
            });

            _peerConnection.ConnectionStateChanged += state =>
            {
                if (!IsOnlineMatch) return;

                // Unity.WebRTC can report Disconnected transiently while a
                // synchronous scene load is in progress. The data channel is
                // the gameplay transport, so only mark the session terminal
                // when it actually closes (handled below), fails, or is closed.
                if (state is RTCPeerConnectionState.Failed or RTCPeerConnectionState.Closed)
                {
                    SetState(P2PSessionState.Disconnected);
                    ConnectionClosed?.Invoke(state.ToString());
                }
                else if (state == RTCPeerConnectionState.Disconnected && P2PLog.ShouldLog(P2PLogLevel.Warn))
                {
                    Debug.LogWarning("[RealtimeP2PKit][P2PManager] peer connection temporarily disconnected; keeping session while data channel remains open");
                }
            };

            _peerConnection.DataChannelOpened += NotifyDataChannelReady;
            _peerConnection.DataChannelClosed += () =>
            {
                if (!IsOnlineMatch) return;
                SetState(P2PSessionState.Disconnected);
                ConnectionClosed?.Invoke("data channel closed");
            };
            _peerConnection.DataReceived += bytes => _packetRouter.Dispatch(bytes);

            if (Session.IsInitiator)
            {
                _peerConnection.CreateOffer(offer =>
                    _signalingClient.Send(new RoomSignalEnvelope { type = "offer", sdp = offer.sdp }));
            }
        }

        private void OnSignalMessage(RoomSignalEnvelope msg)
        {
            if (!IsOnlineMatch) return;

            switch (msg.type)
            {
                case "peer-ready":
                    StartWebRtcNegotiation();
                    break;
                case "offer":
                    if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][P2PManager] received offer, setting remote description and creating answer");
                    _peerConnection.SetRemoteDescription(new RTCSessionDescription { type = RTCSdpType.Offer, sdp = msg.sdp }, () =>
                        _peerConnection.CreateAnswer(answer =>
                            _signalingClient.Send(new RoomSignalEnvelope { type = "answer", sdp = answer.sdp })));
                    break;

                case "answer":
                    if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][P2PManager] received answer, setting remote description");
                    _peerConnection.SetRemoteDescription(new RTCSessionDescription { type = RTCSdpType.Answer, sdp = msg.sdp });
                    break;

                case "ice-candidate":
                    var init = new RTCIceCandidateInit
                    {
                        candidate = msg.candidate,
                        sdpMid = msg.sdpMid,
                        sdpMLineIndex = msg.sdpMLineIndex,
                    };
                    _peerConnection.AddRemoteIceCandidate(init);
                    break;

                case "peer-left":
                    NotifyOpponentLeft();
                    break;

                default:
                    if (P2PLog.ShouldLog(P2PLogLevel.Verbose)) Debug.Log($"[RealtimeP2PKit][P2PManager] unhandled signal type={msg.type}");
                    break;
            }
        }

        private void Update()
        {
            _signalingClient?.DispatchMessageQueue();
            _lobbyListener?.DispatchMessageQueue();

            // Unity.WebRTC can expose an open locally-created data channel
            // without invoking its OnOpen callback. Polling the state makes
            // the game-start signal reliable on both host and guest.
            if (!_dataChannelReadyRaised && _peerConnection?.IsDataChannelOpen == true)
                NotifyDataChannelReady();
        }

        private void NotifyDataChannelReady()
        {
            if (!IsOnlineMatch || _dataChannelReadyRaised) return;
            _dataChannelReadyRaised = true;
            SetState(P2PSessionState.Connected);
            DataChannelReady?.Invoke();
        }

        private void NotifyOpponentLeft()
        {
            if (!IsOnlineMatch || _opponentLeftRaised) return;
            _opponentLeftRaised = true;
            if (P2PLog.ShouldLog(P2PLogLevel.Warn)) Debug.LogWarning("[RealtimeP2PKit][P2PManager] opponent left the room");
            OpponentLeft?.Invoke();
            SetState(P2PSessionState.Disconnected);
            ConnectionClosed?.Invoke("peer-left");
        }

        private void SetState(P2PSessionState state)
        {
            if (Session.State == state) return;
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][P2PManager] state {Session.State} -> {state}");
            Session.State = state;
            StateChanged?.Invoke(state);
        }

        /// <summary>Tears down the current session and leaves the matchmaking queue if still waiting.</summary>
        public void Disconnect()
        {
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][P2PManager] disconnect requested");

            // Leave the server asynchronously, but do not keep WebRTC or
            // signaling alive while a scene is changing.
            var playerId = Session.LocalPlayerId;
            if (_matchmakingClient != null && !string.IsNullOrEmpty(playerId))
            {
                LeaveMatchmakingAsync(_matchmakingClient, playerId);
            }

            IsOnlineMatch = false;
            _peerConnection?.Dispose();
            _signalingClient?.Dispose();
            _lobbyListener?.Dispose();
            _peerConnection = null;
            _signalingClient = null;
            _lobbyListener = null;
            _peerConnectionStarted = false;
            _dataChannelReadyRaised = false;
            _opponentLeftRaised = false;
            SetState(P2PSessionState.Idle);
            Session = new P2PSessionInfo { State = P2PSessionState.Idle };
        }


        private async void LeaveMatchmakingAsync(HttpMatchmakingClient matchmakingClient, string playerId)
        {
            await matchmakingClient.LeaveQueueAsync(playerId);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            _peerConnection?.Dispose();
            _signalingClient?.Dispose();
            _lobbyListener?.Dispose();
        }
    }
}
