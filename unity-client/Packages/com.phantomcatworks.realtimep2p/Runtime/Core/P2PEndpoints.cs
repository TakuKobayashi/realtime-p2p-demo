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
    /// and are persisted per-machine in PlayerPrefs.
    ///
    /// This PlayerPrefs-based switching ONLY applies inside the Unity Editor. In any
    /// Player build, none of the PlayerPrefs values are read at all - GetCurrentEnvironment()
    /// always returns Remote, and every Get*Url() method always returns the hardcoded
    /// "Default*" constant below, regardless of what was last saved in the Editor. This is
    /// intentional: PlayerPrefs entries set by a developer's own Editor session obviously
    /// don't exist on an end user's machine, so a shipped build must never depend on them.
    /// </summary>
    public static class P2PEndpoints
    {
        // -----------------------------------------------------------------
        // Hardcoded defaults.
        //  - Used as the Remote values in EVERY Player build (see class doc above).
        //  - Also used as the pre-filled starting values shown in the Editor window
        //    the first time it's opened (before anything has been Saved).
        // -----------------------------------------------------------------

        public const string DefaultLocalMatchmakingApiUrl = "http://localhost:8787";
        public const string DefaultLocalSignalingWebSocketUrl = "ws://localhost:8787";

        // TODO: replace with your actual deployed endpoint (see /server, `pnpm deploy`).
        public const string DefaultRemoteMatchmakingApiUrl = "https://realtime-p2p-server.example.workers.dev";
        public const string DefaultRemoteSignalingWebSocketUrl = "wss://realtime-p2p-server.example.workers.dev";

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
            return P2PEnvironment.Remote;
#endif
        }

        public static void SetCurrentEnvironment(P2PEnvironment environment)
        {
#if UNITY_EDITOR
            PlayerPrefs.SetInt(PrefKeyEnvironment, (int)environment);
#endif
        }

        public static string GetMatchmakingApiUrl()
        {
#if UNITY_EDITOR
            return GetCurrentEnvironment() == P2PEnvironment.Local
                ? PlayerPrefs.GetString(PrefKeyLocalMatchmakingApiUrl, DefaultLocalMatchmakingApiUrl)
                : PlayerPrefs.GetString(PrefKeyRemoteMatchmakingApiUrl, DefaultRemoteMatchmakingApiUrl);
#else
            return DefaultRemoteMatchmakingApiUrl;
#endif
        }

        public static string GetSignalingWebSocketUrl()
        {
#if UNITY_EDITOR
            return GetCurrentEnvironment() == P2PEnvironment.Local
                ? PlayerPrefs.GetString(PrefKeyLocalSignalingWebSocketUrl, DefaultLocalSignalingWebSocketUrl)
                : PlayerPrefs.GetString(PrefKeyRemoteSignalingWebSocketUrl, DefaultRemoteSignalingWebSocketUrl);
#else
            return DefaultRemoteSignalingWebSocketUrl;
#endif
        }

        /// <summary>Ordered list of STUN server URLs to use for ICE gathering.</summary>
        public static List<string> GetStunServerUrls()
        {
#if UNITY_EDITOR
            var key = GetCurrentEnvironment() == P2PEnvironment.Local ? PrefKeyLocalStunServerUrls : PrefKeyRemoteStunServerUrls;
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
