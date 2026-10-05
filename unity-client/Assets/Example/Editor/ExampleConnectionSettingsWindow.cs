using System;
using UnityEditor;
using UnityEngine;

namespace net.taptappun.RealtimeP2PKit.Example.Editor
{
    public sealed class ExampleConnectionSettingsWindow : EditorWindow
    {
        private const string SettingsAssetPath = "Assets/Example/Resources/ExampleConnectionSettings.asset";
        private ExampleConnectionSettings _settings;
        private SerializedObject _serializedSettings;

        [MenuItem("RealtimeP2PKit/Example Connection Settings")]
        private static void Open() => GetWindow<ExampleConnectionSettingsWindow>("Example Connections");

        private void OnEnable()
        {
            _settings = EnsureAsset();
            _serializedSettings = new SerializedObject(_settings);
        }

        public static ExampleConnectionSettings EnsureAsset()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ExampleConnectionSettings>(SettingsAssetPath);
            if (settings != null) return settings;
            if (!AssetDatabase.IsValidFolder("Assets/Example/Resources"))
                AssetDatabase.CreateFolder("Assets/Example", "Resources");
            settings = CreateInstance<ExampleConnectionSettings>();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private void OnGUI()
        {
            if (_settings == null) OnEnable();
            var current = P2PEndpoints.GetCurrentEnvironment();
            var selected = (P2PEnvironment)EditorGUILayout.EnumPopup("Environment to edit", current);
            if (selected != current)
            {
                P2PEndpoints.SetCurrentEnvironment(selected);
                PlayerPrefs.Save();
            }

            _serializedSettings.Update();
            var endpoints = _serializedSettings.FindProperty(selected == P2PEnvironment.Local ? "Local" : "Remote");
            EditorGUILayout.LabelField(selected.ToString(), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(endpoints.FindPropertyRelative("HttpBaseUrl"), new GUIContent("HTTP Base URL"));
            EditorGUILayout.PropertyField(endpoints.FindPropertyRelative("WebSocketBaseUrl"), new GUIContent("WebSocket Base URL"));
            _serializedSettings.ApplyModifiedProperties();

            var valid = IsValid(_settings.Local) && IsValid(_settings.Remote);
            if (!valid)
                EditorGUILayout.HelpBox("接続先のURL形式が正しくありません。", MessageType.Error);
            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button("Save Local / Remote Settings"))
                {
                    Undo.RecordObject(_settings, "Save Example Connection Settings");
                    Normalize(_settings.Local);
                    Normalize(_settings.Remote);
                    EditorUtility.SetDirty(_settings);
                    AssetDatabase.SaveAssets();
                }
            }
        }

        private static bool IsValid(ExampleConnectionSettings.EndpointSet endpoints)
            => endpoints != null && IsUrl(endpoints.HttpBaseUrl, "http", "https") &&
               IsUrl(endpoints.WebSocketBaseUrl, "ws", "wss");

        private static bool IsUrl(string value, string scheme, string secureScheme)
            => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) &&
               !string.IsNullOrEmpty(uri.Host) && (uri.Scheme == scheme || uri.Scheme == secureScheme);

        private static void Normalize(ExampleConnectionSettings.EndpointSet endpoints)
        {
            endpoints.HttpBaseUrl = endpoints.HttpBaseUrl.Trim().TrimEnd('/');
            endpoints.WebSocketBaseUrl = endpoints.WebSocketBaseUrl.Trim().TrimEnd('/');
        }
    }
}
