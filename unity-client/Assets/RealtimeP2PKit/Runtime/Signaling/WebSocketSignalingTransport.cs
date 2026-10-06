using System;
using System.Text;
using System.Threading.Tasks;
using NativeWebSocket;
using UnityEngine;

namespace net.taptappun.RealtimeP2PKit
{
    /// <summary>Connects to the supplied complete URL unchanged. Protocol and authentication belong to the adapter.</summary>
    public sealed class WebSocketSignalingTransport : ISignalingTransport
    {
        public event Action<string> MessageReceived;
        public event Action<string> Disconnected;
        private readonly string _url;
        private readonly string _logUrl;
        private WebSocket _socket;
        private TaskCompletionSource<bool> _opened;
        private bool _disposed;

        public WebSocketSignalingTransport(string webSocketUrl)
        {
            if (!Uri.TryCreate(webSocketUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "ws" && uri.Scheme != "wss") || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("A valid signaling WebSocket URL without a fragment is required.", nameof(webSocketUrl));
            _url = webSocketUrl;
            // An adapter may put credentials in its query; omit the query and user information from logs.
            _logUrl = new UriBuilder(uri) { Query = string.Empty, UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri;
        }

        public Task ConnectAsync()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebSocketSignalingTransport));
            if (_opened != null) throw new InvalidOperationException("The transport has already been connected.");
            _opened = new TaskCompletionSource<bool>();
            _socket = new WebSocket(_url);
            _socket.OnOpen += () =>
            {
                if (_disposed) return;
                if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketOpen("Signaling", _logUrl));
                _opened.TrySetResult(true);
            };
            _socket.OnMessage += bytes =>
            {
                if (_disposed) return;
                var message = Encoding.UTF8.GetString(bytes);
                if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketReceive("Signaling", message));
                MessageReceived?.Invoke(message);
            };
            _socket.OnError += _ => Fail("signaling transport error");
            _socket.OnClose += code =>
            {
                if (_disposed) return;
                if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketClose("Signaling", _logUrl, code.ToString()));
                Fail($"signaling transport closed: {code}");
            };
            _ = RunConnectionAsync();
            return _opened.Task;
        }
        private async Task RunConnectionAsync()
        {
            try { await _socket.Connect(); }
            catch (Exception) { Fail("signaling transport connection failed"); }
        }
        private void Fail(string reason)
        {
            if (_disposed) return;
            _opened?.TrySetException(new InvalidOperationException(reason));
            Disconnected?.Invoke(reason);
        }
        public async Task SendAsync(string message)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebSocketSignalingTransport));
            if (_socket == null || _socket.State != WebSocketState.Open) throw new InvalidOperationException("The signaling transport is not open.");
            if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketSend("Signaling", message));
            await _socket.SendText(message);
        }
        public void DispatchMessageQueue()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            _socket?.DispatchMessageQueue();
#endif
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _opened?.TrySetCanceled();
            _ = CloseAsync();
        }
        private async Task CloseAsync()
        {
            try { if (_socket != null) await _socket.Close(); }
            catch (Exception) { /* Dispose is best effort; failure reporting stops after disposal. */ }
        }
    }
}
