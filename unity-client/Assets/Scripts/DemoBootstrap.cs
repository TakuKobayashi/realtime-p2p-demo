using PhantomCatWorks.RealtimeP2PKit.Example.Matchmaking;
using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit.Demo
{
    /// <summary>
    /// Entry point for the P2P demo scene. Wires up P2PManager and kicks off
    /// matchmaking on Start. This is the "integration point" between the reusable
    /// RealtimeP2PKit library and this specific demo game.
    /// </summary>
    public class DemoBootstrap : MonoBehaviour
    {
        [SerializeField] private P2PConfig _config;
        [SerializeField] private string _matchmakingApiUrl = "http://localhost:8787";
        [SerializeField] private GameObject _localPlayerPrefab;
        [SerializeField] private GameObject _remotePlayerPrefab;

        private void Start()
        {
            var localPlayerId = "player-" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            Debug.Log($"[Demo] local playerId = {localPlayerId}");

            P2PManager.Instance.Initialize(_config);
            P2PManager.Instance.StateChanged += state => Debug.Log($"[Demo] session state -> {state}");
            P2PManager.Instance.RoomConnecting += info => Debug.Log($"[Demo] connecting with {info.OpponentId} in room {info.RoomId}");
            P2PManager.Instance.DataChannelReady += OnDataChannelReady;
            P2PManager.Instance.ConnectionClosed += reason => Debug.LogWarning($"[Demo] connection closed: {reason}");

            gameObject.AddComponent<ExampleMatchmakingFlow>().StartQueue(localPlayerId, _matchmakingApiUrl);
        }

        private void OnDataChannelReady()
        {
            Debug.Log("[Demo] data channel ready, spawning player objects");
            Instantiate(_localPlayerPrefab, Vector3.zero, Quaternion.identity);
            var remote = Instantiate(_remotePlayerPrefab, new Vector3(2, 0, 0), Quaternion.identity);
            remote.AddComponent<DemoRemotePlayerSync>();
        }

        private void OnDestroy()
        {
            P2PManager.Instance.Disconnect();
        }
    }
}
