// Assets/Scripts/Analytics/InstallReferrerBridge.cs
using System;
using UnityEngine;

namespace Truesoft.Analytics
{
    /// <summary>
    /// Unity ↔ Android AAR 브릿지.
    /// AAR 쪽에 com.truesoft.analytics.InstallReferrerHelper 클래스가 있어야 하며,
    ///  - public void init(boolean verbose)
    ///  - public void request(ReferrerCallback callback)
    /// 등의 메서드를 갖고 있어야 합니다.
    ///
    /// ReferrerCallback (Java 인터페이스) 예시:
    /// package com.truesoft.analytics;
    /// public interface ReferrerCallback {
    ///     void onResult(String referrer, long clickTsSeconds, long installTsSeconds);
    /// }
    ///
    /// InstallReferrerHelper.request 구현은 InstallReferrerClient로 값을 얻은 뒤
    /// callback.onResult(ref, clickTs, installTs) 호출 후 endConnection()으로 닫아주세요.
    /// </summary>
    public static class InstallReferrerBridge
    {
        // 결과 모델
        public sealed class ReferrerInfo
        {
            /// <summary>예: "utm_source=google&utm_campaign=UA01" </summary>
            public string installReferrer;
            /// <summary>광고 클릭 시각(초) - Play Install Referrer에서 내려주는 UTC epoch seconds</summary>
            public long referrerClickTimestampSeconds;
            /// <summary>설치 시작 시각(초) - Play Install Referrer에서 내려주는 UTC epoch seconds</summary>
            public long installBeginTimestampSeconds;

            public override string ToString()
                => $"referrer={installReferrer}, clickTs={referrerClickTimestampSeconds}, installTs={installBeginTimestampSeconds}";
        }

#if UNITY_ANDROID //&& !UNITY_EDITOR
        private const string UnityPlayerClass = "com.unity3d.player.UnityPlayer";
        private const string HelperClass      = "com.truesoft.analytics.InstallReferrerHelper";
#endif

        /// <summary>
        /// (선택) 단순 초기화. 내부에서 InstallReferrer 연결을 시작하거나 준비작업을 수행.
        /// Java 쪽에 public void init(boolean verbose) 가 있어야 합니다.
        /// </summary>
        public static void Initialize(bool verbose = false)
        {
#if UNITY_ANDROID //&& !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass(UnityPlayerClass);
                using var activity    = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var helper      = new AndroidJavaObject(HelperClass, activity, verbose);
                helper.Call("init", verbose);
            }
            catch (Exception e)
            {
                if (verbose) Debug.LogWarning($"[InstallReferrer] Initialize failed: {e.Message}");
            }
#else
            if (verbose) Debug.Log("[InstallReferrer] Initialize() noop (not Android device).");
#endif
        }

        /// <summary>
        /// InstallReferrer 값을 **비동기 콜백**으로 받습니다.
        /// AAR의 Helper에 public void request(ReferrerCallback cb) 가 있어야 하며,
        /// cb.onResult(String referrer, long clickTs, long installTs)를 호출해야 합니다.
        /// </summary>
        public static void RequestReferrer(Action<ReferrerInfo> onResult, bool verbose = false)
        {
#if UNITY_ANDROID //&& !UNITY_EDITOR
            if (onResult == null) return;

            try
            {
                using var unityPlayer = new AndroidJavaClass(UnityPlayerClass);
                using var activity    = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var helper      = new AndroidJavaObject(HelperClass, activity, verbose);

                var proxy = new ReferrerCallbackProxy(info =>
                {
                    if (verbose) Debug.Log($"[InstallReferrer] {info}");
                    onResult?.Invoke(info);
                });

                helper.Call("request", proxy);
            }
            catch (Exception e)
            {
                if (verbose) Debug.LogWarning($"[InstallReferrer] RequestReferrer failed: {e.Message}");
                // 실패 시 null 통지 (원하시면 기본값을 내려도 됩니다)
                onResult?.Invoke(null);
            }
#else
            // 에디터/타 플랫폼: 즉시 null 반환(노옵)
            if (verbose) Debug.Log("[InstallReferrer] RequestReferrer() noop (not Android device).");
            onResult?.Invoke(null);
#endif
        }

#if UNITY_ANDROID //&& !UNITY_EDITOR
        /// <summary>
        /// Java 인터페이스 com.truesoft.analytics.ReferrerCallback 에 대응하는 Proxy.
        /// Java 시그니처:
        /// package com.truesoft.analytics;
        /// public interface ReferrerCallback {
        ///     void onResult(String referrer, long clickTsSeconds, long installTsSeconds);
        /// }
        /// </summary>
        private sealed class ReferrerCallbackProxy : AndroidJavaProxy
        {
            private readonly Action<ReferrerInfo> _onResult;

            public ReferrerCallbackProxy(Action<ReferrerInfo> onResult)
                : base("com.truesoft.analytics.ReferrerCallback")
            {
                _onResult = onResult;
            }

            // Java에서 호출되는 메서드명과 시그니처가 정확히 일치해야 합니다.
            public void onResult(string referrer, long clickTsSeconds, long installTsSeconds)
            {
                var info = new ReferrerInfo {
                    installReferrer                  = referrer,
                    referrerClickTimestampSeconds    = clickTsSeconds,
                    installBeginTimestampSeconds     = installTsSeconds
                };
                _onResult?.Invoke(info);
            }
        }
#endif
    }
}