# BeApi 운영 가이드 (OPERATIONS)

`unreal-backend/BeApi` 의 로컬 Windows 운영(= `dotnet run`)을 위한 통합 체크리스트.

> 본 백엔드는 UDP 통신 네트워크 구성상 **Docker 위에서 동작하지 않고 로컬 Windows 에서 `dotnet run`** 으로 동작한다.
> DB 스키마는 이미 외부에서 구성되어 있으므로 **EF Core 마이그레이션을 수행하지 않는다** (시작 시 스키마 존재 여부만 검증).

---

## 1. 시스템 요구사항

| 항목 | 값 |
|------|------|
| OS | Windows 10/11 또는 Windows Server |
| .NET SDK | .NET 9 (`dotnet --version` 으로 확인) |
| TSDB | TimescaleDB / PostgreSQL — 기본 `localhost:55432` |
| CommandDB | PostgreSQL — 기본 `localhost:55433` |
| OPC UA Server | 기본 `opc.tcp://localhost:54880/UA/DAS` |
| UDP 수신 포트 | 50010 (Relay 입력), 50020 (라이브 멀티캐스트), 50021 (Replay 출력) |
| Listen | `http://localhost:5082` |

---

## 2. 설정 우선순위 (.NET Configuration)

```
OS 환경변수  >  .env 파일  >  appsettings.{Environment}.json  >  appsettings.json
```

- **`.env`** 는 BeApi 프로젝트 루트(`BeApi/.env`) 에 두고, `dotnet run` 실행 시 자동 로드된다 (`DotNetEnv` 패키지).
- 이미 OS 환경변수에 동일 키가 있으면 `.env` 가 덮어쓰지 않는다 (`clobberExistingVars: false`).
- 모든 환경변수 키는 `.env.example` 에 명시되어 있다.
- nested key 는 `__` (이중 언더스코어) — 예: `Database:TsdbConnectionString` ↔ `DATABASE__TSDBCONNECTIONSTRING`.

---

## 3. 환경변수 목록

전체 템플릿은 `BeApi/.env.example` 참조. 운영에서 반드시 확인해야 할 값:

### 3.1 DB (비밀)
| 키 | 설명 |
|------|------|
| `DATABASE__TSDBCONNECTIONSTRING` | TSDB 접속 문자열 |
| `DATABASE__COMMANDDBCONNECTIONSTRING` | CommandDB 접속 문자열 |

> dev 환경의 평문 기본값은 `appsettings.Development.json` 에 남아있으나, **운영(Production) 에서는 반드시 `.env` 또는 OS ENV 로 오버라이드**.

### 3.2 OPC UA
| 키 | 설명 |
|------|------|
| `OPCUA__ENDPOINTURL` | `opc.tcp://...` 형식 |

### 3.3 UDP
| 키 | 설명 |
|------|------|
| `UDP__MULTICASTGROUP` | 라이브 멀티캐스트 그룹 (예: 239.100.0.1) |
| `UDP__LISTENPORT` | 라이브 수신 포트 |
| `UDP__BINDADDRESS` | 보통 0.0.0.0 |
| `UDP__RELAY__LISTENPORT` | Relay 입력 포트 |
| `UDP__RELAY__OUTPUTPORT` | Relay 출력 포트 |
| `UDP__RELAY__MULTICASTINTERFACE` | **Windows 멀티 NIC 환경에서 송신 NIC IP 명시** (잘못 지정하면 패킷이 안 보임) |
| `UDP__RELAY__MULTICASTTTL` | 보통 1 |
| `UDP__REPLAY__OUTPUTPORT` | Replay 출력 포트 |

### 3.4 비디오
| 키 | 설명 |
|------|------|
| `VIDEO_ROOT` | 영상 루트 경로. 비어있으면 `BeApi/videos` 사용. 구조: `{VIDEO_ROOT}/{CAST\|CNC\|WASH\|ASSY\|TEST}/status_{0..4}.webm` (+ `status_default.webm`) |

---

## 4. 시작 전 체크리스트

- [ ] `.env` 파일이 `BeApi/.env` 에 존재하고 비밀값이 채워져 있다.
- [ ] `.env` 가 `.gitignore` 에 의해 무시되는지 확인 (`git status` 에 표시되면 안 됨).
- [ ] TSDB, CommandDB 가 기동되어 있고 접속 가능 (`psql` 또는 DBeaver 로 사전 점검 권장).
- [ ] OPC UA 서버가 기동되어 있다 (없으면 ConnectionStatus 에 `disconnected` 로 표시됨, 기동은 됨).
- [ ] UDP 포트(50010 / 50020 / 50021) 가 다른 프로세스에 점유되어 있지 않다 — `netstat -ano | findstr :50010` 등.
- [ ] `UDP__RELAY__MULTICASTINTERFACE` 가 실제 NIC IP 와 일치 (`ipconfig /all` 로 확인).
- [ ] 방화벽: 50010 / 50020 / 50021 UDP 인바운드 + 5082 TCP 인바운드 허용.
- [ ] `VIDEO_ROOT` 경로가 존재하고, 최소 한 개 이상의 타입 폴더(CAST/CNC/WASH/ASSY/TEST) 가 포함되어 있다.

