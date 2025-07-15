using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Truesoft.Analytics
{
    public class EventStorage : MonoBehaviour
    {
        private static EventStorage _instance;
        private static readonly Queue<EventWrapper> MemoryQueue = new();
        private const int MaxStoredEvents = 500;

        private static bool _isSending;
        private static bool _isSession;
        public static bool IsEnd;
        public static bool TestLog;
        public static string CloudRunBaseUrl;

        private static float _updateTime;
        private const float UpdateMaxTime = 600f;

        private void Awake()
        {
            if (_instance != null)
            {
                Destroy(_instance);
                return;
            }

            _instance = this;
            _updateTime = UpdateMaxTime;
            DontDestroyOnLoad(gameObject);
        }

        public static void StartStorage()
        {
            _isSession = true;
            _instance.LoadFromDisk();
            _instance.TrySend();
        }

        private void Update()
        {
            if (!IsEnd && _isSession)
            {
                _updateTime -= Time.deltaTime;
                if (_updateTime <= 0 && !_isSending)
                {
                    _updateTime = UpdateMaxTime;
                    var payload = new UpdatePayload
                    {
                        session_id = GameEvent.SessionID,
                        event_time = GameEvent.TimeToString(GameEvent.CurrentTime())
                    };
                    Enqueue(JsonUtility.ToJson(payload), Path.Update, false);
                }
            }
        }

        public static void Enqueue(string data, string path, bool isSafe = true, bool isCritical = false)
        {
            if (IsEnd || !_isSession) return;

            var wrapper = new EventWrapper(new EventData(path, data, isSafe, isCritical));
            MemoryQueue.Enqueue(wrapper);
            SaveToDisk();
            _instance.TrySend();
        }

        private static void SaveToDisk()
        {
            var list = new List<EventWrapper>(MemoryQueue);
            if (list.Count > MaxStoredEvents)
                list.RemoveRange(0, list.Count - MaxStoredEvents);

            GameEvent.QueueData = JsonUtility.ToJson(new JsonWrapper(GameEvent.UserID, list));
        }

        private void LoadFromDisk()
        {
            var raw = GameEvent.QueueData;
            if (!string.IsNullOrEmpty(raw))
            {
                var wrapper = JsonUtility.FromJson<JsonWrapper>(raw);
                GameEvent.MemoryKey = wrapper.userId;
                foreach (var item in wrapper.items)
                    MemoryQueue.Enqueue(item);
            }
        }

        private void TrySend()
        {
            if (IsEnd) return;
            StartCoroutine(SendLoop());
        }

        private IEnumerator SendLoop()
        {
            if (_isSending) yield break;

            _isSending = true;
            while (!IsEnd && MemoryQueue.Count > 0)
            {
                yield return StartCoroutine(QueueToServer());
            }
            _isSending = false;
        }

        private IEnumerator QueueToServer()
        {
            var wrapper = MemoryQueue.Peek();
            var env = wrapper.eventData;

            UnityWebRequest request = null;

            try
            {
                string url = $"{CloudRunBaseUrl}{env.eventPath}";
                byte[] bodyRaw = Encoding.UTF8.GetBytes(env.payloadJson);

                request = new UnityWebRequest(url, "POST")
                {
                    uploadHandler = new UploadHandlerRaw(bodyRaw),
                    downloadHandler = new DownloadHandlerBuffer()
                };
                request.SetRequestHeader("Content-Type", "application/json");
            }
            catch (Exception e)
            {
                if (TestLog) Debug.LogError(e);
                MemoryQueue.Dequeue(); // malformed → 제거
                SaveToDisk();
                yield break;
            }

            yield return request.SendWebRequest();

            if (TestLog)
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"{env.eventPath} : {request.result}");
                    Debug.LogError(env.payloadJson);
                }
                else
                {
                    Debug.Log($"{env.eventPath} : {request.result}");
                }
            }

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (env.eventPath == Path.User)
                    GameEvent.MemoryKey = GameEvent.UserID;

                MemoryQueue.Dequeue();
                _updateTime = UpdateMaxTime;
                SaveToDisk();
            }
            else
            {
                if (env.isSafeData || env.isCritical)
                {
                    wrapper.retryCount++;

                    if (wrapper.retryCount >= 3)
                    {
                        if (env.isCritical)
                        {
                            Debug.LogWarning("[EventStorage] Critical event failed 3 times. Moving to back of queue.");
                            MemoryQueue.Dequeue();
                            MemoryQueue.Enqueue(wrapper); // 뒤로 밀기
                        }
                        else
                        {
                            MemoryQueue.Dequeue(); // 폐기
                        }
                    }
                    else
                    {
                        yield return new WaitForSeconds(1f); // 재시도 대기
                    }
                }
                else
                {
                    MemoryQueue.Dequeue(); // 덜 중요한 이벤트는 즉시 폐기
                }

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
            IsEnd = true;
            onComplete?.Invoke();
        }

        [Serializable]
        public class JsonWrapper
        {
            public string userId;
            public List<EventWrapper> items;

            public JsonWrapper(string userId, List<EventWrapper> items)
            {
                this.userId = userId;
                this.items = items;
            }
        }

        [Serializable]
        public class EventWrapper
        {
            public EventData eventData;
            public int retryCount;

            public EventWrapper(EventData data)
            {
                eventData = data;
                retryCount = 0;
            }
        }

        [Serializable]
        public class EventData
        {
            public string eventPath;
            public string payloadJson;
            public bool isSafeData;
            public bool isCritical; // ✅ 추가된 중요도 구분

            public EventData(string path, string json, bool save = true, bool critical = false)
            {
                eventPath = path;
                payloadJson = json;
                isSafeData = save;
                isCritical = critical;
            }
        }

        // Payload 타입 정의들
        [Serializable]
        public class UserPayload
        {
            public string user_id;
            public string project_name;
            public string created_at;
            public string server;
            public string install_source;
            public string ad_campaign;
        }    
        
        [Serializable]
        public class DeletePayload
        {
            public string user_id;
            public string event_time;
            public string grace_period;
        }
        
        [Serializable]
        public class SessionPayload
        {
            public string user_id;
            public string session_id;
            public string event_time;
            public string active_time;
            public string is_closed;
            public string app_version;
            public string platform;
        }
        
        [Serializable]
        public class UpdatePayload
        {
            public string session_id;
            public string event_time;
        }

        [Serializable]
        public class EventPayload
        {
            public string event_id;
            public string session_id;
            public string user_id;
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
            public string event_time;
            public string product_id;
        }    
        
        [Serializable]
        public class AdPayload
        {
            public string event_id;
            public string session_id;
            public string user_id;
            public string event_time;
            public string ad_id;
        }
    }
}
