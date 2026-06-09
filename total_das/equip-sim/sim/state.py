"""설비 상태머신 + 태그 현재값 보관소.

상태 전이:
    IDLE  ──load_request=True──►  RUNNING
    RUNNING / WARNING  ──progress 100%%──►  COMPLETE
    RUNNING  ──sensor err──►  ERROR
    RUNNING  ──sensor warn──►  WARNING
    WARNING  ──sensor 정상──►  RUNNING  (자동 복귀)
    WARNING  ──sensor err──►  ERROR
    ERROR  ──reset_error=True──►  IDLE
    COMPLETE  ──unload_request=True──►  IDLE  (cycle_time 갱신)
    power=OFF 시 모든 상태 IDLE 강제, progress=0

외부 쓰기 진입점:
    set_external(name, value)  →  writable 태그(power/setpoint/event) 만 반영.
    event 태그는 tick() 안에서 소비 후 자동 False 리셋.

읽기:
    read_all()  →  현재 외부 노출 값 dict (내부 파라미터 stddev 등 제외).
    read(name)  →  단일 태그.
"""
from __future__ import annotations

import random
import threading
import time
from typing import Any, Dict, Optional

from .config import SimConfig, TagConfig
from .log import get_logger

log = get_logger("state")


# ---------- 상태 상수 ----------

class ES:
    """EquipmentStatus 정수 상수."""
    IDLE     = 0
    RUNNING  = 1
    WARNING  = 2
    ERROR    = 3
    COMPLETE = 4

    _NAMES = {0: "IDLE", 1: "RUNNING", 2: "WARNING", 3: "ERROR", 4: "COMPLETE"}

    @classmethod
    def name(cls, val: int) -> str:
        return cls._NAMES.get(val, f"UNKNOWN({val})")


# ---------- EquipmentState ----------

