"""3개 라인 × 9개 설비 = 27개 config 일괄 생성기.

변경 사항 (v3):
- 모든 센서에 lo/hi 동시 정의 적용 (공정 물리 특성 반영)
- base_value, stddev, warn/err lo/hi 실제 설비 수준으로 재조정
- tightening_angle / cleaning_temperature / bore_dimension / hole_dimension 등
  상·하한 동시 감시가 필요한 센서 양방향 임계값 추가
- injection_pressure / mold_temperature 등 하한 보호 임계값 추가
- result_ok(bool) 는 임계값 없음 유지
"""

from __future__ import annotations

import json
from pathlib import Path

OUT_ROOT = Path(__file__).parent


# ---------------------------------------------------------------------------
# 공통 태그
# ---------------------------------------------------------------------------

def power_tag() -> dict:
    return {"name": "power", "role": "power", "data_type": "bool", "base_value": True}


def status_tag() -> dict:
    return {"name": "status", "role": "status", "data_type": "int", "base_value": 0}


def event_tags() -> list[dict]:
    return [
        {"name": "load_request",   "role": "event", "data_type": "bool", "base_value": False},
        {"name": "unload_request", "role": "event", "data_type": "bool", "base_value": False},
        {"name": "reset_error",    "role": "event", "data_type": "bool", "base_value": False},
        {"name": "inject_warning", "role": "event", "data_type": "bool", "base_value": False},
        {"name": "inject_error",   "role": "event", "data_type": "bool", "base_value": False},
    ]


def progress_tag(cycle_sec: int) -> dict:
    return {
        "name": "progress", "role": "progress", "data_type": "float",
        "base_value": 0.0, "progress_speed": round(100.0 / cycle_sec, 4), "unit": "%",
    }


def cycle_time_tag(cycle_sec: int) -> dict:
    return {
        "name": "cycle_time", "role": "cycle_time", "data_type": "float",
        "base_value": float(cycle_sec), "unit": "s",
    }


def common_counter_tags() -> list[dict]:
    return [
        {"name": "heartbeat",  "role": "counter", "data_type": "int", "base_value": 0, "step": 1, "unit": "tick"},
        {"name": "part_count", "role": "counter", "data_type": "int", "base_value": 0, "step": 1, "unit": "ea"},
    ]


def common_alarm_tags() -> list[dict]:
    return [
        {"name": "alarm_active", "role": "alarm", "data_type": "bool", "base_value": False},
        {"name": "alarm_code",   "role": "alarm", "data_type": "int",  "base_value": 0},
    ]


# ---------------------------------------------------------------------------
# 설비별 공정 태그  (base / stddev / warn_lo / warn_hi / err_lo / err_hi)
# ---------------------------------------------------------------------------

def cast_process_tags() -> list[dict]:
    """
    다이캐스팅 (사출압력 / 금형온도 / 냉각유량)
    - 사출압력:  정상 80 MPa, 하한 손실 및 상한 과압 모두 감시
    - 금형온도:  정상 215 °C, 저온 미충전 / 고온 소착 모두 위험
    - 냉각유량:  정상 40 L/min, 저유량이 주 위험 + 과유량 밸브 이상
    """
    return [
        # setpoints
        {"name": "injection_pressure_sp", "role": "setpoint", "data_type": "float", "base_value": 80.0,  "unit": "MPa"},
        {"name": "mold_temperature_sp",   "role": "setpoint", "data_type": "float", "base_value": 215.0, "unit": "degC"},
        {"name": "cooling_flow_sp",        "role": "setpoint", "data_type": "float", "base_value": 40.0,  "unit": "L/min"},
        # sensors
        {
            "name": "injection_pressure", "role": "sensor", "data_type": "float",
            "source_sp": "injection_pressure_sp", "stddev": 1.5, "unit": "MPa",
            "warn_lo": 73.0,  "err_lo": 68.0,
            "warn_hi": 87.0,  "err_hi": 93.0,
        },
        {
            "name": "mold_temperature", "role": "sensor", "data_type": "float",
            "source_sp": "mold_temperature_sp", "stddev": 1.2, "unit": "degC",
            "warn_lo": 205.0, "err_lo": 195.0,
            "warn_hi": 225.0, "err_hi": 235.0,
        },
        {
            "name": "cooling_flow", "role": "sensor", "data_type": "float",
            "source_sp": "cooling_flow_sp", "stddev": 1.0, "unit": "L/min",
            "warn_lo": 34.0,  "err_lo": 28.0,
            "warn_hi": 48.0,  "err_hi": 54.0,
        },
    ]


