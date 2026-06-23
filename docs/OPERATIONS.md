# Command Center — Operations Guide

UECADA × MES 통합 환경에서 **command-center** (Node-RED) 와 그 주변 컴포넌트의
운영 매뉴얼이다. 다음 항목을 다룬다.

1. 컴포넌트 / 데이터 흐름 개요
2. command-center 의 5개 flow (Picker / OPC UA Server / Response Sink / Ack Handler / Timeout Sweeper)
3. 환경변수 / 컨테이너 의존성
4. 장애 대응 절차
5. **네트워킹 (BeApi 외부 접근, Windows Firewall, OPC UA, UDP)**
6. **알려진 제약 사항 (known-gaps)**

> **DB 스키마는 어떤 PR 에서도 변경하지 않는다.** 모든 동작은 기존 컬럼
> (`command_request`, `command_latest_response`, `command_response_event`)
> 기반이며, sweep / ack / retry 모두 SELECT/UPDATE 만 사용한다.

---

## 1. 컴포넌트 / 데이터 흐름

```
                  ┌────────────────────────────────────┐
   web UI ─────►  BeApi  ──INSERT──►  command_request  │
                  (Windows local                       │
                   dotnet run                          │
                   :5082)                              │
                                                       ▼
                                          ┌────────────────────────┐
                                          │   command-center       │
                                          │  (Node-RED docker)     │
                                          │                        │
                                          │  Picker (1Hz)          │
                                          │    └─ claim SENT       │
                                          │       → OPC UA Server  │
                                          │         Line-0X.Request│
                                          │                        │
                                          │  OPC UA Server         │
                                          │    └─ ns=1;s=Line-0X.* │
                                          │                        │
                                          │  Response Sink         │
                                          │    └─ Line-0X.Ack 수신 │
                                          │       → INSERT 3 tables│
                                          │                        │
                                          │  Ack Handler (1Hz)     │
                                          │    └─ latest_response  │
                                          │       → SUCCEEDED/FAIL │
                                          │                        │
                                          │  Timeout Sweeper (5s)  │
                                          │    └─ stale SENT       │
                                          │       → FAILED         │
                                          └─────────┬──────────────┘
                                                    │ OPC UA Client
                                                    ▼
                          ┌──────────────────────────────────────────┐
                          │  nodered-line01 / line02 / line03        │
                          │  (line-das Node-RED docker × 3)          │
                          │                                          │
                          │  Line-0X.Request subscribe →             │
                          │    decode + switch → wr_<eq>_NN_<type>   │
                          │      → OpcUa-Client write                │
                          │  Line-0X.Ack write (results back)        │
                          └──────────────────────────────────────────┘
```

### 데이터베이스 분리

| DB                          | 컨테이너                     | host port | 용도                                           |
| --------------------------- | ---------------------------- | --------- | ---------------------------------------------- |
| `total_das_command`         | `smart-factory-commanddb`    | 55433     | command_request, command_latest_response, …    |
| `total_das_ts`              | `smart-factory-timescaledb`  | 55432     | 센서 시계열 (TSDB)                              |

`command-center` 와 BeApi 모두 위 두 DB 에 대해 read/write 한다.

---

## 2. command-center 의 5개 flow

`total_das/equip-sim/data/command-center/flows.json` 안의 탭 5개.

### 2.1 Picker — `tab_picker` (1 Hz)

`command_request` 에서 `REQUESTED` 행을 라인당 1건 atomic claim
(`SELECT … FOR UPDATE SKIP LOCKED` + `DISTINCT ON (line_id)`) → `SENT` 으로
업데이트 후 OPC UA Server `Line-0X.Request` 변수에 JSON 페이로드 write.

**Wire contract (BeApi 측 4 필드 + meta):**

```json
{"command_id": 123, "equipment_id": 401, "command_type": "tightening_angle_sp",
 "value": 12.5, "_retry_epoch": 1735200000}
```

- 앞 4 필드는 BeApi 와의 wire contract (CONTRACT.md §3).
- `_retry_epoch` 는 PR16.5 에서 추가된 nonce. retry 시 같은 값을
  OPC UA Variable 에 다시 쓸 때 deadband/no-change 감지에 걸리지 않도록 함.
  line-das 측 `decode command json once` 함수는 underscore-prefix 메타
  필드를 무시한다.

