using UnityEngine;

namespace net.taptappun.RealtimeP2PKit.Example
{
    /// <summary>One proxy per remote sender; the scene controller owns packet registration.</summary>
    public sealed class ExampleRemotePlayerSync : MonoBehaviour
    {
        [SerializeField] private float _lerpSpeed = 12f;
        private Vector3 _targetPosition;
        private bool _hasTarget;
        private float _lastTimestamp = float.NegativeInfinity;
        public ulong ReceivedPacketCount { get; private set; }
        public float LastReceivedAt { get; private set; }

        public void Apply(PositionPacket packet)
        {
            ReceivedPacketCount++;
            LastReceivedAt = Time.realtimeSinceStartup;
            // Unordered delivery must not rewind an already received position.
            if (packet.TimestampMs <= _lastTimestamp) return;
            _lastTimestamp = packet.TimestampMs;
            _targetPosition = new Vector3(packet.X, packet.Y, packet.Z);
            if (!_hasTarget) transform.position = _targetPosition;
            _hasTarget = true;
        }
        private void Update()
        {
            if (_hasTarget) transform.position = Vector3.Lerp(transform.position, _targetPosition, Time.deltaTime * _lerpSpeed);
        }
    }
}
