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
            PhantomCatWorks.RealtimeP2PKit.Editor.P2PConnectionSettingsWindow.EnsureAsset();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 8, -10);
            camera.transform.LookAt(Vector3.zero);
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2, 1, 2);
            new GameObject("P2P Example").AddComponent<ExampleBootstrap>();
            if (!AssetDatabase.IsValidFolder("Assets/Example/Scenes"))
                AssetDatabase.CreateFolder("Assets/Example", "Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/Example/Scenes/P2PExample.unity");
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Example/Scenes/P2PExample.unity", true)
            };
            Debug.Log("[P2P Example] Open Connection Settings, then press Play in Assets/Example/Scenes/P2PExample.unity.");
        }
    }
}