def cnc_process_tags() -> list[dict]:
    """
    CNC 가공 (주축회전수 / 공구마모 / 절삭유유량)
    - 주축회전수:  정상 600 rpm, 저속 = 절삭 부하 과다, 과속 = 진동 문제
    - 공구마모:    0이 신품, 상한만 감시 (마모 누적)
    - 절삭유유량:  저유량이 주 위험 + 과유량 펌프 이상
    """
    return [
        {"name": "spindle_speed_sp",  "role": "setpoint", "data_type": "int",   "base_value": 600,  "unit": "rpm"},
        {"name": "tool_usage_sp",     "role": "setpoint", "data_type": "float", "base_value": 53.1,  "unit": "%"},
        {"name": "coolant_flow_sp",   "role": "setpoint", "data_type": "float", "base_value": 18.0, "unit": "L/min"},
        {
            "name": "spindle_speed", "role": "sensor", "data_type": "int",
            "source_sp": "spindle_speed_sp", "stddev": 15.0, "unit": "rpm",
            "warn_lo": 530,  "err_lo": 460,
            "warn_hi": 670,  "err_hi": 720,
        },
        {
            "name": "tool_usage", "role": "sensor", "data_type": "float",
            "source_sp": "tool_usage_sp", "stddev": 1.2, "unit": "%",
            # 마모는 하한 없음 (음수 불가 → err_lo=0 으로 경계 표시)
            "warn_hi": 75.0, "err_hi": 90.0,
        },
        {
            "name": "coolant_flow", "role": "sensor", "data_type": "float",
            "source_sp": "coolant_flow_sp", "stddev": 0.6, "unit": "L/min",
            "warn_lo": 14.0, "err_lo": 10.0,
            "warn_hi": 24.0, "err_hi": 28.0,
        },
    ]


def wash_process_tags() -> list[dict]:
    """
    세척기 (세척액농도 / 세척온도 / 세척압력)
    - 농도:    저농도 = 세척 불량, 고농도 = 부품 부식
    - 온도:    저온 = 오염 제거력 저하, 고온 = 부품 변형 위험
    - 압력:    저압 = 세척력 부족, 고압 = 실링 손상
    """
    return [
        {"name": "cleaning_concentration_sp", "role": "setpoint", "data_type": "float", "base_value": 3.2,  "unit": "%"},
        {"name": "cleaning_temperature_sp",   "role": "setpoint", "data_type": "float", "base_value": 62.0, "unit": "degC"},
        {"name": "cleaning_pressure_sp",      "role": "setpoint", "data_type": "float", "base_value": 4.0,  "unit": "bar"},
        {
            "name": "cleaning_concentration", "role": "sensor", "data_type": "float",
            "source_sp": "cleaning_concentration_sp", "stddev": 0.12, "unit": "%",
            "warn_lo": 2.6,  "err_lo": 2.0,
            "warn_hi": 3.8,  "err_hi": 4.5,
        },
        {
            "name": "cleaning_temperature", "role": "sensor", "data_type": "float",
            "source_sp": "cleaning_temperature_sp", "stddev": 0.8, "unit": "degC",
            "warn_lo": 56.0, "err_lo": 50.0,
            "warn_hi": 68.0, "err_hi": 75.0,
        },
        {
            "name": "cleaning_pressure", "role": "sensor", "data_type": "float",
            "source_sp": "cleaning_pressure_sp", "stddev": 0.15, "unit": "bar",
            "warn_lo": 3.3,  "err_lo": 2.7,
            "warn_hi": 4.7,  "err_hi": 5.3,
        },
    ]


