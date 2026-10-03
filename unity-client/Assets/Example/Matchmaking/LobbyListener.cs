using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NativeWebSocket;
using UnityEngine;

namespace net.taptappun.RealtimeP2PKit.Example.Matchmaking
{
    /// <summary>New-room stream. The scene owns reconnects and its received RoomId cursor.</summary>
    public sealed class LobbyListener : IDisposable
    {
        public event Action<List<MachingRoom>> RoomsReceived;
        public event Action Synchronized;
        public event Action<string> Disconnected;
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
        public LobbyListener(string baseWsUrl)
            => _url = baseWsUrl.Trim().TrimEnd('/') + "/parties/lobby/rooms";

        public void Connect(string lastRoomId)
        {
            _lastReceived = Time.realtimeSinceStartup;
            _nextPing = _lastReceived + 15f;
            _ws = new WebSocket(_url);
            _ws.OnOpen += () =>
            {
                if (_disposed || _failed) return;
                _ = SendAsync(JsonConvert.SerializeObject(new { type = "subscribe", lastRoomId }));
            };
            _ws.OnError += error => Fail("ルーム通知に接続できません。再接続します。");
            _ws.OnClose += code => Fail("ルーム通知が切断されました。再接続します。");
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
                catch (Exception ex) { Fail("ルーム通知の読み込みに失敗しました: " + ex.Message); }
            };
            _ = RunAsync();
        }
        private async Task RunAsync()
        {
            try { await _ws.Connect(); }
            catch (Exception) { Fail("ルーム通知に接続できません。再接続します。"); }
        }
        private async Task SendAsync(string json)
        {
            try
            {
                if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketSend("Lobby", json));
                await _ws.SendText(json);
            }
            catch (Exception) { Fail("ルーム通知の送信に失敗しました。再接続します。"); }
        }
        public void Tick()
        {
            if (_disposed || _failed) return;
#if !UNITY_WEBGL || UNITY_EDITOR
            _ws?.DispatchMessageQueue();
#endif
            var now = Time.realtimeSinceStartup;
            if (now - _lastReceived > 40f) { Fail("ルーム通知が応答しません。再接続します。"); return; }
            if (_ws?.State == WebSocketState.Open && now >= _nextPing)
            {
                _nextPing = now + 15f;
                _ = SendAsync("{\"type\":\"ping\"}");
            }
        }
        private void Fail(string reason)
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
