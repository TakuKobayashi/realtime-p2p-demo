using System;
using Unity.WebRTC;
using UnityEngine;
using System.Threading.Tasks;

namespace PhantomCatWorks.RealtimeP2PKit
{
    /// <summary>
    /// Connects a known signaling room, negotiates WebRTC, and routes data packets.
    /// Room discovery and matchmaking belong to the consuming application.
    ///
    /// Typical usage from any other script:
    /// <code>
    ///   P2PManager.Instance.Initialize(config);
    ///   P2PManager.Instance.RegisterPacketHandler&lt;PositionPacket&gt;(1, OnPosition);
    ///   P2PManager.Instance.DataChannelReady += () => ...;
    ///   await P2PManager.Instance.ConnectToRoomAsync(myPlayerId, roomId, peerId, true);
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
        public event Action<P2PSessionInfo> RoomConnecting;
        public event Action DataChannelReady;
        public event Action<string> ConnectionClosed;
        /// <summary>Raised once when the other peer leaves the signaling room.</summary>
        public event Action OpponentLeft;

        public P2PSessionInfo Session { get; private set; } = new() { State = P2PSessionState.Idle };
        /// <summary>True only for a session started through the online room flow.</summary>
        public bool IsOnlineMatch { get; private set; }

        private P2PConfig _config;
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
        /// Optionally supplies data-channel settings. Built-in defaults are used
        /// when no explicit config is supplied.
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
            var signalingUrl = P2PEndpoints.GetSignalingWebSocketUrl(_endpointEnvironment);
            if (!Uri.TryCreate(signalingUrl, UriKind.Absolute, out var wsUri) ||
                (wsUri.Scheme != "ws" && wsUri.Scheme != "wss"))
                throw new ArgumentException("Configure a valid signaling WebSocket URL in RealtimeP2PKit/Connection Settings.");
            if (P2PLog.ShouldLog(P2PLogLevel.Info))
            {
                Debug.Log($"[RealtimeP2PKit][P2PManager] initializing. environment={_endpointEnvironment} " +
                          $"signalingWebSocketUrl={signalingUrl} logLevel={config.LogLevel}");
            }

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

        private void EnsureInitialized()
        {
            if (_packetRouter != null && _config != null) return;
            Initialize(null);
        }

        /// <summary>
        /// Connects to a room after the caller has obtained its ID and peer assignment.
        /// The caller owns matchmaking, room creation, and any HTTP requests.
        /// </summary>
        public async Task ConnectToRoomAsync(string localPlayerId, string roomId, string opponentId, bool isInitiator)
        {
            if (string.IsNullOrWhiteSpace(localPlayerId)) throw new ArgumentException("Player ID is required.", nameof(localPlayerId));
            if (string.IsNullOrWhiteSpace(roomId)) throw new ArgumentException("Room ID is required.", nameof(roomId));
            EnsureInitialized();
            if (IsOnlineMatch) Disconnect();

            IsOnlineMatch = true;
            _peerConnectionStarted = false;
            _dataChannelReadyRaised = false;
            _opponentLeftRaised = false;
            Session = new P2PSessionInfo
            {
                LocalPlayerId = localPlayerId,
                RoomId = roomId,
                OpponentId = opponentId,
                IsInitiator = isInitiator,
                State = P2PSessionState.Idle,
            };
            RoomConnecting?.Invoke(Session);
            SetState(P2PSessionState.SignalingConnecting);
            if (P2PLog.ShouldLog(P2PLogLevel.Info))
                Debug.Log($"[RealtimeP2PKit][P2PManager] joining room. roomId={roomId} opponentId={opponentId} isInitiator={isInitiator}");

            _signalingClient = new PartyKitSignalingClient(P2PEndpoints.GetSignalingWebSocketUrl(_endpointEnvironment));
            _signalingClient.MessageReceived += OnSignalMessage;
            _signalingClient.Connected += OnSignalingConnected;
            _signalingClient.Disconnected += reason =>
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Warn)) Debug.LogWarning($"[RealtimeP2PKit][P2PManager] signaling disconnected: {reason}");
            };
            try
            {
                await _signalingClient.ConnectAsync(roomId);
            }
            catch
            {
                Disconnect();
                SetState(P2PSessionState.Failed);
                throw;
            }
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

        /// <summary>Tears down the current WebRTC and signaling session.</summary>
        public void Disconnect()
        {
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][P2PManager] disconnect requested");

            IsOnlineMatch = false;
            _peerConnection?.Dispose();
            _signalingClient?.Dispose();
            _peerConnection = null;
            _signalingClient = null;
            _peerConnectionStarted = false;
            _dataChannelReadyRaised = false;
            _opponentLeftRaised = false;
            SetState(P2PSessionState.Idle);
            Session = new P2PSessionInfo { State = P2PSessionState.Idle };
        }


        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            _peerConnection?.Dispose();
            _signalingClient?.Dispose();
        }
    }
}