**한 라인에 in-flight 한 건 제약:** 같은 `line_id` 에 `PICKED` 또는 `SENT`
가 이미 있으면 Picker 가 새로 claim 하지 않는다 (`NOT EXISTS`).

### 2.2 OPC UA Server — `tab_command_center`

`opc.tcp://command-center:54850/UA/CommandCenter` 에서 두 종류 변수를 노출.

| Variable          | 방향                         | Datatype |
| ----------------- | ---------------------------- | -------- |
| `Line-0X.Request` | center → line-das (write)    | String   |
| `Line-0X.Ack`     | line-das → center (write)    | String   |

X = 01 / 02 / 03.

### 2.3 Command Response Sink — `tab_command_response_sink` (PR20)

`Line-0X.Ack` 변수에 line-das 가 write 한 응답 JSON 을 수신 → `equipment_structs`
캐시로 equipment_id 매핑 보강 → CommandDB 의 3 테이블에 동시 반영:

- `command_latest_response` (upsert by `(line_id, equipment_id, command_id)`)
- `command_response_event` (append, 감사용)

`postgresql` 노드는 `msg.query` / `msg.params` 패턴을 사용한다. (`msg.payload`
에 `{query, params}` 객체를 넣는 패턴은 동작하지 않음 — PR20 에서 발견.)

### 2.4 Ack Handler — `tab_ack_handler` (1 Hz)

`command_request.status='SENT'` 와 `command_latest_response.cmd_status IN (2..7)`
조인 → terminal 응답이 도착한 행을 `SUCCEEDED` (cmd_status=2) 또는
`FAILED` (3..7) 로 전환.

**Idempotency:** UPDATE 의 WHERE 에 `AND t.status='SENT'` 재가드. 다중 인스턴스
동시 실행해도 한 row 는 한 번만 flip 된다.

| cmd_status | 의미                | request.status |
| ---------- | ------------------- | -------------- |
| 2          | COMPLETED           | `SUCCEEDED`    |
| 3          | BUSY                | `FAILED`       |
| 4          | FAULT               | `FAILED`       |
| 5          | TIMEOUT (장비측)    | `FAILED`       |
| 6          | REJECTED_INVALID    | `FAILED`       |
| 7          | REJECTED_BUSY       | `FAILED`       |

### 2.5 Timeout Sweeper — `tab_timeout_sweeper` (5 s) — **PR18**

`SENT` 상태이지만 `command_latest_response` 에 terminal 응답이 없고
`started_at + TIMEOUT_MS < now()` 인 행을 `FAILED('timeout: no ack within N ms')`
로 전환.

**기본 timeout:** `TIMEOUT_MS` env (default **10000 ms** = 10 초).

**Ack Handler 와 race-free:** SQL UPDATE 의 WHERE 에 `AND t.status='SENT'`
재가드 + `NOT EXISTS (terminal response)` 가드 → Ack Handler 가 먼저
`SUCCEEDED/FAILED` 로 flip 한 행은 Sweeper 가 건드리지 않음.

**worker_id graceful recovery:** Sweeper 는 `worker_id` 로 필터하지 **않는다**.
command-center 가 죽었다 살아나면 새 `WORKER_ID` 로 동작하지만, 기존 SENT
row 들은 Picker 의 `NOT EXISTS (PICKED/SENT)` 가드에 막혀 다시 claim 되지
않고 영원히 stuck 된다. 이 stuck 을 푸는 책임이 Sweeper 다 — 다른 살아있는
command-center 인스턴스 (혹은 재기동된 같은 인스턴스) 가 timeout 으로
정리하면 새 retry 가 정상 진입한다.

---

## 3. 환경변수 / 컨테이너 의존성

### 3.1 command-center 컨테이너 env

