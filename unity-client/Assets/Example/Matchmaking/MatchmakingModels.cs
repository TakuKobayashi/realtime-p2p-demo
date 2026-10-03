using System;

namespace net.taptappun.RealtimeP2PKit.Example.Matchmaking
{
    [Serializable]
    public sealed class ExamplePlayerSession
    {
        public string id;
        public string token;
    }
    [Serializable]
    public sealed class MachingRoom
    {
        public long id;
        public int maxPlayers; // 0 = unlimited
        public int memberCount;
        public long createdAt;
    }
}
