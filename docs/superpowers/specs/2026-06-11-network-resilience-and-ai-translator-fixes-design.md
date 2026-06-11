# 네트워크 복원력 및 AI 번역기 수정 설계

날짜: 2026-06-11
상태: 승인됨

## 배경 / 해결하려는 문제

1. **오프라인 환경에서 요청 폭주**: 인터넷이 없는 환경에서도 번역 큐가 계속 요청을
   날린다. 항목당 `NetworkHelper` 5회 재시도 × `TranslatorManager` 3회 재큐잉이
   수백 개 항목에 대해 반복되어 로그 스팸과 성능 저하를 유발한다.
2. **로컬 LLM 타임아웃** (사용자 이슈): LM Studio + Gemma 사용 시 하드코딩된 30초
   타임아웃(`NetworkHelper.cs`)이 너무 짧아 클라이언트가 계속 끊긴다. 옵션에서
   조절 가능해야 한다.
3. **모델 목록이 비는 문제** (사용자 이슈): Gemini/DeepSeek 키 입력 시 모델 탭이
   비어 있고, 수동 입력해도 "models not found"가 뜬다.
4. **번역기 API 라우트 신뢰성**: Google(gtx)/Papago(JS 스크래핑)/Yandex(browser
   라우트)는 비공식 엔드포인트라 현재 유효성 검증이 필요하다.

## 진단된 근본 원인 (이슈 3 관련)

`Translator_BaseOnlineAIModel.cs`의 캐시 필드들이 설정 변경을 감지하지 못한다:

- `APIKey` 프로퍼티: `_rotater`가 최초 1회만 생성됨. 사용자가 설정 창에서 API 키를
  수정해도 옛 키로 계속 요청 → 401 지속 → 모델 목록 항상 실패.
- `RequestURL`: `_baseURL` 동일한 패턴으로 캐시. 커스텀 Base URL 변경 미반영.
- `Model`: `_model ?? (_model = Config?.UserSelectedModel)` — 기본값 빈 문자열
  `""`이 캐시되면 이후 수동 입력해도 영원히 `""` 반환 → "Model is not set".
- `GetModels()` 실패 시 예외를 삼키고 null 반환 → 사용자에게 원인(401/네트워크/
  파싱)이 전혀 표시되지 않고, 실패가 빈 리스트로 캐시됨.
- Gemini 모델 목록: 페이지네이션(`nextPageToken`) 미처리, embedding 등 번역에
  쓸 수 없는 모델 미필터링.

## 설계

### 1. 오프라인 서킷 브레이커 — `Services/NetworkStateMonitor` (신규)

- **상태**: Closed(정상) / Open(차단) / Half-Open(프로브 중).
- **감지**: `NetworkHelper`에서 연결 수준 실패(`NameResolutionFailure`,
  `ConnectFailure`, `Timeout`, `ConnectionClosed`)를 브레이커에 보고.
  연속 5회 실패 시 Open.
- **Open 동안**: `TranslatorManager` 루프가 큐 처리를 중단한다. 항목은 큐에
  유지되고 요청은 0건. 전환 시 인게임 메시지 1회 표시
  ("네트워크 연결 실패 — 자동 번역이 일시정지되었습니다").
- **복구**: Open 후 60초마다 Half-Open으로 전환해 큐 항목 1건만 실제 요청으로
  흘려보낸다. 성공 → Closed + 정상 재개(재개 메시지 1회). 실패 → 다시 Open.
- **인터넷 체크 API를 쓰지 않는 이유**: LM Studio 등 로컬 번역기는 인터넷 없이
  동작해야 하므로 `Application.internetReachability` 같은 외부 연결 체크는
  부적합. 실제 번역기 엔드포인트에 대한 요청 성패만 기준으로 삼는다.
- **부수 수정**: 현재 `NameResolutionFailure`/`ConnectFailure`(전형적 오프라인
  에러)는 fatal로 분류되어 즉시 실패 처리된다. 이를 "연결 수준 실패"로 분류해
  브레이커에 보고하고, 회로가 Open인 동안에는 항목별 재시도 카운트를 소모하지
  않는다(연결 복구 후 정상 재시도 가능해야 하므로).
