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

        public static string ProjectName;
        public static string UserID;
        public static string SessionID;
        public static string Version;
        
        private static string _adCampaign;
        private static string _adGroup;
        private static string _adKeyword;
        private static string _creativeId;
        private static string _installSource;

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

        //서버시간 불러오는 함수 (시간대 정보 필수)
        //SetUpdateTime(() => Cloud.ServerTime)
        public static void SetUpdateTime(Func<DateTime> getter)
        {
            CurrentTime = getter;
        }
        
        //미전송 로그 데이터를 게임서버에 저장하기 위한 연결
        //SetEventQueue(() => Cloud.EventLogData, (q) => Cloud.EventLogData = q)
        public static void SetEventQueue(Func<string> getter, Action<string> setter)
        {
            _getQueueData = getter;
            _setQueueData = setter;
            
            PlayerPrefs.DeleteKey(QueueDataKey);
        }

        //플레이 스토어 전용 설치정보 설정
        public static void InitPlayStoreInfo()
        {
            //Install Referrer 호출
            PlayInstallReferrer.GetInstallReferrerInfo(details =>
            {
                string referrerString = details.InstallReferrer; // ex: utm_source=google&utm_campaign=UA01
                Dictionary<string, string> queryParams = ParseQueryString(referrerString);

                var adCampaign = queryParams.ContainsKey("utm_campaign") ? queryParams["utm_campaign"] : null;
                var adGroup = queryParams.ContainsKey("utm_adgroup") ? queryParams["utm_adgroup"] : null;
                var adKeyword = queryParams.ContainsKey("utm_term") ? queryParams["utm_term"] : null;
                
                InitInstallInfo(InstallSource.GooglePlay, adCampaign, adGroup, adKeyword);
            });
        }

        //앱스토어 전용 설치정보 설정
        public static void InitAppStoreInfo()
        {
            if (!string.IsNullOrEmpty(Application.absoluteURL))
            {
                Uri uri = new Uri(Application.absoluteURL);
                var queryParams = ParseQueryString(uri.Query);

                var adCampaign = queryParams.ContainsKey("utm_campaign") ? queryParams["utm_campaign"] : null;
                var adGroup = queryParams.ContainsKey("utm_adgroup") ? queryParams["utm_adgroup"] : null;
                var adKeyword = queryParams.ContainsKey("utm_term") ? queryParams["utm_term"] : null;

                InitInstallInfo(InstallSource.AppStore, adCampaign, adGroup, adKeyword);
            }
        }
        
        //직접 설치정보 설정
        //installSource : 설치 경로
        //adCampaign : 광고 캠페인
        //adGroup : 광고 그룹
        //adKeyword : 광고 키워드
        public static void InitInstallInfo(string installSource, string adCampaign, string adGroup, string adKeyword)
        {
            _installSource = installSource;
            
            _adCampaign = adCampaign;
            _adGroup = adGroup;
            _adKeyword = adKeyword;
        }

        //프로젝트 설정
        //projectID : 프로젝트ID (프로젝트별 고유한 이름, (예 : SuperPigIdle))
        //userId : 플레이어ID
        //appVersion : 앱 버전 (실제 버전과 다르더라도 업데이트마다 증가해야 함)
        public static void InitProject(string projectId, string userId, int appVersion)
        {
            ProjectName = projectId;
            UserID = GetID(projectId, userId);
            Version = $"{appVersion:0}";
        }
        
        //유저등록 (회원가입 시 최초 1회)
        //country : 생성 국가 (Country 클래스 사용)
        //platform : 시작 플랫폼 (Platform 클래스 사용)
        public static void StartUser(string platform, string country)
        {
            DateTime createdAt = CurrentTime();

            var data = new EventStorage.UserPayload
            {
                user_id = UserID,
                project_name = ProjectName,
                created_at = TimeToString(createdAt),
                country = country,
                platform = platform,
                ad_campaign = _adCampaign,
                ad_group = _adGroup,
                ad_keyword = _adKeyword,
                creative_id = _creativeId,
                install_Source = _installSource
            };
            EventStorage.Enqueue(JsonUtility.ToJson(data), "/collect/user");
        }     
        
        //탈퇴 요청 (요청 완료 후 자동으로 집계 종료)
        //period : 유예 기간
        public static void DeleteUser(TimeSpan period)
        {
            DateTime requestedAt = CurrentTime();

            var data = new EventStorage.DeletePayload
            {
                user_id = UserID,
                project_name = ProjectName,
                event_time = TimeToString(requestedAt),
                grace_period = $"{period.TotalSeconds:0}",
            };
            
            EventStorage.Enqueue(JsonUtility.ToJson(data), "/collect/user_delete");
            CloseSession();
        }

        //세션 시작 (로그인)
        //country : 세션 국가 (Country 클래스 사용)
        //platform : 세션 플랫폼 (Platform 클래스 사용)
        public static void StartSession(string platform, string country)
        {
            DateTime startedAt = CurrentTime();

            SessionID = GetSessionID(UserID, startedAt);
            
            var data = new EventStorage.SessionPayload
            {
                session_id = SessionID,
                user_id = UserID,
                project_name = ProjectName,
                event_time = TimeToString(startedAt),
                active_time = TimeToString(startedAt),
                is_closed = false.ToString(),
                app_version = Version,
                platform = platform,
                country = country
            };
            
            EventStorage.StartStorage();
            EventStorage.Enqueue(JsonUtility.ToJson(data), "/collect/session");
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
                user_id = UserID,
                project_name = ProjectName,
                event_time = TimeToString(closeAt)
            };
            
            EventStorage.Enqueue(JsonUtility.ToJson(data), "/collect/close_session");
            EventStorage.CloseFlow(onComplete);
        }
        
        
        //테스트용 세션
        //startAt : 시작 시각
        //endAt : 종료 시각
        //country : 세션 국가 (Country 클래스 사용)
        //platform : 세션 플랫폼 (Platform 클래스 사용)
        public static void TestSession(DateTime startAt, DateTime endAt, string platform, string country)
        {
            SessionID = GetSessionID(UserID, startAt);

            var data = new EventStorage.SessionPayload
            {
                session_id = SessionID,
                user_id = UserID,
                project_name = ProjectName,
                event_time = TimeToString(startAt),
                active_time = TimeToString(endAt),
                is_closed = true.ToString(),
                app_version = Version,
                platform = platform,
                country = country
            };
            EventStorage.Enqueue(JsonUtility.ToJson(data), "/collect/session");
        }
        
        //결제 이벤트
        //productName : 상품 ID (추후 LookerStudio에 등록)
        public static void SendPaymentEvent(string productName)
        {
            DateTime eventTime = CurrentTime();
            string eventID = GetEventID(SessionID, productName, eventTime);

            var data = new EventStorage.PaymentsPayload
            {
                event_id = eventID,
                session_id = SessionID,
                user_id = UserID,
                project_name = ProjectName,
                product_id = productName,
                event_time = TimeToString(eventTime)
            };
            EventStorage.Enqueue(JsonUtility.ToJson(data), "/collect/payment");
        }       
        
        //광고 이벤트
        //adId : 광고 이름 (광고 위치에 따라 정하기) (예를들어 무료다이아 광고의 경우 free_dia)
        public static void SendAdEvent(string adId)
        {
            SendEvent(Event.AD, new Parameter("ad_id", adId));
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
            string eventID = GetEventID(SessionID, eventName, eventTime);
            var parameter = ToJsonArray(parameters);
            
            var data = new EventStorage.EventPayload
            {
                event_id = eventID,
                session_id = SessionID,
                user_id = UserID,
                project_name = ProjectName,
                event_name = eventName,
                parameters = parameter,
                event_time = TimeToString(eventTime)
            };
            
            EventStorage.Enqueue(JsonUtility.ToJson(data), "/collect/event");
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
        
        private static string GetEventID(string sessionId, string eventName, DateTime time, int hashLength = 8)
        {
            using (var sha1 = SHA1.Create())
            {
                var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes($"{sessionId}_{eventName}"));
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
            return time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        private static string TimeToID(DateTime time)
        {
            return time.ToUniversalTime().ToString("yyMMddHHmmssff");
        }

        private static string ToJsonArray(params Parameter[] fields) {
            if (fields == null || fields.Length == 0) return "[]";

            StringBuilder sb = new StringBuilder();
            sb.Append("[");

            for (int i = 0; i < fields.Length; i++) {
                var field = fields[i];
                sb.Append("{\"")
                    .Append(Escape(field.Key))
                    .Append("\":\"")
                    .Append(Escape(field.Value))
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
        public const string Mac = "mac";
    }

    //국가코드
    //찾는 국가가 없을 경우 ISO-3166 참고해서 임시로 사용
    public static class Country
    {
        public const string Korea = "KR";
        public const string Japan = "JP";
        public const string USA = "US";
        public const string Taiwan = "TW";
        public const string China = "CN";
    }

    //설치 스토어
    public static class InstallSource
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
        public const string AD = "ad";
        public const string Tutorial = "tutorial";
    }

    [Serializable]
    public class Parameter
    {
        public string Key;
        public string Value;

        public Parameter(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }
}
