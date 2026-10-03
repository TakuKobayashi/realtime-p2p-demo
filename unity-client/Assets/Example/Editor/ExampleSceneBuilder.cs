using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit.Example.Editor
{
    public static class ExampleSceneBuilder
    {
        [MenuItem("RealtimeP2PKit/Build Example Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Main Camera");
            camera.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 8, -8);
            camera.transform.LookAt(Vector3.zero);
            camera.tag = "MainCamera";

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2, 1, 2);

            var local = GameObject.CreatePrimitive(PrimitiveType.Cube);
            local.name = "LocalPlayer";
            ApplyColor(local, Color.cyan);
            local.AddComponent<ExamplePlayerController>();
            var localPrefab = SavePrefab(local, "LocalPlayer");
            Object.DestroyImmediate(local);

            var remote = GameObject.CreatePrimitive(PrimitiveType.Cube);
            remote.name = "RemotePlayer";
            ApplyColor(remote, Color.magenta);
            var remotePrefab = SavePrefab(remote, "RemotePlayer");
            Object.DestroyImmediate(remote);

            var bootstrap = new GameObject("P2P Example").AddComponent<ExampleBootstrap>();
            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("_config").objectReferenceValue = LoadOrCreateConfig();
            serialized.FindProperty("_localPlayerPrefab").objectReferenceValue = localPrefab;
            serialized.FindProperty("_remotePlayerPrefab").objectReferenceValue = remotePrefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ExampleUiBuilder.AddGameplayUi(bootstrap);
            ExampleUiBuilder.EnsureEventSystem();

            EnsureFolder("Assets/Example/Scenes");
            const string scenePath = "Assets/Example/Scenes/P2PExample.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            var matchingScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var matchingController = new GameObject("Room Matching").AddComponent<MatchingRoomExampleController>();
            ExampleUiBuilder.AddMatchingUi(matchingController);
            ExampleUiBuilder.EnsureEventSystem();
            const string matchingPath = "Assets/Example/Scenes/MatchingRoomExample.unity";
            EditorSceneManager.SaveScene(matchingScene, matchingPath);
            EditorBuildSettings.scenes = new[] {
                new EditorBuildSettingsScene(matchingPath, true),
                new EditorBuildSettingsScene(scenePath, true),
            };
            Debug.Log("[P2P Example] Built matching and gameplay scenes. Configure STUN in Connection Settings and HTTP/Room WebSocket in Example Connection Settings.");
        }

        private static void ApplyColor(GameObject go, Color color)
        {
            EnsureFolder("Assets/Example/Materials");
            var path = $"Assets/Example/Materials/{go.name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static GameObject SavePrefab(GameObject go, string name)
        {
            EnsureFolder("Assets/Example/Prefabs");
            return PrefabUtility.SaveAsPrefabAsset(go, $"Assets/Example/Prefabs/{name}.prefab");
        }

        private static P2PConfig LoadOrCreateConfig()
        {
            EnsureFolder("Assets/Example/Config");
            const string path = "Assets/Example/Config/P2PConfig.asset";
            var existing = AssetDatabase.LoadAssetAtPath<P2PConfig>(path);
            if (existing != null) return existing;
            var config = ScriptableObject.CreateInstance<P2PConfig>();
            AssetDatabase.CreateAsset(config, path);
            return config;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
