"""Config loader (role 기반 스키마).

role 종류:
- power      : 공통 1개. bool. 외부 읽기/쓰기 가능.
- setpoint   : 목표값. 외부 읽기/쓰기. sensor 의 기준값으로 쓰임.
- sensor     : 외부 읽기 전용. source_sp 의 현재값 + stddev 노이즈로 생성.
               warn_lo/hi, err_lo/hi 임계값으로 상태머신 Warning/Error 전이.
- counter    : 외부 읽기 전용. base_value 부터 step 씩 증가.
- alarm      : 외부 읽기 전용. base_value 그대로.
- status     : 외부 읽기 전용. 상태머신 현재 노드 int (0=Idle/1=Running/2=Warning/3=Error/4=Complete).
- progress   : 외부 읽기 전용. 0.0~100.0 진행도 float.
               progress_speed(%/s) 로 증가 속도를 조절.
- cycle_time : 외부 읽기 전용. 직전 사이클 소요 초(s) float. COMPLETE 진입 시 갱신.
- event      : 외부 읽기/쓰기 bool. 외부에서 True 를 쓰면 state.tick() 에서 소비 후 자동 False 리셋.
               load_request / unload_request / reset_error 세 가지로 사용.

power == 0 (off) 일 때:
  sensor/counter/alarm 은 0/false 로 강제.
  상태머신은 IDLE 로 강제되고 progress 는 0 으로 리셋.
stddev, warn_lo/hi, err_lo/hi 는 외부(Modbus/OPC UA) 에 노출되지 않는 내부 파라미터.
"""
from __future__ import annotations

import json
import os
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, List, Optional


# ---------- role 상수 ----------

ROLE_WRITABLE = {"power", "setpoint", "event"}
ROLES = {
    "power", "setpoint", "sensor", "counter", "alarm",
    "status", "progress", "cycle_time", "event",
}
DTYPES = {"int", "float", "bool"}


# ---------- MC 디바이스 코드 ----------

MC_DEVICE_CODES = {
    "M": (0x90, True),   # internal relay  (bit)
    "X": (0x9C, True),   # input           (bit)
    "Y": (0x9D, True),   # output          (bit)
    "D": (0xA8, False),  # data register   (word)
    "R": (0xAF, False),  # file register   (word)
    "W": (0xB4, False),  # link register   (word)
}
MC_BIT_DEVICES  = {k for k, (_c, b) in MC_DEVICE_CODES.items() if b}
MC_WORD_DEVICES = {k for k, (_c, b) in MC_DEVICE_CODES.items() if not b}


# ---------- 매핑 dataclass ----------

@dataclass
class MBMapping:
    """Modbus 용 태그 매핑.

    kind:
      'coil'      -> bool, FC=1/5/15
      'hr_int'    -> int  (1 word, signed 16 bit)
      'hr_float'  -> float (2 word, big-endian)
    address: 절대 주소 (coil 번호 또는 HR 워드 번호)
    """
    kind: str
    address: int

    def __post_init__(self) -> None:
        if self.kind not in ("coil", "hr_int", "hr_float"):
            raise ValueError(f"invalid mb kind: {self.kind}")
        if not isinstance(self.address, int) or self.address < 0:
            raise ValueError(f"mb address must be non-negative int, got {self.address!r}")


@dataclass
class MCMapping:
    """MC Protocol 용 디바이스 매핑.

    device: 'D' (word reg), 'M' (bit), 'R' (file reg) 등
    address: PLC 내부 주소 (10진)
    bool  -> 1 bit
    int   -> 1 word (signed 16 bit)
    float -> 2 word (little-endian, IEEE-754)
    """
    device: str
    address: int

    def __post_init__(self) -> None:
        if self.device not in MC_DEVICE_CODES:
            raise ValueError(
                f"invalid mc device '{self.device}' "
                f"(supported: {sorted(MC_DEVICE_CODES)})"
            )
        if not isinstance(self.address, int) or self.address < 0:
            raise ValueError(f"mc address must be non-negative int, got {self.address!r}")


# ---------- TagConfig ----------