def assy_process_tags() -> list[dict]:
    """
    조립 (체결토크 / 체결각도 / 압입력)
    - 토크:   하한 = 체결 불량, 상한 = 나사산 파손
    - 각도:   하한 = 미체결, 상한 = 과체결
    - 압입력: 하한 = 압입 미달, 상한 = 부품 파손
    """
    return [
        {"name": "tightening_torque_sp", "role": "setpoint", "data_type": "float", "base_value": 40.0,   "unit": "Nm"},
        {"name": "tightening_angle_sp",  "role": "setpoint", "data_type": "float", "base_value": 90.0,   "unit": "deg"},
        {"name": "press_force_sp",       "role": "setpoint", "data_type": "float", "base_value": 1800.0, "unit": "N"},
        {
            "name": "tightening_torque", "role": "sensor", "data_type": "float",
            "source_sp": "tightening_torque_sp", "stddev": 0.5, "unit": "Nm",
            "warn_lo": 37.0, "err_lo": 34.0,
            "warn_hi": 43.0, "err_hi": 46.0,
        },
        {
            "name": "tightening_angle", "role": "sensor", "data_type": "float",
            "source_sp": "tightening_angle_sp", "stddev": 1.5, "unit": "deg",
            "warn_lo": 84.0, "err_lo": 78.0,
            "warn_hi": 96.0, "err_hi": 102.0,
        },
        {
            "name": "press_force", "role": "sensor", "data_type": "float",
            "source_sp": "press_force_sp", "stddev": 50.0, "unit": "N",
            "warn_lo": 1620.0, "err_lo": 1450.0,
            "warn_hi": 1980.0, "err_hi": 2150.0,
        },
    ]


def test_process_tags() -> list[dict]:
    """
    검사 (보어치수 / 홀치수 / 합부판정)
    - 보어/홀: 공차 양방향 감시 (과소 = 압입 불가, 과대 = 헐거움)
    - result_ok: bool, 임계값 없음
    """
    return [
        {"name": "bore_dimension_sp", "role": "setpoint", "data_type": "float", "base_value": 40.000, "unit": "mm"},
        {"name": "hole_dimension_sp", "role": "setpoint", "data_type": "float", "base_value": 10.200, "unit": "mm"},
        {
            "name": "bore_dimension", "role": "sensor", "data_type": "float",
            "source_sp": "bore_dimension_sp", "stddev": 0.006, "unit": "mm",
            "warn_lo": 39.975, "err_lo": 39.950,
            "warn_hi": 40.025, "err_hi": 40.050,
        },
        {
            "name": "hole_dimension", "role": "sensor", "data_type": "float",
            "source_sp": "hole_dimension_sp", "stddev": 0.015, "unit": "mm",
            "warn_lo": 10.160, "err_lo": 10.120,
            "warn_hi": 10.240, "err_hi": 10.280,
        },
        {
            "name": "result_ok", "role": "sensor", "data_type": "bool",
            "base_value": True, "stddev": 0.0,
        },
    ]


# ---------------------------------------------------------------------------
# 장비 스펙 목록
# ---------------------------------------------------------------------------

EQUIPMENT_SPECS = [
    ("CAST-01", "mcprotocol",    5001, cast_process_tags, 60,  None),
    ("CNC-01",  "modbus-rtu-tcp", 5101, cnc_process_tags, 180, 1),
    ("CNC-02",  "modbus-rtu-tcp", 5102, cnc_process_tags, 180, 2),
    ("CNC-03",  "modbus-rtu-tcp", 5103, cnc_process_tags, 180, 3),
    ("WASH-01", "modbus",         5021, wash_process_tags, 60, None),
    ("ASSY-01", "opcua",          4841, assy_process_tags, 120, None),
    ("ASSY-02", "opcua",          4842, assy_process_tags, 120, None),
    ("TEST-01", "opcua",          4851, test_process_tags, 120, None),
    ("TEST-02", "opcua",          4852, test_process_tags, 120, None),
]

