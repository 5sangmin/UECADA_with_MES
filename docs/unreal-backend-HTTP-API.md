# unreal-backend (BeApi) — HTTP / URL API 가이드

Windows local 에서 `dotnet run` 으로 동작하는 BeApi (ASP.NET Core, 기본 포트 5082)
가 노출하는 URL 전체 카탈로그.

> **베이스 URL**
> - 같은 머신: `http://localhost:5082`
> - LAN 다른 머신: `http://192.168.5.10:5082` (Kestrel 이 `0.0.0.0:5082` 에 바인딩, 5082 인바운드 방화벽 허용 필요 — `docs/OPERATIONS.md §5.2` 참고)

본 문서는 두 종류 인터페이스를 다룬다.

1. **Razor Pages (사람용 / Unreal embed 용)** — `/`, `/command/*`, `/video/*`, `/test/oneshot`
2. **REST JSON API (프로그램용)** — `/api/commands`, `/api/latest`, `/api/status`, `/api/video`, `/api/replay`

---

## 1. 빠른 시작 — URL 한 줄로 명령 발행

```
http://localhost:5082/command/request?line=3&equip=WASH-01&command_type=load_request&value=true&source_type=unreal-backend&created_by=unreal-backend&autoSubmit=1
```

이 한 줄을 브라우저 주소창 / Unreal WebBrowser / PowerShell `Start-Process` 등
어디서든 열면 — **prefill 된 폼이 자동 제출되어 명령이 즉시 등록**된다.

| 쿼리 파라미터       | 필수 | 예                              | 비고                                         |
| ------------------- | ---- | ------------------------------- | -------------------------------------------- |
| `line`              | ✅   | `1`, `2`, `3`                   | line_id (모든 설비는 1~3 라인에 존재)         |
| `equip`             | ✅   | `WASH-01`                       | 설비 코드 (아래 §2 카탈로그)                  |
| `command_type`      | ✅   | `load_request`, `power`         | (아래 §3 설비별 허용 표)                      |
| `value`             | (조건부) | `true`, `120.5`, `"OPEN"`   | command_type 에 따라 필요. JSON literal 로 해석 |
| `source_type`       | ⬜   | `unreal-backend`                | 누락 시 `API`                                 |
| `created_by`        | ⬜   | `unreal-backend`, `operator-1`  | 발행 주체                                     |
| `priority`          | ⬜   | `1` ~ `10`                      | 정수                                          |
| `max_retry`         | ⬜   | `3`                             | 정수                                          |
| `idempotency_key`   | ⬜   | UUID                            | 같은 키 재발행 시 중복 차단                   |
| `autoSubmit`        | ⬜   | `1`                             | **`1` 이면 즉시 제출**, 없으면 폼만 prefill   |

`autoSubmit=1` 을 빼면 똑같은 URL 이 **prefill 폼**으로 열리고 운영자가 "보내기" 를
직접 눌러야 한다. 자동화/테스트 단축키로는 `1`, 사람 확인이 필요하면 생략.

### PowerShell / curl 예제

```powershell
# PowerShell 5+
$url = "http://localhost:5082/command/request?line=1&equip=CAST-01&command_type=power&value=true&autoSubmit=1"
Invoke-WebRequest -Uri $url -UseBasicParsing | Out-Null
# 또는 그냥 브라우저로 열기
Start-Process $url
```

```bash
# curl
curl "http://localhost:5082/command/request?line=1&equip=CAST-01&command_type=power&value=true&autoSubmit=1"
```

### REST 로 동일 동작

JSON 으로 보내고 싶으면:

```powershell
$body = @{
    line_id      = 1
    equipment_id = "CAST-01"
    command_type = "power"
    value        = $true
    source_type  = "unreal-backend"
    created_by   = "unreal-backend"
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:5082/api/commands" `
    -ContentType "application/json" `
    -Body $body
```

응답:

```json
{
  "success": true,
  "data": {
    "id": 123,
    "command_id": 1735200000,
    "line_id": 1,
    "equipment_id": 101,
    "command_type": "power",
    "status": "REQUESTED",
    "created_at": "2026-06-23T17:00:00+09:00"
  }
}
```

---

## 2. 설비 카탈로그

