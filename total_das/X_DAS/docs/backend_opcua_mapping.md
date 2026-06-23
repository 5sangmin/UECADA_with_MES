# X_DAS Backend OPC UA Mapping

X_DAS receives:

- PLC/line data from `equip-sim` line OPC UA servers.
- Common external sensor data from `DAS`.

X_DAS then publishes both as one equipment-centered OPC UA surface:

```text
opc.tcp://localhost:54880/UA/X_DAS/
```

The backend (`backend/`, Spring Boot) subscribes to **two namespaces** on this
surface:

- **ns=3 (canonical)** — `power` / `status_code` 등 설비 상태 신호. 설비상태 판정
  (`equipment_status`) 및 알람 생성(`alarm`)의 단일 진실 소스.
- **ns=2 (deprecated)** — 기존 numeric leaf. 진동 분석(ai-api) 등 기존 ring
  buffer 경로 호환을 위해 유지하되 신규 로직에서는 사용하지 않는다. 문서 하단 참고.

## ns=3 Node ID Convention (canonical)

```text
ns=3;s=TOTAL_DAS.{LINE_TOKEN}.{EQ_TOKEN}.{field}
```

- `LINE_TOKEN`: `LINE01`, `LINE02`, `LINE03` (대시 없음, 2자리 zero-pad)
- `EQ_TOKEN`: `CAST01`, `CNC01`, `CNC02`, `CNC03`, `WASH01`, `ASSY01`,
  `ASSY02`, `TEST01`, `TEST02`
- `field`: 아래 표의 필드명

예시:

```text
ns=3;s=TOTAL_DAS.LINE01.CAST01.power
ns=3;s=TOTAL_DAS.LINE02.CNC01.status_code
ns=3;s=TOTAL_DAS.LINE03.TEST02.heartbeat
```

### Subscribe 대상 필드

| field | OPC UA Type | EquipmentSnapshot 필드 | 비고 |
|---|---|---|---|
| `power` | Boolean | `power` | 상태 판정/알람 신호 |
| `status_code` | Int32 | `statusCode` | 상태 판정/알람 신호 |
| `heartbeat` | Int32 | `heartbeat` | stale 판정(5초)에 사용 |
| `quality_code` | Int32 | `qualityCode` | |
| `cmd_status` | Int32 | `cmdStatus` | |
| `progress` | Double | `progress` | |
| `cycle_time` | Double | `cycleTime` | |
| `part_count` | Int32 | `partCount` | |
| `data_1_sensor` | Double | `data1Sensor` | |
| `data_2_sensor` | Double | `data2Sensor` | |
| `data_3_sensor` | Double | `data3Sensor` | |
| `ts_epoch_ms` | Double | (무시) | 스냅샷에 저장 안 함 |
| `line_id` | Int32 | `lineId` | 별도 Variable Node |
| `equipment_id` | Int32 | `equipmentId` | 별도 Variable Node |

ns=3 값은 per-equipment `EquipmentSnapshot` POJO 로 in-memory
`ConcurrentHashMap<String, EquipmentSnapshot>` (`EquipmentSnapshotStore`) 에
저장된다. 키는 DB `equip_id` 형식(`LINE-01_CAST-01`)으로 통일한다.

## status_code → equipment_status / alarm 매핑

`status_code` 는 equip-sim 의 ES enum 을 그대로 따른다.

| status_code | 의미 | equipment_status | alarm severity |
|---:|---|---|---|
| 0 | IDLE | `STANDBY` | 생성 안 함 |
| 1 | RUNNING | `RUNNING` | 생성 안 함 |
| 2 | WARNING | `ALARM` | `WARNING` (전환 시 INSERT) |
| 3 | ERROR | `ALARM` | `DANGER` (전환 시 INSERT) |
| 4 | COMPLETE | `STANDBY` | 생성 안 함 |

**`power=false` 는 `status_code` 보다 우선 적용된다.**

| 조건 | equipment_status | alarm severity |
|---|---|---|
| `power=false` (override) | `MAINTENANCE` | `DANGER` (전원 OFF 진입 시 INSERT) |

