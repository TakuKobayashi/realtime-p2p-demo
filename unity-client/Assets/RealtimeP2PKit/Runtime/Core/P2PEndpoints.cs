using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit
{
    /// <summary>
    /// Resolves which matchmaking API / signaling WebSocket / STUN servers to connect to.
    ///
    /// Two named environments are supported, "Local" and "Remote". Which one is active,
    /// and each environment's own set of URLs, are editable at any time from the Unity
    /// Editor via P2PConnectionSettingsWindow ("RealtimeP2PKit &gt; Connection Settings"),
    /// and are saved to a Resources asset shared with Player builds.
    ///
    /// Editor environment selection is per machine. Player builds use the environment
    /// selected in P2PConnectionSettings, included in Resources.
    /// </summary>
    public static class P2PEndpoints
    {
        private static P2PConnectionSettings Settings => Resources.Load<P2PConnectionSettings>("P2PConnectionSettings");

        // -----------------------------------------------------------------
        // Hardcoded defaults.
        //  - Used as the Remote values in EVERY Player build (see class doc above).
        //  - Also used as the pre-filled starting values shown in the Editor window
        //    the first time it's opened (before anything has been Saved).
        // -----------------------------------------------------------------

        public const string DefaultLocalMatchmakingApiUrl = "http://localhost:8787";
        public const string DefaultLocalSignalingWebSocketUrl = "ws://localhost:8787";

        // TODO: replace with your actual deployed endpoint (see /server, `pnpm deploy`).
        public const string DefaultRemoteMatchmakingApiUrl = "http://localhost:8787";
        public const string DefaultRemoteSignalingWebSocketUrl = "ws://localhost:8787";

        /// <summary>
        /// Public STUN servers, tried in order (Unity.WebRTC gathers ICE candidates from
        /// all of them). No TURN server is used - see the repo README for that trade-off.
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultStunServerUrls = new[]
        {
            "stun:stun.l.google.com:19302",
            "stun:stun1.l.google.com:19302",
            "stun:stun.services.mozilla.com:3478",
        };

        // -----------------------------------------------------------------
        // PlayerPrefs keys (Editor-only usage - see class doc above)
        // -----------------------------------------------------------------

        public const string PrefKeyEnvironment = "RealtimeP2PKit.Environment";
        public const string PrefKeyLocalMatchmakingApiUrl = "RealtimeP2PKit.Local.MatchmakingApiUrl";
        public const string PrefKeyLocalSignalingWebSocketUrl = "RealtimeP2PKit.Local.SignalingWebSocketUrl";
        public const string PrefKeyLocalStunServerUrls = "RealtimeP2PKit.Local.StunServerUrls";
        public const string PrefKeyRemoteMatchmakingApiUrl = "RealtimeP2PKit.Remote.MatchmakingApiUrl";
        public const string PrefKeyRemoteSignalingWebSocketUrl = "RealtimeP2PKit.Remote.SignalingWebSocketUrl";
        public const string PrefKeyRemoteStunServerUrls = "RealtimeP2PKit.Remote.StunServerUrls";

        /// <summary>Multiple STUN URLs are stored as one PlayerPrefs string, newline-joined.</summary>
        private const char ListDelimiter = '\n';

        // -----------------------------------------------------------------
        // Public resolution API - this is what P2PManager actually calls.
        // -----------------------------------------------------------------

        public static P2PEnvironment GetCurrentEnvironment()
        {
#if UNITY_EDITOR
            return (P2PEnvironment)PlayerPrefs.GetInt(PrefKeyEnvironment, (int)P2PEnvironment.Local);
#else
            return Settings != null ? Settings.PlayerEnvironment : P2PEnvironment.Remote;
#endif
        }

        public static void SetCurrentEnvironment(P2PEnvironment environment)
        {
#if UNITY_EDITOR
            PlayerPrefs.SetInt(PrefKeyEnvironment, (int)environment);
#endif
        }

        public static string GetMatchmakingApiUrl()
            => GetMatchmakingApiUrl(GetCurrentEnvironment());

        public static string GetMatchmakingApiUrl(P2PEnvironment environment)
        {
            var configured = Settings;
            if (configured != null)
                return environment == P2PEnvironment.Local ? configured.Local.MatchmakingApiUrl : configured.Remote.MatchmakingApiUrl;
#if UNITY_EDITOR
            return environment == P2PEnvironment.Local
                ? PlayerPrefs.GetString(PrefKeyLocalMatchmakingApiUrl, DefaultLocalMatchmakingApiUrl)
                : PlayerPrefs.GetString(PrefKeyRemoteMatchmakingApiUrl, DefaultRemoteMatchmakingApiUrl);
#else
            return DefaultRemoteMatchmakingApiUrl;
#endif
        }

        public static string GetSignalingWebSocketUrl()
            => GetSignalingWebSocketUrl(GetCurrentEnvironment());

        public static string GetSignalingWebSocketUrl(P2PEnvironment environment)
        {
            var configured = Settings;
            if (configured != null)
                return environment == P2PEnvironment.Local ? configured.Local.SignalingWebSocketUrl : configured.Remote.SignalingWebSocketUrl;
#if UNITY_EDITOR
            return environment == P2PEnvironment.Local
                ? PlayerPrefs.GetString(PrefKeyLocalSignalingWebSocketUrl, DefaultLocalSignalingWebSocketUrl)
                : PlayerPrefs.GetString(PrefKeyRemoteSignalingWebSocketUrl, DefaultRemoteSignalingWebSocketUrl);
#else
            return DefaultRemoteSignalingWebSocketUrl;
#endif
        }

        /// <summary>Ordered list of STUN server URLs to use for ICE gathering.</summary>
        public static List<string> GetStunServerUrls()
            => GetStunServerUrls(GetCurrentEnvironment());

        public static List<string> GetStunServerUrls(P2PEnvironment environment)
        {
            var configured = Settings;
            if (configured != null)
            {
                var urls = environment == P2PEnvironment.Local ? configured.Local.StunServerUrls : configured.Remote.StunServerUrls;
                return urls.Where(url => !string.IsNullOrWhiteSpace(url)).Select(url => url.Trim()).ToList();
            }
#if UNITY_EDITOR
            var key = environment == P2PEnvironment.Local ? PrefKeyLocalStunServerUrls : PrefKeyRemoteStunServerUrls;
            return LoadStunServerUrls(key);
#else
            return DefaultStunServerUrls.ToList();
#endif
        }

#if UNITY_EDITOR
        /// <summary>Editor-only: reads a saved STUN list, falling back to the shared defaults if unset.</summary>
        public static List<string> LoadStunServerUrls(string prefKey)
        {
            var raw = PlayerPrefs.GetString(prefKey, null);
            if (string.IsNullOrEmpty(raw)) return DefaultStunServerUrls.ToList();
            return raw.Split(ListDelimiter)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        /// <summary>Editor-only: persists a STUN list under the given PlayerPrefs key.</summary>
        public static void SaveStunServerUrls(string prefKey, List<string> urls)
        {
            var cleaned = urls.Select(s => s.Trim()).Where(s => s.Length > 0);
            PlayerPrefs.SetString(prefKey, string.Join(ListDelimiter, cleaned));
        }
#endif
    }
}