class EquipmentState:
    def __init__(self, cfg: SimConfig) -> None:
        self.cfg = cfg
        self._lock = threading.Lock()

        # 모든 태그 현재값 초기화
        self._values: Dict[str, Any] = {}
        for t in cfg.tags:
            self._values[t.name] = self._initial(t)

        # 자주 쓰는 태그 이름 캐시
        self._power_name: str = next(
            t.name for t in cfg.tags if t.role == "power"
        )
        self._status_name:     Optional[str] = self._find_role("status")
        self._progress_name:   Optional[str] = self._find_role("progress")
        self._cycle_time_name: Optional[str] = self._find_role("cycle_time")

        # 이름 -> TagConfig
        self._by_name: Dict[str, TagConfig] = {t.name: t for t in cfg.tags}

        # 상태머신 내부 변수
        self._cycle_start: float = 0.0   # RUNNING 진입 시각 (time.monotonic)

    # ------------------------------------------------------------------ helpers

    def _find_role(self, role: str) -> Optional[str]:
        for t in self.cfg.tags:
            if t.role == role:
                return t.name
        return None

    @staticmethod
    def _initial(t: TagConfig) -> Any:
        if t.data_type == "bool":
            return bool(t.base_value)
        if t.data_type == "int":
            return int(t.base_value)
        if t.data_type == "float":
            return float(t.base_value)
        return t.base_value

    def _get_status(self) -> int:
        if self._status_name:
            return int(self._values.get(self._status_name, ES.IDLE))
        return ES.IDLE

    def _set_status(self, new: int) -> None:
        if self._status_name:
            old = self._get_status()
            if old != new:
                log.info(
                    "[%s] status %s -> %s",
                    self.cfg.equipment_name,
                    ES.name(old), ES.name(new),
                )
            self._values[self._status_name] = new

    def _get_progress(self) -> float:
        if self._progress_name:
            return float(self._values.get(self._progress_name, 0.0))
        return 0.0

    def _set_progress(self, val: float) -> None:
        if self._progress_name:
            self._values[self._progress_name] = max(0.0, min(100.0, val))

    def _get_event(self, name: str) -> bool:
        return bool(self._values.get(name, False))

    def _consume_event(self, name: str) -> bool:
        """이벤트 태그를 읽고 즉시 False 로 리셋. 존재하지 않으면 False."""
        val = bool(self._values.get(name, False))
        if val:
            self._values[name] = False
        return val

    def _get_progress_speed(self) -> float:
        """progress role 태그의 progress_speed 반환. 없으면 10.0%%/s."""
        if self._progress_name:
            t = self._by_name.get(self._progress_name)
            if t:
                return float(t.progress_speed)
        return 10.0

    # ------------------------------------------------------------------ 센서 건강도

    def _sensor_health(self) -> int:
        """임계값을 가진 센서들의 현재값을 기준으로 최악 상태 반환.

        Returns ES.RUNNING / ES.WARNING / ES.ERROR
        """
        worst = ES.RUNNING
        for t in self.cfg.tags:
            if t.role != "sensor":
                continue
            has_threshold = any(
                v is not None
                for v in (t.warn_lo, t.warn_hi, t.err_lo, t.err_hi)
            )
            if not has_threshold:
                continue

            val = float(self._values.get(t.name, 0.0))

            # Error 판정 (우선순위 높음 — 즉시 반환)
            if t.err_lo is not None and val < t.err_lo:
                return ES.ERROR
            if t.err_hi is not None and val > t.err_hi:
                return ES.ERROR

            # Warning 판정
            if t.warn_lo is not None and val < t.warn_lo:
                worst = ES.WARNING
            if t.warn_hi is not None and val > t.warn_hi:
                worst = ES.WARNING

        return worst

    # ------------------------------------------------------------------ 외부 쓰기

    def set_external(self, name: str, value: Any) -> bool:
        """외부 write 요청. writable 태그(power/setpoint/event)만 반영."""
        t = self._by_name.get(name)
        if not t:
            log.warning("write reject: unknown tag '%s'", name)
            return False
        if not t.writable:
            log.warning(
                "write reject: '%s' is read-only (role=%s)", name, t.role
            )
            return False
        coerced = self._coerce(t, value)
        with self._lock:
            old = self._values[name]
            self._values[name] = coerced
        log.info("write '%s' %s -> %s", name, old, coerced)
        return True

    @staticmethod
    def _coerce(t: TagConfig, value: Any) -> Any:
        if t.data_type == "bool":
            if isinstance(value, (int, float)):
                return bool(value)
            if isinstance(value, str):
                return value.strip().lower() in ("1", "true", "on", "y")
            return bool(value)
        if t.data_type == "int":
            return int(round(float(value)))
        if t.data_type == "float":
            return float(value)
        return value

    # ------------------------------------------------------------------ 공개 접근자

    def get_tag(self, name: str) -> Optional[TagConfig]:
        """TagConfig 공개 접근자 (내부 _by_name 직접 접근 대신 사용)."""
        return self._by_name.get(name)

    # ------------------------------------------------------------------ tick

    def tick(self) -> None:
        """샘플링 주기마다 1회 호출. sensor/counter 재계산 + 상태머신 전이."""
        dt = self.cfg.sampling_ms / 1000.0

        with self._lock:
            power_on = bool(self._values[self._power_name])

            # ── power OFF: 전부 0, 상태 IDLE 강제 ──────────────────────
            if not power_on:
                for t in self.cfg.tags:
                    if t.role in ("power", "setpoint"):
                        continue
                    if t.role in ("status", "progress", "cycle_time", "event",
                                  "counter", "alarm", "sensor"):
                        self._values[t.name] = (
                            False if t.data_type == "bool" else 0
                        )
                self._set_status(ES.IDLE)
                self._set_progress(0.0)
                return

            # ── 1. sensor / counter 값 재계산 ──────────────────────────
            for t in self.cfg.tags:
                if t.role == "sensor":
                    if t.source_sp:
                        center = float(self._values.get(t.source_sp, 0.0))
                    else:
                        center = float(t.base_value or 0.0)
                    noise = random.gauss(0.0, float(t.stddev or 0.0))
                    val = center + noise
                    if t.data_type == "int":
                        val = int(round(val))
                    elif t.data_type == "bool":
                        val = bool(center)
                    self._values[t.name] = val

                elif t.role == "counter":
                    self._values[t.name] = int(self._values[t.name]) + int(t.step)

                # alarm 은 base_value 고정 (변경 없음)

            # ── 2. 상태머신 전이 ────────────────────────────────────────
            status   = self._get_status()
            progress = self._get_progress()
            speed    = self._get_progress_speed()

            # 이벤트 소비 (lock 안에서 읽고 즉시 리셋)
            load_req   = self._consume_event("load_request")
            unload_req = self._consume_event("unload_request")
            reset_err  = self._consume_event("reset_error")

            if status == ES.IDLE:
                if load_req:
                    self._set_progress(0.0)
                    self._cycle_start = time.monotonic()
                    self._set_status(ES.RUNNING)

            elif status == ES.RUNNING:
                health = self._sensor_health()
                if health == ES.ERROR:
                    self._set_status(ES.ERROR)
                elif health == ES.WARNING:
                    self._set_status(ES.WARNING)
                else:
                    new_progress = progress + speed * dt
                    self._set_progress(new_progress)
                    if self._get_progress() >= 100.0:
                        self._set_status(ES.COMPLETE)

            elif status == ES.WARNING:
                health = self._sensor_health()
                if health == ES.ERROR:
                    self._set_status(ES.ERROR)
                else:
                    # warning 이어도 진행은 계속 (정상 범위 복귀 시 RUNNING 복귀)
                    new_progress = progress + speed * dt
                    self._set_progress(new_progress)
                    if health == ES.RUNNING:
                        self._set_status(ES.RUNNING)
                    if self._get_progress() >= 100.0:
                        self._set_status(ES.COMPLETE)

            elif status == ES.ERROR:
                if reset_err:
                    self._set_progress(0.0)
                    self._set_status(ES.IDLE)

            elif status == ES.COMPLETE:
                if unload_req:
                    elapsed = time.monotonic() - self._cycle_start
                    if self._cycle_time_name:
                        self._values[self._cycle_time_name] = round(elapsed, 3)
                        log.info(
                            "[%s] cycle_time = %.3f s",
                            self.cfg.equipment_name, elapsed,
                        )
                    self._set_progress(0.0)
                    self._set_status(ES.IDLE)

    # ------------------------------------------------------------------ 읽기

    def read_all(self) -> Dict[str, Any]:
        """외부 노출 값 dict. stddev, warn_lo/hi, err_lo/hi 등 내부 파라미터는 포함 안 함."""
        with self._lock:
            return {t.name: self._values[t.name] for t in self.cfg.tags}

    def read(self, name: str) -> Any:
        with self._lock:
            return self._values.get(name)
