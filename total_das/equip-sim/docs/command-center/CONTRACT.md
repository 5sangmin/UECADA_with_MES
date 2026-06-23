# Command Center ↔ Line DAS — OPC UA Contract

이 문서는 Command Center (OPC UA Server) 와 Line DAS (OPC UA Client) 사이의
계약 (variable layout, payload schema, FSM hand-off) 을 정의한다.

## 1. Endpoint

- URL (운영 / line-das): `opc.tcp://command-center:5160/UA/CommandCenter`
- URL (호스트 검증용): `opc.tcp://127.0.0.1:5160/UA/CommandCenter`
- 인증: Anonymous (현 단계). TLS / cert 는 별도 작업.

> Endpoint URL 은 Server 가 client 에게 advertise 하는 hostname 과 일치해야 한다.
> compose 에서 `hostname: command-center` 로 설정하고, line-das 들은 같은
> `factory-net` 네트워크에 있어 `command-center` 로 resolve 된다.

## 2. Address Space

Namespace `ns=1`. 각 라인에 대해 2 개 변수 (총 3 라인 × 2 = 6 변수).

| BrowseName       | NodeId                       | DataType | Writable (from client) | 용도                              |
| ---------------- | ---------------------------- | -------- | ---------------------- | --------------------------------- |
| `Line-01.Request` | `ns=1;s=Line-01.Request`     | String   | No (server-only write) | Command Center → Line-01 명령 송신 |
| `Line-01.Ack`     | `ns=1;s=Line-01.Ack`         | String   | **Yes**                | Line-01 → Command Center 응답      |
| `Line-02.Request` | `ns=1;s=Line-02.Request`     | String   | No                     |                                   |
| `Line-02.Ack`     | `ns=1;s=Line-02.Ack`         | String   | **Yes**                |                                   |
| `Line-03.Request` | `ns=1;s=Line-03.Request`     | String   | No                     |                                   |
| `Line-03.Ack`     | `ns=1;s=Line-03.Ack`         | String   | **Yes**                |                                   |

초기값은 모두 빈 문자열 `""`. Line DAS 는 boot 시 빈 문자열을 무시해야 한다.

> 참고: node-red-contrib-opcua 의 OpcUa-Server 는 ACL 을 변수 단위로 강제하지 않는다.
> "Request 는 server-only" 라는 제약은 **운영 컨벤션**이며, line-das 는 절대로
> Request 변수에 write 하지 않는다. 위반 시 server 측 router 에서 무시한다.

## 3. Request Payload (Server → Client)

Command Center 가 `Line-0X.Request` 에 write 하는 JSON 문자열.

**실제 형식은 BeApi (`unreal-backend/BeApi`) 가 `command_request.request_json` 컴럼에 적재한 객체를 그대로 전달한다.** Picker 는 DB 컴럼들을 재조합하지 않고 `request_json` jsonb 를 문자열로 stringify 하여 OPC UA 에 write.

```json
{
  "command_id": 12345,
  "equipment_id": "CAST-01",
  "command_type": "injection_pressure_sp",
  "value": 120.0
}
```

### Field 정의 (wire 계약 — BeApi `CommandService.cs` 과 일치)

| Field          | Type                            | 필수 | 설명                                                                         |
| -------------- | ------------------------------- | ---- | ---------------------------------------------------------------------------- |
| `command_id`   | int32                           | ✅   | `command_id_seq` nextval. CommandDB `command_request.command_id` 와 동일. |
| `equipment_id` | string                          | ✅   | 설비 코드 문자열 (`"CAST-01"`, `"CNC-02"` 등). DB 컴럼 `equipment_id` 에는 LUT 변환된 int 로 저장되지만, `request_json` 에는 원본 코드 문자열을 그대로 유지해 line-das 가 설비 식별에 사용한다. |
| `command_type` | string                          | ✅   | 최대 64자. Equipment prefix 와 조합 검증 됨 (`CommandTypeCatalog`).         |
| `value`        | primitive 또는 any JSON         | ✅   | bool/number/string 또는 임의 JSON. 설비/`command_type` 별 스키마가 결정.  |

