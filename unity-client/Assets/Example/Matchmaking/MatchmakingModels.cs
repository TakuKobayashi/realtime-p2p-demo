using System;

namespace PhantomCatWorks.RealtimeP2PKit.Example.Matchmaking
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
        public string id;
        public int maxPlayers; // 0 = unlimited
        public int memberCount;
        public long createdAt;
    }
}
