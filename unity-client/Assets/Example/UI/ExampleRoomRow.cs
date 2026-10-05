using System;
using net.taptappun.RealtimeP2PKit.Example.Matchmaking;
using UnityEngine;
using UnityEngine.UI;
using net.taptappun.RealtimeP2PKit.Example.Localization;

namespace net.taptappun.RealtimeP2PKit.Example
{
    public sealed class ExampleRoomRow : MonoBehaviour
    {
        [SerializeField] private Text _roomText;
        [SerializeField] private Text _capacityText;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Text _joinLabel;
        private MachingRoom _room;
        private Action<MachingRoom> _join;

        private void Awake() => _joinButton.onClick.AddListener(Join);
        public void Bind(MachingRoom room, Action<MachingRoom> join, bool busy)
        {
            _room = room;
            _join = join;
            LocalizedUGUIText.SetEntry(_roomText, "row.room", room.id);
            LocalizedUGUIText.SetEntry(_capacityText, room.maxPlayers == 0 ? "row.capacity_unlimited" : "row.capacity", room.memberCount, room.maxPlayers);
            SetBusy(busy);
        }
        public void SetBusy(bool busy)
        {
            var full = _room.maxPlayers > 0 && _room.memberCount >= _room.maxPlayers;
            _joinButton.interactable = !busy && !full;
            LocalizedUGUIText.SetEntry(_joinLabel, full ? "row.full" : "row.join");
        }
        private void Join() => _join?.Invoke(_room);
        private void OnDestroy() => _joinButton.onClick.RemoveListener(Join);
    }
}
