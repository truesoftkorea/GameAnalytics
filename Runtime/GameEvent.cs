using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Truesoft.Analytics
{
    public static class GameEvent
    {
        public const string QueueDataKey = "Truesoft.Analytics.QueueData";
        public const string CampaignKey = "Truesoft.Analytics.CampaignKey";

        //저장된 유저 데이터 KEY
        public static string MemoryKey;

        //초기 설정 등록
        public static string ProjectName;
        public static string UserID;
        public static string SessionID;
        public static string Version;
        public static string RunPlatform;
        public static string InstallStore;
        public static string Server;
        
        public static Func<DateTime> CurrentTime = () => DateTime.UtcNow;
        public static string QueueData
        {
            get => _getQueueData != null ? _getQueueData() : PlayerPrefs.GetString(QueueDataKey, "{}");
            set
            {
                if (_setQueueData != null) _setQueueData(value);
                else PlayerPrefs.SetString(QueueDataKey, value);
            }
        }

        private static Func<string> _getQueueData;
        private static Action<string> _setQueueData;

        private const string TestProject = "test_game";

        //환경 설정
        //cloudRunBaseUrl : 이벤트를 전송할 URL
        //testMode : 테스트 모드 여부
        public static void Configure(string cloudRunBaseUrl, bool testMode)
        {
            EventStorage.CloudRunBaseUrl = cloudRunBaseUrl;
            EventStorage.TestLog = testMode;
        }

        //이벤트 수집 종료
        public static void CloseEvent()
        {
            EventStorage.IsEnd = true;
        }

        //서버시간 불러오는 함수 (필수, 시간대 정보 포함)
        //SetUpdateTime(() => Cloud.ServerTime)
        public static void SetUpdateTime(Func<DateTime> getter)
        {
            CurrentTime = getter;
        }
        
        //미전송 로그 데이터를 게임서버에 저장하기 위한 연결(필수)
        //SetEventQueue(() => Cloud.EventLogData, (q) => Cloud.EventLogData = q)
        public static void SetEventQueue(Func<string> getter, Action<string> setter)
        {
            _getQueueData = getter;
            _setQueueData = setter;
            
            PlayerPrefs.DeleteKey(QueueDataKey);
        }

        //플레이 스토어 전용 설치정보 설정(필수)
        public static void InitInstallInfo()
        {
#if UNITY_ANDROID
            InitAndroidInfo();
#elif UNITY_IOS
            InitIOSInfo();
#elif UNITY_STANDALONE_WIN
            InitWindowsInfo();
#endif
        }

        private static void InitAndroidInfo()
        {
            RunPlatform = Platform.Android;
            InstallStore = Store.None;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var buildVersion = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    int sdkInt = buildVersion.GetStatic<int>("SDK_INT");

                    using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
                    {
                        string installer;
                        var packageName = activity.Call<string>("getPackageName");
                        if (sdkInt >= 30)
                        {
                            // Android 11 이상: getInstallSourceInfo()
                            using var installInfo = packageManager.Call<AndroidJavaObject>("getInstallSourceInfo", packageName);
                            installer = installInfo.Call<string>("getInstallingPackageName");
                        }
                        else
                        {
                            // Android 10 이하: getInstallerPackageName()
                            installer = packageManager.Call<string>("getInstallerPackageName", packageName);
                        }
                        
                        switch (installer)
                        {
                            case "com.android.vending":
                                InstallStore = Store.GooglePlay;
                                break;
                            case "com.samsung.android.app.seller":
                                InstallStore = Store.GalaxyStore;
                                break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("GetInstallSource() failed: " + e.Message);
            }
#endif
            
            //Install Referrer 호출
            if (InstallStore == Store.GooglePlay)
            {
                PlayInstallReferrer.GetInstallReferrerInfo(details =>
                {
                    string referrerString = details.InstallReferrer; // ex: utm_source=google&utm_campaign=UA01
                    Dictionary<string, string> queryParams = ParseQueryString(referrerString);

                    var adCampaign = queryParams.ContainsKey("utm_campaign") ? queryParams["utm_campaign"] : null;
                
                    InitAdInfo(adCampaign);
                });
            }
        }

        //앱스토어 전용 설치정보 설정(필수)
        private static void InitIOSInfo()
        {
            RunPlatform = Platform.Android;
            InstallStore = Store.AppStore;

            if (!string.IsNullOrEmpty(Application.absoluteURL))
            {
                Uri uri = new Uri(Application.absoluteURL);
                var queryParams = ParseQueryString(uri.Query);

                var adCampaign = queryParams.ContainsKey("utm_campaign") ? queryParams["utm_campaign"] : null;

                InitAdInfo(adCampaign);
            }
        }

        //윈도우 전용 설치정보 설정(임시)
        private static void InitWindowsInfo()
        {
            RunPlatform = Platform.Windows;
            InstallStore = Store.Steam;
        }
        
        //직접 설치정보 설정(필수)
        //adCampaign : 광고 캠페인
        private static void InitAdInfo(string adCampaign = null)
        {
            PlayerPrefs.SetString(CampaignKey, adCampaign);
        }

        //초기 설정(필수)
        //자동으로 사용 설정 (수집을 원천 차단하려면 이후 세션시작 전 SetEnable로 중단)
        //projectID : 프로젝트ID (프로젝트별 고유한 이름, (예 : SuperPigIdle))
        //userId : 플레이어ID
        //appVersion : 앱 버전 (실제 버전과 다르더라도 업데이트마다 증가해야 함)
        //platform : 접속 플랫폼 (Platform 클래스 사용, 추가할 플랫폼이 있다면 담당자 문의)
        //store : 앱을 설치한 스토어 (Store 클래스 사용, 추가할 스토어가 있다면 담당자 문의)
        //server : 접속 서버 (Server 클래스 사용, 추가할 서버가 있다면 담당자 문의)
        public static void InitGame(string projectId, string userId, int appVersion, string server)
        {
            if (EventStorage.TestLog) projectId = TestProject;
            
            ProjectName = projectId;
            UserID = GetID(projectId, userId);
            Version = $"{appVersion:0}";
            Server = server;
        }
        
        //탈퇴 요청 (요청 완료 후 자동으로 집계 종료)
        //period : 유예 기간
        public static void DeleteUser(TimeSpan period)
        {
            DateTime requestedAt = CurrentTime();

            var data = new EventStorage.DeletePayload
            {
                user_id = UserID,
                event_time = TimeToString(requestedAt),
                grace_period = $"{period.TotalSeconds:0}",
            };
            
            EventStorage.Enqueue(JsonUtility.ToJson(data), Path.UserDelete);
            CloseSession();
        }

        //세션 시작 (로그인)
        public static void StartSession()
        {
            DateTime startedAt = CurrentTime();

            SessionID = GetSessionID(UserID, startedAt);
            
            var data = new EventStorage.SessionPayload
            {
                session_id = SessionID,
                user_id = UserID,
                event_time = TimeToString(startedAt),
                active_time = TimeToString(startedAt),
                is_closed = false.ToString(),
                app_version = Version,
                platform = RunPlatform,
            };
            
            EventStorage.StartStorage();
            
            StartUser(startedAt);
            EventStorage.Enqueue(JsonUtility.ToJson(data), Path.Session);
        }
        
        //유저등록 (회원가입 시 최초 1회)
        private static void StartUser(DateTime createdAt)
        {
            if (MemoryKey == UserID) return;
            
            var data = new EventStorage.UserPayload
            {
                user_id = UserID,
                project_name = ProjectName,
                created_at = TimeToString(createdAt),
                server = Server,
                ad_campaign = PlayerPrefs.GetString(CampaignKey, null),
                install_source = InstallStore
            };

            EventStorage.Enqueue(JsonUtility.ToJson(data), Path.User);
        }     

        //세션 종료 (게임 종료, 선택사항)
        //정확한 종료 타이밍 수집을 위한 기능, 실패해도 큰 리스크 없음
        //onComplete : 처리 완료 액션
        public static void CloseSession(Action onComplete = null)
        {
            DateTime closeAt = CurrentTime();
            
            var data = new EventStorage.UpdatePayload
            {
                session_id = SessionID,
                event_time = TimeToString(closeAt)
            };
            
            EventStorage.Enqueue(JsonUtility.ToJson(data), Path.CloseSession, false);
            EventStorage.CloseFlow(onComplete);
        }
        
        //테스트용 세션
        //startAt : 시작 시각
        //endAt : 종료 시각
        //country : 세션 서버 (Country 클래스 사용)
        //platform : 세션 플랫폼 (Platform 클래스 사용)
        public static void TestSession(DateTime startAt, DateTime endAt)
        {
            SessionID = GetSessionID(UserID, startAt);

            var data = new EventStorage.SessionPayload
            {
                session_id = SessionID,
                user_id = UserID,
                event_time = TimeToString(startAt),
                active_time = TimeToString(endAt),
                is_closed = true.ToString(),
                app_version = Version,
                platform = RunPlatform,
            };
            
            EventStorage.StartStorage();
            
            StartUser(startAt);
            EventStorage.Enqueue(JsonUtility.ToJson(data), Path.Session);
        }

        //결제 이벤트
        //기타 스토어 영수증 검증은 담당자 문의 바랍니다.
        //productName : 상품 이름 (추후 LookerStudio에 등록)
        //receipt : 상품 영수증 (영수증 검증용) e.purchasedProduct.receipt
        public static void SendPaymentEvent(string productName, string receipt)
        {
            var wrapper = JsonUtility.FromJson<UnityReceiptWrapper>(receipt);

            if (wrapper.Store == "GooglePlay")
            {
                var payload = JsonUtility.FromJson<GooglePayload>(wrapper.Payload);
                var inner = JsonUtility.FromJson<GoogleInnerJson>(payload.json);

                SendGooglePaymentEvent(productName, inner.productId, inner.purchaseToken, inner.packageName);
            }
            else if (wrapper.Store == "AppleAppStore")
            {
                SendApplePaymentEvent(productName, wrapper.Payload);
            }
        }
        
        //플레이 스토어 결제
        //productName : 상품 이름 (추후 LookerStudio에 등록)
        //productId : 상품 ID (영수증 검증용, 예: com.some.thing.inapp1) purchasedProduct.definition.storeSpecificId
        //purchaseToken : 결제 토큰 (영수증 검증용)
        //packageName : 앱 패키지 이름 (영수증 검증용, 예: com.some.thing)
        private static void SendGooglePaymentEvent(string productName, string productId, string purchaseToken, string packageName)
        {
            DateTime eventTime = CurrentTime();
            string eventID = GetEventID(SessionID, productName, eventTime);

            var data = new EventStorage.PaymentsPayload
            {
                event_id = eventID,
                session_id = SessionID,
                user_id = UserID,
                product_name = productName,
                event_time = TimeToString(eventTime),
                store = Store.GooglePlay,
                product_id = productId,
                purchase_token = purchaseToken,
                package_name = packageName,
            };
            
            EventStorage.Enqueue(JsonUtility.ToJson(data), Path.Payment);
        }         
        
        //앱스토어 결제
        //productName : 상품 이름 (추후 LookerStudio에 등록)
        //receipt : 상품 영수증 (영수증 검증용)
        private static void SendApplePaymentEvent(string productName, string payload)
        {
            DateTime eventTime = CurrentTime();
            string eventID = GetEventID(SessionID, productName, eventTime);

            var data = new EventStorage.PaymentsPayload
            {
                event_id = eventID,
                session_id = SessionID,
                user_id = UserID,
                product_name = productName,
                event_time = TimeToString(eventTime),
                store = Store.AppStore,
                receipt_data = payload,
            };
            
            EventStorage.Enqueue(JsonUtility.ToJson(data), Path.Payment);
        }

        //광고 이벤트
        //adId : 광고 이름 (광고 위치에 따라 정하기) (예를들어 무료다이아 광고의 경우 free_dia)
        public static void SendAdEvent(string adId)
        {
            DateTime eventTime = CurrentTime();
            string eventID = GetEventID(SessionID, adId, eventTime);

            var data = new EventStorage.AdPayload
            {
                event_id = eventID,
                session_id = SessionID,
                user_id = UserID,
                ad_id = adId,
                event_time = TimeToString(eventTime)
            };
            
            EventStorage.Enqueue(JsonUtility.ToJson(data), Path.Ad, false);
        } 
        
        //튜토리얼 이벤트
        //step : 클리어 단계 (미션 클리어 타이밍에 전송) (1부터 시작)
        public static void SendTutorialEvent(int step)
        {
            SendEvent(Event.Tutorial, new Parameter("step", $"{step}"));
        }
        
        //커스텀 이벤트
        //eventName : 이벤트 이름
        //parameter : 이벤트 파라미터 (Json)
        public static void SendEvent(string eventName, params Parameter[] parameters)
        {
            DateTime eventTime = CurrentTime();
            var parameter = ToJsonArray(parameters);
            string eventID = GetEventID(SessionID, $"{eventName}-{parameter}", eventTime);
            
            var data = new EventStorage.EventPayload
            {
                event_id = eventID,
                session_id = SessionID,
                user_id = UserID,
                event_name = eventName,
                parameters = parameter,
                event_time = TimeToString(eventTime)
            };
            
            EventStorage.Enqueue(JsonUtility.ToJson(data), Path.Event, false);
        }
        
        private static string GetID(string projectId, string playerId, int hashLength = 8)
        {
            using (var sha1 = SHA1.Create())
            {
                var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes($"{projectId}_{playerId}"));
                return BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, hashLength).ToLower();
            }
        }
        
        private static string GetSessionID(string userId, DateTime time)
        {
            return $"{userId}_{TimeToID(time)}";
        }    
        
        private static string GetEventID(string sessionId, string eventData, DateTime time, int hashLength = 8)
        {
            using (var sha1 = SHA1.Create())
            {
                var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes($"{sessionId}_{eventData}"));
                string hex = BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, hashLength).ToLower();
                
                return $"{hex}_{TimeToID(time)}";
            }
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(query)) return result;

            string[] pairs = query.TrimStart('?').Split('&');
            foreach (string pair in pairs)
            {
                if (string.IsNullOrWhiteSpace(pair)) continue;
                var parts = pair.Split('=');
                if (parts.Length == 2) result[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
            }
            return result;
        }

        public static string TimeToString(DateTime time)
        {
            //시간대 임시 보정
            if (time.Kind == DateTimeKind.Unspecified)
            {
                if (EventStorage.TestLog) Debug.LogWarning("현재 시각의 시간대 정보가 지정되지 않았습니다.\nSetUpdateTime()에서 올바른 시각을 연동해 주세요.");

                time = time.AddHours(-9);
            }
            
            return time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        private static string TimeToID(DateTime time)
        {
            //시간대 임시 보정
            if (time.Kind == DateTimeKind.Unspecified)
            {
                if (EventStorage.TestLog) Debug.LogWarning("현재 시각의 시간대 정보가 지정되지 않았습니다.\nSetUpdateTime()에서 올바른 시각을 연동해 주세요.");

                time = time.AddHours(-9);
            }

            return time.ToUniversalTime().ToString("yyMMddHHmmssff");
        }

        private static string ToJsonArray(params Parameter[] fields) {
            if (fields == null || fields.Length == 0) return "[]";

            StringBuilder sb = new StringBuilder();
            sb.Append("[");

            for (int i = 0; i < fields.Length; i++) {
                var field = fields[i];
                sb.Append("{\"")
                    .Append(Escape(field.key))
                    .Append("\":\"")
                    .Append(Escape(field.value))
                    .Append("\"}");

                if (i < fields.Length - 1)
                    sb.Append(",");
            }
            
            sb.Append("]");
            return sb.ToString();
        }

        private static string Escape(string input)
        {
            return input?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
        }

    }
    
    //플랫폼 코드
    public static class Platform
    {
        public const string Android = "android";
        public const string IOS = "ios";
        public const string Windows = "windows";
    }

    //서버코드
    //찾는 서버가 없을 경우 임의 문자열 사용 후 후속조치
    public static class Server
    {
        public const string Korea = "Korea";
        public const string Global = "Global";
        public const string China = "China";
    }

    //설치 스토어
    public static class Store
    {
        //정보 없음
        public const string None = "none";

        public const string GooglePlay = "play_store";
        public const string AppStore = "app_store";
        public const string GalaxyStore = "galaxy_store";
        public const string Steam = "steam";
    }

    //이벤트 ID
    public static class Event
    {
        public const string Tutorial = "tutorial";
    }
    
    //경로
    public static class Path
    {
        public const string User = "/collect/user";
        public const string UserDelete = "/collect/user_delete";
        public const string Session = "/collect/session";
        public const string CloseSession = "/collect/close_session";
        public const string Payment = "/collect/payment";
        public const string Ad = "/collect/ad";
        public const string Event = "/collect/event";
        public const string Update = "/collect/update";
        public const string FailureLog = "/failure_log";
    }

    [Serializable]
    public class Parameter
    {
        public string key;
        public string value;

        public Parameter(string key, string value)
        {
            this.key = key;
            this.value = value;
        }
    }
    
    [Serializable]
    public class UnityReceiptWrapper
    {
        public string Store;
        public string TransactionID;
        public string Payload;
    }

    [Serializable]
    public class GooglePayload
    {
        public string json;
        public string signature;
    }

    [Serializable]
    public class GoogleInnerJson
    {
        public string productId;
        public string purchaseToken;
        public string packageName;
    }
}