@dataclass
class TagConfig:
    name: str
    role: str
    data_type: str

    # --- 공통 ---
    base_value: Any = 0

    # --- 단위 ---
    unit: str = ""          # 표시 단위 (예: "RPM", "°C", "MPa"). 외부 미노출.
    
    # --- sensor ---
    stddev: float = 0.0          # 노이즈 표준편차 (외부 미노출)
    source_sp: Optional[str] = None  # 참조할 setpoint 태그 이름
    # 임계값 (외부 미노출). None = 해당 방향 판정 안 함.
    warn_lo: Optional[float] = None
    warn_hi: Optional[float] = None
    err_lo:  Optional[float] = None
    err_hi:  Optional[float] = None

    # --- counter ---
    step: int = 1

    # --- progress ---
    progress_speed: float = 10.0  # %/s. progress role 태그에서만 사용.

    # --- 프로토콜 매핑 ---
    mc: Optional[MCMapping] = None
    mb: Optional[MBMapping] = None

    def __post_init__(self) -> None:
        if self.role not in ROLES:
            raise ValueError(f"invalid role: {self.role} ({self.name})")
        if self.data_type not in DTYPES:
            raise ValueError(f"invalid data_type: {self.data_type} ({self.name})")

        # role 별 data_type 강제
        if self.role == "power" and self.data_type != "bool":
            raise ValueError(f"power role must be bool ({self.name})")
        if self.role == "counter" and self.data_type != "int":
            raise ValueError(f"counter must be int ({self.name})")
        if self.role == "status" and self.data_type != "int":
            raise ValueError(f"status must be int ({self.name})")
        if self.role == "progress" and self.data_type != "float":
            raise ValueError(f"progress must be float ({self.name})")
        if self.role == "cycle_time" and self.data_type != "float":
            raise ValueError(f"cycle_time must be float ({self.name})")
        if self.role == "event" and self.data_type != "bool":
            raise ValueError(f"event must be bool ({self.name})")

        # sensor: source_sp 또는 base_value 중 하나는 있어야 함
        if self.role == "sensor" and not self.source_sp and self.base_value in (None, ""):
            raise ValueError(
                f"sensor must define source_sp or base_value ({self.name})"
            )

        # 임계값 논리 검사 (lo < hi)
        if self.warn_lo is not None and self.warn_hi is not None:
            if self.warn_lo >= self.warn_hi:
                raise ValueError(
                    f"warn_lo must be < warn_hi for sensor '{self.name}' "
                    f"(got {self.warn_lo} >= {self.warn_hi})"
                )
        if self.err_lo is not None and self.err_hi is not None:
            if self.err_lo >= self.err_hi:
                raise ValueError(
                    f"err_lo must be < err_hi for sensor '{self.name}' "
                    f"(got {self.err_lo} >= {self.err_hi})"
                )
        # err 범위는 warn 범위보다 넓어야 함
        if self.err_lo is not None and self.warn_lo is not None:
            if self.err_lo >= self.warn_lo:
                raise ValueError(
                    f"err_lo must be < warn_lo for sensor '{self.name}' "
                    f"(err_lo={self.err_lo}, warn_lo={self.warn_lo})"
                )
        if self.err_hi is not None and self.warn_hi is not None:
            if self.err_hi <= self.warn_hi:
                raise ValueError(
                    f"err_hi must be > warn_hi for sensor '{self.name}' "
                    f"(err_hi={self.err_hi}, warn_hi={self.warn_hi})"
                )

        # mc / mb dict -> dataclass 변환
        if isinstance(self.mc, dict):
            self.mc = MCMapping(**self.mc)
        if isinstance(self.mb, dict):
            self.mb = MBMapping(**self.mb)

        # mb kind vs data_type 일치 검사
        if self.mb is not None:
            if self.data_type == "bool" and self.mb.kind != "coil":
                raise ValueError(
                    f"tag '{self.name}': bool requires mb.kind='coil', got {self.mb.kind}"
                )
            if self.data_type == "int" and self.mb.kind != "hr_int":
                raise ValueError(
                    f"tag '{self.name}': int requires mb.kind='hr_int', got {self.mb.kind}"
                )
            if self.data_type == "float" and self.mb.kind != "hr_float":
                raise ValueError(
                    f"tag '{self.name}': float requires mb.kind='hr_float', got {self.mb.kind}"
                )

        # mc device vs data_type 일치 검사
        if self.mc is not None:
            if self.data_type == "bool" and self.mc.device not in MC_BIT_DEVICES:
                raise ValueError(
                    f"tag '{self.name}': bool data_type requires bit device "
                    f"({sorted(MC_BIT_DEVICES)}), got {self.mc.device}"
                )
            if self.data_type in ("int", "float") and self.mc.device not in MC_WORD_DEVICES:
                raise ValueError(
                    f"tag '{self.name}': {self.data_type} data_type requires word device "
                    f"({sorted(MC_WORD_DEVICES)}), got {self.mc.device}"
                )

    @property
    def writable(self) -> bool:
        """외부 쓰기 허용 여부."""
        return self.role in ROLE_WRITABLE


# ---------- SimConfig ----------

