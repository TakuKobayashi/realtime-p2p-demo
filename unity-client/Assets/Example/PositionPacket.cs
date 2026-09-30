using MessagePack;

namespace PhantomCatWorks.RealtimeP2PKit.Example
{
    [MessagePackObject]
    public struct PositionPacket
    {
        [Key(0)] public float X;
        [Key(1)] public float Y;
        [Key(2)] public float Z;
    }
}
