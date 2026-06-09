"""TUI wrapper IPC 프로토콜 (JSON line over UDS).

소켓 경로:
    /tmp/sim-tui.sock   (컨테이너 내부 전용)

프레이밍:
    각 메시지 = 한 줄 JSON + '\\n'.
    line-buffered 라서 구현이 단순하고 디버그가 쉽다.

요청 / 응답:
    READ  ── 모든 태그 현재값과 메타정보를 한 번에 받기.
    WRITE ── 단일 태그에 값을 쓰기. set_external() 로 들어가므로 RO 자동 거절.
    PING  ── 연결 확인.

향후 확장:
    - SUBSCRIBE / EVENT: 서버 push (현재는 클라이언트 풀링으로 충분)
    - SNAPSHOT (binary) : msgpack 등. 현재 규모(태그 수십개)에선 불필요.
"""
from __future__ import annotations

import json
import socket
from dataclasses import dataclass, field
from typing import Any, Optional

# 컨테이너 안에서만 보이는 경로. compose 가 마운트하지 않아 호스트로 새 나가지 않음.
SOCKET_PATH = "/tmp/sim-tui.sock"

# 단일 메시지 최대 길이 (한 줄). 태그 수십개 × 메타 포함 페이로드 여유.
MAX_LINE = 64 * 1024


# ---------------------------------------------------------------------------
# 메시지 종류
# ---------------------------------------------------------------------------
OP_PING = "ping"
OP_READ = "read"
OP_WRITE = "write"
OP_CYCLE_TIME = "get_cycle_time"   # ← 추가

STATUS_OK = "ok"
STATUS_ERR = "err"


@dataclass
class TagInfo:
    name: str
    role: str
    data_type: str
    writable: bool
    source_sp: Optional[str]
    value: Any
    unit: str = field(default="")
    warn_lo: Optional[float] = field(default=None)   # ← 추가
    warn_hi: Optional[float] = field(default=None)   # ← 추가
    err_lo:  Optional[float] = field(default=None)   # ← 추가
    err_hi:  Optional[float] = field(default=None)   # ← 추가

    def to_json(self) -> dict[str, Any]:
        d = {
            "name": self.name, "role": self.role,
            "data_type": self.data_type, "writable": self.writable,
            "source_sp": self.source_sp, "value": self.value,
            "unit": self.unit,
        }
        for f in ("warn_lo", "warn_hi", "err_lo", "err_hi"):
            v = getattr(self, f)
            if v is not None:
                d[f] = v
        return d

    @classmethod
    def from_json(cls, d: dict[str, Any]) -> "TagInfo":
        return cls(
            name=d["name"], role=d["role"], data_type=d["data_type"],
            writable=bool(d["writable"]), source_sp=d.get("source_sp"),
            value=d.get("value"), unit=d.get("unit", ""),
            warn_lo=d.get("warn_lo"), warn_hi=d.get("warn_hi"),
            err_lo=d.get("err_lo"),  err_hi=d.get("err_hi"),
        )

# ---------------------------------------------------------------------------
# encode / decode
# ---------------------------------------------------------------------------
def encode(msg: dict[str, Any]) -> bytes:
    """dict -> JSON line bytes."""
    return (json.dumps(msg, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")


def decode_line(line: bytes) -> dict[str, Any]:
    return json.loads(line.decode("utf-8"))


# ---------------------------------------------------------------------------
# 소켓 헬퍼 (양쪽이 같은 프레이밍을 쓰도록)
# ---------------------------------------------------------------------------
def recv_line(sock: socket.socket, buf: bytearray) -> Optional[bytes]:
    """sock 에서 '\\n' 종료 한 줄을 읽는다. EOF 면 None.

    buf 는 호출자가 보관하는 상태 (반복 호출 시 잔여 데이터 누적).
    """
    while b"\n" not in buf:
        chunk = sock.recv(4096)
        if not chunk:
            return None
        buf.extend(chunk)
        if len(buf) > MAX_LINE:
            raise ValueError(f"line too long ({len(buf)} > {MAX_LINE})")
    nl = buf.index(b"\n")
    line = bytes(buf[:nl])
    del buf[: nl + 1]
    return line


def send_msg(sock: socket.socket, msg: dict[str, Any]) -> None:
    sock.sendall(encode(msg))


# ---------------------------------------------------------------------------
# 요청 / 응답 빌더 (양쪽이 같은 schema 를 쓰도록)
# ---------------------------------------------------------------------------
def req_ping() -> dict[str, Any]:
    return {"op": OP_PING}


def req_read() -> dict[str, Any]:
    return {"op": OP_READ}


def req_write(name: str, value: Any) -> dict[str, Any]:
    return {"op": OP_WRITE, "name": name, "value": value}


def resp_ok(payload: Optional[dict[str, Any]] = None) -> dict[str, Any]:
    out: dict[str, Any] = {"status": STATUS_OK}
    if payload:
        out.update(payload)
    return out


def resp_err(reason: str) -> dict[str, Any]:
    return {"status": STATUS_ERR, "reason": reason}

# ---------------------------------------------------------------------------
# CycleTime 요청 / 응답
# ---------------------------------------------------------------------------
@dataclass
class CycleTimeInfo:
    last:   float = 0.0
    mean:   float = 0.0
    stddev: float = 0.0
    count:  int   = 0


def req_cycle_time() -> dict[str, Any]:
    return {"op": OP_CYCLE_TIME}


def parse_cycle_time(resp: dict[str, Any]) -> CycleTimeInfo:
    return CycleTimeInfo(
        last=float(resp.get("last", 0.0)),
        mean=float(resp.get("mean", 0.0)),
        stddev=float(resp.get("stddev", 0.0)),
        count=int(resp.get("count", 0)),
    )