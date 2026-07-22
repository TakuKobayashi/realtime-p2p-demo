using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit.Editor
{
    /// <summary>
    /// "RealtimeP2PKit &gt; Connection Settings" - lets a developer switch between a
    /// "Local" (e.g. `wrangler dev` on localhost) and "Remote" (deployed) backend via a
    /// dropdown, editing only the URLs for whichever one is currently selected (matchmaking
    /// API URL / signaling WebSocket URL / STUN server list), all persisted per-machine in
    /// PlayerPrefs (see P2PEndpoints).
    ///
    /// Everything in this window is Editor-only by construction: it lives under an
    /// Editor/-only asmdef and is never compiled into a Player build. The environment
    /// switch and the "Network Logging" toggle it also exposes both read/write
    /// PlayerPrefs directly through P2PEndpoints / P2PNetworkLog, which themselves
    /// only honor PlayerPrefs when UNITY_EDITOR is defined - so even code that isn't in
    /// this window can't accidentally end up reading a stale Editor-only setting at
    /// runtime in a build.
    /// </summary>
    public class P2PConnectionSettingsWindow : EditorWindow
    {
        private P2PEnvironment _environment;

        // Working copy of whichever environment is currently selected - reloaded from
        // PlayerPrefs every time the dropdown switches, so Local and Remote never show
        // (or get edited) at the same time.
        private string _matchmakingApiUrl;
        private string _signalingWebSocketUrl;
        private List<string> _stunServerUrls;

        private bool _networkLoggingEnabled;

        private Vector2 _scrollPos;

        [MenuItem("RealtimeP2PKit/Connection Settings")]
        private static void Open()
        {
            GetWindow<P2PConnectionSettingsWindow>("P2P Connection Settings");
        }

        private void OnEnable()
        {
            _environment = P2PEndpoints.GetCurrentEnvironment();
            LoadFieldsForCurrentEnvironment();
            _networkLoggingEnabled = P2PNetworkLog.IsEnabled;
        }

        private void LoadFieldsForCurrentEnvironment()
        {
            if (_environment == P2PEnvironment.Local)
            {
                _matchmakingApiUrl = PlayerPrefs.GetString(P2PEndpoints.PrefKeyLocalMatchmakingApiUrl, P2PEndpoints.DefaultLocalMatchmakingApiUrl);
                _signalingWebSocketUrl = PlayerPrefs.GetString(P2PEndpoints.PrefKeyLocalSignalingWebSocketUrl, P2PEndpoints.DefaultLocalSignalingWebSocketUrl);
                _stunServerUrls = P2PEndpoints.LoadStunServerUrls(P2PEndpoints.PrefKeyLocalStunServerUrls);
            }
            else
            {
                _matchmakingApiUrl = PlayerPrefs.GetString(P2PEndpoints.PrefKeyRemoteMatchmakingApiUrl, P2PEndpoints.DefaultRemoteMatchmakingApiUrl);
                _signalingWebSocketUrl = PlayerPrefs.GetString(P2PEndpoints.PrefKeyRemoteSignalingWebSocketUrl, P2PEndpoints.DefaultRemoteSignalingWebSocketUrl);
                _stunServerUrls = P2PEndpoints.LoadStunServerUrls(P2PEndpoints.PrefKeyRemoteStunServerUrls);
            }
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorGUILayout.HelpBox(
                "この画面の設定はUnityEditor上でのみ有効です。ビルドしたアプリは常にRemoteの" +
                "ハードコードされた既定値(P2PEndpoints.DefaultRemote*)を使用し、ここで保存した値は参照しません。",
                MessageType.Info);
            EditorGUILayout.Space();

            // --- Environment dropdown: switching this changes which fields are shown below ---
            EditorGUILayout.LabelField("接続先", EditorStyles.boldLabel);
            var newEnv = (P2PEnvironment)EditorGUILayout.EnumPopup("Environment", _environment);
            if (newEnv != _environment)
            {
                _environment = newEnv;
                P2PEndpoints.SetCurrentEnvironment(_environment);
                LoadFieldsForCurrentEnvironment(); // reload so the fields below match the new selection
                GUI.FocusControl(null);
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(GUI.skin.box);

            // --- Only the SELECTED environment's fields are shown/editable here ---
            EditorGUILayout.LabelField($"{_environment} の設定", EditorStyles.boldLabel);
            _matchmakingApiUrl = EditorGUILayout.TextField("Web API URL", _matchmakingApiUrl);
            _signalingWebSocketUrl = EditorGUILayout.TextField("Signaling WebSocket URL", _signalingWebSocketUrl);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("STUN Server URLs (上から順に使用)", EditorStyles.boldLabel);
            for (var i = 0; i < _stunServerUrls.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                _stunServerUrls[i] = EditorGUILayout.TextField(_stunServerUrls[i]);
                GUI.enabled = i > 0;
                if (GUILayout.Button("↑", GUILayout.Width(24)))
                {
                    (_stunServerUrls[i - 1], _stunServerUrls[i]) = (_stunServerUrls[i], _stunServerUrls[i - 1]);
                }
                GUI.enabled = i < _stunServerUrls.Count - 1;
                if (GUILayout.Button("↓", GUILayout.Width(24)))
                {
                    (_stunServerUrls[i + 1], _stunServerUrls[i]) = (_stunServerUrls[i], _stunServerUrls[i + 1]);
                }
                GUI.enabled = true;
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    _stunServerUrls.RemoveAt(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ Add STUN Server"))
            {
                _stunServerUrls.Add("stun:");
            }

            EditorGUILayout.Space();
            if (GUILayout.Button($"Save {_environment}"))
            {
                SaveCurrentEnvironmentFields();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("実際に参照されるURL(保存済みの値)", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("Web API: " + P2PEndpoints.GetMatchmakingApiUrl(), EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Signaling: " + P2PEndpoints.GetSignalingWebSocketUrl(), EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Network Logging", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "HTTP(マッチングAPI)/WebSocket(シグナリング)/WebRTC DataChannelの送受信内容を" +
                "そのままログ出力します。UnityEditor上でのみON/OFFを切り替えられ、この設定自体もビルドには" +
                "含まれません(ビルドしたアプリでは常にOFFです)。",
                MessageType.None);
            var newLogValue = EditorGUILayout.Toggle("Enable Network Logging", _networkLoggingEnabled);
            if (newLogValue != _networkLoggingEnabled)
            {
                _networkLoggingEnabled = newLogValue;
                P2PNetworkLog.IsEnabled = _networkLoggingEnabled;
            }

            EditorGUILayout.EndScrollView();
        }

        private void SaveCurrentEnvironmentFields()
        {
            if (_environment == P2PEnvironment.Local)
            {
                PlayerPrefs.SetString(P2PEndpoints.PrefKeyLocalMatchmakingApiUrl, _matchmakingApiUrl);
                PlayerPrefs.SetString(P2PEndpoints.PrefKeyLocalSignalingWebSocketUrl, _signalingWebSocketUrl);
                P2PEndpoints.SaveStunServerUrls(P2PEndpoints.PrefKeyLocalStunServerUrls, _stunServerUrls);
            }
            else
            {
                PlayerPrefs.SetString(P2PEndpoints.PrefKeyRemoteMatchmakingApiUrl, _matchmakingApiUrl);
                PlayerPrefs.SetString(P2PEndpoints.PrefKeyRemoteSignalingWebSocketUrl, _signalingWebSocketUrl);
                P2PEndpoints.SaveStunServerUrls(P2PEndpoints.PrefKeyRemoteStunServerUrls, _stunServerUrls);
            }
            PlayerPrefs.Save();
        }
    }
}
