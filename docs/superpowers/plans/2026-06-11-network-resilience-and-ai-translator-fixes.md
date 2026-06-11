# Network Resilience & AI Translator Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 오프라인 서킷 브레이커, AI 번역기별 타임아웃 설정, AI 모델 목록/설정 캐시 버그 수정, Gemini 페이지네이션, LibreTranslate/DeepL 에러 노출을 구현한다.

**Architecture:** `NetworkHelper`(모든 HTTP의 단일 경로)에 타임아웃 파라미터와 실패 분류를 추가하고, 신규 `NetworkStateMonitor`(정적 서킷 브레이커)가 연결 수준 실패를 집계해 `TranslatorManager` 큐 루프를 일시정지/프로브/재개시킨다. `Translator_BaseOnlineAIModel`의 설정 캐시(`_model`/`_rotater`/`_baseURL`)를 변경 감지형으로 교체하고 모델 목록 실패의 실제 원인을 UI에 노출한다.

**Tech Stack:** C# 7.3, .NET Framework 4.7.2, RimWorld 1.6 modding API (Verse/RimWorld), MSBuild (Debug1.6).

**검증 게이트:** 유닛 테스트 인프라 없음(게임 런타임 의존). 각 태스크의 게이트는 아래 빌드 명령의 성공이다.

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "Source\AutoTranslation\AutoTranslation.csproj" /p:Configuration=Debug1.6 /v:minimal /nologo
```

Expected: 마지막 줄 `AutoTranslation -> ...\1.6\Assemblies\AutoTranslation.dll`, exit code 0. (기존 경고 CS0168 2건은 Task 6에서 사라짐)

**커밋 규칙:** `git add <명시된 파일만>` — 워킹 트리에 사용자의 진행 중인 작업이 있으므로 절대 `git add -A`/`git add .`를 쓰지 않는다.

---

### Task 1: 베이스라인 — 진행 중이던 Gist 작업의 빌드 수정 커밋

이전 세션에서 빌드를 고치기 위해 적용한 두 수정(csproj에 Gist 파일 4개 등록, `Settings.cs`의 `SelectedModel` → `UserSelectedModel`)이 워킹 트리에 있다. 이후 태스크들이 `Settings.cs`를 건드리므로, 사용자의 Gist 작업 전체를 먼저 별도 커밋으로 분리한다.

**Files:**
- Commit: `Source/AutoTranslation/AutoTranslation.csproj`, `Source/AutoTranslation/Settings.cs`, `Source/AutoTranslation/CompatibilityPatches.cs`, `Source/AutoTranslation/Services/GistService.cs`, `Source/AutoTranslation/Services/TranslationSerializer.cs`, `Source/AutoTranslation/UI/Dialog_GistPreview.cs`, `Source/AutoTranslation/UI/Dialog_GistMerge.cs`

- [ ] **Step 1: 빌드로 현재 상태 확인**

빌드 명령 실행. Expected: 성공 (경고 CS0168 2건만).

- [ ] **Step 2: Gist 작업 + 빌드 수정 커밋**

```powershell
git add "Source/AutoTranslation/AutoTranslation.csproj" "Source/AutoTranslation/Settings.cs" "Source/AutoTranslation/CompatibilityPatches.cs" "Source/AutoTranslation/Services/GistService.cs" "Source/AutoTranslation/Services/TranslationSerializer.cs" "Source/AutoTranslation/UI/Dialog_GistPreview.cs" "Source/AutoTranslation/UI/Dialog_GistMerge.cs"
git commit -m "feat: gist translation sharing (wip) + register files in csproj, fix UserSelectedModel field name"
```

---

### Task 2: NetworkHelper — TLS 초기화, 타임아웃 파라미터, 실패 분류, 에러 메시지 추출

**Files:**
- Modify: `Source/AutoTranslation/Utilities/NetworkHelper.cs`

- [ ] **Step 1: 상수/정적 생성자 추가**

`NetworkHelper.cs`의 상수 블록(13~15행)을 다음으로 교체:

```csharp
        private const int DEFAULT_RETRIES = 5;
        private const int BASE_DELAY_MS = 5000;
        private const int RATE_LIMIT_DELAY_MS = 30000; // 30 seconds for 429 errors
        public const int DEFAULT_TIMEOUT_MS = 30000;

        static NetworkHelper()
        {
            try
            {
                // Unity Mono may default to TLS 1.0/1.1 only; OR-in TLS 1.2 (do not overwrite -
                // overwriting could break other mods' requests)
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                // Some non-standard servers (Papago/Yandex) mishandle Expect: 100-continue on POST
                ServicePointManager.Expect100Continue = false;
            }
            catch (Exception e)
            {
                Log.Warning($"{AutoTranslation.LogPrefix} Failed to configure ServicePointManager: {e.Message}");
            }
        }
```

- [ ] **Step 2: Post/Get 시그니처에 timeoutMs 추가**

```csharp
        public static string Post(string url, string body, Dictionary<string, string> headers = null, string contentType = "application/json", int maxRetries = DEFAULT_RETRIES, int timeoutMs = DEFAULT_TIMEOUT_MS)
```

```csharp
        public static string Get(string url, Dictionary<string, string> headers = null, int maxRetries = DEFAULT_RETRIES, int timeoutMs = DEFAULT_TIMEOUT_MS)
```

두 메서드 내부의 `CreateRequest(url, "POST", headers)` / `CreateRequest(url, "GET", headers)` 호출을 `CreateRequest(url, "POST", headers, timeoutMs)` / `CreateRequest(url, "GET", headers, timeoutMs)`로 변경.

`CreateRequest` 시그니처를 `private static WebRequest CreateRequest(string url, string method, Dictionary<string, string> headers, int timeoutMs)`로 바꾸고 본문의 `request.Timeout = 30000; // 30s timeout`을 `request.Timeout = timeoutMs;`로 교체.

