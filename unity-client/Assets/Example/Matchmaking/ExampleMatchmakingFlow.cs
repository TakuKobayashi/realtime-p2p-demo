using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace net.taptappun.RealtimeP2PKit.Example.Matchmaking
{
    /// <summary>HTTP snapshots and reservations. Each scene owns its WebSocket lifetime.</summary>
    [DisallowMultipleComponent]
    public sealed class ExampleMatchmakingFlow : MonoBehaviour
    {
        private HttpMatchmakingClient Client => new HttpMatchmakingClient(ExampleEndpoints.GetHttpBaseUrl());
        private async Task EnsurePlayerAsync()
        {
            var http = ExampleEndpoints.GetHttpBaseUrl();
            if (ExampleRoomSession.Player == null || ExampleRoomSession.HttpBaseUrl != http)
                ExampleRoomSession.Player = await Client.RegisterPlayerAsync();
            ExampleRoomSession.HttpBaseUrl = http;
        }
        public Task<List<MachingRoom>> ListRoomsAsync() => Client.ListRoomsAsync();
        public async Task<MachingRoom> CreateRoomAsync(int maxPlayers = 0)
        {
            if (maxPlayers < 0) throw new ArgumentOutOfRangeException(nameof(maxPlayers));
            await EnsurePlayerAsync();
            var room = await Client.CreateRoomAsync(ExampleRoomSession.Player, maxPlayers);
            Remember(room);
            return room;
        }
        public async Task JoinRoomAsync(MachingRoom room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            await EnsurePlayerAsync();
            Remember(await Client.JoinRoomAsync(room.id, ExampleRoomSession.Player));
        }
        private static void Remember(MachingRoom room)
        {
            ExampleRoomSession.Room = room;
            ExampleRoomSession.WebSocketBaseUrl = ExampleEndpoints.GetWebSocketBaseUrl();
        }
        public static async Task LeaveCurrentRoomAsync()
        {
            var room = ExampleRoomSession.Room;
            var player = ExampleRoomSession.Player;
            ExampleRoomSession.Room = null;
            if (P2PManager.TryGetExistingInstance(out var manager)) manager.Disconnect();
            if (room == null || player == null) return;
            try { await new HttpMatchmakingClient(ExampleRoomSession.HttpBaseUrl).LeaveRoomAsync(room.id, player); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[P2P Example] Leave request failed; the server lease will expire: {ex.Message}");
            }
        }
    }
}