LINES = (1, 2, 3)
PORT_STRIDE = 100


def line_port(base_port: int, line_no: int) -> int:
    return base_port + (line_no - 1) * PORT_STRIDE


# ---------------------------------------------------------------------------
# 프로토콜 매핑
# ---------------------------------------------------------------------------

MC_MAPPING_CAST = {
    # coil / bool → M 영역
    "power":           {"device": "M", "address": 0},
    "load_request":    {"device": "M", "address": 1},
    "unload_request":  {"device": "M", "address": 2},
    "reset_error":     {"device": "M", "address": 3},
    "inject_warning":  {"device": "M", "address": 4},
    "inject_error":    {"device": "M", "address": 5},
    "alarm_active":    {"device": "M", "address": 10},
    # int / word → D 영역
    "status":                  {"device": "D", "address": 10},
    "alarm_code":              {"device": "D", "address": 12},
    "heartbeat":               {"device": "D", "address": 14},
    "part_count":              {"device": "D", "address": 16},
    # setpoints
    "injection_pressure_sp":   {"device": "D", "address": 0},
    "mold_temperature_sp":     {"device": "D", "address": 2},
    "cooling_flow_sp":         {"device": "D", "address": 4},
    # sensors (float → 2 word)
    "injection_pressure":      {"device": "D", "address": 100},
    "mold_temperature":        {"device": "D", "address": 102},
    "cooling_flow":            {"device": "D", "address": 104},
    "progress":                {"device": "D", "address": 106},
    "cycle_time":              {"device": "D", "address": 108},
}

MB_MAPPING_CNC = {
    # coils
    "power":          {"kind": "coil", "address": 0},
    "load_request":   {"kind": "coil", "address": 1},
    "unload_request": {"kind": "coil", "address": 2},
    "reset_error":    {"kind": "coil", "address": 3},
    "inject_warning": {"kind": "coil", "address": 4},
    "inject_error":   {"kind": "coil", "address": 5},
    "alarm_active":   {"kind": "coil", "address": 10},
    # holding registers – int
    "spindle_speed_sp": {"kind": "hr_int",   "address": 0},
    "spindle_speed":    {"kind": "hr_int",   "address": 2},
    "status":           {"kind": "hr_int",   "address": 4},
    "alarm_code":       {"kind": "hr_int",   "address": 6},
    "heartbeat":        {"kind": "hr_int",   "address": 8},
    "part_count":       {"kind": "hr_int",   "address": 10},
    # holding registers – float (2 regs each)
    "tool_usage_sp":    {"kind": "hr_float", "address": 1000},
    "tool_usage":       {"kind": "hr_float", "address": 1002},
    "coolant_flow_sp":  {"kind": "hr_float", "address": 1004},
    "coolant_flow":     {"kind": "hr_float", "address": 1006},
    "progress":         {"kind": "hr_float", "address": 1008},
    "cycle_time":       {"kind": "hr_float", "address": 1010},
}

MB_MAPPING_WASH = {
    "power":          {"kind": "coil", "address": 0},
    "load_request":   {"kind": "coil", "address": 1},
    "unload_request": {"kind": "coil", "address": 2},
    "reset_error":    {"kind": "coil", "address": 3},
    "inject_warning": {"kind": "coil", "address": 4},
    "inject_error":   {"kind": "coil", "address": 5},
    "alarm_active":   {"kind": "coil", "address": 10},
    "status":         {"kind": "hr_int",   "address": 4},
    "alarm_code":     {"kind": "hr_int",   "address": 6},
    "heartbeat":      {"kind": "hr_int",   "address": 8},
    "part_count":     {"kind": "hr_int",   "address": 10},
    "cleaning_concentration_sp": {"kind": "hr_float", "address": 1000},
    "cleaning_temperature_sp":   {"kind": "hr_float", "address": 1002},
    "cleaning_pressure_sp":      {"kind": "hr_float", "address": 1004},
    "cleaning_concentration":    {"kind": "hr_float", "address": 1006},
    "cleaning_temperature":      {"kind": "hr_float", "address": 1008},
    "cleaning_pressure":         {"kind": "hr_float", "address": 1010},
    "progress":                  {"kind": "hr_float", "address": 1012},
    "cycle_time":                {"kind": "hr_float", "address": 1014},
}


