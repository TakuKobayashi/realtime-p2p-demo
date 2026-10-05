using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using net.taptappun.RealtimeP2PKit.Example.Matchmaking;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Localization;

namespace net.taptappun.RealtimeP2PKit.Example
{
    [RequireComponent(typeof(ExampleMatchmakingFlow))]
    public sealed class ExampleBootstrap : MonoBehaviour
    {
        [Tooltip("Optional. Leave empty to use the built-in defaults.")]
        [SerializeField] private P2PConfig _config;
        [SerializeField] private GameObject _localPlayerPrefab;
        [SerializeField] private GameObject _remotePlayerPrefab;
        [SerializeField] private ExampleGameplayView _view;
        private readonly Dictionary<string, ExampleRemotePlayerSync> _remotePlayers = new();
        private P2PManager _manager;
        private GameObject _localPlayer;
        private bool _exiting;
        private LocalizedString _status;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnableBackgroundExecution()
        {
            // Also applies to Multiplayer Play Mode virtual players, before lobby/room setup.
            Application.runInBackground = true;
        }

        private async void Start()
        {
            _status = ExampleLocalization.Message("status.room_connecting");
            _view.LeaveRequested += OnLeaveRequested;
            _view.SetStatus(_status, false);
            if (ExampleRoomSession.Room == null || ExampleRoomSession.Player == null)
            {
                _exiting = true;
                SceneManager.LoadScene(ExampleRoomSession.MatchingScene);
                return;
            }
            try
            {
                _manager = P2PManager.Instance;
                _manager.Initialize(_config);
                _manager.ConnectionClosed += OnConnectionClosed;
                _manager.PeerConnected += OnPeerConnected;
                _manager.PeerLeft += RemoveRemote;
                _manager.PeerConnectionFailed += OnPeerConnectionFailed;
                _manager.RegisterPacketHandler<PositionPacket>(ExamplePlayerController.PositionPacketId, OnPositionReceived);
                _localPlayer = Instantiate(_localPlayerPrefab, new Vector3(UnityEngine.Random.Range(-4f, 4f), 0.5f, UnityEngine.Random.Range(-4f, 4f)), Quaternion.identity);
                _localPlayer.name = $"Player {ExampleRoomSession.Player.id} (Local)";
                await _manager.ConnectToRoomAsync(ExampleRoomSession.Player.id,
                    ExampleRoomSession.Room.id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ExampleRoomSession.Player.token, ExampleRoomSession.WebSocketBaseUrl);
                if (!_exiting) _status = ExampleLocalization.Message("status.controls");
            }
            catch (Exception ex) { Debug.LogException(ex); if (!_exiting) await ExitAsync(ExampleLocalization.Message("status.connection_failed")); }
        }
        private void OnPeerConnected(string id)
        {
            if (_exiting || _remotePlayers.ContainsKey(id)) return;
            var remote = Instantiate(_remotePlayerPrefab, Vector3.zero, Quaternion.identity);
            remote.name = $"Player {id} (Remote)";
            var sync = remote.GetComponent<ExampleRemotePlayerSync>();
            if (sync == null) sync = remote.AddComponent<ExampleRemotePlayerSync>();
            _remotePlayers.Add(id, sync);
        }
        private void OnPositionReceived(string senderId, PositionPacket packet)
        {
            if (_remotePlayers.TryGetValue(senderId, out var remote)) remote.Apply(packet);
        }
        private void RemoveRemote(string id)
        {
            if (!_remotePlayers.TryGetValue(id, out var remote)) return;
            _remotePlayers.Remove(id);
            if (remote != null) Destroy(remote.gameObject);
        }
        private void OnPeerConnectionFailed(string id)
        {
            RemoveRemote(id);
            _status = ExampleLocalization.Message("status.peer_failed", id);
        }
        private void OnConnectionClosed(string reason)
        {
            Debug.LogWarning(reason);
            _ = ExitAsync(ExampleLocalization.Message("status.disconnected"));
        }
        private async Task ExitAsync(LocalizedString notice)
        {
            if (_exiting) return;
            _exiting = true;
            _status = ExampleLocalization.Message("status.leaving");
            ExampleRoomSession.Notice = notice;
            await ExampleMatchmakingFlow.LeaveCurrentRoomAsync();
            if (this != null) SceneManager.LoadScene(ExampleRoomSession.MatchingScene);
        }
        private void OnLeaveRequested() => _ = ExitAsync(ExampleLocalization.Message("status.left"));
        private void LateUpdate()
        {
            var room = ExampleRoomSession.Room;
            if (room != null)
            {
                var count = 1 + (_manager?.PeerIds.Count ?? 0);
                _view.SetSummary(room.id, count, room.maxPlayers, ExampleRoomSession.Player.id, _manager?.ConnectedPeerIds.Count ?? 0);
            }
            _view.SetStatus(_status, _exiting);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            var lines = $"Focus: {Application.isFocused}   Run in Background: {Application.runInBackground}";
            foreach (var entry in _remotePlayers)
            {
                var remote = entry.Value;
                var age = remote.ReceivedPacketCount == 0 ? "waiting" : $"{Time.realtimeSinceStartup - remote.LastReceivedAt:0.0}s ago";
                lines += $"\nPlayer {entry.Key}: RX {remote.ReceivedPacketCount}   {age}";
            }
            var height = 30f + _remotePlayers.Count * 22f;
            GUI.Box(new Rect(10f, Screen.height - height - 10f, Mathf.Min(520f, Screen.width - 20f), height), GUIContent.none);
            GUI.Label(new Rect(18f, Screen.height - height - 6f, Mathf.Min(504f, Screen.width - 36f), height), lines);
        }
#endif
        private void OnDestroy()
        {
            if (_view != null) _view.LeaveRequested -= OnLeaveRequested;
            if (_manager != null)
            {
                _manager.ConnectionClosed -= OnConnectionClosed;
                _manager.PeerConnected -= OnPeerConnected;
                _manager.PeerLeft -= RemoveRemote;
                _manager.PeerConnectionFailed -= OnPeerConnectionFailed;
                _manager.UnregisterPacketHandler(ExamplePlayerController.PositionPacketId);
            }
            if (!_exiting) _ = ExampleMatchmakingFlow.LeaveCurrentRoomAsync();
        }
    }
}