- [ ] **Step 3: 실패 분류 + 에러 추출 public 헬퍼 추가**

`GetResponseText` 메서드 아래에 추가:

```csharp
        /// <summary>
        /// True when the failure indicates the endpoint could not be reached at all
        /// (offline, DNS failure, refused connection) as opposed to a server-side error response.
        /// </summary>
        public static bool IsConnectionLevelFailure(WebException ex)
        {
            return ex.Status == WebExceptionStatus.NameResolutionFailure ||
                   ex.Status == WebExceptionStatus.ConnectFailure ||
                   ex.Status == WebExceptionStatus.Timeout ||
                   ex.Status == WebExceptionStatus.ConnectionClosed ||
                   ex.Status == WebExceptionStatus.ReceiveFailure ||
                   ex.Status == WebExceptionStatus.SendFailure;
        }

        /// <summary>
        /// Extracts a human-readable error from an exception, including the API error body
        /// (e.g. Gemini's "API key not valid") when the server returned one.
        /// </summary>
        public static string ExtractErrorMessage(Exception e)
        {
            if (e is WebException webEx)
            {
                try
                {
                    if (webEx.Response is HttpWebResponse resp)
                    {
                        string body;
                        using (var stream = resp.GetResponseStream())
                        {
                            if (stream == null) return $"HTTP {(int)resp.StatusCode} {resp.StatusCode}";
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                body = reader.ReadToEnd();
                            }
                        }

                        var message = body.GetStringValueFromJson("message") ?? body.GetStringValueFromJson("error");
                        var prefix = $"HTTP {(int)resp.StatusCode}";
                        if (!string.IsNullOrEmpty(message)) return $"{prefix}: {message}";
                        if (!string.IsNullOrEmpty(body)) return $"{prefix}: {body.Substring(0, Math.Min(200, body.Length))}";
                        return prefix;
                    }
                }
                catch
                {
                    // fall through to status-based message
                }
                return $"{webEx.Status}: {webEx.Message}";
            }
            return e.Message;
        }
```

(`GetStringValueFromJson`은 `AutoTranslation.Utilities.Helpers`의 internal 확장 메서드 — 같은 어셈블리이므로 사용 가능. 파일 상단 using에 `AutoTranslation.Utilities`는 이미 같은 네임스페이스.)

- [ ] **Step 4: 빌드 확인**

빌드 명령 실행. Expected: 성공.

- [ ] **Step 5: 커밋**

```powershell
git add "Source/AutoTranslation/Utilities/NetworkHelper.cs"
git commit -m "feat: configurable request timeout, TLS 1.2 init, error classification helpers"
```

---

### Task 3: NetworkStateMonitor 서킷 브레이커 + NetworkHelper 연동

**Files:**
- Create: `Source/AutoTranslation/Services/NetworkStateMonitor.cs`
- Modify: `Source/AutoTranslation/Utilities/NetworkHelper.cs` (ExecuteWithRetry)
- Modify: `Source/AutoTranslation/AutoTranslation.csproj` (Compile 등록)

- [ ] **Step 1: NetworkStateMonitor.cs 생성**

```csharp
using System;
using Verse;

namespace AutoTranslation.Services
{
    /// <summary>
    /// Circuit breaker for translation network requests.
    /// Opens after consecutive connection-level failures so the queue stops
    /// hammering an unreachable network; lets a single probe item through
    /// periodically to detect recovery. Judges reachability ONLY by actual
    /// translator request outcomes, so local endpoints (LM Studio) keep
    /// working with no internet.
    /// </summary>
    public static class NetworkStateMonitor
    {
        private const int FAILURE_THRESHOLD = 5;
        private const int PROBE_INTERVAL_MS = 60000;
        private const int PROBE_STUCK_TIMEOUT_MS = 300000; // safety: probe never reported back

        private static readonly object _lock = new object();
        private static int _consecutiveFailures;
        private static bool _isOpen;
        private static DateTime _lastFailureTime = DateTime.MinValue;
        private static bool _probeInFlight;
        private static DateTime _probeStartTime = DateTime.MinValue;

        public static bool IsOpen
        {
            get { lock (_lock) return _isOpen; }
        }

        /// <summary>Called for every connection-level request failure.</summary>
        public static void ReportFailure()
        {
            lock (_lock)
            {
                _probeInFlight = false;
                _lastFailureTime = DateTime.UtcNow;
                _consecutiveFailures++;
                if (!_isOpen && _consecutiveFailures >= FAILURE_THRESHOLD)
                {
                    _isOpen = true;
                    Log.Warning($"{AutoTranslation.LogPrefix} Network unreachable after {_consecutiveFailures} consecutive failures. Pausing translation queue.");
                }
            }
        }

        /// <summary>
        /// Called whenever the endpoint was reachable - including server error
        /// responses (401/429 etc.), which prove connectivity.
        /// </summary>
        public static void ReportSuccess()
        {
            lock (_lock)
            {
                _consecutiveFailures = 0;
                _probeInFlight = false;
                if (_isOpen)
                {
                    _isOpen = false;
                    Log.Message($"{AutoTranslation.LogPrefix} Network recovered. Resuming translation queue.");
                }
            }
        }

        /// <summary>
        /// When the circuit is open, grants a single probe slot every PROBE_INTERVAL_MS.
        /// Returns true if the caller may send one real request as a probe.
        /// </summary>
        public static bool TryEnterProbe()
        {
            lock (_lock)
            {
                if (!_isOpen) return false;
                if (_probeInFlight)
                {
                    // Safety: a probe that never made a network call would leave the flag stuck
                    if ((DateTime.UtcNow - _probeStartTime).TotalMilliseconds < PROBE_STUCK_TIMEOUT_MS) return false;
                    _probeInFlight = false;
                }
                if ((DateTime.UtcNow - _lastFailureTime).TotalMilliseconds < PROBE_INTERVAL_MS) return false;
                _probeInFlight = true;
                _probeStartTime = DateTime.UtcNow;
                return true;
            }
        }
    }
}
```

