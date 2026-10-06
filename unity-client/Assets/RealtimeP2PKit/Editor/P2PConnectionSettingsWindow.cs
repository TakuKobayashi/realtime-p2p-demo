using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace net.taptappun.RealtimeP2PKit.Editor
{
    /// <summary>
    /// Edits the signaling WebSocket URL and STUN servers for the selected environment.
    /// Endpoint values are saved in a Resources asset for Editor and Player builds.
    /// </summary>
    public class P2PConnectionSettingsWindow : EditorWindow
    {
        private const string SettingsAssetPath = "Assets/Resources/P2PConnectionSettings.asset";
        private P2PConnectionSettings _settings;
        private string _localSignalingWebSocketUrl;
        private List<string> _localStunServerUrls;

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
                if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                    AssetDatabase.CreateFolder("Assets", "Resources");
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
                _localSignalingWebSocketUrl = _settings.Local.SignalingWebSocketUrl;
                _localStunServerUrls = new List<string>(_settings.Local.StunServerUrls);
                _remoteSignalingWebSocketUrl = _settings.Remote.SignalingWebSocketUrl;
                _remoteStunServerUrls = new List<string>(_settings.Remote.StunServerUrls);
                return;
            }
            _localSignalingWebSocketUrl = PlayerPrefs.GetString(P2PEndpoints.PrefKeyLocalSignalingWebSocketUrl, P2PEndpoints.DefaultLocalSignalingWebSocketUrl);
            _localStunServerUrls = P2PEndpoints.LoadStunServerUrls(P2PEndpoints.PrefKeyLocalStunServerUrls);

            _remoteSignalingWebSocketUrl = PlayerPrefs.GetString(P2PEndpoints.PrefKeyRemoteSignalingWebSocketUrl, P2PEndpoints.DefaultRemoteSignalingWebSocketUrl);
            _remoteStunServerUrls = P2PEndpoints.LoadStunServerUrls(P2PEndpoints.PrefKeyRemoteStunServerUrls);
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            var current = P2PEndpoints.GetCurrentEnvironment();
            var selected = (P2PEnvironment)EditorGUILayout.EnumPopup("Environment to edit", current);
            if (selected != current) P2PEndpoints.SetCurrentEnvironment(selected);

            EditorGUILayout.Space();

            if (selected == P2PEnvironment.Local)
                DrawEnvironmentSettings("Local", ref _localSignalingWebSocketUrl, _localStunServerUrls);
            else
                DrawEnvironmentSettings("Remote", ref _remoteSignalingWebSocketUrl, _remoteStunServerUrls);

            EditorGUILayout.Space();
            if (GUILayout.Button("Save Local / Remote Settings"))
            {
                SaveFields();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Network Logging", EditorStyles.boldLabel);
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
            ref string signalingWebSocketUrl,
            List<string> stunServerUrls)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            signalingWebSocketUrl = EditorGUILayout.TextField("Signaling WebSocket URL", signalingWebSocketUrl);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("STUN Server URLs", EditorStyles.boldLabel);
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
                var remove = GUILayout.Button("✕", GUILayout.Width(24));
                EditorGUILayout.EndHorizontal();
                if (remove)
                {
                    stunServerUrls.RemoveAt(i);
                    break;
                }
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
                _settings.Local.SignalingWebSocketUrl = _localSignalingWebSocketUrl.Trim();
                _settings.Local.StunServerUrls = new List<string>(_localStunServerUrls);
                _settings.Remote.SignalingWebSocketUrl = _remoteSignalingWebSocketUrl.Trim();
                _settings.Remote.StunServerUrls = new List<string>(_remoteStunServerUrls);
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
            }
            PlayerPrefs.SetString(P2PEndpoints.PrefKeyLocalSignalingWebSocketUrl, _localSignalingWebSocketUrl);
            P2PEndpoints.SaveStunServerUrls(P2PEndpoints.PrefKeyLocalStunServerUrls, _localStunServerUrls);

            PlayerPrefs.SetString(P2PEndpoints.PrefKeyRemoteSignalingWebSocketUrl, _remoteSignalingWebSocketUrl);
            P2PEndpoints.SaveStunServerUrls(P2PEndpoints.PrefKeyRemoteStunServerUrls, _remoteStunServerUrls);
            PlayerPrefs.Save();
        }
    }
}
