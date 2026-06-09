"""설비 상태머신 + 태그 현재값 보관소.

상태 전이:
    IDLE  --load_request=True-->  RUNNING
    RUNNING / WARNING  --progress 100%-->  COMPLETE
    RUNNING  --sensor err-->  ERROR
    RUNNING  --sensor warn-->  WARNING
    WARNING  --sensor 정상-->  RUNNING  (자동 복귀)
    WARNING  --sensor err-->  ERROR
    ERROR / WARNING  --reset_error=True-->  RUNNING 또는 IDLE(조건 불충족 시)
    COMPLETE  --unload_request=True-->  IDLE

추가:
- inject_warning / inject_error event 소비
- status 에 따라 alarm_active / alarm_code 갱신
- part_count 는 COMPLETE 진입 시 1 증가
- heartbeat 는 tick 마다 증가
"""
from __future__ import annotations

import math
import random
import threading
import time
from collections import deque
from typing import Any, Dict, Optional

from .config import SimConfig, TagConfig
from .log import get_logger

log = get_logger("state")


# ---------- 상태 상수 ----------


class ES:
    """EquipmentStatus 정수 상수."""
    IDLE = 0
    RUNNING = 1
    WARNING = 2
    ERROR = 3
    COMPLETE = 4

    _NAMES = {
        0: "IDLE",
        1: "RUNNING",
        2: "WARNING",
        3: "ERROR",
        4: "COMPLETE",
    }

    @classmethod
    def name(cls, val: int) -> str:
        return cls._NAMES.get(val, f"UNKNOWN({val})")


# ---------- 알람 코드 상수 ----------


class ALARM:
    NONE = 0
    WARNING = 100
    ERROR = 200
    INJECT_WARNING = 110
    INJECT_ERROR = 210


# ---------- EquipmentState ----------


