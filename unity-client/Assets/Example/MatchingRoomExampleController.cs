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
    public sealed class MatchingRoomExampleController : MonoBehaviour
    {
        [Tooltip("Example room capacity. 0 means unlimited. The library has no fixed peer limit.")]
        [Min(0)] [SerializeField] private int _defaultMaxPlayers = 4;
        [SerializeField] private ExampleMatchingRoomView _view;
        private ExampleMatchmakingFlow _flow;
        private List<MachingRoom> _rooms = new();
        private LocalizedString _status;
        private bool _busy;
        private bool _refreshing;
        private bool _destroyed;
        private LobbyListener _lobby;
        private long _lastRoomId;
        private bool _hasSnapshot;
        private float _nextReconnect;

        private void Start()
        {
            _flow = GetComponent<ExampleMatchmakingFlow>();
            _view.Initialize(_defaultMaxPlayers, () => _ = EnterAsync(), () => _ = RefreshAsync(), room => _ = EnterAsync(room));
            _status = ExampleRoomSession.Notice;
            ExampleRoomSession.Notice = null;
            UpdateView();
            _ = RefreshAsync();
        }
        private void Update()
        {
            _lobby?.Tick();
            if (_hasSnapshot && _lobby == null && !_busy && !_refreshing && Time.unscaledTime >= _nextReconnect)
                ConnectLobby();
        }
        private async Task RefreshAsync()
        {
            if (_refreshing || _busy || _destroyed) return;
            _refreshing = true;
            // Stop the old stream before replacing its HTTP snapshot; late callbacks cannot overwrite it.
            StopLobby();
            UpdateView();
            try
            {
                var rooms = await _flow.ListRoomsAsync();
                if (!_destroyed)
                {
                    _rooms = rooms ?? new List<MachingRoom>();
                    _lastRoomId = 0;
                    foreach (var room in _rooms)
                        _lastRoomId = Math.Max(_lastRoomId, room.id);
                    _hasSnapshot = true;
                    _view.ShowRooms(_rooms);
                    _status = ExampleLocalization.Message("status.lobby_connecting");
                    ConnectLobby();
                }
            }
            catch (Exception ex) { Debug.LogException(ex); if (!_destroyed) _status = ExampleLocalization.Message("status.request_failed"); }
            finally { _refreshing = false; UpdateView(); }
        }
        private void ConnectLobby()
        {
            if (_destroyed || _lobby != null) return;
            var listener = new LobbyListener(ExampleEndpoints.GetLobbyWebSocketUrl());
            _lobby = listener;
            listener.RoomsReceived += rooms =>
            {
                if (_destroyed || _lobby != listener) return;
                foreach (var room in rooms)
                {
                    if (room.id <= _lastRoomId) continue;
                    _rooms.Add(room);
                    _lastRoomId = room.id;
                }
                _view.ShowRooms(_rooms);
            };
            listener.Synchronized += () =>
            {
                if (_destroyed || _lobby != listener) return;
                if (!_busy) _status = ExampleLocalization.Message("status.lobby_ready");
                UpdateView();
            };
            listener.Disconnected += reason =>
            {
                if (_destroyed || _lobby != listener) return;
                StopLobby();
                _nextReconnect = Time.unscaledTime + 3f;
                if (!_busy) _status = reason;
                UpdateView();
            };
            listener.Connect(_lastRoomId);
        }
        private void StopLobby() { var listener = _lobby; _lobby = null; listener?.Dispose(); }
        private async Task EnterAsync(MachingRoom room = null)
        {
            if (_busy) return;
            var capacity = _view.Capacity;
            if (room == null && (!int.TryParse(capacity, out var maximum) || maximum < 0))
            { _status = ExampleLocalization.Message("status.invalid_capacity"); UpdateView(); return; }
            _busy = true;
            _status = ExampleLocalization.Message("status.connecting");
            UpdateView();
            try
            {
                if (room == null) await _flow.CreateRoomAsync(int.Parse(capacity));
                else await _flow.JoinRoomAsync(room);
                if (_destroyed) { await ExampleMatchmakingFlow.LeaveCurrentRoomAsync(); return; }
                SceneManager.LoadScene(ExampleRoomSession.GameplayScene);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                _status = ExampleLocalization.Message("status.request_failed");
                if (ExampleRoomSession.Room != null) await ExampleMatchmakingFlow.LeaveCurrentRoomAsync();
            }
            finally { _busy = false; UpdateView(); }
        }
        private void UpdateView() { if (!_destroyed) _view.SetState(_status, _busy, _refreshing); }
        private void OnDestroy() { _destroyed = true; StopLobby(); }
    }
}
