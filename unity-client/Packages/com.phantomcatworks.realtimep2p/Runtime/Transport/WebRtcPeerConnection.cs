using System;
using System.Collections;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit
{
    /// <summary>
    /// Thin wrapper around Unity.WebRTC's RTCPeerConnection plus a single data
    /// channel. Owns ICE negotiation and exposes byte-level send/receive events;
    /// application-level (de)serialization is handled one layer up by
    /// PacketRouter / IPayloadCodec. STUN-only configuration: no TURN relay is
    /// used, so a pair where both peers are behind a symmetric NAT may fail to
    /// establish a direct connection - that is an accepted limitation of this
    /// proof of concept, not a bug.
    /// </summary>
    public class WebRtcPeerConnection : IDisposable
    {
        public event Action<RTCIceCandidate> LocalIceCandidateGathered;
        public event Action<RTCPeerConnectionState> ConnectionStateChanged;
        public event Action DataChannelOpened;
        public event Action DataChannelClosed;
        public event Action<byte[]> DataReceived;

        private readonly MonoBehaviour _coroutineRunner;
        private readonly P2PConfig _config;
        private readonly System.Collections.Generic.List<string> _stunServerUrls;
        private RTCPeerConnection _pc;
        private RTCDataChannel _dataChannel;

        public RTCPeerConnectionState State => _pc?.ConnectionState ?? RTCPeerConnectionState.New;

        /// <param name="stunServerUrls">See P2PEndpoints.GetStunServerUrls().</param>
        public WebRtcPeerConnection(MonoBehaviour coroutineRunner, P2PConfig config, System.Collections.Generic.List<string> stunServerUrls)
        {
            _coroutineRunner = coroutineRunner;
            _config = config;
            _stunServerUrls = stunServerUrls;
        }

        public void Initialize(bool isInitiator)
        {
            var rtcConfig = new RTCConfiguration
            {
                iceServers = new[] { new RTCIceServer { urls = _stunServerUrls.ToArray() } }
            };

            if (P2PLog.ShouldLog(P2PLogLevel.Info))
            {
                Debug.Log($"[RealtimeP2PKit][WebRTC] creating RTCPeerConnection isInitiator={isInitiator} " +
                          $"stunServers=[{string.Join(", ", _stunServerUrls)}]");
            }
            _pc = new RTCPeerConnection(ref rtcConfig);

            _pc.OnIceCandidate = candidate =>
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Verbose)) Debug.Log($"[RealtimeP2PKit][WebRTC] local ICE candidate gathered: {candidate.Candidate}");
                LocalIceCandidateGathered?.Invoke(candidate);
            };

            _pc.OnIceConnectionChange = state =>
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][WebRTC] ICE connection state -> {state}");
            };
            _pc.OnIceGatheringStateChange = state =>
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Verbose)) Debug.Log($"[RealtimeP2PKit][WebRTC] ICE gathering state -> {state}");
            };

            _pc.OnConnectionStateChange = state =>
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][WebRTC] peer connection state -> {state}");
                ConnectionStateChanged?.Invoke(state);
            };

            if (isInitiator)
            {
                var init = new RTCDataChannelInit
                {
                    ordered = _config.Reliable,
                    maxRetransmits = _config.Reliable ? (int?)null : _config.MaxRetransmits,
                };
                if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][WebRTC] creating data channel label='{_config.DataChannelLabel}' reliable={_config.Reliable}");
                _dataChannel = _pc.CreateDataChannel(_config.DataChannelLabel, init);
                SetupDataChannel(_dataChannel);
            }
            else
            {
                _pc.OnDataChannel = channel =>
                {
                    if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][WebRTC] received remote data channel label='{channel.Label}'");
                    _dataChannel = channel;
                    SetupDataChannel(_dataChannel);
                };
            }
        }

        private void SetupDataChannel(RTCDataChannel channel)
        {
            channel.OnOpen = () =>
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][WebRTC] data channel OPEN label='{channel.Label}'");
                DataChannelOpened?.Invoke();
            };
            channel.OnClose = () =>
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][WebRTC] data channel CLOSED label='{channel.Label}'");
                DataChannelClosed?.Invoke();
            };
            channel.OnMessage = bytes =>
            {
                if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebRtcReceive(bytes));
                DataReceived?.Invoke(bytes);
            };
        }

        public void CreateOffer(Action<RTCSessionDescription> onOfferCreated) =>
            _coroutineRunner.StartCoroutine(CreateOfferCoroutine(onOfferCreated));

        private IEnumerator CreateOfferCoroutine(Action<RTCSessionDescription> onOfferCreated)
        {
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][WebRTC] creating offer...");
            var op = _pc.CreateOffer();
            yield return op;
            if (op.IsError)
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Error)) Debug.LogError($"[RealtimeP2PKit][WebRTC] CreateOffer failed: {op.Error.message}");
                yield break;
            }
            var desc = op.Desc;
            yield return _coroutineRunner.StartCoroutine(SetLocalDescriptionCoroutine(desc));
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][WebRTC] offer created & set as local description ({desc.sdp.Length} chars)");
            onOfferCreated?.Invoke(desc);
        }

        public void CreateAnswer(Action<RTCSessionDescription> onAnswerCreated) =>
            _coroutineRunner.StartCoroutine(CreateAnswerCoroutine(onAnswerCreated));

        private IEnumerator CreateAnswerCoroutine(Action<RTCSessionDescription> onAnswerCreated)
        {
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][WebRTC] creating answer...");
            var op = _pc.CreateAnswer();
            yield return op;
            if (op.IsError)
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Error)) Debug.LogError($"[RealtimeP2PKit][WebRTC] CreateAnswer failed: {op.Error.message}");
                yield break;
            }
            var desc = op.Desc;
            yield return _coroutineRunner.StartCoroutine(SetLocalDescriptionCoroutine(desc));
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][WebRTC] answer created & set as local description ({desc.sdp.Length} chars)");
            onAnswerCreated?.Invoke(desc);
        }

        private IEnumerator SetLocalDescriptionCoroutine(RTCSessionDescription desc)
        {
            var op = _pc.SetLocalDescription(ref desc);
            yield return op;
            if (op.IsError)
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Error)) Debug.LogError($"[RealtimeP2PKit][WebRTC] SetLocalDescription failed: {op.Error.message}");
            }
        }

        public void SetRemoteDescription(RTCSessionDescription desc) =>
            _coroutineRunner.StartCoroutine(SetRemoteDescriptionCoroutine(desc));

        private IEnumerator SetRemoteDescriptionCoroutine(RTCSessionDescription desc)
        {
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][WebRTC] setting remote description type={desc.type}");
            var op = _pc.SetRemoteDescription(ref desc);
            yield return op;
            if (op.IsError)
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Error)) Debug.LogError($"[RealtimeP2PKit][WebRTC] SetRemoteDescription failed: {op.Error.message}");
            }
            else
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][WebRTC] remote description set successfully");
            }
        }

        public void AddRemoteIceCandidate(RTCIceCandidate candidate)
        {
            if (P2PLog.ShouldLog(P2PLogLevel.Verbose)) Debug.Log($"[RealtimeP2PKit][WebRTC] adding remote ICE candidate: {candidate.Candidate}");
            _pc.AddIceCandidate(candidate);
        }

        public void Send(byte[] payload)
        {
            if (_dataChannel == null || _dataChannel.ReadyState != RTCDataChannelState.Open)
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Warn)) Debug.LogWarning("[RealtimeP2PKit][WebRTC] cannot send, data channel not open");
                return;
            }
            if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebRtcSend(payload));
            _dataChannel.Send(payload);
        }

        public void Dispose()
        {
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][WebRTC] disposing peer connection");
            _dataChannel?.Close();
            _dataChannel?.Dispose();
            _pc?.Close();
            _pc?.Dispose();
        }
    }
}
