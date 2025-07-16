# Unity Analytics Connector SDK

Unity에서 게임 지표 데이터를 수집하여 서버로 전송하고,  
BigQuery 기반의 분석 및 Looker Studio 시각화를 지원하는 경량 SDK입니다.

## 설치 방법

1. Unity Package Manager에서 다음 Git URL을 사용해 설치하세요.
   `https://github.com/truesoftkorea/GameAnalytics.git`


2. 또는 `Packages/manifest.json`에 아래와 같이 직접 추가할 수 있습니다.
   `"com.truesoft.analytics": "https://github.com/truesoftkorea/GameAnalytics.git"`


3. 특정 버전을 설치하려는 경우  
`https://github.com/truesoftkorea/GameAnalytics.git#1.0.0`  
와 같이 `#버전` 을 추가하여 설치할 수 있습니다.  
사용 가능한 버전은 CHANGELOG를 확인하세요.

## EventStorage 오브젝트 등록

지표 데이터 전송은 `EventStorage.cs`를 통해 이루어지며, 이 스크립트는 MonoBehaviour입니다.  
게임 시작 시 항상 존재하는 오브젝트에 `EventStorage`를 추가해야 합니다.

1. 시작 씬에 새로운 GameObject를 생성합니다. (이름 예: EventStorage)
2. 해당 오브젝트에 `EventStorage` 스크립트를 추가합니다.

## 환경 정보 설정

앱 실행 시 환경 정보를 설정합니다.

`GameEvent.Configure("https://***", true);`

- cloudRunBaseUrl : 이벤트를 전송할 URL (담당자에게 문의)
- testMode : 테스트 모드 여부

## 설치 정보 설정

앱 실행 시 설치 경로 및 광고 유입 정보를 설정합니다.

- Android : `GameEvent.InitPlayStoreInfo();`
- iOS : `GameEvent.InitAppStoreInfo();`
- 기타 수동 설정 : `GameEvent.InitInstallInfo("play_store", "test_campaign");`

## 이벤트 수집 중단

예기치 못한 오류 또는 다른 이유로 인해 이벤트 수집을 중단할 수 있습니다.
* 각 프로젝트 서버의 RemoteConfig를 통해 실시간으로 이벤트 수집 여부를 변경할 수 있습니다.
* 이후 다시 접속할 때까지 이벤트 수집이 불가능합니다.

`GameEvent.CloseEvent()`

## 프로젝트 및 유저 설정

로그인 완료 시 이후 전송할 정보를 설정합니다.

`GameEvent.InitGame("mygame_live", "user_001", 101, Platform.Android, Server.Korea);`

- projectId : 고유 프로젝트 이름 (테스트 : `GameEvent.TestProject`)
- userId : 유저 ID (게임 서버에서 할당 받은 고유ID)
- appVersion : 앱 빌드 버전 (업데이트마다 증가)
- platform : (접속 플랫폼, Platform 클래스 사용)
- server : (글로벌 게임인 경우 유저 그룹 구분, Server 클래스 사용)

## 현재 시각 연동

로그를 전송할 때 현재 시각을 동기화하기 위해 프로젝트에서 사용하는 현재 시각이 필요합니다.  
* 연동하지 않을 경우 자동으로 `DateTime.UtcNow`를 사용합니다.  
* 별도의 게임서버가 있는 경우 반드시 연동이 필요합니다.
* 현재 시각에 시간대 정보를 추가하려면 UTC 시각(한국 시각X)에  
`DateTime.SpecifyKind(time, DateTimeKind.Utc);`를 적용하면 됩니다.

`GameEvent.SetUpdateTime(() => TimeManager.serverTime);`

- getter : 프로젝트에서 실제로 사용하는 현재시각 getter (UTC 정보 필수)

## 미 전송 로그 서버 연동

갑작스러운 종료 또는 오류로 인해 로그가 전송되지 않은 경우를 대비해 미 전송 로그를 서버에 저장합니다.

* 연동하지 않을 경우 자동으로 로컬 위치에 저장합니다.

`GameEvent.SetEventQueue(() => PlayerPrefs.GetString(QueueDataKey, "{}"), s => PlayerPrefs.SetString(QueueDataKey, s));`

- getter : 게임서버에 저장된 미 전송 로그 데이터 getter
- setter : 미 전송 로그 데이터를 서버에 저장하기 위한 setter

## 세션 관리

### 세션 시작 (로그인 완료)

DB에 유저가 접속했음을 알리는 로그를 전송합니다.
* 최초 로그인의 경우 유저 데이터도 자동으로 전송합니다.

`GameEvent.StartSession();`

### 세션 종료 (게임 종료) 