`equipment_status` vocabulary 는 기존 4종(`RUNNING` / `STANDBY` /
`MAINTENANCE` / `ALARM`)만 사용한다. 스냅샷이 없거나 stale(heartbeat 기준
5초 경과)이면 override 하지 않고 base status 로 폴백한다.

## Alarm 생성 규칙 (상태 전환 시점에만 INSERT)

휴리스틱(센서 임계치)이 아니라 실데이터 상태 신호 기반이므로, 쿨다운 대신
**상태 전환 시점에만** alarm 을 INSERT 한다. 직전 상태(`power`,
`status_code`)를 in-memory 로 추적하며 동일 상태가 지속되는 동안에는 중복
INSERT 하지 않는다. 최초 관측 시에는 기준선만 기록하고 INSERT 하지 않는다.

| 전환 | alarm_code | alarm_type | severity |
|---|---|---|---|
| `power` true → false | `EQUIP_POWER_OFF` | 전원 차단 | `DANGER` |
| `status_code` 정상(0/1/4) → 2 | `EQUIP_STATUS_WARNING` | 설비 경고 | `WARNING` |
| `status_code` 정상(0/1/4) → 3 | `EQUIP_STATUS_ERROR` | 설비 에러 | `DANGER` |
| `status_code` 2 → 3 (escalation) | `EQUIP_STATUS_ERROR` | 설비 에러 | `DANGER` |

전원이 꺼진 상태(`power=false`)에서는 `status_code` 전환 알람을 생성하지 않는다
(전원 알람으로 충분). 알람 해소(close)는 담당자가 직접 처리한다.

INSERT 컬럼: `equipment_code`(=equipId), `alarm_code`, `alarm_type`,
`alarm_category`(="공통"), `severity`, `alarm_message`, `status`(="OPEN"),
`occurred_at`(=now), `sensor_snapshot`(스냅샷 JSON). 나머지는 nullable.

## ID 형식 변환 규약

세 가지 식별자 형식이 공존한다.

| 용도 | 형식 | 예시 |
|---|---|---|
| DB `equipment_status.equip_id` / `alarm.equipment_code` | 대시 + 언더스코어 | `LINE-01_CAST-01` |
| ns=3 OPC node token | 대시 없음 | `TOTAL_DAS.LINE01.CAST01` |
| ns=2 sensor buffer key | 콜론 메트릭 | `LINE01.CAST01:metric` |

ns=3 의 `LINE01.CAST01` 을 DB `equip_id`(`LINE-01_CAST-01`)로 정규화하는
양방향 헬퍼는 `EquipmentIdConverter`
(`backend/.../sensor/opcua/EquipmentIdConverter.java`) 에 있다. 토큰의 마지막
2자리를 번호로 보고 앞 prefix 와의 사이에 대시를 삽입하며, line/eq 토큰은 `_`
로 결합한다.

---

## (deprecated) ns=2 Mapping

> ns=2 는 기존 ring buffer 호환을 위해서만 유지된다. 신규 상태/알람 로직은
> 위의 ns=3 를 사용한다. 아래 표는 기존 numeric leaf 매핑 기록이며, 향후
> 영향 평가 후 별도 PR 에서 정리 예정.

All lines are published with a line prefix to prevent collisions:

```text
ns=2;s=LINE01.CAST01.InjectionPressure
ns=2;s=LINE02.CNC01.SpindleSpeed
ns=2;s=LINE03.WASH01.CleaningTemperature
```

For compatibility with the BE table, LINE-01 is also published without the line
prefix:

```text
ns=2;s=CAST01.InjectionPressure
```

Each equipment also has a combined JSON snapshot:

```text
ns=2;s=LINE01.CAST01.Payload
ns=2;s=CAST01.Payload
```

The payload contains `line_id`, `equipment_id`, `equipment_code`, `plc`,
`sensor`, and `updated_at`.

### PLC Mapping (ns=2)

Use `{LINE}` as `LINE01`, `LINE02`, or `LINE03`. LINE-01 also has the alias
without `{LINE}.`.

