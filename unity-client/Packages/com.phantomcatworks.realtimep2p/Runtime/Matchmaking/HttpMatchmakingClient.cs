using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace PhantomCatWorks.RealtimeP2PKit
{
    /// <summary>
    /// Matchmaking client for the Hono-based "matching-api" worker
    /// (see /server, the same worker that also hosts the Lobby/Room signaling
    /// Durable Objects). Endpoints:
    ///   POST {baseUrl}/api/matchmaking/join  { playerId } -> MatchmakingResult
    ///   POST {baseUrl}/api/matchmaking/leave { playerId }
    /// </summary>
    public class HttpMatchmakingClient : IMatchmakingClient
    {
        private readonly string _baseUrl;

        public HttpMatchmakingClient(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public async Task<MatchmakingResult> JoinQueueAsync(string playerId)
        {
            var url = $"{_baseUrl}/api/matchmaking/join";
            var body = JsonConvert.SerializeObject(new MatchmakingJoinRequest { playerId = playerId });
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][Matchmaking] POST {url}");

            var responseText = await PostJsonAsync("POST", url, body);

            var result = JsonConvert.DeserializeObject<MatchmakingResult>(responseText);
            if (P2PLog.ShouldLog(P2PLogLevel.Info))
            {
                Debug.Log($"[RealtimeP2PKit][Matchmaking] status={result.status} roomId={result.roomId} " +
                          $"opponentId={result.opponentId} isInitiator={result.isInitiator}");
            }
            return result;
        }

        public async Task LeaveQueueAsync(string playerId)
        {
            var url = $"{_baseUrl}/api/matchmaking/leave";
            var body = JsonConvert.SerializeObject(new MatchmakingJoinRequest { playerId = playerId });
            if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log($"[RealtimeP2PKit][Matchmaking] POST {url} (leave)");
            try
            {
                await PostJsonAsync("POST", url, body);
                if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][Matchmaking] left queue");
            }
            catch (Exception ex)
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Warn)) Debug.LogWarning($"[RealtimeP2PKit][Matchmaking] leave failed (ignored): {ex.Message}");
            }
        }

        private static async Task<string> PostJsonAsync(string method, string url, string jsonBody)
        {
            if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.HttpRequest(method, url, jsonBody));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using var req = new UnityWebRequest(url, method);
            var bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            stopwatch.Stop();

            var isError = req.result != UnityWebRequest.Result.Success;
            if (P2PNetworkLog.IsEnabled)
            {
                Debug.Log(P2PNetworkLogFormat.HttpResponse(method, url, req.responseCode, isError,
                    req.downloadHandler?.text, stopwatch.Elapsed));
            }

            if (isError)
            {
                if (P2PLog.ShouldLog(P2PLogLevel.Error)) Debug.LogError($"[RealtimeP2PKit][Matchmaking] request failed: {req.error} (HTTP {req.responseCode}) url={url}");
                throw new Exception($"Matchmaking request failed: {req.error}");
            }

            return req.downloadHandler.text;
        }
    }
}