DB에 유저가 접속 종료했음을 알리는 로그를 전송합니다.
* 강제 종료로 인해 전송이 실패한 경우, DB에서 일정시간 후 자동으로 세션을 종료합니다.

`GameEvent.CloseSession(() => Application.Quit());`

- onComplete : 세션 종료 후 행동 (선택)

### 테스트 세션 (임의 시간 지정)

임의의 접속 시간을 지정해 로그를 전송합니다.
* 세션 시작과 세션 종료를 원하는 시간에 전송한 것과 같은 기능입니다.

`GameEvent.TestSession(startAt, endAt);`

- startAt : 세션 시작 시각 (시간대 정보 필수)
- endAt : 세션 종료 시각 (시간대 정보 필수)

## 유저 탈퇴 요청

게임 서버에 탈퇴를 요청한 경우 DB에 유저 데이터 삭제를 예약합니다.
 
`GameEvent.DeleteUser(TimeSpan.FromDays(3));`

- period : 삭제 유예 기간 (즉시 삭제한 경우 `TimeSpan.Zero`)

## 결제 이벤트 전송

상품 결제를 성공한 경우 DB에 구매 데이터를 전송합니다.

`GameEvent.SendPaymentEvent("starter_pack_01");`

- productName : 상품 ID (한글 상품 이름은 DB의 상품 카탈로그에 별도 등록)

## 광고 이벤트 전송

광고 송출을 완료한 경우 DB에 광고 송출 데이터를 전송합니다. 
* 광고 ID는 광고 위치에 따라 임의로 지정합니다.

`GameEvent.SendAdEvent("free_dia_01");`

- adId : 광고 ID (광고 구분을 위한 이름, 한글 이름은 DB의 광고 카탈로그에 별도 등록)

## 이벤트 전송

### 튜토리얼 이벤트 전송

가이드 미션을 완료한 경우 DB에 클리어한 단계를 전송합니다.

`GameEvent.SendTutorialEvent(1);` // 1단계 클리어 시

### 커스텀 이벤트 전송

각 프로젝트별 추가 집계가 필요한 로그 데이터를 전송합니다.

`GameEvent.SendEvent("stage_clear", new Parameter("stage", "3"));`

- eventName : 커스텀 이벤트 이름 (추후 Event 클래스에 등록)
- parameter : 커스텀 이벤트 파라미터 (로그에 필요한 매개변수를 필요한 만큼 지정)

## 전송 구조 요약

- 대부분의 이벤트는 전송 전 메모리 큐 + 로컬 또는 게임 서버에 백업됩니다.
- 중요 이벤트는 실패 시 자동으로 Cloud Run 로그 서버에 전송됩니다.
- 세션 갱신은 10분 주기로 자동 전송됩니다.

## 전송 API 엔드포인트 요약

- /collect/user : 유저 등록
- /collect/user_delete : 유저 탈퇴 요청
- /collect/session : 세션 시작
- /collect/close_session : 세션 종료
- /collect/update : 세션 갱신
- /collect/payment : 결제 이벤트
- /collect/ad : 광고 시청 이벤트
- /collect/event : 튜토리얼, 커스텀 이벤트

## 기타 참고 사항

- 광고 및 상품 ID는 영문 ID로만 전송되며, 이름은 BigQuery의 카탈로그 테이블에서 매칭합니다.
- SDK는 플랫폼(Android, iOS 등), 버전, 설치스토어 등 다양한 메타정보를 포함합니다.
- 유저 ID는 SHA1 해시로 처리되어 프로젝트별 고유 ID로 구성됩니다.

## 샘플 테스트 시나리오

### 1. 게임 실행  

`GameEvent.Configure("https://***", true);`  
`GameEvent.InitInstallInfo(InstallSource.GooglePlay, "adCam");`

### 2. 서버 연결

`GameEvent.SetUpdateTime(() => TimeManager.serverTime);`  
`GameEvent.SetEventQueue(() => null, s => { });`

### 3. 로그인

`GameEvent.InitGame(GameEvent.TestProject, "testUser0", 1, Platform.Android, Server.Korea);`  
`GameEvent.StartSession();`

### 4. 게임 진행

`GameEvent.SendTutorialEvent(1);`  
`GameEvent.SendPaymentEvent("gem_110");`  
`GameEvent.SendAdEvent("test_ad");`

### 5. 게임 종료 또는 탈퇴

`GameEvent.CloseSession(() => Application.Quit());`
or 
`GameEvent.DeleteUser(TimeSpan.Zero);`

## 문의 및 기여

이슈, 기능 제안, 버그 리포트는 GitHub Issue 탭을 통해 공유해 주세요.  
내부 프로젝트 확장이나 기능 추가가 필요한 경우 담당자에게 직접 문의 바랍니다.