| 변수                  | 기본값              | 설명                                                     |
| --------------------- | ------------------- | -------------------------------------------------------- |
| `WORKER_ID`           | `command-center`    | Picker claim 시 row 에 박는 식별자. 다중 인스턴스 운영시 unique  |
| `TIMEOUT_MS`          | `10000`             | Sweeper 가 SENT 행을 timeout 으로 판정하는 임계 (밀리초)  |
| `COMMANDDB_HOST`      | `commanddb`         | Postgres 호스트 (docker network 내 dns)                  |
| `COMMANDDB_PORT`      | `5432`              | (host 에서는 55433 매핑)                                 |
| `COMMANDDB_DB`        | `total_das_command` |                                                          |
| `COMMANDDB_USER`      | `total_das_cmd_user`|                                                          |
| `COMMANDDB_PASSWORD`  | `total_das_cmd_1234`|                                                          |

### 3.2 컨테이너 의존성

```
commanddb ── timescaledb ── command-center ── nodered-line01/02/03
                                ▲
                                │ OPC UA Client
                                │
                            unreal-backend BeApi (Windows local, NOT docker)
```

- `command-center` 가 죽으면 새 command_request 가 처리되지 않지만,
  BeApi insert / line-das 의 OPC UA Server / DB 는 계속 동작한다.
  command-center 가 복구되면 누락 없이 처리 재개 (Picker 가 REQUESTED 부터,
  Sweeper 가 stuck SENT 를 정리).
- `nodered-line0X` 가 죽으면 해당 라인의 명령만 5초 후 timeout → FAILED.

---

## 4. 장애 대응 절차

### 4.1 command-center 컨테이너 재기동

```bash
docker restart command-center
```

재기동 직후 첫 5초 안에 Sweeper 가 stuck SENT 들을 모두 timeout 처리.
이후 Picker 가 REQUESTED 부터 다시 진행. **DB 손실 없음.**

### 4.2 line-das (line0X) 컨테이너 재기동

```bash
docker restart nodered-line01 nodered-line02 nodered-line03
```

해당 라인의 in-flight SENT 들이 10초 후 Sweeper 에 의해 FAILED 처리되며,
사용자는 web UI 에서 재발행 가능. **다른 라인은 무영향.**

### 4.3 commanddb 복구 후

`command-center` 의 postgresql 노드는 connection pool 을 자동 reconnect 한다.
DB downtime 중 누적된 `REQUESTED` 는 그대로 남아있다 → 복구 후 Picker 가
정상적으로 처리.

### 4.4 BeApi 재기동

BeApi 는 Windows local 에서 `dotnet run` 으로 동작 (docker 아님).
재기동해도 commanddb 의 데이터는 영향 없음. UDP / OPC UA 클라이언트 세션은
자동 재연결.

### 4.5 web UI 명령이 reject 됨

BeApi 로그에서 `command_type` validation 메시지 확인. `CommandTypeCatalog.cs`
의 prefix별 허용 목록과 비교. 정답 카탈로그:

| Prefix | Setpoint (`_sp`)                                                  |
| ------ | ----------------------------------------------------------------- |
| CAST   | `inject_pressure_sp`, `mold_temperature_sp`, `cooling_flow_sp`    |
| CNC    | `spindle_speed_sp`, `tool_usage_sp`, `coolant_flow_sp`            |
| WASH   | `cleaning_concentration_sp`, `cleaning_temperature_sp`, `cleaning_pressure_sp` |
| ASSY   | `tightening_torque_sp`, `tightening_angle_sp`, `press_force_sp`   |
| TEST   | `bore_dimension_sp`, `hole_dimension_sp`                          |

공통 (모든 prefix): `power`, `unload_request`, `load_request`,
`inject_warning`, `inject_error`, `reset_error`.

---

## 5. 네트워킹

### 5.1 토폴로지 (현재 구성)

- Windows 머신 A (`192.168.5.10`) — BeApi 가 `dotnet run` 으로 돈다. 5082 listen.
- 같은 switch 의 다른 머신 (`192.168.5.11`) — web UI / 운영자 단말 등.
- 둘 다 같은 L2 segment.

### 5.2 BeApi 가 LAN 다른 머신에서 접근 안 될 때 (예: `192.168.5.11` → `192.168.5.10:5082` Connection refused / timeout)

**원인은 거의 항상 두 단계 중 하나, 또는 둘 다이다.**

#### 5.2.1 Kestrel binding (1차 원인)