@dataclass
class SimConfig:
    equipment_name: str
    protocol: str
    host: str = ""
    port: int = 0
    sampling_ms: int = 1000
    namespace: Optional[str] = None
    tags: List[TagConfig] = field(default_factory=list)
    # Modbus RTU 전용
    serial_path: Optional[str] = None
    baudrate: int = 9600
    parity: str = "N"
    stopbits: int = 1
    bytesize: int = 8
    slave_id: int = 1

    def __post_init__(self) -> None:
        if self.protocol not in (
            "modbus", "modbus-rtu", "modbus-rtu-tcp", "opcua", "mcprotocol"
        ):
            raise ValueError(f"invalid protocol: {self.protocol}")
        if self.protocol == "modbus-rtu" and not self.serial_path:
            raise ValueError("modbus-rtu requires 'serial_path'")
        if self.protocol == "modbus-rtu-tcp" and (not self.host or not self.port):
            raise ValueError("modbus-rtu-tcp requires 'host' and 'port'")
        if self.sampling_ms <= 0:
            raise ValueError("sampling_ms must be positive")

        # power 태그가 정확히 1개
        powers = [t for t in self.tags if t.role == "power"]
        if len(powers) != 1:
            raise ValueError(f"exactly one 'power' role tag is required, got {len(powers)}")

        # 싱글턴 role: 각 0 or 1개만 허용
        for singleton in ("status", "progress", "cycle_time"):
            found = [t for t in self.tags if t.role == singleton]
            if len(found) > 1:
                raise ValueError(
                    f"role '{singleton}' must appear at most once, got {len(found)}"
                )

        # sensor.source_sp 가 실제 setpoint 를 가리키는지
        names = {t.name: t for t in self.tags}
        for t in self.tags:
            if t.role == "sensor" and t.source_sp:
                sp = names.get(t.source_sp)
                if not sp or sp.role != "setpoint":
                    raise ValueError(
                        f"sensor '{t.name}' source_sp='{t.source_sp}' "
                        f"is not a valid setpoint tag"
                    )

        # modbus 계열: 모든 태그에 mb 매핑 필수 + 주소 중복 검사
        if self.protocol in ("modbus", "modbus-rtu", "modbus-rtu-tcp"):
            for t in self.tags:
                if t.mb is None:
                    raise ValueError(
                        f"{self.protocol} requires mb mapping for all tags, "
                        f"missing on '{t.name}'"
                    )
            seen_mb: dict[tuple[str, int], str] = {}
            for t in self.tags:
                key = (t.mb.kind, t.mb.address)
                if key in seen_mb:
                    raise ValueError(
                        f"mb address conflict: {t.name} and {seen_mb[key]} "
                        f"both map to {t.mb.kind}@{t.mb.address}"
                    )
                seen_mb[key] = t.name
            hr_occ: dict[int, str] = {}
            for t in self.tags:
                if t.mb.kind == "hr_int":
                    if t.mb.address in hr_occ:
                        raise ValueError(
                            f"HR conflict: {t.name}@{t.mb.address} vs {hr_occ[t.mb.address]}"
                        )
                    hr_occ[t.mb.address] = t.name
                elif t.mb.kind == "hr_float":
                    for off in (0, 1):
                        a = t.mb.address + off
                        if a in hr_occ:
                            raise ValueError(
                                f"HR conflict: {t.name}@{a} vs {hr_occ[a]}"
                            )
                        hr_occ[a] = t.name + ("(+1)" if off else "")

        # mcprotocol: 모든 태그에 mc 매핑 필수 + 주소 중복 검사
        if self.protocol == "mcprotocol":
            for t in self.tags:
                if t.mc is None:
                    raise ValueError(
                        f"mcprotocol requires mc mapping for all tags, "
                        f"missing on '{t.name}'"
                    )
            seen: dict[tuple[str, int], str] = {}
            for t in self.tags:
                key = (t.mc.device, t.mc.address)
                if key in seen:
                    raise ValueError(
                        f"mc address conflict: {t.name} and {seen[key]} "
                        f"both map to {t.mc.device}{t.mc.address}"
                    )
                seen[key] = t.name
                if t.data_type == "float":
                    key2 = (t.mc.device, t.mc.address + 1)
                    if key2 in seen:
                        raise ValueError(
                            f"mc address conflict (float occupies 2 words): "
                            f"{t.name}@{t.mc.device}{t.mc.address + 1} vs {seen[key2]}"
                        )
                    seen[key2] = t.name + "(+1)"


# ---------- 환경변수 전개 ----------

_ENV_RE = re.compile(r"\$\{([A-Z0-9_]+)(?::-([^}]*))?\}")


def _expand_env(value: Any) -> Any:
    """재귀적으로 문자열 안의 ${VAR} / ${VAR:-default} 처리."""
    if isinstance(value, str):
        def repl(m: re.Match) -> str:
            var, default = m.group(1), m.group(2)
            val = os.environ.get(var)
            if val is not None:
                return val
            if default is not None:
                return default
            raise KeyError(
                f"environment variable '{var}' is not set "
                f"(used in config: '{value}')"
            )
        return _ENV_RE.sub(repl, value)
    if isinstance(value, dict):
        return {k: _expand_env(v) for k, v in value.items()}
    if isinstance(value, list):
        return [_expand_env(v) for v in value]
    return value


def load_config(path: str | Path) -> SimConfig:
    p = Path(path)
    if not p.exists():
        raise FileNotFoundError(f"config not found: {p}")
    with p.open("r", encoding="utf-8") as f:
        raw = json.load(f)
    raw = _expand_env(raw)
    tags = [TagConfig(**t) for t in raw.pop("tags", [])]
    return SimConfig(tags=tags, **raw)