| Equipment Code | line_id 범위 | equipment_id |
| -------------- | ------------ | ------------ |
| `CAST-01`      | 1, 2, 3      | 101          |
| `CNC-01`       | 1, 2, 3      | 201          |
| `CNC-02`       | 1, 2, 3      | 202          |
| `CNC-03`       | 1, 2, 3      | 203          |
| `WASH-01`      | 1, 2, 3      | 301          |
| `ASSY-01`      | 1, 2, 3      | 401          |
| `ASSY-02`      | 1, 2, 3      | 402          |
| `TEST-01`      | 1, 2, 3      | 501          |
| `TEST-02`      | 1, 2, 3      | 502          |

`equipment_id` 는 자동 도출되므로 URL/JSON 에는 `equipment_code` (예: `WASH-01`) 만 넣으면 된다.

---

## 3. 설비별 허용 `command_type` 표

### 공통 (모든 설비)

| command_type     | 의미                | `value` 예             |
| ---------------- | ------------------- | ---------------------- |
| `power`          | 전원 on/off         | `true`, `false`        |
| `load_request`   | 적재 요청           | `true`                 |
| `unload_request` | 언로드 요청         | `true`                 |
| `inject_warning` | 경고 주입 (테스트)  | `1`                    |
| `inject_error`   | 에러 주입 (테스트)  | `1`                    |
| `reset_error`    | 에러 리셋           | `true`                 |

### Prefix 전용 (setpoint — 이름 끝에 `_sp`)

| Prefix | 설비           | command_type                  | `value` 예  |
| ------ | -------------- | ----------------------------- | ----------- |
| CAST   | `CAST-01`      | `injection_pressure_sp`       | `120.5`     |
|        |                | `mold_temperature_sp`         | `180`       |
|        |                | `cooling_flow_sp`             | `8.5`       |
| CNC    | `CNC-01/02/03` | `spindle_speed_sp`            | `3500`      |
|        |                | `tool_usage_sp`               | `45`        |
|        |                | `coolant_flow_sp`             | `6.0`       |
| WASH   | `WASH-01`      | `cleaning_concentration_sp`   | `2.5`       |
|        |                | `cleaning_temperature_sp`     | `60`        |
|        |                | `cleaning_pressure_sp`        | `3.5`       |
| ASSY   | `ASSY-01/02`   | `tightening_torque_sp`        | `12.5`      |
|        |                | `tightening_angle_sp`         | `90`        |
|        |                | `press_force_sp`              | `500`       |
| TEST   | `TEST-01/02`   | `bore_dimension_sp`           | `25.4`      |
|        |                | `hole_dimension_sp`           | `12.8`      |

> **오타 주의** — `tightening_angles_sp` (복수형) 는 무효. 정답은 `tightening_angle_sp`.
> 비슷한 오탈자가 한 번 발견되어 카탈로그를 보정한 이력 있음.

검증 규칙은 `unreal-backend/BeApi/src/Features/Commands/CommandTypeCatalog.cs` 가 ground truth.

---

## 4. 명령 추적 / 조회

명령을 발행한 뒤 (REST 응답의 `command_id` 또는 `id` 를 가지고) 다음 화면/엔드포인트로 추적한다.

### 4.1 화면 (Razor Pages)

| URL                                                  | 용도                                          |
| ---------------------------------------------------- | --------------------------------------------- |
| `/`                                                  | 대시보드 — 연결 상태 카드 (TSDB/CommandDB/UDP/OPC UA) |
| `/command/list?PageNumber=1&PageSize=50`             | command_request 목록 (페이지네이션, 최신 순)   |
| `/command/detail?CommandId={id}`                     | 단건 상세 — request + history + latest_response |
| `/test/oneshot`                                      | (Dev 전용) one-shot URL 모음 — 시나리오별 prefill 링크 |

### 4.2 명령 발행 ~ 결과까지의 흐름

```
POST /api/commands  (or  /command/request?autoSubmit=1)
   │
   ▼  REQUESTED         ← row 등장. /command/list 에서 보임
command_request
   │
   ▼  SENT              ← Picker (1s) 가 claim 후 OPC UA write
   │
   ├─►  SUCCEEDED       ← Ack Handler (1s) 가 latest_response 보고 flip
   │
   └─►  FAILED          ← 같은 Ack Handler / Timeout Sweeper (5s, no-ack 10s 초과)
```

상태별 의미는 `docs/OPERATIONS.md §2.4` 참고.

### 4.3 REST 로 추적

```bash
# 발행한 단건 추적
curl http://localhost:5082/api/commands/123

# 설비별 최신 응답 1건만
curl http://localhost:5082/api/commands/latest/1/101

# 페이지네이션
curl "http://localhost:5082/api/commands?page=1&page_size=50"
```

