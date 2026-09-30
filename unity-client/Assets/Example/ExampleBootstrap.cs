using System;
using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit.Example
{
    public sealed class ExampleBootstrap : MonoBehaviour
    {
        private const byte PositionPacketId = 1;
        [SerializeField] private P2PConfig _config;
        [SerializeField] private float _speed = 4f;

        private Transform _localPlayer;
        private Transform _remotePlayer;
        private float _sendTimer;

        private void Start()
        {
            var manager = P2PManager.Instance;
            manager.Initialize(_config);
            manager.RegisterPacketHandler<PositionPacket>(PositionPacketId, OnPosition);
            manager.StateChanged += OnStateChanged;
            manager.ConnectionClosed += OnConnectionClosed;
            _localPlayer = CreateCube("Local player", Color.cyan, Vector3.left);
            _remotePlayer = CreateCube("Remote player", Color.magenta, Vector3.right);
            manager.StartMatchmaking(Guid.NewGuid().ToString("N"));
        }

        private static Transform CreateCube(string name, Color color, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.GetComponent<Renderer>().material.color = color;
            return go.transform;
        }

        private void Update()
        {
            if (_localPlayer == null || P2PManager.Instance.Session.State != P2PSessionState.Connected) return;
            var movement = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
            _localPlayer.position += movement.normalized * (_speed * Time.deltaTime);
            _sendTimer += Time.deltaTime;
            if (_sendTimer < 0.05f) return;
            _sendTimer = 0;
            var p = _localPlayer.position;
            P2PManager.Instance.Send(PositionPacketId, new PositionPacket { X = p.x, Y = p.y, Z = p.z });
        }

        private void OnPosition(PositionPacket packet)
        {
            if (_remotePlayer != null)
                _remotePlayer.position = new Vector3(packet.X, packet.Y, packet.Z);
        }

        private static void OnStateChanged(P2PSessionState state) => Debug.Log($"[P2P Example] {state}");
        private static void OnConnectionClosed(string reason) => Debug.LogWarning($"[P2P Example] Closed: {reason}");

        private void OnDestroy()
        {
            if (!P2PManager.TryGetExistingInstance(out var manager)) return;
            manager.UnregisterPacketHandler(PositionPacketId);
            manager.StateChanged -= OnStateChanged;
            manager.ConnectionClosed -= OnConnectionClosed;
            manager.Disconnect();
        }
    }
}
