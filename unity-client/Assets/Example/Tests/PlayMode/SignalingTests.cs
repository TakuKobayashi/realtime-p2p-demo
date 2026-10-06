#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using net.taptappun.RealtimeP2PKit.Example.Signaling;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.WebRTC;

namespace net.taptappun.RealtimeP2PKit.Example.Tests
{
    public sealed class SignalingTests
    {
        private sealed class LoopbackClient : ISignalingClient
        {
            public event Action<string, bool> PeerJoined;
            public event Action<string> PeerLeft;
            public event Action<string, SignalingMessage> MessageReceived;
            public event Action<string> Disconnected;
            private readonly WebRtcPeerConnection _remote;
            public bool RemoteOpen => _remote.IsDataChannelOpen;
            public int Received;
            private readonly PacketRouter _packets = new((IPayloadCodec)Activator.CreateInstance(typeof(MessagePackPayloadCodec), new object[] { null }));
            public LoopbackClient(MonoBehaviour runner, P2PConfig config)
            {
                _remote = new WebRtcPeerConnection(runner, config, new List<string>());
                _remote.LocalIceCandidateGathered += ice => MessageReceived?.Invoke("remote", new SignalingMessage
                { Type = SignalingMessageType.IceCandidate, Candidate = ice.Candidate, SdpMid = ice.SdpMid, SdpMLineIndex = ice.SdpMLineIndex });
                _remote.DataReceived += bytes => _packets.Dispatch(bytes, "local");
                _packets.Register<int>(1, value => Received = value);
            }
            public Task ConnectAsync()
            {
                _remote.Initialize(false);
                PeerJoined?.Invoke("remote", true);
                return Task.CompletedTask;
            }
            public void Send(string id, SignalingMessage message)
            {
                if (message.Type == SignalingMessageType.Offer)
                    _remote.SetRemoteDescription(new RTCSessionDescription { type = RTCSdpType.Offer, sdp = message.Sdp },
                        () => _remote.CreateAnswer(answer => MessageReceived?.Invoke("remote",
                            new SignalingMessage { Type = SignalingMessageType.Answer, Sdp = answer.sdp })));
                else if (message.Type == SignalingMessageType.IceCandidate)
                    _remote.AddRemoteIceCandidate(new RTCIceCandidateInit
                    { candidate = message.Candidate, sdpMid = message.SdpMid, sdpMLineIndex = message.SdpMLineIndex });
            }
            public void SendData(int value) => _remote.Send(_packets.Encode(1, value));
            public void DispatchMessageQueue() { }
            public void Dispose() { _remote.Dispose(); PeerLeft?.Invoke("remote"); Disconnected?.Invoke("disposed"); }
        }
        private sealed class Transport : ISignalingTransport
        {
            public event Action<string> MessageReceived;
            public event Action<string> Disconnected;
            public readonly List<string> Sent = new();
            public bool Disposed;
            public Task ConnectAsync() => Task.CompletedTask;
            public Task SendAsync(string message) { Sent.Add(message); return Task.CompletedTask; }
            public void DispatchMessageQueue() { }
            public void Receive(string message) => MessageReceived?.Invoke(message);
            public void Close() => Disconnected?.Invoke("closed");
            public void Dispose() => Disposed = true;
        }
        private sealed class Client : ISignalingClient
        {
            public event Action<string, bool> PeerJoined;
            public event Action<string> PeerLeft;
            public event Action<string, SignalingMessage> MessageReceived;
            public event Action<string> Disconnected;
            public readonly TaskCompletionSource<bool> Ready = new();
            public bool Disposed;
            public Task ConnectAsync() => Ready.Task;
            public void Send(string id, SignalingMessage message) { }
            public void DispatchMessageQueue() { }
            public void Dispose() => Disposed = true;
            public void Close() => Disconnected?.Invoke("closed");
            public void StaleEvents()
            {
                PeerJoined?.Invoke("stale-peer", true);
                PeerLeft?.Invoke("stale-peer");
                MessageReceived?.Invoke("stale-peer", new SignalingMessage());
            }
        }

        [Test]
        public void DemoAdapterWaitsForMembershipAndTranslatesPeerDiscovery()
        {
            var transport = new Transport();
            using var adapter = new PartyKitSignalingClient(transport, 42, "local", "credential");
            var peers = new List<string>();
            adapter.PeerJoined += (id, initiator) => peers.Add(id + ":" + initiator);
            var connecting = adapter.ConnectAsync();
            Assert.IsFalse(connecting.IsCompleted);
            var join = JsonUtility.FromJson<RoomSignalEnvelope>(transport.Sent[0]);
            Assert.AreEqual("join", join.type);
            Assert.AreEqual("42", join.roomId);
            Assert.AreEqual("local", join.playerId);
            Assert.AreEqual("credential", join.token);
            transport.Receive(@"{""type"":""room-joined"",""peers"":[""existing""],""isInitiator"":true}");
            connecting.GetAwaiter().GetResult();
            transport.Receive(@"{""type"":""peer-joined"",""from"":""new"",""isInitiator"":false}");
            CollectionAssert.AreEqual(new[] { "existing:True", "new:False" }, peers);
        }

