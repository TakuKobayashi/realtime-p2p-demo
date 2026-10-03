using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using net.taptappun.RealtimeP2PKit.Example.Matchmaking;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private string _status = "Roomに接続中...";

        private async void Start()
        {
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
                await _manager.ConnectToRoomAsync(ExampleRoomSession.Player.id, ExampleRoomSession.Room.id,
                    ExampleRoomSession.Player.token, ExampleRoomSession.WebSocketBaseUrl);
                if (!_exiting) _status = "WASD / 矢印キーで移動できます。参加者を待っています。";
            }
            catch (Exception ex) { if (!_exiting) await ExitAsync($"接続に失敗しました: {ex.Message}"); }
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
            _status = $"Player {id} とのP2P接続に失敗しました。他の参加者との通信は継続しています。";
        }
        private void OnConnectionClosed(string reason) => _ = ExitAsync($"ネットワーク接続が切れました: {reason}");
        private async Task ExitAsync(string notice)
        {
            if (_exiting) return;
            _exiting = true;
            _status = "退出中...";
            ExampleRoomSession.Notice = notice;
            await ExampleMatchmakingFlow.LeaveCurrentRoomAsync();
            if (this != null) SceneManager.LoadScene(ExampleRoomSession.MatchingScene);
        }
        private void OnLeaveRequested() => _ = ExitAsync("Roomから退出しました。");
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
