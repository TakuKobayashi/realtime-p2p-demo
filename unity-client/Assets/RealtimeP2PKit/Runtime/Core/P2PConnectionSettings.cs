using System;
using System.Collections.Generic;
using UnityEngine;

namespace net.taptappun.RealtimeP2PKit
{
    [CreateAssetMenu(menuName = "RealtimeP2PKit/Connection Settings", fileName = "P2PConnectionSettings")]
    public sealed class P2PConnectionSettings : ScriptableObject
    {
        [Serializable]
        public sealed class EndpointSet
        {
            public string SignalingWebSocketUrl = "ws://localhost:8787";
            public List<string> StunServerUrls = new List<string>
            {
                "stun:stun.l.google.com:19302",
                "stun:stun1.l.google.com:19302",
            };
        }

        public P2PEnvironment PlayerEnvironment = P2PEnvironment.Remote;
        public EndpointSet Local = new EndpointSet();
        public EndpointSet Remote = new EndpointSet();
    }
}