응답 모델은 `unreal-backend/BeApi/src/Features/Commands/CommandModels.cs` 의
`CommandResponseDto`, `CommandDetailDto`, `CommandLatestResponseDto`.

---

## 5. 설비 영상 (embed)

Unreal Engine WebBrowser 플러그인에서 풀스크린으로 띄우는 영상 뷰어.

| URL                                                         | 용도                                  |
| ----------------------------------------------------------- | ------------------------------------- |
| `/video/embed/{lineId}/{equipmentId}`                       | **풀스크린 + controls 숨김 + autoplay + loop + muted** — Unreal 임베드용 |
| `/video/{lineId}/{equipmentId}`                             | 일반 컨트롤 있는 영상 페이지           |
| `/video` (목록 인덱스)                                       | 등록된 영상 목록                       |
| `/api/video/{lineId}/{equipmentId}`                         | 영상 파일 stream (GET/HEAD)            |
| `/api/video/{lineId}/{equipmentId}/thumbnail.jpg`           | 썸네일 (GET/HEAD)                      |

예시 (Unreal Blueprint 의 WebBrowser URL):

```
http://192.168.5.10:5082/video/embed/1/101
```

`lineId` / `equipmentId` 는 **정수** (위 §2 의 equipment_id, 예: CAST-01 = 101).
설비 코드 문자열이 아닌 정수 ID 임에 주의.

---

## 6. 연결 상태 모니터링

```bash
# 전체 — TSDB / CommandDB / UDP / OPC UA 4 개 타겟
curl http://localhost:5082/api/status

# 특정 타겟만
curl http://localhost:5082/api/status/commanddb
curl http://localhost:5082/api/status/tsdb
curl http://localhost:5082/api/status/udp
curl http://localhost:5082/api/status/opcua_xdas
```

응답 예:

```json
{
  "overall": "Ok",
  "targets": [
    { "target": "tsdb",     "state": "Ok", "latency_ms": 3.1,  "checked_at": "..." },
    { "target": "commanddb","state": "Ok", "latency_ms": 2.8,  "checked_at": "..." }
  ]
}
```

대시보드 화면은 `/` (Razor) 에서 카드 형태로 동일 데이터 조회.

---

## 7. 최신 센서값 조회 (TSDB)

```bash
# 모든 설비의 최신값 한 번에
curl http://localhost:5082/api/latest

# 특정 설비
curl http://localhost:5082/api/latest/1/101
```

LatestCache (in-memory) 를 거치므로 매우 빠름. 캐시 미스 시 TSDB 조회.

---

## 8. UDP Replay (테스트 / 시뮬레이션)

저장된 UDP wire 데이터를 일정 구간 재생하는 기능. **테스트/리허설 용**.

### 8.1 URL 한 줄로 시작 / 정지 (GET)

```bash
# 최근 10분간의 데이터를 1배속 1회 재생
curl "http://localhost:5082/api/replay/start-link?last_minutes=10&speed=1"

# 절대 시각으로 (2배속)
curl "http://localhost:5082/api/replay/start-link?from=2026-06-22T15:00:00&to=2026-06-22T15:10:00&speed=2"

# 무한 반복
curl "http://localhost:5082/api/replay/start-link?last_minutes=10&loop=true"

# 정지
curl http://localhost:5082/api/replay/stop-link

# 상태
curl http://localhost:5082/api/replay/status
```

| 파라미터        | 필수      | 의미                                 |
| --------------- | --------- | ------------------------------------ |
| `last_minutes`  | (택1)     | 현재 기준 상대 시간 (분)             |
| `from` / `to`   | (택1)     | 절대 시각 (ISO-8601)                 |
| `speed`         | ⬜        | 재생 배속 (기본 1)                   |
| `loop`          | ⬜        | `true` 이면 무한 반복                |

### 8.2 REST (POST) 로도 가능

```powershell
$body = @{ from = "2026-06-22T15:00:00"; to = "2026-06-22T15:10:00"; speed = 2 } | ConvertTo-Json
Invoke-RestMethod -Method Post `
    -Uri "http://localhost:5082/api/replay/start" `
    -ContentType "application/json" -Body $body
```

---

## 9. 전체 URL 카탈로그 (한눈에)

### Razor Pages