# ---------------------------------------------------------------------------
# 태그 조립 + 매핑 주입
# ---------------------------------------------------------------------------

def build_tags(process_tags: list[dict], protocol: str, cycle_sec: int, eq_id: str) -> list[dict]:
    tags = (
        [power_tag(), status_tag()]
        + event_tags()
        + common_counter_tags()
        + common_alarm_tags()
        + process_tags
        + [progress_tag(cycle_sec), cycle_time_tag(cycle_sec)]
    )

    if protocol == "mcprotocol":
        for t in tags:
            mc = MC_MAPPING_CAST.get(t["name"])
            if mc is None:
                raise KeyError(f"MC mapping missing '{t['name']}'")
            t["mc"] = mc

    elif protocol in ("modbus", "modbus-rtu", "modbus-rtu-tcp"):
        mapping = MB_MAPPING_WASH if eq_id == "WASH-01" else MB_MAPPING_CNC
        for t in tags:
            mb = mapping.get(t["name"])
            if mb is None:
                raise KeyError(f"Modbus mapping missing '{t['name']}' for {eq_id}")
            t["mb"] = mb

    return tags


# ---------------------------------------------------------------------------
# config 빌드
# ---------------------------------------------------------------------------

def build_config(
    equipment_id: str,
    protocol: str,
    base_port: int | None,
    process_fn,
    cycle_sec: int,
    serial_slot: int | None,
) -> dict:
    cfg = {
        "equipment_name": "${LINE_ID:-LINE-00}_" + equipment_id,
        "protocol": protocol,
        "sampling_ms": 1000,
        "tags": build_tags(process_fn(), protocol, cycle_sec, equipment_id),
    }

    if protocol == "modbus-rtu":
        cfg["serial_path"] = f"/dev/vserial/cnc0{serial_slot}.slave"
        cfg["baudrate"] = 9600
        cfg["parity"] = "N"
        cfg["stopbits"] = 1
        cfg["bytesize"] = 8
        cfg["slave_id"] = 1
        cfg["host"] = ""
        cfg["port"] = 0
    elif protocol == "modbus-rtu-tcp":
        cfg["host"] = "0.0.0.0"
        cfg["port"] = base_port
        cfg["slave_id"] = 1
    else:
        cfg["host"] = "0.0.0.0"
        cfg["port"] = base_port

    if protocol == "opcua":
        cfg["namespace"] = "${LINE_ID:-LINE-00}_" + equipment_id

    return cfg


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------

def main() -> None:
    written: list[Path] = []

    for ln in LINES:
        line_dir = OUT_ROOT / f"line{ln}"
        line_dir.mkdir(exist_ok=True)

        for eq_id, proto, base_port, fn, cycle_sec, serial_slot in EQUIPMENT_SPECS:
            if proto == "modbus-rtu":
                port = None
            elif proto == "modbus-rtu-tcp":
                port = base_port
            else:
                port = line_port(base_port, ln)

            cfg = build_config(eq_id, proto, port, fn, cycle_sec, serial_slot)
            p = line_dir / f"{eq_id}.json"
            p.write_text(json.dumps(cfg, indent=2, ensure_ascii=False), encoding="utf-8")
            written.append(p)

    for p in written:
        print(f"wrote {p.relative_to(OUT_ROOT.parent)}")
    print(f"total {len(written)} files")


if __name__ == "__main__":
    main()