- [ ] **Step 2: csproj에 Compile 등록**

`AutoTranslation.csproj`의 `<Compile Include="Services\GistService.cs" />` 아래에 추가:

```xml
    <Compile Include="Services\NetworkStateMonitor.cs" />
```

- [ ] **Step 3: NetworkHelper.ExecuteWithRetry에서 브레이커 보고**

`ExecuteWithRetry`를 다음으로 교체 (성공/실패 보고와 회로 열림 시 즉시 실패 추가):

```csharp
        private static string ExecuteWithRetry(Func<string> action, int maxRetries, string context)
        {
            int attempts = 0;
            while (true)
            {
                try
                {
                    attempts++;
                    var result = action();
                    NetworkStateMonitor.ReportSuccess();
                    return result;
                }
                catch (WebException ex)
                {
                    if (IsConnectionLevelFailure(ex))
                    {
                        NetworkStateMonitor.ReportFailure();
                    }
                    else
                    {
                        // The server responded (401/404/429/...) - the network itself is up
                        NetworkStateMonitor.ReportSuccess();
                    }

                    // While the circuit is open, fail fast: no inner retries, the queue is paused anyway
                    if (NetworkStateMonitor.IsOpen) throw;

                    if (attempts > maxRetries)
                    {
                        Log.Warning($"{AutoTranslation.LogPrefix} Network request failed after {maxRetries} attempts. URL: {context}. Error: {ex.Message}");
                        throw;
                    }

                    var response = ex.Response as HttpWebResponse;
                    if (response != null && (int)response.StatusCode == 429) // Too Many Requests
                    {
                        // For rate limits, use minimum 30 seconds with exponential backoff
                        int exponentialDelay = BASE_DELAY_MS * (int)Math.Pow(2, attempts - 1);
                        int delay = Math.Max(RATE_LIMIT_DELAY_MS, exponentialDelay);
                        Log.Warning($"{AutoTranslation.LogPrefix} Rate limit (429) hit. Retrying in {delay}ms ({delay/1000}s)... ({attempts}/{maxRetries})");
                        Thread.Sleep(delay);
                    }
                    else if (ex.Status == WebExceptionStatus.Timeout || ex.Status == WebExceptionStatus.ConnectionClosed)
                    {
                        int delay = BASE_DELAY_MS * (int)Math.Pow(2, attempts - 1); // Exponential backoff
                        Log.Warning($"{AutoTranslation.LogPrefix} Network error ({ex.Status}). Retrying in {delay}ms ({delay/1000}s)... ({attempts}/{maxRetries})");
                        Thread.Sleep(delay);
                    }
                    else
                    {
                        // Fatal error (e.g. 401 Unauthorized, 404 Not Found) or unreachable network - do not retry
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"{AutoTranslation.LogPrefix} Unexpected error during request to {context}: {ex}");
                    throw;
                }
            }
        }
```

파일 상단에 `using AutoTranslation.Services;` 추가.

- [ ] **Step 4: 빌드 확인**

빌드 명령 실행. Expected: 성공.

- [ ] **Step 5: 커밋**

```powershell
git add "Source/AutoTranslation/Services/NetworkStateMonitor.cs" "Source/AutoTranslation/Utilities/NetworkHelper.cs" "Source/AutoTranslation/AutoTranslation.csproj"
git commit -m "feat: circuit breaker for offline detection (NetworkStateMonitor)"
```

---

### Task 4: TranslatorManager 큐 일시정지/프로브 + 사용자 알림 + 상태 표시

**Files:**
- Modify: `Source/AutoTranslation/Services/TranslatorManager.cs`
- Modify: `Source/AutoTranslation/Settings.cs` (Advanced 탭 상태)
- Modify: `Languages/English/Keyed/Mod.xml`, `Languages/Korean/Keyed/Mod.xml`

- [ ] **Step 1: TranslatorManager에 using 추가**

파일 상단에 `using RimWorld;` 추가 (`MessageTypeDefOf` 사용을 위해).

- [ ] **Step 2: 알림 헬퍼 추가**

`StartThread()` 메서드 위에 추가:

```csharp
        private static bool _networkPauseNotified;

        private static void NotifyNetworkStateIfChanged()
        {
            var isOpen = NetworkStateMonitor.IsOpen;
            if (isOpen == _networkPauseNotified) return;
            _networkPauseNotified = isOpen;
            // Messages must be shown from the main thread
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (isOpen)
                    Messages.Message("AT_Message_NetworkPaused".Translate(), MessageTypeDefOf.NegativeEvent);
                else
                    Messages.Message("AT_Message_NetworkResumed".Translate(), MessageTypeDefOf.PositiveEvent);
            });
        }
```

- [ ] **Step 3: 큐 루프에 서킷 브레이커 게이트 삽입**

`StartThread()`의 루프에서 `if (_queue.Count > 0)` 블록 시작 부분(기존 `var aiConfig = ...` 직전)에 다음을 삽입:

