using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace net.taptappun.RealtimeP2PKit.Example.Signaling
{
    /// <summary>Demo adapter for the included partyserver's room membership and JSON protocol.</summary>
    public sealed class PartyKitSignalingClient : ISignalingClient
    {
        public event Action<string> Disconnected;
        public event Action<string, bool> PeerJoined;
        public event Action<string> PeerLeft;
        public event Action<string, SignalingMessage> MessageReceived;
        private readonly ISignalingTransport _transport;
        private readonly string _playerId;
        private readonly TaskCompletionSource<bool> _ready = new();
        private bool _started;
        private bool _disposed;
        private float _lastHeartbeat;
        private float _lastSignal;
        // The included server expires reservations after 45 seconds. Renew every 10 seconds,
        // and detect an unresponsive signaling connection before its reservation expires.
        private const float HeartbeatIntervalSeconds = 10;
        private const float HeartbeatTimeoutSeconds = 40;

        public PartyKitSignalingClient(string webSocketUrl, string playerId, string token)
            : this(new WebSocketSignalingTransport(BuildConnectionUrl(webSocketUrl, playerId, token)), playerId) { }

        public PartyKitSignalingClient(ISignalingTransport transport, string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("Player ID is required.", nameof(playerId));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _playerId = playerId;
            _transport.MessageReceived += OnMessage;
            _transport.Disconnected += Fail;
        }
        internal static string BuildConnectionUrl(string webSocketUrl, string playerId, string token)
        {
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("Player ID is required.", nameof(playerId));
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Player token is required.", nameof(token));
            var uri = new UriBuilder(webSocketUrl);
            var query = uri.Query.TrimStart('?');
            uri.Query = (query.Length == 0 ? string.Empty : query + "&") +
                $"playerId={Uri.EscapeDataString(playerId)}&token={Uri.EscapeDataString(token)}";
            return uri.Uri.AbsoluteUri;
        }
        public async Task ConnectAsync()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PartyKitSignalingClient));
            if (_started) throw new InvalidOperationException("The adapter has already been connected.");
            _started = true;
            _lastSignal = _lastHeartbeat = Time.realtimeSinceStartup;
            await _transport.ConnectAsync();
            await SendProtocolAsync(new RoomSignalEnvelope { type = "client-ready" });
            await _ready.Task;
        }
        private void OnMessage(string raw)
        {
            if (_disposed) return;
            RoomSignalEnvelope message;
            try { message = JsonConvert.DeserializeObject<RoomSignalEnvelope>(raw); }
            catch (JsonException) { return; }
            if (message == null) return;
            _lastSignal = Time.realtimeSinceStartup;
            switch (message.type)
            {
                case "heartbeat": return;
                case "room-joined":
                    if (_ready.Task.IsCompleted) return;
                    foreach (var id in message.peers ?? Array.Empty<string>()) PeerJoined?.Invoke(id, message.isInitiator);
                    _ready.TrySetResult(true);
                    return;
                case "peer-joined": PeerJoined?.Invoke(message.from, message.isInitiator); return;
                case "peer-left": PeerLeft?.Invoke(message.from); return;
            }
            if (message.to != _playerId || string.IsNullOrEmpty(message.from)) return;
            SignalingMessageType type;
            switch (message.type)
            {
                case "offer": type = SignalingMessageType.Offer; break;
                case "answer": type = SignalingMessageType.Answer; break;
                case "ice-candidate": type = SignalingMessageType.IceCandidate; break;
                default: return;
            }
            MessageReceived?.Invoke(message.from, new SignalingMessage { Type = type, Sdp = message.sdp,
                Candidate = message.candidate, SdpMid = message.sdpMid, SdpMLineIndex = message.sdpMLineIndex });
        }
        public void Send(string peerId, SignalingMessage message)
        {
            if (_disposed) return;
            var type = message.Type switch
            {
                SignalingMessageType.Offer => "offer",
                SignalingMessageType.Answer => "answer",
                SignalingMessageType.IceCandidate => "ice-candidate",
                _ => throw new ArgumentOutOfRangeException(nameof(message))
            };
            _ = SendProtocolAsync(new RoomSignalEnvelope { type = type, to = peerId, sdp = message.Sdp,
                candidate = message.Candidate, sdpMid = message.SdpMid, sdpMLineIndex = message.SdpMLineIndex });
        }
        private async Task SendProtocolAsync(RoomSignalEnvelope message)
        {
            try { await _transport.SendAsync(JsonConvert.SerializeObject(message)); }
            catch (Exception) { Fail("signaling send failed"); }
        }
        private void Fail(string reason)
        {
            if (_disposed) return;
            _ready.TrySetException(new InvalidOperationException(reason));
            Disconnected?.Invoke(reason);
        }
        public void DispatchMessageQueue()
        {
            _transport.DispatchMessageQueue();
            if (_disposed || !_started) return;
            var now = Time.realtimeSinceStartup;
            if (now - _lastSignal > HeartbeatTimeoutSeconds) { Fail("signaling heartbeat timeout"); return; }
            if (now - _lastHeartbeat > HeartbeatIntervalSeconds)
            {
                _lastHeartbeat = now;
                _ = SendProtocolAsync(new RoomSignalEnvelope { type = "heartbeat" });
            }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _transport.MessageReceived -= OnMessage;
            _transport.Disconnected -= Fail;
            _ready.TrySetCanceled();
            _transport.Dispose();
        }
    }
}
