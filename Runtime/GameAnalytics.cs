// 공용 수집 SDK (패키지화)

using UnityEngine;
using UnityEngine.Networking;

namespace TrueSoft.Analytics
{
    public static class GameAnalytics
    {
        private static string endpoint = "https://collect-event-782622202301.asia-northeast3.run.app/collect";
        private static string _projectId = "DEFAULT_PROJECT_ID";
    
        public static void Initialize(string newProjectId)
        {
            _projectId = newProjectId;
        }
    
        public static void SendEvent(string eventName, string userId, string platform, string country, object eventParams = null)
        {
            EventData data = new EventData()
            {
                event_name = eventName,
                user_id = userId,
                project_id = _projectId,
                platform = platform,
                country = country,
                event_time = System.DateTime.UtcNow.ToString("o"),
                event_parameters = eventParams
            };
    
            string json = JsonUtility.ToJson(data);
            SendRequest(json);
        }
    
        private static void SendRequest(string json)
        {
            UnityWebRequest request = new UnityWebRequest(endpoint, "POST");
            byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(jsonBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SendWebRequest();
        }
    
        [System.Serializable]
        private class EventData
        {
            public string event_name;
            public string user_id;
            public string project_id;
            public string platform;
            public string country;
            public string event_time;
            public object event_parameters;
        }
    }
}