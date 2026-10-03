using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace net.taptappun.RealtimeP2PKit.Example.Matchmaking
{
    public sealed class HttpMatchmakingClient
    {
        private readonly string _baseUrl;
        public HttpMatchmakingClient(string baseUrl) => _baseUrl = baseUrl.Trim().TrimEnd('/');
        public async Task<ExamplePlayerSession> RegisterPlayerAsync()
            => JsonConvert.DeserializeObject<ExamplePlayerSession>(await HttpRequestAsync("POST", $"{_baseUrl}/api/matchmaking/players", "{}"));
        public async Task<MachingRoom> CreateRoomAsync(ExamplePlayerSession player, int maxPlayers)
            => JsonConvert.DeserializeObject<MachingRoom>(await HttpRequestAsync("POST", $"{_baseUrl}/api/matchmaking/rooms",
                JsonConvert.SerializeObject(new { playerId = player.id, player.token, maxPlayers })));
        public async Task<List<MachingRoom>> ListRoomsAsync()
            => JsonConvert.DeserializeObject<List<MachingRoom>>(await HttpRequestAsync("GET", $"{_baseUrl}/api/matchmaking/rooms"));
        public async Task<MachingRoom> JoinRoomAsync(string roomId, ExamplePlayerSession player)
            => JsonConvert.DeserializeObject<MachingRoom>(await HttpRequestAsync("POST",
                $"{_baseUrl}/api/matchmaking/rooms/{UnityWebRequest.EscapeURL(roomId)}/join",
                JsonConvert.SerializeObject(new { playerId = player.id, player.token })));
        public async Task LeaveRoomAsync(string roomId, ExamplePlayerSession player)
            => await HttpRequestAsync("POST", $"{_baseUrl}/api/matchmaking/rooms/{UnityWebRequest.EscapeURL(roomId)}/leave",
                JsonConvert.SerializeObject(new { playerId = player.id, player.token }), 5);

        public static async Task<string> HttpRequestAsync(string method, string url, string jsonBody = null, int timeoutSeconds = 15)
        {
            if (P2PNetworkLog.IsEnabled) Debug.Log($"[P2P Example][HTTP] -> {method} {url}");
            using var request = new UnityWebRequest(url, method);
            request.timeout = timeoutSeconds;
            if (jsonBody != null) request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            var operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();
            // Payloads contain credentials: do not include them in network logs.
            if (P2PNetworkLog.IsEnabled) Debug.Log($"[P2P Example][HTTP] <- {method} {url} status={request.responseCode}");
            if (request.result != UnityWebRequest.Result.Success)
            {
                var details = request.downloadHandler.text;
                throw new Exception($"HTTP {request.responseCode}: {(string.IsNullOrEmpty(details) ? request.error : details)}");
            }
            return request.downloadHandler.text;
        }
    }
}