> Wire 는 4 필드만 고정, 순서도 고정 이다 (BeApi `JsonObject` 에서 이 순서대로 직렬화). 추가 필드가 필요해지면 BeApi 측과 함께 contract bump 한다 (아래 6 Versioning 참고).

> **시각은 DB `now()` 기준으로 통일** (사용자 결정). Picker 가 OPC UA 에 issue 하는 시점은 `command_request.started_at` 컬럼 에 `now()` 로 명시 기록되며 wire payload 엔 timestamp 필드 자체가 없다. 수신측(line-das) 시계 논란을 소거해 일관성 확보.

## 4. Ack Payload (Client → Server)

Line DAS 가 `Line-0X.Ack` 에 write 하는 JSON 문자열. **Single-shot DONE only.**
중간 progress / heartbeat 는 보내지 않는다.

```json
{
  "cmd_id": 12345,
  "line_id": 1,
  "equipment_id": 101,
  "accepted": true,
  "cmd_status": 0,
  "result": "OK",
  "error_message": null,
  "source_ts_ms": 1745478124789,
  "source_node_id": "line-das-01"
}
```

### Field 정의

| Field            | Type      | 필수 | 설명                                                                                            |
| ---------------- | --------- | ---- | ----------------------------------------------------------------------------------------------- |
| `cmd_id`         | int64     | ✅   | Request 의 cmd_id 그대로 반환. correlation key.                                                  |
| `line_id`        | int16     | ✅   | 1 / 2 / 3.                                                                                       |
| `equipment_id`   | int32     | ✅   | 101..502.                                                                                        |
| `accepted`       | bool      | ✅   | line-das 가 명령을 받아 처리했는가 (success/failure 와는 다름).                                    |
| `cmd_status`     | int       | ✅   | 0=SUCCEEDED, 1=FAILED, 2=REJECTED. (CommandDB FSM status code 와 매핑)                            |
| `result`         | string    | ✅   | 사람 읽기용 단축 라벨 (`OK` / `TIMEOUT_PLC` / `INVALID_VALUE` 등).                                  |
| `error_message`  | string?   | -    | `cmd_status != 0` 인 경우 상세 에러. 성공 시 `null`.                                              |
| `source_ts_ms`   | int64     | ✅   | line-das 가 명령 완료를 인지한 시각 (PLC 응답 timestamp). 진단용. DB 기록은 server `now()` 사용.   |
| `source_node_id` | string    | ✅   | line-das container hostname. 어느 worker 가 처리했는지 추적용.                                    |

## 5. FSM (CommandDB 측)

```
REQUESTED → PICKED → SENT → SUCCEEDED
                          → FAILED
                          → TIMEOUT
```

| 전이               | 누가          | 트리거                                                          |
| ------------------ | ------------- | --------------------------------------------------------------- |
| `→ REQUESTED`      | 외부 API      | (Command Center 외부) command_request INSERT.                    |
| `REQUESTED → PICKED` | Picker (PR13) | `FOR UPDATE SKIP LOCKED` 로 행 선점. worker_id, picked_at 기록.     |
| `PICKED → SENT`    | Picker (PR13) | OPC UA Server 에 Request write 성공. started_at 기록.             |
| `SENT → SUCCEEDED` | AckHandler (PR14) | Ack 수신, `accepted=true && cmd_status=0`. finished_at 기록.    |
| `SENT → FAILED`    | AckHandler (PR14) | Ack 수신, `cmd_status != 0`. finished_at 기록.                  |
| `SENT → TIMEOUT`   | TimeoutSweeper (PR15) | `started_at + timeout < now()` 인데 Ack 미수신.            |

## 6. Versioning

이 contract 의 변경은 line-das / Command Center 양쪽 동시에 반영되어야 한다.
호환성 깨지는 변경 시 namespace 를 분리하거나 (`ns=2` 등), payload 에
`schema_version` 필드를 추가하는 방식으로 처리한다.

현재 버전: **v1** (2025-06).