```csharp
                    if (_queue.Count > 0)
                    {
                        NotifyNetworkStateIfChanged();

                        if (NetworkStateMonitor.IsOpen)
                        {
                            // Circuit open: requests are paused. Periodically let ONE item
                            // through as a probe; its outcome closes or keeps the circuit.
                            if (NetworkStateMonitor.TryEnterProbe() && _queue.TryDequeue(out var probeItem))
                            {
                                _inQueue.TryRemove(probeItem.Key, out _);
                                var original = probeItem;
                                var wrapped = new KeyValuePair<string, Action<string, bool>>(original.Key, (t, s) =>
                                {
                                    if (!s && NetworkStateMonitor.IsOpen)
                                    {
                                        // Probe failed due to network: put the item back, don't drop it
                                        _queue.Enqueue(original);
                                        _inQueue.TryAdd(original.Key, 0);
                                        return;
                                    }
                                    original.Value(t, s);
                                });
                                ProcessIndividualTranslation(wrapped);
                            }
                            else
                            {
                                await Task.Delay(1000);
                            }
                            continue;
                        }

                        // Check if batch translation is supported and enabled
                        var aiConfig = CurrentTranslator?.Settings as TranslatorSettings_AIModel;
```

(기존 `var aiConfig` 이하 로직은 그대로 유지.)

- [ ] **Step 4: HandleTranslationError — 연결 실패 분류 확장 + 회로 열림 시 재시도 카운트 미소모**

`HandleTranslationError`의 앞부분을 다음으로 교체:

```csharp
        private static void HandleTranslationError(KeyValuePair<string, Action<string, bool>> pair, Exception ex)
        {
            // Check if this is a network timeout/error that we can retry
            bool shouldRetry = ex is WebException webEx && NetworkHelper.IsConnectionLevelFailure(webEx);

            if (shouldRetry && NetworkStateMonitor.IsOpen)
            {
                // Network is down: requeue without consuming the retry budget.
                // The queue loop is paused, so this does not spin.
                _queue.Enqueue(pair);
                _inQueue.TryAdd(pair.Key, 0);
                return;
            }

            if (shouldRetry)
            {
```

(이하 기존 `var currentRetries = ...` 로직 그대로. 기존 `bool shouldRetry = ex is WebException webEx && (webEx.Status == ...)` 3줄 조건은 삭제 — `NetworkHelper.IsConnectionLevelFailure`로 대체된다. `using AutoTranslation.Utilities;`는 이미 있음.)

- [ ] **Step 5: Advanced 탭 상태에 일시정지 표시**

`Settings.cs`의 `DoAdvancedTab`에서:

```csharp
            string status;
            if (TranslatorManager._queue.Count > 0)
                status = "AT_Status1".Translate();
```

을 다음으로 교체:

```csharp
            string status;
            if (NetworkStateMonitor.IsOpen)
                status = "AT_Status_NetworkPaused".Translate();
            else if (TranslatorManager._queue.Count > 0)
                status = "AT_Status1".Translate();
```

(`using AutoTranslation.Services;`는 Settings.cs에 이미 있음.)

- [ ] **Step 6: 언어 키 추가**

`Languages/English/Keyed/Mod.xml`의 `<AT_Status3>` 라인 아래에 추가:

```xml
    <AT_Status_NetworkPaused>Paused - network unreachable. Will retry automatically.</AT_Status_NetworkPaused>
    <AT_Message_NetworkPaused>Auto Translation: network unreachable - translation paused. Will retry automatically.</AT_Message_NetworkPaused>
    <AT_Message_NetworkResumed>Auto Translation: network recovered - translation resumed.</AT_Message_NetworkResumed>
```

`Languages/Korean/Keyed/Mod.xml`의 `<AT_Status3>` 라인 아래에 추가:

```xml
    <AT_Status_NetworkPaused>일시정지됨 - 네트워크에 연결할 수 없습니다. 자동으로 재시도합니다.</AT_Status_NetworkPaused>
    <AT_Message_NetworkPaused>자동 번역: 네트워크에 연결할 수 없어 번역이 일시정지되었습니다. 자동으로 재시도합니다.</AT_Message_NetworkPaused>
    <AT_Message_NetworkResumed>자동 번역: 네트워크가 복구되어 번역이 재개되었습니다.</AT_Message_NetworkResumed>
```

- [ ] **Step 7: 빌드 확인**

빌드 명령 실행. Expected: 성공.

- [ ] **Step 8: 커밋**

```powershell
git add "Source/AutoTranslation/Services/TranslatorManager.cs" "Source/AutoTranslation/Settings.cs" "Languages/English/Keyed/Mod.xml" "Languages/Korean/Keyed/Mod.xml"
git commit -m "feat: pause translation queue while offline, auto-resume via probe"
```

---

### Task 5: AI 번역기별 요청 타임아웃 설정

**Files:**
- Modify: `Source/AutoTranslation/Translators/TranslatorSettings_AIModel.cs`
- Modify: `Source/AutoTranslation/Translators/Translator_BaseOnlineAIModel.cs` (TimeoutMs + 슬라이더 + Reset)
- Modify: `Source/AutoTranslation/Translators/Translator_ChatGPT.cs`, `Translator_Claude.cs`, `Translator_Gemini.cs`, `Translator_OpenAICompatible.cs` (timeoutMs 전달)
- Modify: `Languages/English/Keyed/Mod.xml`, `Languages/Korean/Keyed/Mod.xml`

- [ ] **Step 1: 설정 필드 추가**

`TranslatorSettings_AIModel.cs`:

```csharp
        // Batch translation settings
        public bool EnableBatchTranslation = true;
        public int BatchSizeTokens = 2000;

        // Request timeout (local LLMs often need far more than 30s)
        public int RequestTimeoutSeconds = 30;
```

`ExposeData()`에 추가:

```csharp
            Scribe_Values.Look(ref RequestTimeoutSeconds, "RequestTimeoutSeconds", 30);
```

