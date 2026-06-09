# 설비 프로토콜 설명서 (개정판)

본 문서는 설비 시뮬레이터의 프로토콜 노출 구조와 태그 설계 기준을 최신 구성에 맞게 다시 정리한 문서이다.
이번 개정판은 3개 라인 × 9개 설비, 총 27개 설비 설정을 자동 생성하는 구성과 상태머신 기반 태그 확장을 반영한다.

## 문서 목적

이 문서는 설비별 JSON 설정 생성 규칙, 공통 태그 체계, 상태머신 연동 태그, 그리고 프로토콜별 주소 매핑 기준을 한 번에 이해할 수 있도록 재구성한 설명서이다.  
특히 기존 설명서에서 분산되어 있던 status, progress, cycle_time, alarm, counter, fault inject event의 의미를 명확히 하고, MC Protocol / Modbus / OPC UA 설정 작성 기준을 데이터시트 형식으로 통합한다.

## 구성 범위

생성 대상은 3개 생산 라인과 라인당 9개 설비로 구성되며, 전체 설정 파일 수는 27개이다.  
설비군은 CAST-01, CNC-01, CNC-02, CNC-03, WASH-01, ASSY-01, ASSY-02, TEST-01, TEST-02로 고정되며, 각 라인 디렉터리 아래에 동일 패턴으로 JSON이 생성된다.

| 항목 | 내용 |
|---|---|
| 라인 수 | 3  |
| 라인당 설비 수 | 9  |
| 총 설정 파일 수 | 27  |
| 출력 경로 패턴 | `line{n}/{EQUIPMENT_ID}.json`  |

## 설정 생성 구조

설정 생성기는 `power`, `status`, `event`, `counter`, `alarm`, `process tags`, `progress`, `cycle_time` 순으로 태그를 합성해 설비별 설정을 구성한다.  
이 구조는 상태머신이 직접 관리하는 태그와 프로토콜 입출력용 태그를 분리하면서도, 모든 설비가 공통된 운영 인터페이스를 갖도록 만든다.

### 공통 태그 그룹

모든 설비는 최소한 다음 공통 태그 집합을 가진다.

| 태그 그룹 | 태그 | 설명 |
|---|---|---|
| 전원 | `power` | 설비 전원 상태  |
| 상태 | `status` | 상태머신 출력 상태값, `IDLE/RUNNING/WARNING/ERROR/COMPLETE`를 정수로 표현  |
| 이벤트 | `load_request`, `unload_request`, `reset_error`, `inject_warning`, `inject_error` | 외부 제어 입력 이벤트  |
| 카운터 | `heartbeat`, `part_count` | 주기 heartbeat 및 완료 누적 수량  |
| 알람 | `alarm_active`, `alarm_code` | 현재 알람 발생 여부와 코드  |
| 진행 | `progress` | 공정 진행률, 상태머신이 누적 관리하는 값  |
| 사이클 | `cycle_time` | 사이클 시간 추정 또는 최근 완료 시간  |

## 상태머신 연동 규칙

이번 개정의 핵심은 `progress`가 더 이상 sensor 태그가 아니라 상태머신이 누적 관리하는 `role=progress` 태그라는 점이다.  
또한 `status`와 `cycle_time`이 모든 설비에 공통 추가되어, 외부 프로토콜 클라이언트가 설비 상태와 사이클 정보를 직접 읽을 수 있게 되었다.

상태값 정의는 다음과 같다.

| 상태명 | 값 | 의미 |
|---|---:|---|
| IDLE | 0 | 대기 상태  |
| RUNNING | 1 | 정상 운전 상태  |
| WARNING | 2 | 경고 상태, 공정 진행은 유지될 수 있음  |
| ERROR | 3 | 오류 상태  |
| COMPLETE | 4 | 작업 완료 상태  |

이 상태값은 `state.py`의 상태머신 출력과 직접 연결되며, 알람 태그도 동일 상태를 기반으로 갱신된다.  
`alarm_active`와 `alarm_code`는 단순 통신 태그가 아니라 내부 상태 해석 결과를 외부로 노출하는 운영 태그로 봐야 한다.

## 설비별 공정 태그

설비별 process tag는 setpoint와 sensor의 쌍으로 정의되며, 공정 특성에 따라 경고/오류 임계값이 포함된다.  
주조, 가공, 세척, 조립, 검사 설비는 각기 다른 공정 변수 이름을 사용하지만, 전체 구조는 동일하다.

### 설비군별 공정 변수 예시

