#if UNITY_EDITOR
using System;
using NUnit.Framework;

namespace net.taptappun.RealtimeP2PKit.Example.Tests
{
    public sealed class ConnectionSettingsTests
    {
        [TestCase("wss://signaling.example.com/custom/path?mode=test")]
        [TestCase("ws://localhost:8787/signaling")]
        public void CompleteUrlNeedsNoRoomTemplateOrCredentials(string url)
        {
            using var transport = new WebSocketSignalingTransport(url);
            var configuredUrl = typeof(WebSocketSignalingTransport).GetField("_url",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.AreEqual(url, configuredUrl.GetValue(transport));
        }

        [TestCase("https://signaling.example.com/session")]
        [TestCase("/relative/session")]
        [TestCase("wss://signaling.example.com/session#fragment")]
        public void InvalidWebSocketUrlIsRejected(string url)
            => Assert.Throws<ArgumentException>(() => new WebSocketSignalingTransport(url));

        [Test]
        public void EditorEnvironmentSelectionRemainsAvailable()
        {
            var previous = P2PEndpoints.GetCurrentEnvironment();
            try
            {
                P2PEndpoints.SetCurrentEnvironment(P2PEnvironment.Local);
                Assert.AreEqual(P2PEnvironment.Local, P2PEndpoints.GetCurrentEnvironment());
                P2PEndpoints.SetCurrentEnvironment(P2PEnvironment.Remote);
                Assert.AreEqual(P2PEnvironment.Remote, P2PEndpoints.GetCurrentEnvironment());
            }
            finally { P2PEndpoints.SetCurrentEnvironment(previous); }
        }
    }
}
#endif
