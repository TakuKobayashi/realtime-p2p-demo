using UnityEngine.Localization;

namespace net.taptappun.RealtimeP2PKit.Example
{
    // The sample owns its translations. The networking library has no locale or table dependency.
    public static class ExampleLocalization
    {
        public const string TableName = "Example UI";
        public static LocalizedString Message(string key, params object[] arguments)
            => new LocalizedString(TableName, key) { Arguments = arguments };
    }
}
