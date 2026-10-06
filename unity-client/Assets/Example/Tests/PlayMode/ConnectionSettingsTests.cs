#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;
using net.taptappun.RealtimeP2PKit.Example.Signaling;

namespace net.taptappun.RealtimeP2PKit.Example.Tests
{
    public sealed class ConnectionSettingsTests
    {
        [TestCase("wss://signaling.example.com/custom/session/42", "/custom/session/42", "")]
        [TestCase("ws://localhost:8787/nested/session/?mode=demo%20test", "/nested/session/", "mode=demo%20test&")]
        public void FullUrlPreservesPathAndQuery(string url, string path, string queryPrefix)
        {
            using var client = new PartyKitSignalingClient(url, "player /+", "token?&=");
            var build = typeof(PartyKitSignalingClient).GetMethod("BuildConnectionUrl", BindingFlags.Static | BindingFlags.NonPublic);
            var result = new Uri((string)build.Invoke(null, new object[] { url, "player /+", "token?&=" }));
            var original = new Uri(url);
            Assert.AreEqual(original.Scheme, result.Scheme);
            Assert.AreEqual(original.Host, result.Host);
            Assert.AreEqual(original.Port, result.Port);
            Assert.AreEqual(path, result.AbsolutePath);
            Assert.AreEqual("?" + queryPrefix + "playerId=player%20%2F%2B&token=token%3F%26%3D", result.Query);
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
