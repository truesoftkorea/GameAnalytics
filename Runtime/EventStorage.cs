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
        private static readonly HashSet<string> AlreadyLoggedErrors = new();
        
        private const int MaxStoredEvents = 500;

        private static bool _isSending;
        private static bool _isInit;
        private static bool _isSession;
        public static bool IsEnd;
        public static bool TestLog;
        public static string CloudRunBaseUrl;

        public static float UpdateTime;
        private const float UpdateMaxTime = 600f;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else
            {
                Destroy(this);
            }
        }

        public static void StartStorage()
        {
            _isSession = true;
            UpdateTime = UpdateMaxTime;
            
            if (!_isInit)
            {
                _isInit = true;
                _instance.LoadFromDisk();
                TrySend();
            }
        }

        private void Update()
        {
            if (!IsEnd && _isSession)
            {
                UpdateTime -= Time.deltaTime;
                if (UpdateTime <= 0 && !_isSending)
                {
                    UpdateTime = UpdateMaxTime;
                    var payload = new UpdatePayload
                    {
                        session_id = GameEvent.SessionID,
                        event_time = GameEvent.TimeToString(GameEvent.CurrentTime())
                    };
                    Enqueue(JsonUtility.ToJson(payload), Path.Update, false, false);
                }
            }
        }

        public static void Enqueue(string data, string path, bool isCritical = true, bool isSafe = true)
        {
            if (IsEnd || !_isSession) return;

            var wrapper = new EventWrapper(new EventData(path, data, isSafe, isCritical));
            MemoryQueue.Enqueue(wrapper);
            SaveToDisk();
            TrySend();
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

        private static void TrySend()
        {
            if (IsEnd) return;
            
            _instance.StartCoroutine(_instance.SendLoop());
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
                MemoryQueue.Dequeue();
                SaveToDisk();
                yield break;
            }

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (env.eventPath == Path.User) GameEvent.MemoryKey = GameEvent.UserID;
                
                if (TestLog) Debug.Log($"{env.eventPath} : {request.result}");
                
                MemoryQueue.Dequeue();
                UpdateTime = UpdateMaxTime;
                SaveToDisk();
            }
            else
            {
                if (TestLog)
                {
                    Debug.LogWarning($"[EventStorage] Failed {wrapper.retryCount} → {env.eventPath}");
                    Debug.LogWarning(env.payloadJson);
                }

                if (env.isSafeData)
                {
                    wrapper.retryCount++;

                    if (wrapper.retryCount >= 3)
                    {
                        _instance.StartCoroutine(SendFailureLog(wrapper, request.responseCode.ToString(), request.downloadHandler.text));

                        if (env.isCritical)
                        {
                            env.isCritical = false;
                            wrapper.retryCount = 0;
                            MemoryQueue.Dequeue();
                            MemoryQueue.Enqueue(wrapper); // 다시 뒤로 보내서 재시도
                        }
                        else
                        {
                            MemoryQueue.Dequeue(); // 중요하지 않은 이벤트는 폐기
                        }
                    }
                    else
                    {
                        yield return new WaitForSeconds(1f); // 재시도 대기
                    }
                }
                else
                {
                    MemoryQueue.Dequeue(); // 중요하지 않은 이벤트는 폐기
                }

                SaveToDisk();
            }
        }

        private IEnumerator SendFailureLog(EventWrapper wrapper, string responseCode, string errorText)
        {
            string logKey = $"{GameEvent.UserID}_{wrapper.eventData.eventPath}_{responseCode}_{errorText}".GetHashCode().ToString();

            if (AlreadyLoggedErrors.Contains(logKey)) yield break;

            AlreadyLoggedErrors.Add(logKey);

            var body = new FailureLogPayload
            {
                user_id = GameEvent.UserID,
                seeeion_id = GameEvent.SessionID,
                event_path = wrapper.eventData.eventPath,
                payload_json = wrapper.eventData.payloadJson,
                error_message = $"{responseCode} : {errorText}",
                event_time = GameEvent.TimeToString(GameEvent.CurrentTime())
            };

            var json = JsonUtility.ToJson(body);
            byte[] raw = Encoding.UTF8.GetBytes(json);

            using var request = new UnityWebRequest($"{CloudRunBaseUrl}/{Path.FailureLog}", "POST")
            {
                uploadHandler = new UploadHandlerRaw(raw),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();
        }

        public static void CloseFlow(Action onComplete)
        {
            _instance.StartCoroutine(CloseFlow_Cor(onComplete));
        }

        private static IEnumerator CloseFlow_Cor(Action onComplete)
        {
            yield return new WaitWhile(() => _isSending);
            
            IsEnd = true;
            _isSession = false;

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
            public bool isCritical;

            public EventData(string path, string json, bool save = true, bool critical = false)
            {
                eventPath = path;
                payloadJson = json;
                isSafeData = save;
                isCritical = critical;
            }
        }
        
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
            public string product_name;
            public string store;

            public string product_id;
            public string package_name;
            public string purchase_token;
            public string receipt_data;
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
        
        [Serializable]
        public class FailureLogPayload
        {
            public string user_id;
            public string seeeion_id;
            public string event_path;
            public string payload_json;
            public string error_message;
            public string event_time;
        }
    }
}
