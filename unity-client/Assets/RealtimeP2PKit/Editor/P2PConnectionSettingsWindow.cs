using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit.Editor
{
    /// <summary>
    /// "RealtimeP2PKit &gt; Connection Settings" - edits the Local (for example,
    /// `wrangler dev` on localhost) and Remote (deployed) endpoint sets side by side.
    /// Both sets consist of a matchmaking API URL, a signaling WebSocket URL, and a
    /// STUN server list, saved in P2PConnectionSettings for Editor and Player builds.
    ///
    /// Everything in this window is Editor-only by construction: it lives under an
    /// Editor/-only asmdef and is never compiled into a Player build. The environment
    /// Only environment selection in the Editor and the network logging toggle
    /// are stored per machine in PlayerPrefs.
    /// </summary>
    public class P2PConnectionSettingsWindow : EditorWindow
    {
        private const string SettingsAssetPath = "Assets/RealtimeP2PKit/Resources/P2PConnectionSettings.asset";
        private P2PConnectionSettings _settings;
        private string _localMatchmakingApiUrl;
        private string _localSignalingWebSocketUrl;
        private List<string> _localStunServerUrls;

        private string _remoteMatchmakingApiUrl;
        private string _remoteSignalingWebSocketUrl;
        private List<string> _remoteStunServerUrls;

        private bool _networkLoggingEnabled;

        private Vector2 _scrollPos;

        [MenuItem("RealtimeP2PKit/Connection Settings")]
        private static void Open()
        {
            GetWindow<P2PConnectionSettingsWindow>("P2P Connection Settings");
        }

        private void OnEnable()
        {
            _settings = EnsureAsset();
            LoadFields();
            _networkLoggingEnabled = P2PNetworkLog.IsEnabled;
        }

        public static P2PConnectionSettings EnsureAsset()
        {
            var settings = AssetDatabase.LoadAssetAtPath<P2PConnectionSettings>(SettingsAssetPath);
            if (settings == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/RealtimeP2PKit/Resources"))
                    AssetDatabase.CreateFolder("Assets/RealtimeP2PKit", "Resources");
                settings = CreateInstance<P2PConnectionSettings>();
                AssetDatabase.CreateAsset(settings, SettingsAssetPath);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }

        private void LoadFields()
        {
            if (_settings != null)
            {
                _localMatchmakingApiUrl = _settings.Local.MatchmakingApiUrl;
                _localSignalingWebSocketUrl = _settings.Local.SignalingWebSocketUrl;
                _localStunServerUrls = new List<string>(_settings.Local.StunServerUrls);
                _remoteMatchmakingApiUrl = _settings.Remote.MatchmakingApiUrl;
                _remoteSignalingWebSocketUrl = _settings.Remote.SignalingWebSocketUrl;
                _remoteStunServerUrls = new List<string>(_settings.Remote.StunServerUrls);
                return;
            }
            _localMatchmakingApiUrl = PlayerPrefs.GetString(P2PEndpoints.PrefKeyLocalMatchmakingApiUrl, P2PEndpoints.DefaultLocalMatchmakingApiUrl);
            _localSignalingWebSocketUrl = PlayerPrefs.GetString(P2PEndpoints.PrefKeyLocalSignalingWebSocketUrl, P2PEndpoints.DefaultLocalSignalingWebSocketUrl);
            _localStunServerUrls = P2PEndpoints.LoadStunServerUrls(P2PEndpoints.PrefKeyLocalStunServerUrls);

            _remoteMatchmakingApiUrl = PlayerPrefs.GetString(P2PEndpoints.PrefKeyRemoteMatchmakingApiUrl, P2PEndpoints.DefaultRemoteMatchmakingApiUrl);
            _remoteSignalingWebSocketUrl = PlayerPrefs.GetString(P2PEndpoints.PrefKeyRemoteSignalingWebSocketUrl, P2PEndpoints.DefaultRemoteSignalingWebSocketUrl);
            _remoteStunServerUrls = P2PEndpoints.LoadStunServerUrls(P2PEndpoints.PrefKeyRemoteStunServerUrls);
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            var current = P2PEndpoints.GetCurrentEnvironment();
            var selected = (P2PEnvironment)EditorGUILayout.EnumPopup("Editor environment", current);
            if (selected != current) P2PEndpoints.SetCurrentEnvironment(selected);
            if (_settings != null)
            {
                EditorGUI.BeginChangeCheck();
                _settings.PlayerEnvironment = (P2PEnvironment)EditorGUILayout.EnumPopup("Player build environment", _settings.PlayerEnvironment);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_settings);
                    AssetDatabase.SaveAssets();
                }
            }

            EditorGUILayout.HelpBox(
                "接続先と STUN の設定はプロジェクトのアセットに保存され、Player ビルドにも含まれます。" +
                "ビルド前に Player build environment と Remote の接続先を確認してください。",
                MessageType.Info);
            EditorGUILayout.Space();

            DrawEnvironmentSettings(
                "Local (ローカルサーバー)",
                ref _localMatchmakingApiUrl,
                ref _localSignalingWebSocketUrl,
                _localStunServerUrls);

            EditorGUILayout.Space();
            DrawEnvironmentSettings(
                "Remote (デプロイ済みサーバー)",
                ref _remoteMatchmakingApiUrl,
                ref _remoteSignalingWebSocketUrl,
                _remoteStunServerUrls);

            EditorGUILayout.Space();
            if (GUILayout.Button("Save Local / Remote Settings"))
            {
                SaveFields();
            }

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

        private static void DrawEnvironmentSettings(
            string title,
            ref string matchmakingApiUrl,
            ref string signalingWebSocketUrl,
            List<string> stunServerUrls)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            matchmakingApiUrl = EditorGUILayout.TextField("Web API URL", matchmakingApiUrl);
            signalingWebSocketUrl = EditorGUILayout.TextField("Signaling WebSocket URL", signalingWebSocketUrl);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("STUN Server URLs (上から順に使用)", EditorStyles.boldLabel);
            for (var i = 0; i < stunServerUrls.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                stunServerUrls[i] = EditorGUILayout.TextField(stunServerUrls[i]);
                GUI.enabled = i > 0;
                if (GUILayout.Button("↑", GUILayout.Width(24)))
                {
                    (stunServerUrls[i - 1], stunServerUrls[i]) = (stunServerUrls[i], stunServerUrls[i - 1]);
                }
                GUI.enabled = i < stunServerUrls.Count - 1;
                if (GUILayout.Button("↓", GUILayout.Width(24)))
                {
                    (stunServerUrls[i + 1], stunServerUrls[i]) = (stunServerUrls[i], stunServerUrls[i + 1]);
                }
                GUI.enabled = true;
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    stunServerUrls.RemoveAt(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ Add STUN Server"))
            {
                stunServerUrls.Add("stun:");
            }

            EditorGUILayout.EndVertical();
        }

        private void SaveFields()
        {
            if (_settings != null)
            {
                _settings.Local.MatchmakingApiUrl = _localMatchmakingApiUrl.Trim();
                _settings.Local.SignalingWebSocketUrl = _localSignalingWebSocketUrl.Trim();
                _settings.Local.StunServerUrls = new List<string>(_localStunServerUrls);
                _settings.Remote.MatchmakingApiUrl = _remoteMatchmakingApiUrl.Trim();
                _settings.Remote.SignalingWebSocketUrl = _remoteSignalingWebSocketUrl.Trim();
                _settings.Remote.StunServerUrls = new List<string>(_remoteStunServerUrls);
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
            }
            PlayerPrefs.SetString(P2PEndpoints.PrefKeyLocalMatchmakingApiUrl, _localMatchmakingApiUrl);
            PlayerPrefs.SetString(P2PEndpoints.PrefKeyLocalSignalingWebSocketUrl, _localSignalingWebSocketUrl);
            P2PEndpoints.SaveStunServerUrls(P2PEndpoints.PrefKeyLocalStunServerUrls, _localStunServerUrls);

            PlayerPrefs.SetString(P2PEndpoints.PrefKeyRemoteMatchmakingApiUrl, _remoteMatchmakingApiUrl);
            PlayerPrefs.SetString(P2PEndpoints.PrefKeyRemoteSignalingWebSocketUrl, _remoteSignalingWebSocketUrl);
            P2PEndpoints.SaveStunServerUrls(P2PEndpoints.PrefKeyRemoteStunServerUrls, _remoteStunServerUrls);
            PlayerPrefs.Save();
        }
    }
}
