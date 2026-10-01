using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace PhantomCatWorks.RealtimeP2PKit.Example.Matchmaking
{
    /// <summary>
    /// Example-only room discovery. The library receives the selected room and peer roles.
    /// Replace this class with a game's own matchmaking system as needed.
    /// </summary>
    public sealed class ExampleMatchmakingFlow : MonoBehaviour
    {
        private HttpMatchmakingClient _client;
        private LobbyListener _lobby;
        private string _playerId;
        private bool _connecting;
        private bool _disposed;

        public void StartQueue(string playerId, string apiUrl = null)
        {
            Configure(playerId, apiUrl);
            _ = RunQueueAsync();
        }

        private void Configure(string playerId, string apiUrl)
        {
            apiUrl = (apiUrl ?? ExampleEndpoints.GetHttpBaseUrl()).Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("Player ID is required.", nameof(playerId));
            if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("A valid matchmaking HTTP URL is required.", nameof(apiUrl));
            _playerId = playerId;
            _client = new HttpMatchmakingClient(apiUrl);
        }

        private async Task RunQueueAsync()
        {
            try
            {
                _lobby = new LobbyListener(ExampleEndpoints.GetWebSocketBaseUrl());
                _lobby.Matched += OnLobbyMatched;
                await _lobby.ConnectAsync(_playerId);
                if (_disposed) return;
                var result = await _client.JoinQueueAsync(_playerId);
                if (_disposed || result.status != "matched") return;
                await ConnectMatchAsync(result.roomId, result.opponentId, result.isInitiator);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[P2P Example] Matchmaking failed: {ex}");
            }
        }

        private async void OnLobbyMatched(LobbyMatchedMessage message)
        {
            try
            {
                await ConnectMatchAsync(message.roomId, message.opponentId, message.isInitiator);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[P2P Example] Room connection failed: {ex}");
            }
        }

        private async Task ConnectMatchAsync(string roomId, string opponentId, bool isInitiator)
        {
            if (_connecting || _disposed) return;
            _connecting = true;
            _lobby?.Dispose();
            _lobby = null;
            await P2PManager.Instance.ConnectToRoomAsync(_playerId, roomId, opponentId, isInitiator);
        }

        public async Task<MachingRoom> CreateRoomAsync(string playerId, string apiUrl = null)
        {
            Configure(playerId, apiUrl);
            var room = await _client.CreateRoomAsync(playerId);
            await P2PManager.Instance.ConnectToRoomAsync(playerId, room.id, null, true);
            return room;
        }

        public Task<List<MachingRoom>> ListRoomsAsync(string playerId, string apiUrl = null)
        {
            Configure(playerId, apiUrl);
            return _client.ListRoomsAsync(playerId);
        }

        public async Task JoinRoomAsync(string playerId, string apiUrl, MachingRoom room)
        {
            Configure(playerId, apiUrl);
            var joined = await _client.JoinRoomAsync(room.id, playerId);
            await P2PManager.Instance.ConnectToRoomAsync(playerId, joined.id, joined.hostPlayerId, false);
        }

        public Task JoinRoomAsync(string playerId, MachingRoom room)
            => JoinRoomAsync(playerId, ExampleEndpoints.GetHttpBaseUrl(), room);

        private void Update() => _lobby?.DispatchMessageQueue();

        private void OnDestroy()
        {
            _disposed = true;
            _lobby?.Dispose();
            if (_client != null && !string.IsNullOrEmpty(_playerId))
                _ = _client.LeaveQueueAsync(_playerId);
        }
    }
}