| 설비군 | Setpoint 예시 | Sensor 예시 |
|---|---|---|
| CAST | `injection_pressure_sp`, `mold_temperature_sp`, `cooling_flow_sp`  | `injection_pressure`, `mold_temperature`, `cooling_flow`  |
| CNC | `spindle_speed_sp`, `tool_usage_sp`, `coolant_flow_sp`  | `spindle_speed`, `tool_usage`, `coolant_flow`  |
| WASH | `cleaning_concentration_sp`, `cleaning_temperature_sp`, `cleaning_pressure_sp`  | `cleaning_concentration`, `cleaning_temperature`, `cleaning_pressure`  |
| ASSY | `tightening_torque_sp`, `tightening_angle_sp`, `press_force_sp`  | `tightening_torque`, `tightening_angle`, `press_force`  |
| TEST | `bore_dimension_sp`, `hole_dimension_sp`  | `bore_dimension`, `hole_dimension`, `result_ok`  |  
  
  
## 센서별 경고/오류 임계값 (lo/hi 동시 정의)

### 개요

모든 센서는 **상·하한 4단계 임계값**을 기준으로 상태를 판정한다.

```
err_lo  warn_lo  ← 정상 영역 →  warn_hi  err_hi
  ▼        ▼                      ▼        ▼
 ERROR  WARNING       NORMAL     WARNING  ERROR
```

| 상태 | 조건 |
|------|------|
| `NORMAL`  | `warn_lo ≤ 측정값 ≤ warn_hi` |
| `WARNING` | `warn_lo > 측정값 ≥ err_lo` 또는 `warn_hi < 측정값 ≤ err_hi` |
| `ERROR`   | `측정값 < err_lo` 또는 `측정값 > err_hi` |

> **bool 센서** (`result_ok`)는 임계값을 정의하지 않고 값 자체(`True/False`)로 판정한다.

***

### CAST-01 — 다이캐스팅

| 태그명 | 단위 | base | stddev | warn_lo | err_lo | warn_hi | err_hi |
|--------|------|-----:|-------:|--------:|-------:|--------:|-------:|
| `injection_pressure` | MPa    |  80.0 | 1.5 |  73.0 |  68.0 |  87.0 |  93.0 |
| `mold_temperature`   | °C     | 215.0 | 1.2 | 205.0 | 195.0 | 225.0 | 235.0 |
| `cooling_flow`       | L/min  |  40.0 | 1.0 |  34.0 |  28.0 |  48.0 |  54.0 |

**설계 근거**
- `injection_pressure` : 사출 부족(하한) · 과압(상한) 모두 금형 손상 원인
- `mold_temperature` : 저온 미충전 / 고온 소착·변형 동시 위험
- `cooling_flow` : 저유량 냉각 불량이 주요 위험, 과유량은 밸브·펌프 이상 신호

***

### CNC-01 / CNC-02 / CNC-03 — CNC 가공

| 태그명 | 단위 | base | stddev | warn_lo | err_lo | warn_hi | err_hi |
|--------|------|-----:|-------:|--------:|-------:|--------:|-------:|
| `spindle_speed` | rpm   | 600 | 15.0 |  530 |  460 |  670 |  720 |
| `tool_usage`    | %     |   0 |  1.2 |  0.0 |  0.0 |  75.0 |  90.0 |
| `coolant_flow`  | L/min |  18 |  0.6 | 14.0 | 10.0 | 24.0 | 28.0 |

**설계 근거**
- `spindle_speed` : 저속 = 절삭 부하 과다, 과속 = 진동·베어링 손상
- `tool_usage` : 누적 마모량, 음수 없으므로 `warn_lo = err_lo = 0.0` (경계 표시용)
- `coolant_flow` : 저유량이 주 위험(열 축적), 과유량은 펌프 이상 신호

***

### WASH-01 — 세척기

| 태그명 | 단위 | base | stddev | warn_lo | err_lo | warn_hi | err_hi |
|--------|------|-----:|-------:|--------:|-------:|--------:|-------:|
| `cleaning_concentration` | %    |  3.2 | 0.12 |  2.6 |  2.0 |  3.8 |  4.5 |
| `cleaning_temperature`   | °C   | 62.0 | 0.8  | 56.0 | 50.0 | 68.0 | 75.0 |
| `cleaning_pressure`      | bar  |  4.0 | 0.15 |  3.3 |  2.7 |  4.7 |  5.3 |

**설계 근거**
- `cleaning_concentration` : 저농도 = 세척 불량, 고농도 = 부품 부식
- `cleaning_temperature` : 저온 = 오염 제거력 저하, 고온 = 부품 변형
- `cleaning_pressure` : 저압 = 세척력 부족, 고압 = 실링 손상