---

## 5. 기동

```powershell
cd BeApi
dotnet run
```

기동 직후 콘솔에서 다음 항목을 확인:

1. `be-api 시작 중...`
2. `.env 파일 로드 완료: <경로>` (또는 `.env 파일을 찾지 못함 — OS 환경변수 / appsettings 만 사용.`)
3. `DB 스키마 검증 완료` (실패 시 즉시 종료 — fail-fast)
4. `Self-check: VIDEO_ROOT OK` (실패 시 즉시 종료 — fail-fast)
5. OPC UA / UDP / Multicast NIC 관련 **경고 로그가 있다면 모두 점검** (경고만이므로 기동은 됨)
6. `EquipmentLut 초기화 완료. ...`
7. `Now listening on: http://localhost:5082`

---

## 6. 시작 후 검증

PowerShell 기준:

```powershell
# 1) Health 체크
curl.exe http://localhost:5082/health

# 2) 운영 대시보드 (Razor Pages)
start http://localhost:5082/

# 3) ConnectionStatus — opcua/udp/tsdb/commanddb 의 last_ok 시각
curl.exe http://localhost:5082/api/connection-status

# 4) 최신 데이터 — 장비 ID 예시 (CAST-01)
curl.exe http://localhost:5082/api/latest/101

# 5) 영상 (HEAD — ETag/Last-Modified 확인용)
curl.exe -I http://localhost:5082/api/video/1/501

# 6) Swagger (Development 환경에서만)
start http://localhost:5082/swagger
```

---

## 7. 트러블슈팅

### 7.1 기동 즉시 종료 — "VIDEO_ROOT" 관련 메시지
- `.env` 의 `VIDEO_ROOT` 가 비어있고 `BeApi/videos` 도 없는 경우.
- 또는 지정 경로에 `CAST/CNC/WASH/ASSY/TEST` 폴더가 전혀 없는 경우.
- 해결: 경로를 만들고 `status_default.webm` 정도라도 배치 → 재시작.

### 7.2 기동 즉시 종료 — DB 스키마 누락
- `VerifyDatabaseSchemaAsync` 가 필요한 테이블/뷰를 못 찾음.
- 해결: 운영팀에 DB 스키마 적용 여부 확인. 본 백엔드는 마이그레이션을 수행하지 않는다.

### 7.3 UDP 패킷이 안 보임
- 멀티캐스트 NIC 인터페이스 미지정 또는 오지정 → `UDP__RELAY__MULTICASTINTERFACE` 점검.
- 방화벽 차단 가능성 → 인바운드 UDP 허용 룰 확인.
- `netstat -ano | findstr :50020` 으로 실제로 수신 중인지 확인.

### 7.4 OPC UA `disconnected`
- 서버 미기동 또는 endpoint 오타. `OPCUA__ENDPOINTURL` 이 `opc.tcp://` 로 시작하는지 확인.
- 본 백엔드는 OPC UA 실패 시 기동은 계속한다 (경고만).

### 7.5 영상이 새로고침해야 바뀜 (브라우저 캐시)
- 운영 화면(`/video/...`, `/embed/video/...`) 은 3초마다 HEAD 폴링 → `Last-Modified` 변경 시 src 갱신.
- 그래도 안 바뀌면 브라우저 캐시/프록시 캐시 의심. `curl.exe -I` 로 백엔드 응답의 `Last-Modified` 가 갱신되는지 먼저 확인.

### 7.6 `.env` 가 안 읽힘
- `EnvFileLoader` 의 탐색 경로: 실행 디렉터리 → `AppContext.BaseDirectory` → 각각의 상위 1단계.
- 콘솔에 `.env 파일을 찾지 못함` 이 보이면 `dotnet run` 실행 위치 확인 (반드시 `BeApi/` 에서).

---

## 8. 운영 명령 모음

PowerShell only (jq / curl 가정 안 함):

```powershell
# Replay 시작 — last_minutes 우선
curl.exe "http://localhost:5082/api/replay/start-link?last_minutes=5&speed=1.0&loop=false"

# Replay 시작 — from/to 절대시각
curl.exe "http://localhost:5082/api/replay/start-link?from=2026-06-23T05:00:00Z&to=2026-06-23T05:05:00Z"

# Replay 중지
curl.exe http://localhost:5082/api/replay/stop-link

# Command 발행 — 예: TEST-01 (501) 에 명령
$body = @{ equipmentId = 501; command = "START"; idempotencyKey = [guid]::NewGuid().ToString() } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:5082/api/commands -ContentType "application/json" -Body $body

# 최근 명령 조회
curl.exe http://localhost:5082/api/commands?limit=20
```

---

## 9. 변경 시 주의

- `.env.example` 에 키를 추가/변경하면 본 문서 §3 도 동시에 갱신할 것.
- 새로운 fail-fast 점검을 추가할 경우 `StartupSelfCheck` 의 정책 주석과 §7 트러블슈팅을 함께 갱신.
- DB 커넥션 문자열을 코드에 평문으로 하드코딩하지 말 것 — 반드시 `.env` 또는 OS ENV 로.
