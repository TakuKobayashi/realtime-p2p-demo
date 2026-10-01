using System;
using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit.Example
{
    /// <summary>Example-only matchmaking endpoints, included in Player builds through Resources.</summary>
    [CreateAssetMenu(menuName = "RealtimeP2PKit/Example Connection Settings", fileName = "ExampleConnectionSettings")]
    public sealed class ExampleConnectionSettings : ScriptableObject
    {
        [Serializable]
        public sealed class EndpointSet
        {
            public string HttpBaseUrl = "http://localhost:8787";
            public string WebSocketBaseUrl = "ws://localhost:8787";
        }

        public EndpointSet Local = new EndpointSet();
        public EndpointSet Remote = new EndpointSet();
    }

    public static class ExampleEndpoints
    {
        private static ExampleConnectionSettings.EndpointSet Current
        {
            get
            {
                var settings = Resources.Load<ExampleConnectionSettings>("ExampleConnectionSettings");
                if (settings == null) return new ExampleConnectionSettings.EndpointSet();
                return P2PEndpoints.GetCurrentEnvironment() == P2PEnvironment.Local
                    ? settings.Local : settings.Remote;
            }
        }

        public static string GetHttpBaseUrl() => Current.HttpBaseUrl;
        public static string GetWebSocketBaseUrl() => Current.WebSocketBaseUrl;
    }
}
