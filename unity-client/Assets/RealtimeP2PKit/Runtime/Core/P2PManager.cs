using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;

namespace net.taptappun.RealtimeP2PKit
{
    /// <summary>A peer-to-peer mesh: one WebRTC connection per remote participant, without a fixed peer limit.</summary>
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
        public event Action<P2PSessionInfo> SignalingConnecting;
        public event Action SignalingReady;
        public event Action DataChannelReady;
        public event Action<string> ConnectionClosed;
        public event Action<string> PeerJoined;
        public event Action<string> PeerConnected;
        public event Action<string> PeerLeft;
        public event Action<string> PeerConnectionFailed;
        public P2PSessionInfo Session { get; private set; } = new() { State = P2PSessionState.Idle };
        public bool IsSessionActive { get; private set; }
        public IReadOnlyCollection<string> PeerIds => _sessionPeers.ToArray();
        public IReadOnlyCollection<string> ConnectedPeerIds => _peers.Where(p => p.Value.Ready).Select(p => p.Key).ToArray();

        private sealed class Peer
        {
            public WebRtcPeerConnection Connection;
            public bool Ready;
            public float StartedAt;
        }
        private readonly Dictionary<string, Peer> _peers = new();
        private readonly HashSet<string> _sessionPeers = new();
        private P2PConfig _config;
        private P2PConfig _defaultConfig;
        private PacketRouter _packetRouter;
        private ISignalingClient _signalingClient;
        private TaskCompletionSource<bool> _connecting;
        private Action _detachSignaling;
        private P2PEnvironment _endpointEnvironment;
        private bool _webRtcUpdateStarted;

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
#if UNITY_EDITOR
            _endpointEnvironment = environment;
#else
            // Player builds always connect using the Remote configuration.
            _endpointEnvironment = P2PEnvironment.Remote;
#endif
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
            if (!IsSessionActive || _peers.Count == 0) return;
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

