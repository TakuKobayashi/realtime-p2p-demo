using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;

namespace net.taptappun.RealtimeP2PKit
{
    /// <summary>A room-scoped mesh: one WebRTC connection per remote participant, without a fixed peer limit.</summary>
    [DisallowMultipleComponent]
    public class P2PManager : MonoBehaviour
    {
        private static P2PManager _instance;
        public static P2PManager Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = new GameObject(nameof(P2PManager)).AddComponent<P2PManager>();
                return _instance;
            }
        }
        public static bool TryGetExistingInstance(out P2PManager manager)
        {
            manager = _instance;
            return manager != null;
        }

        public event Action<P2PSessionState> StateChanged;
        public event Action<P2PSessionInfo> RoomConnecting;
        public event Action RoomJoined;
        public event Action DataChannelReady;
        public event Action<string> ConnectionClosed;
        public event Action<string> PeerJoined;
        public event Action<string> PeerConnected;
        public event Action<string> PeerLeft;
        public event Action<string> PeerConnectionFailed;
        public P2PSessionInfo Session { get; private set; } = new() { State = P2PSessionState.Idle };
        public bool IsOnlineMatch { get; private set; }
        public IReadOnlyCollection<string> PeerIds => _roomPeers.ToArray();
        public IReadOnlyCollection<string> ConnectedPeerIds => _peers.Where(p => p.Value.Ready).Select(p => p.Key).ToArray();

        private sealed class Peer
        {
            public WebRtcPeerConnection Connection;
            public bool Ready;
            public float StartedAt;
        }
        private readonly Dictionary<string, Peer> _peers = new();
        private readonly HashSet<string> _roomPeers = new();
        private P2PConfig _config;
        private P2PConfig _defaultConfig;
        private PacketRouter _packetRouter;
        private PartyKitSignalingClient _signalingClient;
        private TaskCompletionSource<bool> _joined;
        private P2PEnvironment _endpointEnvironment;
        private bool _webRtcUpdateStarted;
        private float _lastHeartbeat;
        private float _lastSignal;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        public void Initialize(P2PConfig config = null) => Initialize(config, P2PEndpoints.GetCurrentEnvironment());
        public void Initialize(P2PConfig config, P2PEnvironment environment)
        {
            if (config == null)
            {
                if (_defaultConfig == null)
                {
                    _defaultConfig = ScriptableObject.CreateInstance<P2PConfig>();
                    _defaultConfig.hideFlags = HideFlags.HideAndDontSave;
                }
                config = _defaultConfig;
            }
            _config = config;
            _endpointEnvironment = environment;
            P2PLog.Level = config.LogLevel;
            _packetRouter ??= new PacketRouter(new MessagePackPayloadCodec());
            if (!_webRtcUpdateStarted)
            {
                StartCoroutine(WebRTC.Update());
                _webRtcUpdateStarted = true;
            }
        }
        private void EnsureInitialized()
        {
            if (_packetRouter == null || _config == null) Initialize();
        }
        public void RegisterPacketHandler<T>(byte packetId, Action<T> handler)
        {
            EnsureInitialized();
            _packetRouter.Register(packetId, handler);
        }
        public void RegisterPacketHandler<T>(byte packetId, Action<string, T> handler)
        {
            EnsureInitialized();
            _packetRouter.Register(packetId, handler);
        }
        public void UnregisterPacketHandler(byte packetId) => _packetRouter?.Unregister(packetId);

        /// <summary>Broadcast to every open peer data channel. Waiting alone is valid.</summary>
        public void Send<T>(byte packetId, T value)
        {
            if (!IsOnlineMatch || _peers.Count == 0) return;
            var buffer = _packetRouter.Encode(packetId, value);
            if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebRtcSend(packetId, value, buffer.Length));
            foreach (var peer in _peers.Values.ToArray())
                if (peer.Ready) peer.Connection.Send(buffer);
        }
        public void SendTo<T>(string peerId, byte packetId, T value)
        {
            if (_peers.TryGetValue(peerId, out var peer) && peer.Ready)
            {
                var buffer = _packetRouter.Encode(packetId, value);
                if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebRtcSend(packetId, value, buffer.Length));
                peer.Connection.Send(buffer);
            }
        }

        /// <summary>Join a room reserved through the consuming application's HTTP API.</summary>
        public async Task ConnectToRoomAsync(string localPlayerId, string roomId, string token, string signalingWebSocketUrl = null)
        {
            if (string.IsNullOrWhiteSpace(localPlayerId)) throw new ArgumentException("Player ID is required.", nameof(localPlayerId));
            if (string.IsNullOrWhiteSpace(roomId)) throw new ArgumentException("Room ID is required.", nameof(roomId));
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Player token is required.", nameof(token));
            EnsureInitialized();
            var url = signalingWebSocketUrl ?? P2PEndpoints.GetSignalingWebSocketUrl(_endpointEnvironment);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
                throw new ArgumentException("A valid signaling WebSocket URL is required.");
            Disconnect();
            IsOnlineMatch = true;
            Session = new P2PSessionInfo { LocalPlayerId = localPlayerId, RoomId = roomId };
            _lastSignal = _lastHeartbeat = Time.realtimeSinceStartup;
            var joined = _joined = new TaskCompletionSource<bool>();
            var client = _signalingClient = new PartyKitSignalingClient(url);
            client.MessageReceived += message => { if (_signalingClient == client) OnSignalMessage(message); };
            client.Connected += () => { if (_signalingClient == client) client.Send(new RoomSignalEnvelope { type = "client-ready" }); };
            client.Disconnected += reason => { if (_signalingClient == client) FailSession(reason); };
            RoomConnecting?.Invoke(Session);
            SetState(P2PSessionState.SignalingConnecting);
            try
            {
                await client.ConnectAsync(roomId, localPlayerId, token);
                await joined.Task; // Server acknowledgement, not waiting for another player.
            }
            catch
            {
                if (_signalingClient == client) FailSession("room connection failed");
                throw;
            }
        }
        private void OnSignalMessage(RoomSignalEnvelope message)
        {
            if (!IsOnlineMatch || message == null) return;
            _lastSignal = Time.realtimeSinceStartup;
            switch (message.type)
            {
                case "heartbeat": return;
                case "room-joined":
                    Session.IsRoomJoined = true;
                    foreach (var id in message.peers ?? Array.Empty<string>()) AddPeer(id, message.isInitiator);
                    RefreshState();
                    _joined.TrySetResult(true);
                    RoomJoined?.Invoke();
                    return;
                case "peer-joined": AddPeer(message.from, message.isInitiator); return;
                case "peer-left":
                    if (_roomPeers.Remove(message.from))
                    {
                        RemoveConnection(message.from);
                        PeerLeft?.Invoke(message.from);
                    }
                    RefreshState();
                    return;
            }
            if (message.to != Session.LocalPlayerId || message.from == null || !_peers.TryGetValue(message.from, out var peer)) return;
            switch (message.type)
            {
                case "offer":
                    peer.Connection.SetRemoteDescription(new RTCSessionDescription { type = RTCSdpType.Offer, sdp = message.sdp },
                        () => peer.Connection.CreateAnswer(answer => SendSignal(message.from, new RoomSignalEnvelope { type = "answer", sdp = answer.sdp })));
                    break;
                case "answer":
                    peer.Connection.SetRemoteDescription(new RTCSessionDescription { type = RTCSdpType.Answer, sdp = message.sdp });
                    break;
                case "ice-candidate":
                    peer.Connection.AddRemoteIceCandidate(new RTCIceCandidateInit { candidate = message.candidate,
                        sdpMid = message.sdpMid, sdpMLineIndex = message.sdpMLineIndex });
                    break;
            }
        }
        private void AddPeer(string id, bool initiator)
        {
            if (string.IsNullOrEmpty(id) || id == Session.LocalPlayerId || _peers.ContainsKey(id)) return;
            var peer = new Peer { Connection = new WebRtcPeerConnection(this, _config, P2PEndpoints.GetStunServerUrls(_endpointEnvironment)),
                StartedAt = Time.realtimeSinceStartup };
            _peers.Add(id, peer);
            if (_roomPeers.Add(id)) PeerJoined?.Invoke(id);
            peer.Connection.LocalIceCandidateGathered += candidate => SendSignal(id, new RoomSignalEnvelope { type = "ice-candidate",
                candidate = candidate.Candidate, sdpMid = candidate.SdpMid, sdpMLineIndex = candidate.SdpMLineIndex });
            peer.Connection.DataReceived += data => { if (IsCurrent(id, peer)) _packetRouter.Dispatch(data, id); };
            peer.Connection.DataChannelOpened += () => MarkConnected(id, peer);
            peer.Connection.DataChannelClosed += () => FailPeer(id, peer);
            peer.Connection.ConnectionStateChanged += state =>
            {
                if (state is RTCPeerConnectionState.Failed or RTCPeerConnectionState.Closed) FailPeer(id, peer);
            };
            peer.Connection.Initialize(initiator);
            if (initiator) peer.Connection.CreateOffer(offer => SendSignal(id, new RoomSignalEnvelope { type = "offer", sdp = offer.sdp }));
            RefreshState();
        }
        private void SendSignal(string id, RoomSignalEnvelope message)
        {
            if (!IsOnlineMatch || !_peers.ContainsKey(id)) return;
            message.to = id;
            _signalingClient?.Send(message);
        }
        private void MarkConnected(string id, Peer peer)
        {
            if (!IsCurrent(id, peer) || peer.Ready) return;
            peer.Ready = true;
            RefreshState();
            PeerConnected?.Invoke(id);
            DataChannelReady?.Invoke();
        }
        private bool IsCurrent(string id, Peer peer) => IsOnlineMatch && _peers.TryGetValue(id, out var current) && current == peer;
        private void FailPeer(string id, Peer peer)
        {
            if (!IsCurrent(id, peer)) return;
            RemoveConnection(id);
            RefreshState();
            PeerConnectionFailed?.Invoke(id); // Other participants and the room stay connected.
        }
        private void RemoveConnection(string id)
        {
            if (!_peers.TryGetValue(id, out var peer)) return;
            _peers.Remove(id); // Invalidate callbacks before disposing.
            peer.Connection.Dispose();
        }
        private void Update()
        {
            _signalingClient?.DispatchMessageQueue();
            if (!IsOnlineMatch) return;
            var now = Time.realtimeSinceStartup;
            if (now - _lastSignal > 40f) { FailSession("signaling heartbeat timeout"); return; }
            if (now - _lastHeartbeat > 10f)
            {
                _lastHeartbeat = now;
                _signalingClient.Send(new RoomSignalEnvelope { type = "heartbeat" });
            }
            foreach (var entry in _peers.ToArray())
            {
                if (entry.Value.Connection.IsDataChannelOpen) MarkConnected(entry.Key, entry.Value);
                else if (!entry.Value.Ready && now - entry.Value.StartedAt > 30f) FailPeer(entry.Key, entry.Value);
            }
        }
        private void RefreshState()
        {
            if (!IsOnlineMatch) return;
            SetState(_peers.Values.Any(p => p.Ready) ? P2PSessionState.Connected :
                _peers.Count > 0 ? P2PSessionState.Negotiating :
                Session.IsRoomJoined ? P2PSessionState.WaitingForPeers : P2PSessionState.SignalingConnecting);
        }
        private void SetState(P2PSessionState state)
        {
            if (Session.State == state) return;
            Session.State = state;
            StateChanged?.Invoke(state);
        }
        private void FailSession(string reason)
        {
            if (!IsOnlineMatch) return;
            Disconnect();
            SetState(P2PSessionState.Failed);
            ConnectionClosed?.Invoke(reason);
        }
        public void Disconnect()
        {
            IsOnlineMatch = false;
            var client = _signalingClient;
            _signalingClient = null;
            client?.Dispose();
            _joined?.TrySetCanceled();
            foreach (var id in _peers.Keys.ToArray()) RemoveConnection(id);
            _roomPeers.Clear();
            SetState(P2PSessionState.Idle);
            Session = new P2PSessionInfo { State = P2PSessionState.Idle };
        }
        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            Disconnect();
            if (_defaultConfig != null) Destroy(_defaultConfig);
        }
    }
}