***

### ASSY-01 / ASSY-02 — 조립

| 태그명 | 단위 | base | stddev | warn_lo | err_lo | warn_hi | err_hi |
|--------|------|-----:|-------:|--------:|-------:|--------:|-------:|
| `tightening_torque` | N·m |   40.0 |  0.5 |  37.0 |  34.0 |  43.0 |  46.0 |
| `tightening_angle`  | deg |   90.0 |  1.5 |  84.0 |  78.0 |  96.0 | 102.0 |
| `press_force`       | N   | 1800.0 | 50.0 | 1620.0 | 1450.0 | 1980.0 | 2150.0 |

**설계 근거**
- `tightening_torque` : 하한 = 체결 불량(풀림), 상한 = 나사산 파손
- `tightening_angle` : 하한 = 미체결, 상한 = 과체결(나사산 손상)
- `press_force` : 하한 = 압입 미달(빠짐 불량), 상한 = 부품 파손

***

### TEST-01 / TEST-02 — 검사

| 태그명 | 단위 | base | stddev | warn_lo | err_lo | warn_hi | err_hi |
|--------|------|-----:|-------:|--------:|-------:|--------:|-------:|
| `bore_dimension` | mm | 40.000 | 0.006 | 39.975 | 39.950 | 40.025 | 40.050 |
| `hole_dimension` | mm | 10.200 | 0.015 | 10.160 | 10.120 | 10.240 | 10.280 |
| `result_ok`      | — | `True` | — | — | — | — | — |

**설계 근거**
- `bore_dimension` / `hole_dimension` : 공차 양방향 감시 (과소 = 압입 불가, 과대 = 헐거움)
- `result_ok` : bool 판정값, 임계값 없이 `False` 시 즉시 ERROR

***

### state.py 판정 로직 예시

```python
def sensor_health(tag: dict, value: float | bool) -> str:
    """
    role='sensor' 태그에 대해 NORMAL / WARNING / ERROR 를 반환한다.
    bool 타입은 value 자체로 판정 (False → ERROR).
    """
    if tag.get("data_type") == "bool":
        return "NORMAL" if value else "ERROR"

    err_lo  = tag.get("err_lo",  float("-inf"))
    warn_lo = tag.get("warn_lo", float("-inf"))
    warn_hi = tag.get("warn_hi", float("inf"))
    err_hi  = tag.get("err_hi",  float("inf"))

    if value < err_lo or value > err_hi:
        return "ERROR"
    if value < warn_lo or value > warn_hi:
        return "WARNING"
    return "NORMAL"
```

> **참고** : `warn_lo = err_lo = 0.0` 처럼 동일값 설정 시 WARNING 구간이 0이 되어
> 실질적으로 하한 ERROR 경계만 존재한다 (`tool_usage` 등 단방향 감시 센서에 활용).

## 프로토콜별 매핑 규칙

기존 설명서와 마찬가지로 태그는 프로토콜별 매핑 필드를 통해 주소에 연결되며, MC Protocol은 `mc`, Modbus 계열은 `mb`, OPC UA는 네임스페이스 기반 노출 방식을 사용한다.  
이번 개정에서는 status, cycle_time, counter, alarm, inject fault event까지 모두 매핑 대상에 포함되었다.

### MC Protocol

CAST-01은 MC Protocol 매핑을 사용하며, bit 계열 이벤트는 `M`, 수치 계열은 `D` 디바이스에 배치된다.  
예를 들어 `power`, `load_request`, `inject_warning`, `inject_error`는 `M` 디바이스에, `status`, `alarm_code`, `heartbeat`, `progress`, `cycle_time`은 `D` 디바이스에 매핑된다.

| 태그 | 디바이스 | 주소 |
|---|---|---:|
| `power` | M | 0  |
| `load_request` | M | 1  |
| `unload_request` | M | 2  |
| `reset_error` | M | 3  |
| `inject_warning` | M | 4  |
| `inject_error` | M | 5  |
| `status` | D | 10  |
| `alarm_active` | M | 10  |
| `alarm_code` | D | 12  |
| `heartbeat` | D | 14  |
| `part_count` | D | 16  |
| `progress` | D | 106  |
| `cycle_time` | D | 108  |

### Modbus

CNC와 WASH 설비는 Modbus 계열 매핑을 사용하며, coil과 holding register 기반으로 구성된다.  
CNC는 정수형 spindle 관련 태그와 float 공정 태그를 분리해 주소를 배치하고, WASH는 세척 공정용 float 태그 중심으로 구성된다.

#### CNC 계열 주요 매핑