class EquipmentState:
    def __init__(self, cfg: SimConfig) -> None:
        self.cfg = cfg
        self._lock = threading.Lock()

        self._values: Dict[str, Any] = {}
        for t in cfg.tags:
            self._values[t.name] = self._initial(t)

        self._power_name: str = next(t.name for t in cfg.tags if t.role == "power")
        self._status_name: Optional[str] = self._find_role("status")
        self._progress_name: Optional[str] = self._find_role("progress")
        self._cycle_time_name: Optional[str] = self._find_role("cycle_time")

        self._by_name: Dict[str, TagConfig] = {t.name: t for t in cfg.tags}

        self._cycle_start: float = 0.0
        self._cycle_elapsed_before_error: float = 0.0
        self._cycle_paused_at: float = 0.0

        self._forced_warning: bool = False
        self._forced_error: bool = False

        CYCLE_HISTORY_LEN = 20
        self._cycle_last: float = 0.0
        self._cycle_history: deque[float] = deque(maxlen=CYCLE_HISTORY_LEN)

        if self._cycle_time_name:
            self._values[self._cycle_time_name] = self._initial_cycle_time_estimate()

        self._update_alarm_fields()

    # ------------------------------------------------------------------ helpers

    def _find_role(self, role: str) -> Optional[str]:
        for t in self.cfg.tags:
            if t.role == role:
                return t.name
        return None

    def _find_name(self, name: str) -> Optional[str]:
        return name if name in self._by_name else None

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
                    ES.name(old),
                    ES.name(new),
                )
            self._values[self._status_name] = new

    def _get_progress(self) -> float:
        if self._progress_name:
            return float(self._values.get(self._progress_name, 0.0))
        return 0.0

    def _set_progress(self, val: float) -> None:
        if self._progress_name:
            self._values[self._progress_name] = max(0.0, min(100.0, val))

    def _set_cycle_time(self, val: float) -> None:
        if self._cycle_time_name:
            self._values[self._cycle_time_name] = max(0.0, round(float(val), 3))

    def _get_event(self, name: str) -> bool:
        return bool(self._values.get(name, False))

    def _consume_event(self, name: str) -> bool:
        val = bool(self._values.get(name, False))
        if val:
            self._values[name] = False
        return val

    def _get_progress_speed(self) -> float:
        if self._progress_name:
            t = self._by_name.get(self._progress_name)
            if t and t.progress_speed is not None:
                return float(t.progress_speed)
        return 10.0

    def _initial_cycle_time_estimate(self) -> float:
        if self._cycle_last > 0:
            return self._cycle_last

        if self._cycle_history:
            return round(sum(self._cycle_history) / len(self._cycle_history), 3)

        if self._cycle_time_name:
            t = self._by_name.get(self._cycle_time_name)
            if t and t.base_value is not None:
                return round(float(t.base_value), 3)

        speed = self._get_progress_speed()
        if speed > 0:
            return round(100.0 / speed, 3)

        return 0.0

    def _current_elapsed(self) -> float:
        status = self._get_status()
        if status == ES.ERROR:
            return self._cycle_elapsed_before_error
        if self._cycle_start <= 0.0:
            return 0.0
        return self._cycle_elapsed_before_error + (time.monotonic() - self._cycle_start)

    def _update_cycle_time_estimate(self) -> None:
        if not self._cycle_time_name:
            return

        progress = self._get_progress()
        elapsed = self._current_elapsed()

        if progress > 0.0 and elapsed > 0.0:
            est_total = elapsed * (100.0 / progress)
            self._set_cycle_time(est_total)
            return

        self._set_cycle_time(self._initial_cycle_time_estimate())

    def _set_if_exists(self, name: str, value: Any) -> None:
        if name in self._by_name:
            self._values[name] = value

    def _inc_if_exists(self, name: str, delta: int = 1) -> None:
        if name in self._by_name:
            self._values[name] = int(self._values.get(name, 0)) + int(delta)

    def _update_alarm_fields(self) -> None:
        status = self._get_status()

        alarm_active = False
        alarm_code = ALARM.NONE

        if self._forced_error:
            alarm_active = True
            alarm_code = ALARM.INJECT_ERROR
        elif self._forced_warning:
            alarm_active = True
            alarm_code = ALARM.INJECT_WARNING
        elif status == ES.ERROR:
            alarm_active = True
            alarm_code = ALARM.ERROR
        elif status == ES.WARNING:
            alarm_active = True
            alarm_code = ALARM.WARNING

        self._set_if_exists("alarm_active", alarm_active)
        self._set_if_exists("alarm_code", alarm_code)

    def _enter_running(self, reset_progress: bool) -> None:
        if reset_progress:
            self._set_progress(0.0)
            self._cycle_elapsed_before_error = 0.0

        self._cycle_start = time.monotonic()
        self._cycle_paused_at = 0.0
        self._set_status(ES.RUNNING)
        self._update_cycle_time_estimate()
        self._update_alarm_fields()

    def _enter_error(self) -> None:
        self._cycle_elapsed_before_error = self._current_elapsed()
        self._cycle_paused_at = time.monotonic()
        self._set_status(ES.ERROR)
        self._update_alarm_fields()

    def _resume_from_error(self) -> None:
        self._cycle_start = time.monotonic()
        self._cycle_paused_at = 0.0
        self._set_status(ES.RUNNING)
        self._update_cycle_time_estimate()
        self._update_alarm_fields()

    def _complete_cycle(self) -> None:
        elapsed = self._current_elapsed()
        self._cycle_last = round(elapsed, 3)
        self._cycle_history.append(elapsed)
        self._set_cycle_time(self._cycle_last)
        self._inc_if_exists("part_count", 1)
        log.info("[%s] cycle_time = %.3f s", self.cfg.equipment_name, elapsed)
        self._set_status(ES.COMPLETE)
        self._forced_warning = False
        self._forced_error = False
        self._update_alarm_fields()

    def _reset_cycle_runtime(self) -> None:
        self._cycle_start = 0.0
        self._cycle_paused_at = 0.0
        self._cycle_elapsed_before_error = 0.0

    # ------------------------------------------------------------------ 센서 건강도

    def _sensor_health(self) -> int:
        worst = ES.RUNNING
        for t in self.cfg.tags:
            if t.role != "sensor":
                continue

            has_threshold = any(
                v is not None for v in (t.warn_lo, t.warn_hi, t.err_lo, t.err_hi)
            )
            if not has_threshold:
                continue

            val = float(self._values.get(t.name, 0.0))

            if t.err_lo is not None and val < t.err_lo:
                return ES.ERROR
            if t.err_hi is not None and val > t.err_hi:
                return ES.ERROR

            if t.warn_lo is not None and val < t.warn_lo:
                worst = ES.WARNING
            if t.warn_hi is not None and val > t.warn_hi:
                worst = ES.WARNING

        return worst

    # ------------------------------------------------------------------ 외부 쓰기

    def set_external(self, name: str, value: Any) -> bool:
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
        return self._by_name.get(name)

    # ------------------------------------------------------------------ tick

    def tick(self) -> None:
        dt = self.cfg.sampling_ms / 1000.0

        with self._lock:
            power_on = bool(self._values[self._power_name])

            if not power_on:
                for t in self.cfg.tags:
                    if t.role in ("power", "setpoint"):
                        continue
                    if t.role in (
                        "status",
                        "progress",
                        "cycle_time",
                        "event",
                        "counter",
                        "alarm",
                        "sensor",
                    ):
                        self._values[t.name] = False if t.data_type == "bool" else 0
                self._forced_warning = False
                self._forced_error = False
                self._reset_cycle_runtime()
                self._set_status(ES.IDLE)
                self._set_progress(0.0)
                self._set_cycle_time(self._initial_cycle_time_estimate())
                self._update_alarm_fields()
                return

            status = self._get_status()

            for t in self.cfg.tags:
                if t.role == "sensor":
                    if status in (ES.IDLE, ES.COMPLETE, ES.ERROR):
                        self._values[t.name] = False if t.data_type == "bool" else 0
                        continue

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
                    if t.name == "heartbeat":
                        self._values[t.name] = int(self._values[t.name]) + int(t.step)

            status = self._get_status()
            progress = self._get_progress()
            speed = self._get_progress_speed()

            load_req = self._consume_event("load_request")
            unload_req = self._consume_event("unload_request")
            reset_err = self._consume_event("reset_error")
            inject_warning = self._consume_event("inject_warning")
            inject_error = self._consume_event("inject_error")

            if inject_warning:
                self._forced_warning = True
                self._forced_error = False

            if inject_error:
                self._forced_error = True
                self._forced_warning = False

            if reset_err and status == ES.WARNING:
                self._forced_warning = False
                self._forced_error = False
                self._set_status(ES.RUNNING)
                self._update_alarm_fields()
                status = self._get_status()

            health = self._sensor_health()
            if self._forced_error:
                health = ES.ERROR
            elif self._forced_warning and health != ES.ERROR:
                health = ES.WARNING

            if status == ES.IDLE:
                self._set_cycle_time(self._initial_cycle_time_estimate())
                if load_req:
                    self._enter_running(reset_progress=True)

            elif status == ES.RUNNING:
                if health == ES.ERROR:
                    self._enter_error()
                else:
                    new_progress = progress + speed * dt
                    self._set_progress(new_progress)
                    self._update_cycle_time_estimate()

                    if self._get_progress() >= 100.0:
                        self._complete_cycle()
                    elif health == ES.WARNING:
                        self._set_status(ES.WARNING)
                        self._update_alarm_fields()
                    else:
                        self._update_alarm_fields()

            elif status == ES.WARNING:
                if health == ES.ERROR:
                    self._enter_error()
                else:
                    new_progress = progress + (speed * 0.6) * dt
                    self._set_progress(new_progress)
                    self._update_cycle_time_estimate()

                    if self._get_progress() >= 100.0:
                        self._complete_cycle()
                    elif health == ES.RUNNING and not self._forced_warning:
                        self._set_status(ES.RUNNING)
                        self._update_alarm_fields()
                    else:
                        self._update_alarm_fields()

            elif status == ES.ERROR:
                self._update_cycle_time_estimate()
                self._update_alarm_fields()

                if reset_err:
                    self._forced_warning = False
                    self._forced_error = False
                    health = self._sensor_health()

                    if health == ES.ERROR:
                        self._set_status(ES.IDLE)
                        self._set_progress(0.0)
                        self._reset_cycle_runtime()
                        self._set_cycle_time(self._initial_cycle_time_estimate())
                        self._update_alarm_fields()
                    else:
                        self._resume_from_error()

            elif status == ES.COMPLETE:
                self._update_alarm_fields()
                if unload_req:
                    self._set_progress(0.0)
                    self._reset_cycle_runtime()
                    self._set_status(ES.IDLE)
                    self._set_cycle_time(self._initial_cycle_time_estimate())
                    self._update_alarm_fields()

    # ------------------------------------------------------------------ 읽기

    def cycle_time_stats(self) -> dict:
        h = list(self._cycle_history)
        n = len(h)
        mean = sum(h) / n if n > 0 else 0.0
        stddev = (
            math.sqrt(sum((x - mean) ** 2 for x in h) / n)
            if n >= 2 else 0.0
        )
        return {
            "last": self._cycle_last,
            "mean": round(mean, 3),
            "stddev": round(stddev, 3),
            "count": n,
        }

    def read_all(self) -> Dict[str, Any]:
        with self._lock:
            return {t.name: self._values[t.name] for t in self.cfg.tags}

    def read(self, name: str) -> Any:
        with self._lock:
            return self._values.get(name)