- **성공 보고**: 모든 성공 응답은 브레이커의 연속 실패 카운트를 리셋한다.
- **UI**: Advanced 탭 상태 표시에 "일시정지됨(네트워크 오류)" 상태 추가.

### 2. AI 번역기별 요청 타임아웃

- `TranslatorSettings_AIModel`에 `RequestTimeoutSeconds` 필드 추가.
  기본 30, 범위 10–300초, `ExposeData`로 저장.
- `NetworkHelper.Post`/`Get`에 `timeoutMs` 파라미터 추가(기본 30000 →
  전통 번역기는 기존 동작 유지).
- `Translator_BaseOnlineAIModel` 및 파생 클래스의 요청 경로(`GetResponseUnsafe`,
  `GetModels`)에서 설정값을 전달.
- 설정 UI(`DrawSettings`)에 슬라이더 추가 + 번역 키(`AT_Setting_RequestTimeout`)
  및 툴팁. 영어/한국어 LanguageData 모두 추가.

### 3. AI 모델 목록/설정 캐시 수정

- **설정 변경 감지형 캐시**: `_rotater`/`_baseURL`/`_model`을 "캐시를 만들 때
  사용한 원본 설정 문자열"과 함께 보관하고, 현재 설정값과 다르면 재생성한다.
  (rotater는 키 순환 상태를 가지므로 매 호출 재생성 대신 이 방식 사용.
  `_model`은 단순해서 캐시 자체를 제거하고 직접 읽기.)
- **에러 노출**: `GetModels()` 실패 시 `WebException.Response` 본문에서 API 에러
  메시지(예: Gemini "API key not valid")를 읽어 `LastModelsError`에 보관.
  모델 버튼 클릭 실패 시 해당 메시지를 `Messages.Message`로 표시하고 설정 UI에
  빨간 라벨로도 표시한다. 실패를 빈 리스트로 영구 캐시하지 않는다
  (다음 버튼 클릭 시 재시도 가능).
- **Gemini**: `nextPageToken` 페이지네이션 처리(`pageSize=1000` 요청 +
  토큰 루프), `supportedGenerationMethods`에 `generateContent`가 포함된 모델만
  노출.
- **수동 입력 항상 노출**: 모델 수동 입력 텍스트필드를 실패 시에만이 아니라
  항상 표시한다 ("또는 직접 입력" 라벨).

### 4. API 라우트 검증 및 수정

- 공식 API(DeepL/OpenAI/Claude/Gemini/DeepSeek/LibreTranslate)는 경로·헤더가
  현행 문서와 일치하는지 확인.
- 비공식 엔드포인트(Google gtx, Papago 스크래핑, Yandex browser 라우트)는 웹
  조사로 현재 유효성을 확인하고, 깨졌거나 변경된 것은 이번 작업에서 수정한다.
- 검증 결과는 구현 계획에 항목별 verdict(OK/변경/깨짐+대안)로 반영한다.

## 범위 외

- 전통 번역기(Google 등)의 타임아웃 설정화 — AI 번역기만 대상.
- 번역 품질/프롬프트 개선.
- 진행 중인 Gist 공유 기능(별도 작업) — 단, 빌드를 깨뜨리던 두 가지
  (csproj 미등록 파일, `SelectedModel` 오타)는 이번에 수정 완료.

## 테스트 / 검증

- 유닛 테스트가 없는 RimWorld 모드 프로젝트이므로: MSBuild(Debug1.6) 컴파일
  통과를 기본 게이트로 한다.
- 수동 시나리오: (a) 잘못된 키 → 모델 버튼 클릭 시 구체적 에러 메시지 표시,
  (b) 키 수정 후 재클릭 → 재시작 없이 정상 로드, (c) 네트워크 차단 시 큐
  일시정지 메시지 + 재연결 시 자동 재개, (d) 타임아웃 슬라이더 값이 실제 요청에
  반영되는지 로그로 확인.
