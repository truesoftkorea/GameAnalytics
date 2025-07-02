using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Networking;

namespace Truesoft.Analytics
{
    public class EventStorage : MonoBehaviour
    {
        private static EventStorage _instance;
        
        //클라우드 URL
        private const string CloudRunBaseUrl = "https://collect-event-782622202301.asia-northeast1.run.app";
        
        //전송 예약 큐
        private static readonly Queue<string> MemoryQueue = new();
        
        //최대 저장 갯수
        private const int MaxStoredEvents = 500;
        
        //전송 상태
        private static bool _isSending;
        
        //세션 새로고침
        private static float _updateTime;
        private const float UpdateMaxTime = 600;

        //전송 재시도
        private static int _failCount;
        private static bool _isEnd;
        
        //세션 시작
        private static bool _isStart;
        
        private void Awake()
        {
            if (_instance != null)
            {
                Destroy(_instance);
                return;
            }
            
            _instance = this;
            _updateTime = UpdateMaxTime;
        }

        public static void StartStorage()
        {
            _isStart = true;
            
            _instance.LoadFromDisk();
            _instance.TrySend();
        }

        private void Update()
        {
            if (_isStart)
            {
                _updateTime -= Time.deltaTime;
                if (_updateTime <= 0 && !_isSending)
                {
                    _updateTime = UpdateMaxTime;
                    var payload = new UpdatePayload()
                    {
                        user_id = GameEvent.UserID,
                        session_id = GameEvent.SessionID,
                        project_name = GameEvent.ProjectName,
                        event_time = GameEvent.TimeToString(GameEvent.CurrentTime())
                    };
                    
                    Enqueue("/collect/update", JsonUtility.ToJson(payload), false);
                }
            }
        }

        //전송 예약
        public static void Enqueue(string data, string path, bool isSafe = true)
        {
            if (!_isStart) StartStorage();
            
            var wrapper = new EventData(path, data, isSafe);
            string json = JsonUtility.ToJson(wrapper);
            
            MemoryQueue.Enqueue(json);
            
            SaveToDisk();
            _instance.TrySend();
        }

        //백업
        private static void SaveToDisk()
        {
            var storedList = new List<string>(MemoryQueue);
            if (storedList.Count > MaxStoredEvents) storedList.RemoveRange(0, storedList.Count - MaxStoredEvents);

            GameEvent.QueueData = JsonUtility.ToJson(new JsonListWrapper(storedList));
        }

        //백업 불러오기
        public void LoadFromDisk()
        {
            if (GameEvent.QueueData != null)
            {
                var wrapper = JsonUtility.FromJson<JsonListWrapper>(GameEvent.QueueData);
                foreach (var item in wrapper.items) MemoryQueue.Enqueue(item);
            }
        }

        //전송 시도
        private void TrySend()
        {
            if (_isEnd) return;
            
            StartCoroutine(SendLoop());
        }

        //전송 시작
        private IEnumerator SendLoop()
        {
            if (!_isSending)
            {
                _isSending = true;
                while (!_isEnd && MemoryQueue.Count > 0)
                {
                    _failCount = 0;
                    yield return StartCoroutine(QueueToServer());
                }
                _isSending = false;
            }
        }

        //전송
        private IEnumerator QueueToServer()
        {
            string json = MemoryQueue.Peek();
            var env = JsonUtility.FromJson<EventData>(json);

            string url = $"{CloudRunBaseUrl}{env.eventPath}";
            byte[] bodyRaw = Encoding.UTF8.GetBytes(env.payloadJson);
            
            var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            Debug.LogWarning($"{env.eventPath} : {request.result}");

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (env.isSafeData)
                {
                    _failCount++;
                    if (_failCount > 3)
                    {
                        _isEnd = true;
                    }
                    else
                    {
                        yield return new WaitForSeconds(2f);

                        yield return StartCoroutine(QueueToServer());
                    }
                }
            }
            else
            {
                _updateTime = UpdateMaxTime;
                MemoryQueue.Dequeue();
                SaveToDisk();
            }
        }

        public static void CloseFlow(Action onComplete)
        {
            _instance.StartCoroutine(CloseFlow_Cor(onComplete));
        }

        private static IEnumerator CloseFlow_Cor(Action onComplete)
        {
            yield return new WaitWhile(() => _isSending);

            _isEnd = true;
            onComplete?.Invoke();
        }

        [Serializable]
        private class JsonWrapper
        {
            public Dictionary<string, object> wrapper;
            public JsonWrapper(Dictionary<string, object> d) { wrapper = d; }
        }

        [Serializable]
        private class JsonListWrapper
        {
            public List<string> items;
            public JsonListWrapper(List<string> l) { items = l; }
        }

        [Serializable]
        public class EventData
        {
            public string eventPath; // 예: "user", "event", "session"
            public string payloadJson; // JsonUtility.ToJson()으로 만든 JSON 문자열
            public bool isSafeData; //저장 여부

            public EventData(string path, string json, bool save = true)
            {
                eventPath = path;
                payloadJson = json;
                isSafeData = save;
            }
        }
        
        [Serializable]
        public class UserPayload
        {
            public string user_id;
            public string project_name;
            public string created_at;
            public string country;
            public string platform;
            public string install_source;
            public string ad_campaign;
            public string ad_group;
            public string ad_keyword;
            public string creative_id;
            public string install_Source;
        }    
        
        [Serializable]
        public class DeletePayload
        {
            public string user_id;
            public string project_name;
            public string event_time;
            public string grace_period;
        }
        
        [Serializable]
        public class SessionPayload
        {
            public string user_id;
            public string project_name;
            public string session_id;
            public string event_time;
            public string active_time;
            public string is_closed;
            public string app_version;
            public string platform;
            public string country;
        }
        
        [Serializable]
        public class UpdatePayload
        {
            public string user_id;
            public string project_name;
            public string session_id;
            public string event_time;
        }

        [Serializable]
        public class EventPayload
        {
            public string event_id;
            public string session_id;
            public string user_id;
            public string project_name;
            public string event_time;
            public string event_name;
            public string parameters;
        }

        [Serializable]
        public class PaymentsPayload
        {
            public string event_id;
            public string session_id;
            public string user_id;
            public string project_name;
            public string event_time;
            public string product_id;
        }
    }
}