| URL                                               | 메서드 | 용도                                       |
| ------------------------------------------------- | ------ | ------------------------------------------ |
| `/`                                               | GET    | 대시보드                                   |
| `/command/request`                                | GET    | 명령 발행 폼 (prefill + `autoSubmit=1`)    |
| `/command/request`                                | POST   | 명령 발행 (폼 제출)                        |
| `/command/list`                                   | GET    | 명령 목록                                  |
| `/command/detail`                                 | GET    | 명령 상세 (`?CommandId=...`)               |
| `/test/oneshot`                                   | GET    | 테스트 링크 모음 (**Dev 전용**)             |
| `/video`                                          | GET    | 영상 목록                                  |
| `/video/{lineId}/{equipmentId}`                   | GET    | 영상 상세 (controls)                       |
| `/video/embed/{lineId}/{equipmentId}`             | GET    | 영상 풀스크린 임베드 (Unreal 용)            |

### REST JSON API

| URL                                               | 메서드      | 용도                              |
| ------------------------------------------------- | ----------- | --------------------------------- |
| `/api/commands`                                   | POST        | 명령 발행 (JSON body)             |
| `/api/commands`                                   | GET         | 페이지네이션 목록 (`?page=`, `?page_size=`) |
| `/api/commands/{commandId:int}`                   | GET         | 명령 단건 (history + latest)      |
| `/api/commands/latest/{lineId}/{equipmentId}`     | GET         | 설비별 최신 응답 1건              |
| `/api/latest`                                     | GET         | 모든 설비 최신 센서값             |
| `/api/latest/{lineId}/{equipmentId}`              | GET         | 특정 설비 최신 센서값             |
| `/api/status`                                     | GET         | 전체 연결 상태                    |
| `/api/status/{target}`                            | GET         | 특정 타겟 상태                    |
| `/api/video/{lineId}/{equipmentId}`               | GET / HEAD  | 영상 stream                       |
| `/api/video/{lineId}/{equipmentId}/thumbnail.jpg` | GET / HEAD  | 영상 썸네일                       |
| `/api/replay/start`                               | POST        | replay 시작 (JSON)                |
| `/api/replay/stop`                                | POST        | replay 정지                       |
| `/api/replay/start-link`                          | GET         | replay 시작 (URL 한 줄)           |
| `/api/replay/stop-link`                           | GET         | replay 정지 (URL 한 줄)           |
| `/api/replay/status`                              | GET         | replay 상태                       |

---

## 10. 멱등성 (idempotency)

같은 명령을 중복 발행하지 않으려면 `idempotency_key` 를 함께 보낸다.

### URL 방식

```
http://localhost:5082/command/request?line=1&equip=CAST-01&command_type=power&value=true&idempotency_key=my-unique-key-001&autoSubmit=1
```

### REST 방식 — 헤더 또는 body

```bash
curl -X POST http://localhost:5082/api/commands \
    -H "Content-Type: application/json" \
    -H "Idempotency-Key: my-unique-key-001" \
    -d '{"line_id":1,"equipment_id":"CAST-01","command_type":"power","value":true}'
```

같은 key 로 재발행 시 새 row 가 만들어지지 않고 **기존 row 의 결과를 그대로 반환**한다.
운영자 화면 (autoSubmit) 에서 같은 링크를 여러 번 클릭하면 매번 새 row 가 생성됨에 주의 —
brute-force 클릭 방지가 필요하면 idempotency_key 를 URL 에 포함시킬 것.

---

## 11. 트러블슈팅

| 증상                                       | 원인 / 해결                                                                  |
| ------------------------------------------ | ---------------------------------------------------------------------------- |
| LAN 다른 머신에서 5082 연결 거부            | `docs/OPERATIONS.md §5.2` (Kestrel binding + Windows Firewall)               |
| `command_type` 유효성 에러                  | §3 표 또는 `CommandTypeCatalog.cs` 와 비교                                   |
| `equipment_id` 유효성 에러                  | §2 표 확인 (`equip` 파라미터는 코드 문자열이 정답)                            |
| `/test/oneshot` 이 404                      | Production 환경 — Development 일 때만 노출                                    |
| 발행은 됐지만 status 가 SENT 에서 안 변함   | line-das 또는 설비 측 응답 없음. 10초 후 Timeout Sweeper 가 FAILED 로 정리   |
| 같은 명령이 두 번 등록됨                    | `idempotency_key` 누락. §10 참고                                              |

---

## 변경 이력

| 일자        | 변경                                                                  |
| ----------- | --------------------------------------------------------------------- |
| 2026-06-23  | 초안 — chore PR (uecada CLI + docs)                                   |