`launchSettings.json` 의 `applicationUrl` 이 `localhost:5082` 이면 Kestrel
은 **127.0.0.1 (loopback)** 에만 바인딩한다. NIC 의 LAN IP 로는 listen
자체를 하지 않는다 → 방화벽 이전 단계에서 차단.

**fix (이미 본 PR 에서 적용됨):**

```jsonc
// unreal-backend/BeApi/Properties/launchSettings.json
"applicationUrl": "http://0.0.0.0:5082"
```

`0.0.0.0` 은 모든 NIC 에서 listen 하라는 의미. `dotnet run` 재기동 필수.

**검증 (Windows PowerShell):**

```powershell
# 127.0.0.1 외에도 LAN IP 가 listen 에 등장해야 함
netstat -an | Select-String ":5082"
# 정상 예: TCP    0.0.0.0:5082           0.0.0.0:0              LISTENING
```

#### 5.2.2 Windows Defender Firewall 인바운드 규칙 (2차 원인)

Kestrel 이 `0.0.0.0:5082` 에 정상 바인딩됐는데도 LAN 에서 접근이 안 되면
Windows Defender Firewall 이 인바운드 TCP 5082 를 차단하고 있다.

**fix (관리자 PowerShell):**

```powershell
# 5082 인바운드 허용 (Private + Domain profile)
New-NetFirewallRule `
    -DisplayName "BeApi 5082 (LAN inbound)" `
    -Direction Inbound `
    -Action Allow `
    -Protocol TCP `
    -LocalPort 5082 `
    -Profile Private,Domain
```

운영망이 "공용 네트워크" 로 분류돼 있다면 `-Profile Public` 도 추가해야
한다. 현 사이트 토폴로지 (factory LAN) 는 보통 Private/Domain 으로 설정하는
것이 옳다.

**규칙 확인:**

```powershell
Get-NetFirewallRule -DisplayName "BeApi 5082*" |
    Format-Table DisplayName,Enabled,Direction,Action
```

**규칙 삭제 (잘못 추가했을 때):**

```powershell
Remove-NetFirewallRule -DisplayName "BeApi 5082 (LAN inbound)"
```

#### 5.2.3 진단 체크리스트 (위에서 아래로 순서대로 확인)

| #   | 단계                                                                  | 명령 (Windows PowerShell, 별도 표기 외)                                              |
| --- | --------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| 1   | NIC IP 가 실제로 `192.168.5.10` 인가                                  | `ipconfig`                                                                            |
| 2   | Kestrel 이 모든 NIC 에 listen 중인가                                  | `netstat -an \| Select-String ":5082"` → `0.0.0.0:5082 LISTENING` 이어야 함            |
| 3   | 같은 머신에서 LAN IP 로 접근되는가                                    | `curl http://192.168.5.10:5082/` (BeApi 헬스체크 엔드포인트가 있으면 그 경로)         |
| 4   | 클라이언트(192.168.5.11)에서 ping 되는가                              | `ping 192.168.5.10`                                                                  |
| 5   | 클라이언트에서 TCP 5082 가 열려 있는가                                | `Test-NetConnection 192.168.5.10 -Port 5082` → `TcpTestSucceeded : True` 이어야 함     |
| 6   | 방화벽 규칙이 존재하고 enabled 인가                                   | `Get-NetFirewallRule -DisplayName "BeApi 5082*"`                                     |
| 7   | 안티바이러스/EDR 가 추가 차단하지 않는가 (Trend Micro, V3 등)         | 해당 제품 콘솔에서 5082 inbound 허용                                                  |

`Test-NetConnection` 가 `TcpTestSucceeded : False` 면 그 단계에서 막힌
것이므로 위 표의 다음 단계가 아니라, 해당 단계 (Kestrel binding → 방화벽
→ 안티바이러스) 를 다시 점검한다.

#### 5.2.4 운영 시 권장 사항

- BeApi 를 Windows 서비스 (NSSM 등) 로 등록할 때는 `applicationUrl` 을
  `http://0.0.0.0:5082` 또는 `http://+:5082` 로 명시 — 둘 다 동일하게
  모든 NIC 에 바인딩한다.