| Process | Equipment | Backend Node ID | Type | Source line tag | BE handling |
|---|---|---|---|---|---|
| Casting | CAST-01 | `ns=2;s={LINE}.CAST01.InjectionPressure` | Double | `injection_pressure` | Buffer: `CAST01:injection_pressure` |
| Casting | CAST-01 | `ns=2;s={LINE}.CAST01.MoldTemperature` | Double | `mold_temperature` | Buffer: `CAST01:mold_temperature` |
| Casting | CAST-01 | `ns=2;s={LINE}.CAST01.CoolingFlow` | Double | `cooling_flow` | Buffer: `CAST01:cooling_flow` |
| Casting | CAST-01 | `ns=2;s={LINE}.CAST01.CycleTime` | Double | `cycle_time` | Buffer: `CAST01:cycle_time` |
| Machining | CNC-01/02/03 | `ns=2;s={LINE}.CNC01.SpindleSpeed` | Int32 | `spindle_speed` | Buffer: `CNC01:spindle_speed` |
| Machining | CNC-01/02/03 | `ns=2;s={LINE}.CNC01.ToolUsage` | Double | `tool_usage` | Buffer: `CNC01:tool_usage` |
| Machining | CNC-01/02/03 | `ns=2;s={LINE}.CNC01.CoolantFlow` | Double | `coolant_flow` | Buffer: `CNC01:coolant_flow` |
| Washing | WASH-01 | `ns=2;s={LINE}.WASH01.CleaningConcentration` | Double | `cleaning_concentration` | Buffer: `WASH01:cleaning_concentration` |
| Washing | WASH-01 | `ns=2;s={LINE}.WASH01.CleaningTemperature` | Double | `cleaning_temperature` | Buffer: `WASH01:cleaning_temperature` |
| Washing | WASH-01 | `ns=2;s={LINE}.WASH01.CleaningPressure` | Double | `cleaning_pressure` | Buffer: `WASH01:cleaning_pressure` |
| Assembly | ASSY-01/02 | `ns=2;s={LINE}.ASSY01.TighteningTorque` | Double | `tightening_torque` | Buffer: `ASSY01:tightening_torque` |
| Assembly | ASSY-01/02 | `ns=2;s={LINE}.ASSY01.TighteningAngle` | Double | `tightening_angle` | Buffer: `ASSY01:tightening_angle` |
| Assembly | ASSY-01/02 | `ns=2;s={LINE}.ASSY01.PressForce` | Double | `press_force` | Buffer: `ASSY01:press_force` |
| Test | TEST-01/02 | `ns=2;s={LINE}.TEST01.BoreDimension` | Double | `bore_dimension` | Buffer: `TEST01:bore_dimension` |
| Test | TEST-01/02 | `ns=2;s={LINE}.TEST01.HoleDimension` | Double | `hole_dimension` | Buffer: `TEST01:hole_dimension` |
| Test | TEST-01/02 | `ns=2;s={LINE}.TEST01.ResultOk` | Boolean | `result_ok` | Buffer: `TEST01:result_ok` |

For `CNC-02`, `CNC-03`, `ASSY-02`, and `TEST-02`, the equipment code changes
to `CNC02`, `CNC03`, `ASSY02`, and `TEST02`.

> 참고: 기존 ns=2 의 `Status` / `AlarmCode` leaf 기반 `equipment_status`
> UPSERT 및 `alarms` INSERT 휴리스틱은 폐기되었다. 해당 판정은 위 ns=3
> `status_code` / `power` 기반으로 대체되었다.

### Common Sensor Mapping (ns=2)

The same four sensor fields are attached to every equipment payload.
These are equipment-scoped DAS values.

| Backend Node ID | Type | Sensor DAS source tag |
|---|---|---|
| `ns=2;s={LINE}.{EQUIP}.SensorVibration` | Double | `vibration_rms` |
| `ns=2;s={LINE}.{EQUIP}.SensorCurrent` | Double | `current_a` |
| `ns=2;s={LINE}.{EQUIP}.SensorVoltage` | Double | `voltage_v` |
| `ns=2;s={LINE}.{EQUIP}.SensorTemperature` | Double | `equipment_temperature_c` |
| `ns=2;s={LINE}.{EQUIP}.Payload` | String JSON | PLC + sensor merged snapshot |
