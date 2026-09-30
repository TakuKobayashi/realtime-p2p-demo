using System;

namespace PhantomCatWorks.RealtimeP2PKit
{
    [Serializable]
    public class MatchmakingResult
    {
        public string status; // "waiting" | "matched"
        public string roomId;
        public string opponentId;
        public bool isInitiator;
    }

    [Serializable]
    internal class MatchmakingJoinRequest
    {
        public string playerId;
    }

    [Serializable]
    public class MachingRoom
    {
        public string id;
        public string hostPlayerId;
        public string guestPlayerId;
        public string status; // "waiting" | "matched"
        public long createdAt;
    }
}
