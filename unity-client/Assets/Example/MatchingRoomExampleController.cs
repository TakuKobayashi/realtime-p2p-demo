using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PhantomCatWorks.RealtimeP2PKit.Example.Matchmaking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhantomCatWorks.RealtimeP2PKit.Example
{
    [RequireComponent(typeof(ExampleMatchmakingFlow))]
    public sealed class MatchingRoomExampleController : MonoBehaviour
    {
        [Tooltip("Example room capacity. 0 means unlimited. The library has no fixed peer limit.")]
        [Min(0)] [SerializeField] private int _defaultMaxPlayers = 4;
        [SerializeField] private ExampleMatchingRoomView _view;
        private ExampleMatchmakingFlow _flow;
        private List<MachingRoom> _rooms = new();
        private string _status;
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
                        if (long.TryParse(room.id, out var id)) _lastRoomId = Math.Max(_lastRoomId, id);
                    _hasSnapshot = true;
                    _view.ShowRooms(_rooms);
                    _status = "新しいRoomの通知に接続中...";
                    ConnectLobby();
                }
            }
            catch (Exception ex) { if (!_destroyed) _status = ex.Message; }
            finally { _refreshing = false; UpdateView(); }
        }
        private void ConnectLobby()
        {
            if (_destroyed || _lobby != null) return;
            var listener = new LobbyListener(ExampleEndpoints.GetWebSocketBaseUrl());
            _lobby = listener;
            listener.RoomsReceived += rooms =>
            {
                if (_destroyed || _lobby != listener) return;
                foreach (var room in rooms)
                {
                    if (!long.TryParse(room.id, out var id) || id <= _lastRoomId) continue;
                    _rooms.Add(room);
                    _lastRoomId = id;
                }
                _view.ShowRooms(_rooms);
            };
            listener.Synchronized += () =>
            {
                if (_destroyed || _lobby != listener) return;
                if (!_busy) _status = "新しいRoomを自動追加します。人数・削除の反映は「一覧を更新」で行えます。";
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
            listener.Connect(_lastRoomId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        private void StopLobby() { var listener = _lobby; _lobby = null; listener?.Dispose(); }
        private async Task EnterAsync(MachingRoom room = null)
        {
            if (_busy) return;
            var capacity = _view.Capacity;
            if (room == null && (!int.TryParse(capacity, out var maximum) || maximum < 0))
            { _status = "定員は0以上の整数で指定してください。0は無制限です。"; UpdateView(); return; }
            _busy = true;
            _status = "接続中...";
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
                _status = ex.Message;
                if (ExampleRoomSession.Room != null) await ExampleMatchmakingFlow.LeaveCurrentRoomAsync();
            }
            finally { _busy = false; UpdateView(); }
        }
        private void UpdateView() { if (!_destroyed) _view.SetState(_status, _busy, _refreshing); }
        private void OnDestroy() { _destroyed = true; StopLobby(); }
    }
}
