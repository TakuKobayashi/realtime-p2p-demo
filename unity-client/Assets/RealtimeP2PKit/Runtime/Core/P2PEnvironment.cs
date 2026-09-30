namespace PhantomCatWorks.RealtimeP2PKit
{
    /// <summary>
    /// Which backend deployment to talk to. Only meaningful inside the Unity Editor
    /// (see P2PEndpoints) - a Player build always behaves as if this were Remote.
    /// </summary>
    public enum P2PEnvironment
    {
        Local = 0,
        Remote = 1,
    }
}
