using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NativeWebSocket;
using UnityEngine;
using UnityEngine.Localization;

namespace net.taptappun.RealtimeP2PKit.Example.Matchmaking
{
    /// <summary>New-room stream. The scene owns reconnects and its received RoomId cursor.</summary>
    public sealed class LobbyListener : IDisposable
    {
        public event Action<List<MachingRoom>> RoomsReceived;
        public event Action Synchronized;
        public event Action<LocalizedString> Disconnected;
        private readonly string _url;
        private WebSocket _ws;
        private bool _disposed;
        private bool _failed;
        private float _lastReceived;
        private float _nextPing;

        private sealed class Message
        {
            public string type;
            public List<MachingRoom> rooms;
        }
        public LobbyListener(string webSocketUrl)
            => _url = webSocketUrl.Trim();

        public void Connect(long lastRoomId)
        {
            _lastReceived = Time.realtimeSinceStartup;
            _nextPing = _lastReceived + 15f;
            _ws = new WebSocket(_url);
            _ws.OnOpen += () =>
            {
                if (_disposed || _failed) return;
                _ = SendAsync(JsonConvert.SerializeObject(new { type = "subscribe", lastRoomId }));
            };
            _ws.OnError += error => Fail(ExampleLocalization.Message("lobby.connection_failed"));
            _ws.OnClose += code => Fail(ExampleLocalization.Message("lobby.disconnected"));
            _ws.OnMessage += bytes =>
            {
                if (_disposed || _failed) return;
                _lastReceived = Time.realtimeSinceStartup;
                var json = Encoding.UTF8.GetString(bytes);
                if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketReceive("Lobby", json));
                try
                {
                    var message = JsonConvert.DeserializeObject<Message>(json);
                    if (message?.type == "rooms-created" && message.rooms != null) RoomsReceived?.Invoke(message.rooms);
                    else if (message?.type == "subscribed") Synchronized?.Invoke();
                }
                catch (Exception ex) { Debug.LogException(ex); Fail(ExampleLocalization.Message("lobby.invalid_message")); }
            };
            _ = RunAsync();
        }
        private async Task RunAsync()
        {
            try { await _ws.Connect(); }
            catch (Exception) { Fail(ExampleLocalization.Message("lobby.connection_failed")); }
        }
        private async Task SendAsync(string json)
        {
            try
            {
                if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketSend("Lobby", json));
                await _ws.SendText(json);
            }
            catch (Exception) { Fail(ExampleLocalization.Message("lobby.send_failed")); }
        }
        public void Tick()
        {
            if (_disposed || _failed) return;
#if !UNITY_WEBGL || UNITY_EDITOR
            _ws?.DispatchMessageQueue();
#endif
            var now = Time.realtimeSinceStartup;
            if (now - _lastReceived > 40f) { Fail(ExampleLocalization.Message("lobby.timeout")); return; }
            if (_ws?.State == WebSocketState.Open && now >= _nextPing)
            {
                _nextPing = now + 15f;
                _ = SendAsync("{\"type\":\"ping\"}");
            }
        }
        private void Fail(LocalizedString reason)
        {
            if (_disposed || _failed) return;
            _failed = true;
            Disconnected?.Invoke(reason);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ = CloseAsync();
        }
        private async Task CloseAsync()
        {
            try { if (_ws != null) await _ws.Close(); }
            catch (Exception) { }
        }
    }
}