        [Test]
        public void DemoAdapterTranslatesNegotiationAndRejectsMessagesForOtherRecipients()
        {
            var transport = new Transport();
            using var adapter = new PartyKitSignalingClient(transport, 42, "local", "credential");
            SignalingMessage received = null;
            string sender = null;
            adapter.MessageReceived += (id, message) => { sender = id; received = message; };
            transport.Receive(@"{""type"":""offer"",""from"":""remote"",""to"":""other"",""sdp"":""ignored""}");
            Assert.IsNull(received);
            transport.Receive(@"{""type"":""offer"",""from"":""remote"",""to"":""local"",""sdp"":""session-description""}");
            Assert.AreEqual("remote", sender);
            Assert.AreEqual(SignalingMessageType.Offer, received.Type);
            Assert.AreEqual("session-description", received.Sdp);
            adapter.Send("remote", new SignalingMessage { Type = SignalingMessageType.IceCandidate,
                Candidate = "candidate-data", SdpMid = "data", SdpMLineIndex = 0 });
            var sent = JsonUtility.FromJson<RoomSignalEnvelope>(transport.Sent[0]);
            Assert.AreEqual("ice-candidate", sent.type);
            Assert.AreEqual("remote", sent.to);
            Assert.AreEqual("candidate-data", sent.candidate);
            Assert.AreEqual("data", sent.sdpMid);
            StringAssert.Contains("\"sdpMLineIndex\":0", transport.Sent[0]);
        }

        [Test]
        public void DemoAdapterFailsPendingMembershipWhenTransportCloses()
        {
            var transport = new Transport();
            using var adapter = new PartyKitSignalingClient(transport, 42, "local", "credential");
            var connecting = adapter.ConnectAsync();
            transport.Close();
            Assert.Throws<InvalidOperationException>(() => connecting.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator InjectedAdapterNegotiatesWebRtcAndTransfersDataInBothDirections()
        {
            var manager = P2PManager.Instance;
            var config = ScriptableObject.CreateInstance<P2PConfig>();
            manager.Initialize(config);
            var client = new LoopbackClient(manager, config);
            var received = 0;
            string sender = null;
            manager.RegisterPacketHandler<int>(1, (id, value) => { sender = id; received = value; });
            try
            {
                var connecting = manager.ConnectAsync("local", client);
                connecting.GetAwaiter().GetResult();
                // A finite test deadline prevents a broken SDP/ICE exchange from hanging the test runner.
                var deadline = Time.realtimeSinceStartup + 15;
                while ((!client.RemoteOpen || manager.ConnectedPeerIds.Count == 0) && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsTrue(client.RemoteOpen, "Remote DataChannel did not open.");
                CollectionAssert.Contains(manager.ConnectedPeerIds, "remote");
                manager.SendTo("remote", 1, 123);
                client.SendData(456);
                while ((client.Received != 123 || received != 456) && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(123, client.Received);
                Assert.AreEqual(456, received);
                Assert.AreEqual("remote", sender);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(manager.gameObject);
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [UnityTest]
        public IEnumerator ManagerAcceptsSignalingWithoutRoomCredentialsOrUrl()
        {
            var manager = P2PManager.Instance;
            var client = new Client();
            try
            {
                var connecting = manager.ConnectAsync("local-peer", client);
                Assert.IsFalse(connecting.IsCompleted);
                client.Ready.SetResult(true);
                yield return null;
                connecting.GetAwaiter().GetResult();
                Assert.AreEqual("local-peer", manager.Session.LocalPeerId);
                Assert.IsTrue(manager.Session.IsSignalingReady);
                Assert.AreEqual(P2PSessionState.WaitingForPeers, manager.Session.State);
                manager.Disconnect();
                Assert.IsTrue(client.Disposed);
                client.StaleEvents();
                Assert.IsFalse(manager.IsSessionActive);
                Assert.IsEmpty(manager.PeerIds);
            }
            finally { UnityEngine.Object.DestroyImmediate(manager.gameObject); }
        }

        [UnityTest]
        public IEnumerator DisconnectCancelsPendingConnectionAndIgnoresOldAdapterCompletion()
        {
            var manager = P2PManager.Instance;
            var previous = new Client();
            var current = new Client();
            try
            {
                var pending = manager.ConnectAsync("previous", previous);
                manager.Disconnect();
                Assert.IsTrue(pending.IsCanceled);
                Assert.IsTrue(previous.Disposed);
                var connecting = manager.ConnectAsync("current", current);
                previous.Ready.SetResult(true);
                previous.StaleEvents();
                yield return null;
                Assert.IsFalse(connecting.IsCompleted);
                Assert.AreEqual("current", manager.Session.LocalPeerId);
                current.Ready.SetResult(true);
                yield return null;
                connecting.GetAwaiter().GetResult();
                previous.Close();
                Assert.IsTrue(manager.IsSessionActive);
            }
            finally { UnityEngine.Object.DestroyImmediate(manager.gameObject); }
        }
    }
}
#endif
