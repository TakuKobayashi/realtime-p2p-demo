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
        private readonly string _roomId;
        private readonly string _token;
        private readonly TaskCompletionSource<bool> _ready = new();
        private bool _started;
        private bool _disposed;
        private float _lastHeartbeat;
        private float _lastSignal;
        // The included server expires reservations after 45 seconds. Renew every 10 seconds,
        // and detect an unresponsive signaling connection before its reservation expires.
        private const float HeartbeatIntervalSeconds = 10;
        private const float HeartbeatTimeoutSeconds = 40;

        public PartyKitSignalingClient(string webSocketUrl, long roomId, string playerId, string token)
            : this(new WebSocketSignalingTransport(webSocketUrl), roomId, playerId, token) { }

        public PartyKitSignalingClient(ISignalingTransport transport, long roomId, string playerId, string token)
        {
            if (roomId <= 0) throw new ArgumentOutOfRangeException(nameof(roomId));
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("Player ID is required.", nameof(playerId));
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Player token is required.", nameof(token));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _roomId = roomId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _playerId = playerId;
            _token = token;
            _transport.MessageReceived += OnMessage;
            _transport.Disconnected += Fail;
        }
        public async Task ConnectAsync()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PartyKitSignalingClient));
            if (_started) throw new InvalidOperationException("The adapter has already been connected.");
            _started = true;
            _lastSignal = _lastHeartbeat = Time.realtimeSinceStartup;
            await _transport.ConnectAsync();
            await SendProtocolAsync(new RoomSignalEnvelope { type = "join", roomId = _roomId, playerId = _playerId, token = _token });
            await _ready.Task;
        }
        private void OnMessage(string raw)
        {
            if (_disposed) return;
            if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketReceive("Signaling", raw));
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
            try
            {
                var raw = JsonConvert.SerializeObject(message);
                if (P2PNetworkLog.IsEnabled)
                {
                    var log = Newtonsoft.Json.Linq.JObject.Parse(raw);
                    if (log["token"] != null) log["token"] = "[redacted]";
                    Debug.Log(P2PNetworkLogFormat.WebSocketSend("Signaling", log.ToString(Formatting.None)));
                }
                await _transport.SendAsync(raw);
            }
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
            if (_ready.Task.Status == TaskStatus.RanToCompletion && now - _lastHeartbeat > HeartbeatIntervalSeconds)
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