| 태그 | kind | 주소 |
|---|---|---:|
| `power` | coil | 0  |
| `load_request` | coil | 1  |
| `reset_error` | coil | 3  |
| `inject_warning` | coil | 4  |
| `inject_error` | coil | 5  |
| `alarm_active` | coil | 10  |
| `status` | hr_int | 4  |
| `alarm_code` | hr_int | 6  |
| `heartbeat` | hr_int | 8  |
| `part_count` | hr_int | 10  |
| `progress` | hr_float | 1008  |
| `cycle_time` | hr_float | 1010  |

#### WASH 계열 주요 매핑

| 태그 | kind | 주소 |
|---|---|---:|
| `power` | coil | 0  |
| `load_request` | coil | 1  |
| `reset_error` | coil | 3  |
| `inject_warning` | coil | 4  |
| `inject_error` | coil | 5  |
| `alarm_active` | coil | 10  |
| `status` | hr_int | 4  |
| `alarm_code` | hr_int | 6  |
| `heartbeat` | hr_int | 8  |
| `part_count` | hr_int | 10  |
| `progress` | hr_float | 1012  |
| `cycle_time` | hr_float | 1014  |

### OPC UA

ASSY와 TEST 설비는 OPC UA 프로토콜을 사용하며, `namespace`는 `${LINE_ID:-LINE-00}_{equipment_id}` 형식으로 생성된다.  
따라서 같은 설비군이라도 라인별 네임스페이스가 분리되며, 라인 단위 브라우징과 설비 식별이 가능하다.

## 라인별 포트 규칙

TCP 기반 프로토콜은 기본 포트에 라인 오프셋을 적용하며, 오프셋 간격은 100이다.  
예를 들어 line 1은 기본 포트, line 2는 기본 포트 +100, line 3은 기본 포트 +200 규칙을 따른다.

공식은 다음과 같다.

$$
port = base\_port + (line\_no - 1) \times 100
$$

예를 들어 CAST-01의 base port가 5001이면 line1은 5001, line2는 5101, line3은 5201이 된다.  
다만 `modbus-rtu-tcp`로 정의된 CNC 계열은 코드상 별도 분기 때문에 현재 라인 오프셋을 적용하지 않고 base port를 그대로 사용한다.

## 생성 파일 예시 구조

각 설비 JSON은 공통적으로 `equipment_name`, `protocol`, `sampling_ms`, `tags`를 포함하며, 프로토콜에 따라 `host`, `port`, `slave_id`, `namespace`, `serial_path` 등이 추가된다.  
즉 문서화 시에는 공통 스키마와 프로토콜별 확장 필드를 구분해서 설명하는 것이 가장 명확하다.

```json
{
  "equipment_name": "${LINE_ID:-LINE-00}_CAST-01",
  "protocol": "mcprotocol",
  "sampling_ms": 1000,
  "tags": [
    {"name": "power", "role": "power", "data_type": "bool", "base_value": true},
    {"name": "status", "role": "status", "data_type": "int", "base_value": 0}
  ],
  "host": "0.0.0.0",
  "port": 5001
}
```

## 문서 개정 시 강조할 항목

설명서를 업데이트할 때는 다음 세 가지를 반드시 명시하는 것이 좋다.  
첫째, `progress`는 센서값이 아니라 상태머신이 누적 계산하는 내부 상태 태그라는 점, 둘째 `status/cycle_time/alarm/counter`는 모든 설비의 공통 운영 태그라는 점, 셋째 fault inject 이벤트가 이제 공식 제어 인터페이스에 포함되었다는 점이다.

- `status` 공통 추가: 외부 시스템이 설비 상태를 정수 코드로 직접 조회 가능.
- `progress` 역할 변경: sensor가 아니라 상태머신 관리 태그.
- `cycle_time` 공통 추가: 추정 또는 실측 사이클 시간 조회 가능.
- `alarm_active`, `alarm_code` 공통 추가: 경고/에러 상태 노출.
- `inject_warning`, `inject_error` 추가: 테스트/시뮬레이션용 fault 주입 지원.

## 권장 문서 구조

개정판 설명서는 기존 PDF의 프로토콜 일반 설명을 유지하되, 태그 정의와 설비별 데이터시트 부분을 현재 생성기 기준으로 다시 쓰는 방식이 가장 효율적이다.  
실무적으로는 `개요 → 공통 태그 → 상태머신 태그 → 프로토콜별 매핑 → 설비군별 태그 목록 → 라인/포트 규칙 → JSON 예시` 순서로 재구성하면 유지보수가 쉬워진다.