using System;
using System.Collections.Generic;
using System.Linq;
using net.taptappun.RealtimeP2PKit.Example.Matchmaking;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization;
using net.taptappun.RealtimeP2PKit.Localization;

namespace net.taptappun.RealtimeP2PKit.Example
{
    public sealed class ExampleMatchingRoomView : MonoBehaviour
    {
        [SerializeField] private InputField _capacityInput;
        [SerializeField] private Button _createButton;
        [SerializeField] private Button _refreshButton;
        [SerializeField] private Text _statusText;
        [SerializeField] private Text _emptyText;
        [SerializeField] private RectTransform _roomContent;
        [SerializeField] private ExampleRoomRow _roomRowPrefab;
        private readonly Dictionary<long, ExampleRoomRow> _rows = new();
        private Action _create;
        private Action _refresh;
        private Action<MachingRoom> _join;
        private bool _busy;
        public string Capacity => _capacityInput.text;

        public void Initialize(int capacity, Action create, Action refresh, Action<MachingRoom> join)
        {
            _capacityInput.text = capacity.ToString();
            _create = create;
            _refresh = refresh;
            _join = join;
            _createButton.onClick.AddListener(Create);
            _refreshButton.onClick.AddListener(Refresh);
        }
        public void SetState(LocalizedString status, bool busy, bool refreshing)
        {
            _busy = busy;
            LocalizedUGUIText.SetMessage(_statusText, status);
            _capacityInput.interactable = !busy;
            _createButton.interactable = !busy;
            _refreshButton.interactable = !busy && !refreshing;
            foreach (var row in _rows.Values) row.SetBusy(busy);
        }
        public void ShowRooms(IReadOnlyList<MachingRoom> rooms)
        {
            var ids = new HashSet<long>(rooms.Select(room => room.id));
            foreach (var id in _rows.Keys.Where(id => !ids.Contains(id)).ToArray())
            {
                // Remove from layout immediately; Destroy completes at the end of the frame.
                _rows[id].gameObject.SetActive(false);
                Destroy(_rows[id].gameObject);
                _rows.Remove(id);
            }
            for (var i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                if (!_rows.TryGetValue(room.id, out var row))
                {
                    row = Instantiate(_roomRowPrefab, _roomContent);
                    row.name = $"Room {room.id}";
                    _rows.Add(room.id, row);
                }
                row.transform.SetSiblingIndex(i);
                row.Bind(room, _join, _busy);
            }
            _emptyText.gameObject.SetActive(rooms.Count == 0);
        }
        private void Create() => _create?.Invoke();
        private void Refresh() => _refresh?.Invoke();
        private void OnDestroy()
        {
            _createButton.onClick.RemoveListener(Create);
            _refreshButton.onClick.RemoveListener(Refresh);
        }
    }
}