- [ ] **Step 2: 베이스 클래스에 TimeoutMs 프로퍼티 추가**

`Translator_BaseOnlineAIModel.cs`의 `protected string APIKey =>` 위에 추가:

```csharp
        protected int TimeoutMs => Math.Max(10, Config?.RequestTimeoutSeconds ?? 30) * 1000;
```

- [ ] **Step 3: DrawSettings에 슬라이더 추가**

`DrawSettings`에서 배치 설정 블록(`if (Config.EnableBatchTranslation) { ... }`) 다음의 `ls.Gap();` 뒤, Reset 버튼 앞에 추가:

```csharp
            var timeoutLabelRect = ls.GetRect(Text.LineHeight);
            Widgets.Label(timeoutLabelRect, "AT_Setting_RequestTimeout".Translate() + $": {Config.RequestTimeoutSeconds}s");
            TooltipHandler.TipRegion(timeoutLabelRect, "AT_Setting_RequestTimeout_Tooltip".Translate());

            var newTimeout = Widgets.HorizontalSlider(
                ls.GetRect(22f),
                Config.RequestTimeoutSeconds,
                10f,
                300f,
                true,
                null,
                "10s",
                "300s",
                10f
            );
            Config.RequestTimeoutSeconds = Mathf.RoundToInt(newTimeout / 10f) * 10;

            ls.Gap();
```

Reset 버튼 블록에 추가:

```csharp
                Config.RequestTimeoutSeconds = 30;
```

- [ ] **Step 4: 4개 AI 번역기의 모든 NetworkHelper 호출에 timeoutMs 전달**

`Translator_ChatGPT.cs`:
- `GetModels`: `NetworkHelper.Get(Helpers.CombineUrl(RequestURL, "models"), headers, timeoutMs: TimeoutMs)`
- `GetResponseUnsafe`: `NetworkHelper.Post(Helpers.CombineUrl(RequestURL, "chat", "completions"), requestBody, headers, timeoutMs: TimeoutMs)`

`Translator_Claude.cs`:
- `GetModels`: `NetworkHelper.Get(Helpers.CombineUrl(RequestURL, "models"), headers, timeoutMs: TimeoutMs)`
- `GetResponseUnsafe`: `NetworkHelper.Post(Helpers.CombineUrl(RequestURL, "messages"), requestBody, headers, timeoutMs: TimeoutMs)`

`Translator_Gemini.cs`:
- `GetModels`: `NetworkHelper.Get(url, timeoutMs: TimeoutMs)`
- `GetResponseUnsafe`: `NetworkHelper.Post(url, requestBody, timeoutMs: TimeoutMs)`

`Translator_OpenAICompatible.cs`:
- `GetModels`: `NetworkHelper.Get(url, headers, timeoutMs: TimeoutMs)`
- `GetResponseUnsafe`: `NetworkHelper.Post(url, requestBody, headers, timeoutMs: TimeoutMs)`

(DeepSeek은 ChatGPT 상속이므로 자동 적용.)

- [ ] **Step 5: 언어 키 추가**

English `<AT_Setting_BatchSize_Tooltip>` 아래:

```xml
    <AT_Setting_RequestTimeout>Request timeout</AT_Setting_RequestTimeout>
    <AT_Setting_RequestTimeout_Tooltip>How long to wait for one API response before giving up. Cloud APIs usually answer within 30s; local LLMs (LM Studio, Ollama) often need 120-300s, especially with large batch sizes.</AT_Setting_RequestTimeout_Tooltip>
```

Korean 동일 위치:

```xml
    <AT_Setting_RequestTimeout>요청 타임아웃</AT_Setting_RequestTimeout>
    <AT_Setting_RequestTimeout_Tooltip>API 응답을 포기하기 전까지 기다리는 시간입니다. 클라우드 API는 보통 30초 안에 응답하지만, 로컬 LLM(LM Studio, Ollama)은 배치 크기가 크면 120-300초가 필요할 수 있습니다.</AT_Setting_RequestTimeout_Tooltip>
```

- [ ] **Step 6: 빌드 확인 + 커밋**

빌드 명령 실행. Expected: 성공.

```powershell
git add "Source/AutoTranslation/Translators/TranslatorSettings_AIModel.cs" "Source/AutoTranslation/Translators/Translator_BaseOnlineAIModel.cs" "Source/AutoTranslation/Translators/Translator_ChatGPT.cs" "Source/AutoTranslation/Translators/Translator_Claude.cs" "Source/AutoTranslation/Translators/Translator_Gemini.cs" "Source/AutoTranslation/Translators/Translator_OpenAICompatible.cs" "Languages/English/Keyed/Mod.xml" "Languages/Korean/Keyed/Mod.xml"
git commit -m "feat: per-AI-translator request timeout setting (10-300s)"
```

---

### Task 6: AI 베이스 클래스 — 설정 캐시 버그 수정 + 모델 목록 에러 노출

**Files:**
- Modify: `Source/AutoTranslation/Translators/Translator_BaseOnlineAIModel.cs`
- Modify: `Source/AutoTranslation/Translators/Translator_ChatGPT.cs`, `Translator_Claude.cs`, `Translator_OpenAICompatible.cs` (GetModels에서 예외 전파)
- Modify: `Languages/English/Keyed/Mod.xml`, `Languages/Korean/Keyed/Mod.xml`

- [ ] **Step 1: Model 캐시 제거**

`Translator_BaseOnlineAIModel.cs`:

```csharp
        public virtual string Model => _model ?? (_model = Config?.UserSelectedModel);
```

을 다음으로 교체 (빈 문자열 `""`이 캐시되면 이후 수동 입력이 영원히 무시되는 버그):

