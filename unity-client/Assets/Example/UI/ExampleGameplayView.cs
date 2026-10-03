using System;
using UnityEngine;
using UnityEngine.UI;

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
        public void SetSummary(string roomId, int count, int maximum, string playerId, int connectedPeers)
        {
            SetText(_roomText, $"Room {roomId}    {count} / {(maximum == 0 ? "無制限" : maximum.ToString())}");
            SetText(_playerText, $"Player {playerId}    P2P接続数: {connectedPeers}");
        }
        public void SetStatus(string status, bool exiting)
        {
            SetText(_statusText, status);
            _leaveButton.interactable = !exiting;
        }
        private static void SetText(Text text, string value)
        {
            if (text.text != value) text.text = value;
        }
        private void Leave() => LeaveRequested?.Invoke();
        private void OnDestroy() => _leaveButton.onClick.RemoveListener(Leave);
    }
}
