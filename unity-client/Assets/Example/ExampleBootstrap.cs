using System;
using PhantomCatWorks.RealtimeP2PKit.Example.Matchmaking;
using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit.Example
{
    public sealed class ExampleBootstrap : MonoBehaviour
    {
        [Tooltip("Optional. Leave empty to use the built-in data-channel and logging defaults.")]
        [SerializeField] private P2PConfig _config;
        [SerializeField] private GameObject _localPlayerPrefab;
        [SerializeField] private GameObject _remotePlayerPrefab;

        private void Start()
        {
            var manager = P2PManager.Instance;
            manager.Initialize(_config);
            manager.StateChanged += OnStateChanged;
            manager.ConnectionClosed += OnConnectionClosed;
            manager.DataChannelReady += OnDataChannelReady;
            gameObject.AddComponent<ExampleMatchmakingFlow>()
                .StartQueue(Guid.NewGuid().ToString("N"));
        }

        private void OnDataChannelReady()
        {
            Instantiate(_localPlayerPrefab, Vector3.zero, Quaternion.identity);
            var remote = Instantiate(_remotePlayerPrefab, new Vector3(2, 0, 0), Quaternion.identity);
            remote.AddComponent<ExampleRemotePlayerSync>();
        }

        private static void OnStateChanged(P2PSessionState state) => Debug.Log($"[P2P Example] {state}");
        private static void OnConnectionClosed(string reason) => Debug.LogWarning($"[P2P Example] Closed: {reason}");

        private void OnDestroy()
        {
            if (!P2PManager.TryGetExistingInstance(out var manager)) return;
            manager.StateChanged -= OnStateChanged;
            manager.ConnectionClosed -= OnConnectionClosed;
            manager.DataChannelReady -= OnDataChannelReady;
            manager.Disconnect();
        }
    }
}