```csharp
        public virtual string Model => Config?.UserSelectedModel;
```

필드 선언부에서 `private string _model = null;` 삭제.

- [ ] **Step 2: Models 게터 — 실패를 영구 캐시하지 않고 에러 보관**

`Models` 프로퍼티 전체를 다음으로 교체:

```csharp
        /// <summary>Last error from loading the model list, for display in settings UI. Null when OK.</summary>
        public string LastModelsError { get; private set; }

        public List<string> Models
        {
            get
            {
                if (_models != null && _models.Count > 0) return _models;

                try
                {
                    var result = GetModels();

                    if (result == null || result.Count == 0)
                    {
                        LastModelsError = "AT_Setting_NoModelFound".Translate();
                        _models = null; // do NOT cache failure - allow retry on next click
                        return new List<string>();
                    }

                    LastModelsError = null;
                    _models = result;
                    return _models;
                }
                catch (Exception e)
                {
                    LastModelsError = NetworkHelper.ExtractErrorMessage(e);
                    var msg = AutoTranslation.LogPrefix + $"{Name}: Failed to load models: {LastModelsError}";
                    Log.WarningOnce(msg, msg.GetHashCode());
                    _models = null;
                    return new List<string>();
                }
            }
        }
```

- [ ] **Step 3: APIKey — 키 변경 감지형 rotater**

```csharp
        protected string APIKey =>
            _rotater == null ? (_rotater = new APIKeyRotater(Config?.UserAPIKey?.Split(',') ?? new string[0])).Key : _rotater.Key;
```

을 다음으로 교체 (사용자가 설정에서 키를 고쳐도 옛 키로 계속 요청하던 버그):

```csharp
        protected string APIKey
        {
            get
            {
                var source = Config?.UserAPIKey ?? string.Empty;
                if (_rotater == null || _rotaterSource != source)
                {
                    _rotaterSource = source;
                    var keys = source.Split(',').Where(k => !string.IsNullOrWhiteSpace(k)).ToArray();
                    _rotater = keys.Length > 0 ? new APIKeyRotater(keys) : null;
                }
                return _rotater?.Key ?? string.Empty;
            }
        }

        private string _rotaterSource;
```

(기존 `protected APIKeyRotater _rotater = null;` 필드는 유지. `APIKeyRotater`는 빈 배열이면 throw하므로 빈 키일 때 생성하지 않는다.)

- [ ] **Step 4: RequestURL — 캐시 제거**

`RequestURL` 프로퍼티 전체를 다음으로 교체 (커스텀 URL 변경이 반영되지 않던 버그; 계산이 싸므로 캐시 불필요):

```csharp
        protected string RequestURL
        {
            get
            {
                var url = Config?.UserCustomBaseURL;
                if (string.IsNullOrEmpty(url))
                {
                    url = BaseURL;
                }

                if (!url.EndsWith("/"))
                {
                    url += "/";
                }

                return url;
            }
        }
```

필드 선언부에서 `private string _baseURL = null;` 삭제.

- [ ] **Step 5: ResetSettings 갱신**

```csharp
        public void ResetSettings()
        {
            _models = null;
            _rotater = null;
            _rotaterSource = null;
            LastModelsError = null;
            Prepare();
        }
```

- [ ] **Step 6: DrawSettings — 에러 표시 + 수동 입력 항상 노출**

모델 버튼 블록의 else 분기를 교체:

```csharp
                else
                {
                    var failMsg = "AT_Message_NoModelsFound".Translate().ToString();
                    if (!string.IsNullOrEmpty(LastModelsError)) failMsg += "\n" + LastModelsError;
                    Messages.Message(failMsg, MessageTypeDefOf.NegativeEvent);
                }
```

그 아래의 조건부 수동 입력 블록:

```csharp
            // Show text entry if models is empty (as fallback for manual entry)
            // Don't use Models property here to avoid triggering API call every frame
            if (_models != null && _models.Count == 0)
            {
                Config.UserSelectedModel = ls.TextEntry(Config.UserSelectedModel);
            }
```

을 다음으로 교체 (항상 노출 + 에러 라벨):

```csharp
            if (!string.IsNullOrEmpty(LastModelsError))
            {
                var prevColor = GUI.color;
                GUI.color = Color.red;
                ls.Label(LastModelsError);
                GUI.color = prevColor;
            }

            ls.Label("AT_Setting_ManualModelEntry".Translate());
            Config.UserSelectedModel = ls.TextEntry(Config.UserSelectedModel);
```

- [ ] **Step 7: 파생 클래스 GetModels에서 예외 삼키기 제거**

`Translator_ChatGPT.cs`:

```csharp
        public override List<string> GetModels()
        {
            var headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer " + APIKey }
            };

            var raw = NetworkHelper.Get(Helpers.CombineUrl(RequestURL, "models"), headers, timeoutMs: TimeoutMs);
            return raw.GetStringValuesFromJson("id");
        }
```

`Translator_Claude.cs`:

```csharp
        public override List<string> GetModels()
        {
            var headers = new Dictionary<string, string>
            {
                { "x-api-key", APIKey },
                { "anthropic-version", AnthropicVersion }
            };

            var raw = NetworkHelper.Get(Helpers.CombineUrl(RequestURL, "models"), headers, timeoutMs: TimeoutMs);
            return raw.GetStringValuesFromJson("id");
        }
```

`Translator_OpenAICompatible.cs`:

```csharp
        public override List<string> GetModels()
        {
            var url = Helpers.CombineUrl(RequestURL, "models");
            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(APIKey))
            {
                headers.Add("Authorization", "Bearer " + APIKey);
            }

            var raw = NetworkHelper.Get(url, headers, timeoutMs: TimeoutMs);

            // Try standard OpenAI format
            var models = raw.GetStringValuesFromJson("id");

            // Fallback for some local servers that might return just a list
            if (models == null || models.Count == 0)
            {
                models = raw.GetStringValuesFromJson("name");
            }

            return models ?? new List<string>();
        }
```