- HTTPS 사용 시 (`:7258`) 동일한 방화벽 규칙을 7258 에 대해서도 추가.
- 운영 LAN 외부 (사내 무선, VPN 등) 에서의 접근이 불필요하면 `RemoteAddress`
  옵션으로 인바운드 허용 범위를 좁히는 것을 권장:

  ```powershell
  New-NetFirewallRule -DisplayName "BeApi 5082 (LAN inbound)" `
      -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5082 `
      -Profile Private,Domain -RemoteAddress 192.168.5.0/24
  ```

### 5.3 OPC UA 포트 (참고)

| 컴포넌트                       | endpoint                                              |
| ------------------------------ | ----------------------------------------------------- |
| command-center OPC UA Server   | `opc.tcp://command-center:54850/UA/CommandCenter`     |
| line-das (각 라인) OPC UA      | `opc.tcp://nodered-line0X:54860/UA/LINE-0X` 등         |
| total-das OPC UA Server (DAS)  | `opc.tcp://localhost:54880/UA/DAS` (BeApi 가 client)   |

OPC UA endpoint 가 docker 내부 dns name 으로 노출되는 경우 외부 (Windows
local BeApi) 에서 접근하려면 `docker-compose.yml` 의 포트 매핑이 필요하다.
현재는 BeApi 가 docker network 외부에서 도는 구조이므로 host 매핑된 포트
(예: 54880) 를 사용한다.

### 5.4 UDP multicast (참고)

BeApi 의 `appsettings.json` 의 `Udp` 섹션:

- `MulticastGroup`: `239.100.0.1`
- `MulticastInterface`: `192.168.5.10` (BeApi 가 동작하는 머신의 NIC IP)
- `ListenPort`: `50020`, `Relay.ListenPort`: `50010`

머신 IP 가 바뀌면 `MulticastInterface` 도 그에 맞춰 변경해야 한다.

---

## 6. 알려진 제약 사항 (known-gaps)

### 6.1 BeApi value validation 부재

`command_request.request_json.value` 의 JSON type 이 boolean / number /
string 중 무엇이어야 하는지에 대한 validation 이 BeApi 측에 없다. 예를
들어 web UI 가 `"ture"` (오타) 같은 string 을 보내도 reject 되지 않고
그대로 commanddb 에 들어가며, 시스템은 그대로 통과시킨다 (line-das 의
write 노드가 datatype 캐스팅 시 실패하면 BadTypeMismatch 가 발생할 수
있음).

**영향:** 위 같은 케이스에서 line-das OpcUa-Client write 에러로 라인이
일시적으로 stuck 될 수 있으나, **Timeout Sweeper (PR18) 가 10초 안에
FAILED 로 정리**한다. 사용자는 정상값으로 재발행하면 된다.

**향후 fix (이번 PR 범위 아님):** BeApi 의 `CommandTypeCatalog` 에 prefix /
command_type 별 value JSON schema 를 추가, controller 단에서 reject.

### 6.2 다중 command-center 인스턴스의 WORKER_ID

현재는 단일 command-center 인스턴스를 가정한다. 다중 인스턴스로 확장 시
각 인스턴스의 `WORKER_ID` env 가 서로 달라야 한다 (Picker 의 atomic claim
은 이미 race-free 이지만, 운영자가 로그/대시보드에서 추적하려면 unique id
권장).

### 6.3 Retry 정책

`max_retry`, `retry_count` 컬럼은 `command_request` 에 존재하지만 자동
retry 는 현재 구현되지 않는다 (수동 재발행만). 자동 retry 는 별도
PR 에서 다룰 예정 — 본 가이드 범위 밖.

---

## 변경 이력

| PR  | 변경                                                                    |
| --- | ----------------------------------------------------------------------- |
| 15  | Picker flow                                                             |
| 16  | line-das command_in sub → command-center rewire                         |
| 16.5| `_retry_epoch` nonce                                                    |
| 17  | Ack Handler                                                             |
| 19  | Line-0X.Ack channel → command-center sink (Response Sink)               |
| 18  | **Timeout Sweeper + BeApi Kestrel binding + OPERATIONS.md (이 문서)**    |
