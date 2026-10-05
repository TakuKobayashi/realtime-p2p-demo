using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization;
using net.taptappun.RealtimeP2PKit.Example.Localization;

namespace net.taptappun.RealtimeP2PKit.Example
{
    public sealed class ExampleGameplayView : MonoBehaviour
    {
        [SerializeField] private Text _roomText;
        [SerializeField] private Text _playerText;
        [SerializeField] private Text _statusText;
        [SerializeField] private Button _leaveButton;
        public event Action LeaveRequested;
        private void Awake() => _leaveButton.onClick.AddListener(Leave);
        public void SetSummary(long roomId, int count, int maximum, string playerId, int connectedPeers)
        {
            LocalizedUGUIText.SetEntry(_roomText, maximum == 0 ? "gameplay.room_unlimited" : "gameplay.room", roomId, count, maximum);
            LocalizedUGUIText.SetEntry(_playerText, "gameplay.player", playerId, connectedPeers);
        }
        public void SetStatus(LocalizedString status, bool exiting)
        {
            LocalizedUGUIText.SetMessage(_statusText, status);
            _leaveButton.interactable = !exiting;
        }
        private void Leave() => LeaveRequested?.Invoke();
        private void OnDestroy() => _leaveButton.onClick.RemoveListener(Leave);
    }
}