(Gemini는 Task 7에서 페이지네이션과 함께 재작성. 이 단계에서 CS0168 경고 2건이 사라진다.)

- [ ] **Step 8: 언어 키 추가**

English `<AT_ChooseModel>` 아래:

```xml
    <AT_Setting_ManualModelEntry>Or enter a model name manually:</AT_Setting_ManualModelEntry>
```

Korean 동일 위치:

```xml
    <AT_Setting_ManualModelEntry>또는 모델 이름 직접 입력:</AT_Setting_ManualModelEntry>
```

- [ ] **Step 9: 빌드 확인 + 커밋**

빌드 명령 실행. Expected: 성공, CS0168 경고 사라짐.

```powershell
git add "Source/AutoTranslation/Translators/Translator_BaseOnlineAIModel.cs" "Source/AutoTranslation/Translators/Translator_ChatGPT.cs" "Source/AutoTranslation/Translators/Translator_Claude.cs" "Source/AutoTranslation/Translators/Translator_OpenAICompatible.cs" "Languages/English/Keyed/Mod.xml" "Languages/Korean/Keyed/Mod.xml"
git commit -m "fix: stale API key/URL/model caches, surface model list errors in UI"
```

---

### Task 7: Gemini — 모델 목록 페이지네이션 + generateContent 필터

**Files:**
- Modify: `Source/AutoTranslation/Translators/Translator_Gemini.cs`

- [ ] **Step 1: GetModels 재작성**

`Translator_Gemini.cs`의 `GetModels()`를 다음으로 교체 (기본 pageSize 50이라 모델 누락 + embedding 모델 미필터 + 예외 삼키기 문제):

```csharp
        public override List<string> GetModels()
        {
            var key = APIKey; // capture once: APIKey rotates per access, pagination must use one key
            var models = new List<string>();
            string pageToken = null;

            do
            {
                var url = Helpers.CombineUrl(RequestURL, "models") + $"?key={key}&pageSize=1000";
                if (!string.IsNullOrEmpty(pageToken))
                {
                    url += $"&pageToken={pageToken}";
                }

                var raw = NetworkHelper.Get(url, timeoutMs: TimeoutMs);
                models.AddRange(ParseGenerateContentModels(raw));
                pageToken = raw.GetStringValueFromJson("nextPageToken");
            } while (!string.IsNullOrEmpty(pageToken));

            return models;
        }

        /// <summary>
        /// Extracts model names that support generateContent (filters out embedding/aqa models).
        /// Splits the JSON into per-model segments on "name" keys; each segment holds that
        /// model's fields including supportedGenerationMethods.
        /// </summary>
        private static List<string> ParseGenerateContentModels(string json)
        {
            var result = new List<string>();
            var segments = json.Split(new[] { "\"name\"" }, StringSplitOptions.None);

            for (int i = 1; i < segments.Length; i++)
            {
                var match = Regex.Match(segments[i], "^\\s*:\\s*\"models/([^\"]+)\"");
                if (!match.Success) continue;

                if (segments[i].Contains("\"generateContent\""))
                {
                    result.Add(match.Groups[1].Value);
                }
            }

            return result;
        }
```

파일 상단에 `using System.Text.RegularExpressions;` 추가.

- [ ] **Step 2: 빌드 확인 + 커밋**

빌드 명령 실행. Expected: 성공.

```powershell
git add "Source/AutoTranslation/Translators/Translator_Gemini.cs"
git commit -m "fix: Gemini model list pagination and generateContent filtering"
```

---

### Task 8: LibreTranslate — 에러 노출 + API 키 로그 유출 제거 + 키 안내

(검증 결과: 공개 인스턴스 `libretranslate.com`은 이제 API 키 없이는 400을 반환한다. `api_key` 전달 코드는 이미 있으므로, 서버 에러를 사용자에게 보이게 하고 안내를 추가한다.)

**Files:**
- Modify: `Source/AutoTranslation/Translators/Translator_LibreTranslate.cs`
- Modify: `Languages/English/Keyed/Mod.xml`, `Languages/Korean/Keyed/Mod.xml`

- [ ] **Step 1: 요청 본문 로그 제거 (api_key가 로그에 그대로 남는 문제) + 에러 메시지 노출**

`TryTranslate` 내부의 세 줄:

```csharp
                Log.Message(AutoTranslation.LogPrefix + $"{Name}: Request URL: {url}");
                Log.Message(AutoTranslation.LogPrefix + $"{Name}: Request body: {body}");
                Log.Message(AutoTranslation.LogPrefix + $"{Name}: Target language: {TranslateLanguage}");
```

및 응답 로그:

```csharp
                var response = NetworkHelper.Post(url, body, headers);
                Log.Message(AutoTranslation.LogPrefix + $"{Name}: Response: {response?.Substring(0, Math.Min(200, response?.Length ?? 0))}");
```

을 다음으로 교체:

```csharp
                var response = NetworkHelper.Post(url, body, headers);
```

catch 블록을 다음으로 교체 (서버가 보낸 에러 본문을 노출):

```csharp
            catch (Exception ex)
            {
                var reason = NetworkHelper.ExtractErrorMessage(ex);
                var msg = $"{AutoTranslation.LogPrefix} {Name} failed: {reason}";
                Log.WarningOnce(msg, msg.GetHashCode());
                translated = text;
                return false;
            }
```

- [ ] **Step 2: 설정 UI에 공개 인스턴스 키 필요 안내**

