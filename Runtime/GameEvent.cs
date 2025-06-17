using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Truesoft.Analytics
{
    public class GameEvent : MonoBehaviour
    {
        private const string CloudRunBaseUrl = "https://collect-event-782622202301.asia-northeast3.run.app";

        private static string projectName;

        private const int MaxRetryCount = 3;

        
        
        
        public static void InitUser(string userId, DateTime createdAt, string country, string platform,
            string adChannel, string adCampaign, string adGroup, string adKeyword, string installSource)
        {
            
        }

        public async Task SendUserData(string userId, DateTime createdAt, string country, string platform,
            string adChannel, string adCampaign, string adGroup, string adKeyword, string installSource)
        {
            var data = new Dictionary<string, object>
            {
                { "user_id", userId },
                { "project_name", projectName },
                { "created_at", createdAt.ToUniversalTime().ToString("o") },
                { "country", country },
                { "platform", platform },
                { "ad_channel", adChannel },
                { "ad_campaign", adCampaign },
                { "ad_group", adGroup },
                { "ad_keyword", adKeyword },
                { "install_source", installSource }
            };

            await PostJson($"{CloudRunBaseUrl}/collect/user", data);
        }

        public async Task SendSessionData(string sessionId, string userId, DateTime startedAt, DateTime endedAt,
            int playTimeSec, string platform, string country)
        {
            var data = new Dictionary<string, object>
            {
                { "session_id", sessionId },
                { "user_id", userId },
                { "project_name", projectName },
                { "started_at", startedAt.ToUniversalTime().ToString("o") },
                { "ended_at", endedAt.ToUniversalTime().ToString("o") },
                { "play_time_sec", playTimeSec },
                { "platform", platform },
                { "country", country }
            };

            await PostJson($"{CloudRunBaseUrl}/collect/session", data);
        }

        public async Task SendEventData(string eventId, string userId, string sessionId, string eventName,
            DateTime eventTime, Dictionary<string, object> parameters)
        {
            var data = new Dictionary<string, object>
            {
                { "event_id", eventId },
                { "user_id", userId },
                { "project_name", projectName },
                { "session_id", sessionId },
                { "event_name", eventName },
                { "event_time", eventTime.ToUniversalTime().ToString("o") },
                { "parameters", parameters }
            };

            await PostJson($"{CloudRunBaseUrl}/collect/event", data);
        }

        public async Task SendPaymentData(string paymentId, string userId, DateTime paidAt, string currency,
            double amountUsd, double amountLocal)
        {
            var data = new Dictionary<string, object>
            {
                { "payment_id", paymentId },
                { "user_id", userId },
                { "project_name", projectName },
                { "paid_at", paidAt.ToUniversalTime().ToString("o") },
                { "currency", currency },
                { "amount_usd", amountUsd },
                { "amount_local", amountLocal }
            };

            await PostJson($"{CloudRunBaseUrl}/collect/payment", data);
        }

        private async Task PostJson(string url, Dictionary<string, object> data)
        {
            string jsonBody = JsonUtility.ToJson(new JsonWrapper(data));

            int attempt = 0;
            while (attempt < MaxRetryCount)
            {
                using (UnityWebRequest www = UnityWebRequest.Put(url, jsonBody))
                {
                    www.method = "POST";
                    www.SetRequestHeader("Content-Type", "application/json");

                    await www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"[CollectEvent] Success: {url}");
                        return;
                    }
                    else
                    {
                        Debug.LogWarning($"[CollectEvent] Failed: {url}, Attempt: {attempt + 1}, Error: {www.error}");
                        attempt++;
                    }
                }

                await Task.Delay(500); // 간단한 재시도 대기
            }

            Debug.LogError($"[CollectEvent] Final failure after retries: {url}");
        }

        // 유니티의 Dictionary Json 변환을 위한 임시 래퍼 클래스
        [Serializable]
        private class JsonWrapper
        {
            public Dictionary<string, object> wrapper;

            public JsonWrapper(Dictionary<string, object> data)
            {
                wrapper = data;
            }
        }
    }
}
