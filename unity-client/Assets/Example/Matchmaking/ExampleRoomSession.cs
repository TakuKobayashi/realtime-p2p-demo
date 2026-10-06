using UnityEngine;
using UnityEngine.Localization;

namespace net.taptappun.RealtimeP2PKit.Example.Matchmaking
{
    /// <summary>Example-only handoff across scenes. No Unity object or WebRTC connection is recreated during handoff.</summary>
    public static class ExampleRoomSession
    {
        public const string MatchingScene = "MatchingRoomExample";
        public const string GameplayScene = "P2PExample";
        public static ExamplePlayerSession Player;
        public static MachingRoom Room;
        public static string HttpBaseUrl;
        public static string SignalingWebSocketUrl;
        public static LocalizedString Notice;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Player = null;
            Room = null;
            HttpBaseUrl = null;
            SignalingWebSocketUrl = null;
            Notice = null;
        }
    }
}