        /// <summary>Connect using an application-provided signaling implementation. This manager owns its lifetime.</summary>
        public async Task ConnectAsync(string localPeerId, ISignalingClient signalingClient)
        {
            if (string.IsNullOrWhiteSpace(localPeerId)) throw new ArgumentException("Peer ID is required.", nameof(localPeerId));
            if (signalingClient == null) throw new ArgumentNullException(nameof(signalingClient));
            if (ReferenceEquals(signalingClient, _signalingClient)) throw new InvalidOperationException("The signaling client is already in use.");
            EnsureInitialized();
            Disconnect();
            IsSessionActive = true;
            Session = new P2PSessionInfo { LocalPeerId = localPeerId };
            var completion = _connecting = new TaskCompletionSource<bool>();
            var client = _signalingClient = signalingClient;
            Action<string, bool> joined = (id, initiator) => { if (_signalingClient == client) AddPeer(id, initiator); };
            Action<string> left = id =>
            {
                if (_signalingClient != client) return;
                if (_sessionPeers.Remove(id)) { RemoveConnection(id); PeerLeft?.Invoke(id); }
                RefreshState();
            };
            Action<string, SignalingMessage> received = (id, message) => { if (_signalingClient == client) OnSignalMessage(id, message); };
            Action<string> closed = reason => { if (_signalingClient == client) FailSession(reason); };
            client.PeerJoined += joined;
            client.PeerLeft += left;
            client.MessageReceived += received;
            client.Disconnected += closed;
            _detachSignaling = () =>
            {
                client.PeerJoined -= joined;
                client.PeerLeft -= left;
                client.MessageReceived -= received;
                client.Disconnected -= closed;
            };
            SetState(P2PSessionState.SignalingConnecting);
            SignalingConnecting?.Invoke(Session);
            // Observe adapter failures even if Disconnect cancels the caller's pending connection.
            _ = CompleteConnectionAsync(client, completion);
            await completion.Task;
        }
        private async Task CompleteConnectionAsync(ISignalingClient client, TaskCompletionSource<bool> completion)
        {
            try
            {
                await client.ConnectAsync();
                if (_signalingClient != client) { completion.TrySetCanceled(); return; }
                Session.IsSignalingReady = true;
                RefreshState();
                SignalingReady?.Invoke();
                completion.TrySetResult(true);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
                if (_signalingClient == client) FailSession("signaling connection failed");
            }
        }
        private void OnSignalMessage(string from, SignalingMessage message)
        {
            if (!IsSessionActive || message == null || from == null || !_peers.TryGetValue(from, out var peer)) return;
            switch (message.Type)
            {
                case SignalingMessageType.Offer:
                    peer.Connection.SetRemoteDescription(new RTCSessionDescription { type = RTCSdpType.Offer, sdp = message.Sdp },
                        () => peer.Connection.CreateAnswer(answer => SendSignal(from, new SignalingMessage { Type = SignalingMessageType.Answer, Sdp = answer.sdp })));
                    break;
                case SignalingMessageType.Answer:
                    peer.Connection.SetRemoteDescription(new RTCSessionDescription { type = RTCSdpType.Answer, sdp = message.Sdp });
                    break;
                case SignalingMessageType.IceCandidate:
                    peer.Connection.AddRemoteIceCandidate(new RTCIceCandidateInit { candidate = message.Candidate,
                        sdpMid = message.SdpMid, sdpMLineIndex = message.SdpMLineIndex });
                    break;
            }
        }
        private void AddPeer(string id, bool initiator)
        {
            if (string.IsNullOrEmpty(id) || id == Session.LocalPeerId || _peers.ContainsKey(id)) return;
            var peer = new Peer { Connection = new WebRtcPeerConnection(this, _config, P2PEndpoints.GetStunServerUrls(_endpointEnvironment)),
                StartedAt = Time.realtimeSinceStartup };
            _peers.Add(id, peer);
            if (_sessionPeers.Add(id)) PeerJoined?.Invoke(id);
            peer.Connection.LocalIceCandidateGathered += candidate => SendSignal(id, new SignalingMessage { Type = SignalingMessageType.IceCandidate,
                Candidate = candidate.Candidate, SdpMid = candidate.SdpMid, SdpMLineIndex = candidate.SdpMLineIndex });
            peer.Connection.DataReceived += data => { if (IsCurrent(id, peer)) _packetRouter.Dispatch(data, id); };
            peer.Connection.DataChannelOpened += () => MarkConnected(id, peer);
            peer.Connection.DataChannelClosed += () => FailPeer(id, peer);
            peer.Connection.ConnectionStateChanged += state =>
            {
                if (state is RTCPeerConnectionState.Failed or RTCPeerConnectionState.Closed) FailPeer(id, peer);
            };
            peer.Connection.Initialize(initiator);
            if (initiator) peer.Connection.CreateOffer(offer => SendSignal(id, new SignalingMessage { Type = SignalingMessageType.Offer, Sdp = offer.sdp }));
            RefreshState();
        }
        private void SendSignal(string id, SignalingMessage message)
        {
            if (!IsSessionActive || !_peers.ContainsKey(id)) return;
            _signalingClient?.Send(id, message);
        }
        private void MarkConnected(string id, Peer peer)
        {
            if (!IsCurrent(id, peer) || peer.Ready) return;
            peer.Ready = true;
            RefreshState();
            PeerConnected?.Invoke(id);
            DataChannelReady?.Invoke();
        }
        private bool IsCurrent(string id, Peer peer) => IsSessionActive && _peers.TryGetValue(id, out var current) && current == peer;
        private void FailPeer(string id, Peer peer)
        {
            if (!IsCurrent(id, peer)) return;
            RemoveConnection(id);
            RefreshState();
            PeerConnectionFailed?.Invoke(id); // Other peers and the signaling session stay connected.
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
            if (!IsSessionActive) return;
            var now = Time.realtimeSinceStartup;
            foreach (var entry in _peers.ToArray())
            {
                if (entry.Value.Connection.IsDataChannelOpen) MarkConnected(entry.Key, entry.Value);
                else if (!entry.Value.Ready && _config.PeerConnectionTimeoutSeconds > 0 && now - entry.Value.StartedAt > _config.PeerConnectionTimeoutSeconds) FailPeer(entry.Key, entry.Value);
            }
        }
        private void RefreshState()
        {
            if (!IsSessionActive) return;
            SetState(_peers.Values.Any(p => p.Ready) ? P2PSessionState.Connected :
                _peers.Count > 0 ? P2PSessionState.Negotiating :
                Session.IsSignalingReady ? P2PSessionState.WaitingForPeers : P2PSessionState.SignalingConnecting);
        }
        private void SetState(P2PSessionState state)
        {
            if (Session.State == state) return;
            Session.State = state;
            StateChanged?.Invoke(state);
        }
        private void FailSession(string reason)
        {
            if (!IsSessionActive) return;
            Disconnect();
            SetState(P2PSessionState.Failed);
            ConnectionClosed?.Invoke(reason);
        }
        public void Disconnect()
        {
            IsSessionActive = false;
            var client = _signalingClient;
            _signalingClient = null;
            _detachSignaling?.Invoke();
            _detachSignaling = null;
            _connecting?.TrySetCanceled();
            _connecting = null;
            client?.Dispose();
            foreach (var id in _peers.Keys.ToArray()) RemoveConnection(id);
            _sessionPeers.Clear();
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