`DrawSettings`의 `ls.Label("LibreTranslate URL (Default: https://libretranslate.com)");` 아래에 추가:

```csharp
            var noticeRect = ls.GetRect(Text.LineHeight);
            var prevColor = GUI.color;
            GUI.color = Color.yellow;
            Widgets.Label(noticeRect, "AT_Setting_LibreTranslateKeyNotice".Translate());
            GUI.color = prevColor;
```

파일에 `using UnityEngine;`은 이미 있음.

- [ ] **Step 3: 언어 키 추가**

English (`AT_Setting_ManualModelEntry` 아래):

```xml
    <AT_Setting_LibreTranslateKeyNotice>Note: the official libretranslate.com instance now requires a paid API key (portal.libretranslate.com). Self-hosted instances need no key.</AT_Setting_LibreTranslateKeyNotice>
```

Korean:

```xml
    <AT_Setting_LibreTranslateKeyNotice>참고: 공식 libretranslate.com 인스턴스는 이제 유료 API 키가 필요합니다 (portal.libretranslate.com). 셀프호스팅 인스턴스는 키가 필요 없습니다.</AT_Setting_LibreTranslateKeyNotice>
```

- [ ] **Step 4: 빌드 확인 + 커밋**

빌드 명령 실행. Expected: 성공.

```powershell
git add "Source/AutoTranslation/Translators/Translator_LibreTranslate.cs" "Languages/English/Keyed/Mod.xml" "Languages/Korean/Keyed/Mod.xml"
git commit -m "fix: LibreTranslate error surfacing, stop logging api_key, key requirement notice"
```

---

### Task 9: DeepL — Free/Pro 키-엔드포인트 불일치 경고

(검증 결과: Free 키는 `:fx`로 끝나며 `api-free.deepl.com` 전용. Pro 호스트에 Free 키를 쓰면 403.)

**Files:**
- Modify: `Source/AutoTranslation/Translators/Translator_DeepL.cs`
- Modify: `Languages/English/Keyed/Mod.xml`, `Languages/Korean/Keyed/Mod.xml`

- [ ] **Step 1: DrawSettings에 불일치 경고 추가**

`Translator_DeepL.cs`의 `DrawSettings` 끝(`Config.APIKey = ls.TextEntry(Config.APIKey);` 아래)에 추가:

```csharp
            var key = Config.APIKey?.Split(',')[0].Trim() ?? string.Empty;
            if (key.Length > 0)
            {
                var isFreeKey = key.EndsWith(":fx");
                var isFreeEndpoint = url.Contains("api-free.deepl.com");
                if (isFreeKey != isFreeEndpoint)
                {
                    var prevColor = GUI.color;
                    GUI.color = Color.red;
                    ls.Label(isFreeKey
                        ? "AT_Setting_DeepLFreeKeyOnPro".Translate()
                        : "AT_Setting_DeepLProKeyOnFree".Translate());
                    GUI.color = prevColor;
                }
            }
```

파일 상단에 `using UnityEngine;` 추가 (`GUI`, `Color` 사용).

(`url`은 protected virtual 프로퍼티라 DeepL_Pro에서도 올바른 호스트로 평가됨.)

- [ ] **Step 2: 언어 키 추가**

English:

```xml
    <AT_Setting_DeepLFreeKeyOnPro>This looks like a DeepL Free key (ends with ":fx"). Please select the "DeepL" translator instead of "DeepL Pro".</AT_Setting_DeepLFreeKeyOnPro>
    <AT_Setting_DeepLProKeyOnFree>This looks like a DeepL Pro key. Please select the "DeepL Pro" translator instead of "DeepL".</AT_Setting_DeepLProKeyOnFree>
```

Korean:

```xml
    <AT_Setting_DeepLFreeKeyOnPro>DeepL Free 키(":fx"로 끝남)로 보입니다. "DeepL Pro" 대신 "DeepL" 번역기를 선택해주세요.</AT_Setting_DeepLFreeKeyOnPro>
    <AT_Setting_DeepLProKeyOnFree>DeepL Pro 키로 보입니다. "DeepL" 대신 "DeepL Pro" 번역기를 선택해주세요.</AT_Setting_DeepLProKeyOnFree>
```

- [ ] **Step 3: 빌드 확인 + 커밋**

빌드 명령 실행. Expected: 성공.

```powershell
git add "Source/AutoTranslation/Translators/Translator_DeepL.cs" "Languages/English/Keyed/Mod.xml" "Languages/Korean/Keyed/Mod.xml"
git commit -m "feat: warn on DeepL Free/Pro key-endpoint mismatch"
```

---

### Task 10: 최종 검증

- [ ] **Step 1: 클린 재빌드**

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "Source\AutoTranslation\AutoTranslation.csproj" /p:Configuration=Debug1.6 /t:Rebuild /v:minimal /nologo
```

Expected: 성공, 경고 0건 (CS0168 2건은 Task 6에서 제거됨).

- [ ] **Step 2: 워킹 트리 확인**

```powershell
git status --short
```

Expected: 출력 없음 (모든 변경이 커밋됨).

- [ ] **Step 3: 스펙 대비 커버리지 확인**

스펙(`docs/superpowers/specs/2026-06-11-network-resilience-and-ai-translator-fixes-design.md`)의 각 섹션이 구현되었는지 점검:
- §1 서킷 브레이커 → Task 3, 4
- §2 타임아웃 → Task 2, 5
- §3 모델 목록/캐시 → Task 6, 7
- §4 API 검증 조치 (Gemini 페이지네이션 → Task 7, LibreTranslate → Task 8, DeepL :fx → Task 9, TLS/Expect100 → Task 2)
