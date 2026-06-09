"""TUI wrapper application — Textual 기반 시뮬 제어반.

섹션:
    [P] Power / Setpoint  — 전원 토글, SP 수치 편집
    [A] Alarm / Counter   — alarm 활성 표시, counter 값 조회, fault_inject 토글
    [S] Status            — status/progress/cycle_time 등 RO 태그 조회
                            · sensor 태그: threshold 기반 3단계 색상 (red/yellow/green)
                            · cycle_time 태그: last/mean/σ/n 통계 표시

단축키:
    ↑↓←→ / hjkl / wasd   포커스 이동
    Enter                 선택 / 편집 확정
    Esc                   편집 취소
    Tab / Shift-Tab       섹션 전환 (P → A → S → P)
    R                     reset_error 이벤트 전송
    L                     load_request 이벤트 전송
    U                     unload_request 이벤트 전송
    Q                     종료
"""
from __future__ import annotations

import asyncio
import os
import time
from dataclasses import dataclass, field
from typing import Any, Optional

from textual.app import App, ComposeResult
from textual.binding import Binding
from textual.containers import Container, Grid, Horizontal, Vertical
from textual.reactive import reactive
from textual.widgets import Static

from .client import TuiClient
from .protocol import (
    SOCKET_PATH,
    TagInfo,
    CycleTimeInfo,        # ← 추가
    req_cycle_time,       # ← 추가
    parse_cycle_time,     # ← 추가
)


# ─── 색상 토큰 ────────────────────────────────────────────────────────────────
DARK_TEXT       = "#010101"
LIGHT_LABEL_BG  = "#f3f3f3"
CARD_BG         = "#bdbdbd"
SCREEN_BG       = "#8a8a8a"
BORDER          = "#efefef"
ALARM_CARD_BG   = "#e8d5d5"
COUNTER_CARD_BG = "#d5e0e8"
STATUS_CARD_BG  = "#d5e8d8"
FAULT_CARD_BG   = "#ede5d5"


# ─── threshold 색상 판정 ──────────────────────────────────────────────────────
def _threshold_style(value: Any, ti: TagInfo) -> str:
    """err > warn > 정상 우선순위로 Rich 색상 태그 문자열을 반환.

    TagInfo에 threshold 필드가 없거나 값이 숫자로 변환 불가하면
    기본 "white" 반환.
    """
    try:
        fv = float(value)
    except (TypeError, ValueError):
        return "white"
    # ERROR 범위 확인
    if (ti.err_lo is not None and fv < ti.err_lo) or \
       (ti.err_hi is not None and fv > ti.err_hi):
        return "bold red"
    # WARNING 범위 확인
    if (ti.warn_lo is not None and fv < ti.warn_lo) or \
       (ti.warn_hi is not None and fv > ti.warn_hi):
        return "yellow"
    # 정상
    return "green"


# ─── 레이아웃 상수 ────────────────────────────────────────────────────────────
BASE_BOX_W          = 25
MIN_BOX_W           = 14
TARGET_BOX_W        = 25
GRID_GUTTER         = 1
PAIR_GAP            = 1
CARD_INNER_PADDING  = 2
CARD_BORDER         = 2
CARD_CONTENT_EXTRA  = PAIR_GAP + CARD_INNER_PADDING + CARD_BORDER

# ─── 섹션 상수 ───────────────────────────────────────────────────────────────
SEC_POWER   = "P"   # Power + Setpoint
SEC_ALARM   = "A"   # Alarm + Counter + fault_inject
SEC_STATUS  = "S"   # Status / progress / cycle_time / 기타 RO

SECTION_ORDER = [SEC_POWER, SEC_ALARM, SEC_STATUS]
SECTION_LABEL = {SEC_POWER: "[P]ower/SP", SEC_ALARM: "[A]larm/Ctr", SEC_STATUS: "[S]tatus"}

# ─── 가이드 바 텍스트 ─────────────────────────────────────────────────────────
GUIDE_NORMAL = (
    "[bold red]⇦⇧⇨⇩[/] [black]이동[/]  "
    "[bold red]Enter[/] 선택  "
    "[bold red]Tab[/] 섹션전환  "
    "[bold red]R[/]reset [bold red]L[/]load [bold red]U[/]unload  "
    "[bold red]Q[/] 종료"
)
GUIDE_EDIT = (
    "[bold red]⇦⇧⇨⇩[/] [black]이동[/]  "
    "[bold red]Enter[/] 확인  "
    "[bold red]Esc[/] 취소"
)


# ─── 데이터 클래스 ────────────────────────────────────────────────────────────
@dataclass
class FocusItem:
    kind: str   # power / sp / fault_inject
    name: str
    section: str  # SEC_POWER / SEC_ALARM / SEC_STATUS


@dataclass
class UiState:
    tags: list[TagInfo]             = field(default_factory=list)
    by_name: dict[str, TagInfo]     = field(default_factory=dict)
    focus_items: list[FocusItem]    = field(default_factory=list)
    focus_idx: int                  = 0
    section: str                    = SEC_POWER
    editing: bool                   = False
    edit_buf: str                   = ""
    notice: str                     = ""
    notice_until: float             = 0.0
    connected: bool                 = False
    last_error: str                 = ""
    socket_path: str                = SOCKET_PATH
    cycle_info: CycleTimeInfo       = field(default_factory=CycleTimeInfo)  # ← 추가

    def current(self) -> Optional[FocusItem]:
        sec_items = [i for i in self.focus_items if i.section == self.section]
        if not sec_items:
            return None
        cur = self.focus_items[self.focus_idx] if self.focus_items else None
        if cur and cur.section == self.section:
            return cur
        return sec_items[0]

    def _section_items(self) -> list[FocusItem]:
        return [i for i in self.focus_items if i.section == self.section]

    def set_notice(self, msg: str, seconds: float = 2.5) -> None:
        self.notice = msg
        self.notice_until = time.monotonic() + seconds

    def clear_expired_notice(self) -> None:
        if self.notice and time.monotonic() >= self.notice_until:
            self.notice = ""
            self.notice_until = 0.0

    def refresh_tags(self, tags: list[TagInfo]) -> None:
        prev = self.current()
        prev_key = (prev.kind, prev.name) if prev else None
        self.tags = tags
        self.by_name = {t.name: t for t in tags}
        self._build_focus_items()
        if prev_key:
            for i, item in enumerate(self.focus_items):
                if (item.kind, item.name) == prev_key:
                    self.focus_idx = i
                    break
        if self.focus_items and self.focus_idx >= len(self.focus_items):
            self.focus_idx = 0

    def _build_focus_items(self) -> None:
        items: list[FocusItem] = []
        # --- SEC_POWER ---
        power = next((t for t in self.tags if t.role == "power"), None)
        if power:
            items.append(FocusItem(kind="power", name=power.name, section=SEC_POWER))
        for t in self.tags:
            if t.role == "setpoint":
                items.append(FocusItem(kind="sp", name=t.name, section=SEC_POWER))
        # --- SEC_ALARM ---
        for t in self.tags:
            if t.role == "fault_inject" and t.writable:
                items.append(FocusItem(kind="fault_inject", name=t.name, section=SEC_ALARM))
        self.focus_items = items

    def prev_in_section(self) -> None:
        sec = [i for i, fi in enumerate(self.focus_items) if fi.section == self.section]
        if not sec:
            return
        cur_pos = self.focus_idx
        if cur_pos in sec:
            idx_in_sec = sec.index(cur_pos)
            self.focus_idx = sec[(idx_in_sec - 1) % len(sec)]
        else:
            self.focus_idx = sec[-1]

    def next_in_section(self) -> None:
        sec = [i for i, fi in enumerate(self.focus_items) if fi.section == self.section]
        if not sec:
            return
        cur_pos = self.focus_idx
        if cur_pos in sec:
            idx_in_sec = sec.index(cur_pos)
            self.focus_idx = sec[(idx_in_sec + 1) % len(sec)]
        else:
            self.focus_idx = sec[0]

    def next_section(self) -> None:
        idx = SECTION_ORDER.index(self.section)
        self.section = SECTION_ORDER[(idx + 1) % len(SECTION_ORDER)]
        sec = [i for i, fi in enumerate(self.focus_items) if fi.section == self.section]
        if sec:
            self.focus_idx = sec[0]

    def prev_section(self) -> None:
        idx = SECTION_ORDER.index(self.section)
        self.section = SECTION_ORDER[(idx - 1) % len(SECTION_ORDER)]
        sec = [i for i, fi in enumerate(self.focus_items) if fi.section == self.section]
        if sec:
            self.focus_idx = sec[0]


# ─── 값 포매터 ────────────────────────────────────────────────────────────────
def _format_value(v: Any, data_type: str, unit: str = "") -> str:
    if v is None:
        return "----"
    if data_type == "bool" or isinstance(v, bool):
        return "ON" if v else "OFF"
    if data_type == "float":
        try:
            s = f"{float(v):.2f}"
        except Exception:
            s = str(v)
    elif data_type == "int":
        try:
            s = f"{int(v)}"
        except Exception:
            s = str(v)
    else:
        s = str(v)
    return f"{s} {unit}".rstrip() if unit else s


# ─── 상태 레이블 ──────────────────────────────────────────────────────────────
_STATUS_NAMES = {0: "IDLE", 1: "RUNNING", 2: "WARNING", 3: "ERROR", 4: "COMPLETE"}
_STATUS_COLORS = {
    0: "bold white on #555555",
    1: "bold white on green",
    2: "bold black on yellow",
    3: "bold white on red",
    4: "bold white on blue",
}

def _status_markup(val: Any) -> str:
    try:
        v = int(val)
    except Exception:
        return str(val)
    name = _STATUS_NAMES.get(v, f"?({v})")
    color = _STATUS_COLORS.get(v, "bold white")
    return f"[{color}]  {name}  [/]"


# ─── Textual 위젯 ─────────────────────────────────────────────────────────────
class TitleBar(Static):
    pass

class SectionTabBar(Static):
    pass

class GuideBar(Static):
    pass

class StatusBar(Static):
    pass


class PowerTrack(Static):
    powered = reactive(False)
    focused = reactive(False)

    def watch_focused(self, focused: bool) -> None:
        self.set_class(focused, "-focused")

    def watch_powered(self, powered: bool) -> None:
        self.set_class(powered, "-on")
        self.set_class(not powered, "-off")
        if powered:
            self.update("[bold #202020]POWER [/][bold white on green] ON [/] ")
        else:
            self.update("[bold #202020]POWER [/][bold white on red] OFF [/] ")


class TagLabel(Static):
    pass

class TagValue(Static):
    editing = reactive(False)

    def watch_editing(self, editing: bool) -> None:
        self.set_class(editing, "-editing")


class TagColumn(Container):
    def __init__(self, label_id: str, value_id: str, box_width: int) -> None:
        super().__init__()
        self.box_width = box_width
        self.label_widget = TagLabel(id=label_id)
        self.value_gap = Static(classes="value-gap")
        self.value_widget = TagValue(id=value_id)

    def compose(self) -> ComposeResult:
        yield self.label_widget
        yield self.value_gap
        yield self.value_widget

    def set_sizes(self, box_width: int) -> None:
        self.box_width = box_width
        for widget in (self.label_widget, self.value_widget):
            widget.styles.width = box_width
            widget.styles.min_width = box_width
            widget.styles.max_width = box_width

    def set_data(
        self, label: str, value: str, *, markup: str, editing: bool = False
    ) -> None:
        clipped = label[: self.box_width]
        self.label_widget.update(
            f"[bold {DARK_TEXT} on {LIGHT_LABEL_BG}]{clipped:<{self.box_width}}[/]"
        )
        self.value_widget.update(markup.format(value=f"{value:^{self.box_width}}"))
        self.value_widget.editing = editing


class SpCard(Container):
    focused = reactive(False)

    def __init__(self, sp_name: str, box_width: int) -> None:
        super().__init__(id=f"card-{sp_name}")
        self.sp_name = sp_name
        self.row = Horizontal(classes="sp-row")
        self.sp_col = TagColumn(f"sp-label-{sp_name}", f"sp-value-{sp_name}", box_width)
        self.gap = Static(classes="pair-gap")
        self.actual_col = TagColumn(
            f"actual-label-{sp_name}", f"actual-value-{sp_name}", box_width
        )

    def compose(self) -> ComposeResult:
        with self.row:
            yield self.sp_col
            yield self.gap
            yield self.actual_col

    def set_sizes(self, box_width: int) -> None:
        self.sp_col.set_sizes(box_width)
        self.actual_col.set_sizes(box_width)

    def watch_focused(self, focused: bool) -> None:
        self.set_class(focused, "-focused")

    def set_data(
        self,
        *,
        sp_label: str,
        sp_value: str,
        actual_label: str,
        actual_value: str,
        has_actual: bool,
        editing: bool,
        focused: bool,
    ) -> None:
        self.focused = focused
        self.sp_col.set_data(
            sp_label,
            sp_value,
            markup="[bold yellow on black]{value}[/]" if editing else "[bold red on black]{value}[/]",
            editing=editing,
        )
        self.actual_col.display = has_actual
        self.gap.display = has_actual
        if has_actual:
            self.actual_col.set_data(
                actual_label,
                actual_value,
                markup="[bold white on black]{value}[/]",
                editing=False,
            )


class InfoRow(Static):
    """단순 레이블=값 한 줄 카드 (alarm / counter / status / RO 태그)."""
    pass


# ─── CSS ─────────────────────────────────────────────────────────────────────
_CSS = f"""
Screen {{
    background: {SCREEN_BG};
    color: white;
    overflow: hidden hidden;
}}

#root {{
    layout: vertical;
    height: 100%;
    width: 100%;
    padding: 0 1;
    overflow: hidden hidden;
}}

TitleBar {{
    height: 3;
    min-height: 3;
    max-height: 3;
    border: solid {BORDER};
    background: #d7d7d7;
    color: {DARK_TEXT};
    text-style: bold;
    content-align: left middle;
    padding: 0 1;
    margin: 1 0 0 0;
}}

SectionTabBar {{
    height: 1;
    min-height: 1;
    max-height: 1;
    background: #c0c0c0;
    color: {DARK_TEXT};
    padding: 0 1;
    margin: 0 0 1 0;
    content-align: left middle;
}}

#power-wrap {{
    height: 3;
    min-height: 3;
    max-height: 3;
    margin: 0 0 1 0;
}}

PowerTrack {{
    width: 24;
    height: 3;
    min-height: 3;
    max-height: 3;
    border: solid {BORDER};
    background: #dadada;
    color: {DARK_TEXT};
    content-align: center middle;
    text-style: bold;
    padding: 0 1;
}}

PowerTrack.-focused {{
    border: solid yellow;
}}

PowerTrack.-on {{
    background: #d7e8d7;
}}

PowerTrack.-off {{
    background: #edd6d6;
}}

#sp-grid {{
    height: 1fr;
    min-height: 1fr;
    grid-size: 2;
    grid-columns: 1fr 1fr;
    grid-gutter: 1 1;
    overflow: hidden hidden;
}}

SpCard {{
    height: 6;
    min-height: 6;
    max-height: 6;
    border: solid {BORDER};
    background: {CARD_BG};
    padding: 0 1;
    overflow: hidden hidden;
}}

SpCard.-focused {{
    border: solid yellow;
}}

.sp-row {{
    layout: horizontal;
    width: 100%;
    height: 100%;
    align: center middle;
}}

TagColumn {{
    layout: vertical;
    width: 1fr;
    height: 100%;
    align: center middle;
    overflow: hidden hidden;
}}

TagLabel {{
    height: 1;
    min-height: 1;
    max-height: 1;
    background: {LIGHT_LABEL_BG};
    color: {DARK_TEXT};
    text-style: bold;
    content-align: left middle;
    overflow: hidden hidden;
}}

.value-gap {{
    height: 1;
    min-height: 1;
    max-height: 1;
}}

TagValue {{
    height: 1;
    min-height: 1;
    max-height: 1;
    background: black;
    color: white;
    text-style: bold;
    content-align: center middle;
    overflow: hidden hidden;
}}

TagValue.-editing {{
    color: yellow;
}}

.pair-gap {{
    width: 1;
    min-width: 1;
    max-width: 1;
    height: 100%;
}}

#alarm-panel {{
    height: 1fr;
    overflow-y: auto;
    background: {ALARM_CARD_BG};
    border: solid {BORDER};
    padding: 0 1;
    margin: 0 0 1 0;
}}

#counter-panel {{
    height: 1fr;
    overflow-y: auto;
    background: {COUNTER_CARD_BG};
    border: solid {BORDER};
    padding: 0 1;
    margin: 0 0 1 0;
}}

#fault-panel {{
    height: auto;
    overflow-y: auto;
    background: {FAULT_CARD_BG};
    border: solid {BORDER};
    padding: 0 1;
    margin: 0 0 1 0;
}}

#status-panel {{
    height: 1fr;
    overflow-y: auto;
    background: {STATUS_CARD_BG};
    border: solid {BORDER};
    padding: 0 1;
}}

.panel-title {{
    height: 1;
    min-height: 1;
    max-height: 1;
    background: #888888;
    color: white;
    text-style: bold;
    content-align: left middle;
    padding: 0 1;
    margin: 0 0 0 0;
}}

InfoRow {{
    height: 1;
    min-height: 1;
    max-height: 1;
    color: {DARK_TEXT};
    content-align: left middle;
    padding: 0 1;
}}

InfoRow.-active-alarm {{
    background: #e05050;
    color: white;
    text-style: bold;
}}

InfoRow.-focused-fault {{
    border: solid yellow;
    background: #f5e8c0;
}}

/* ── cycle_time 통계 행 ── */
.cycle-stat-row {{
    height: 2;
    min-height: 2;
    max-height: 2;
    color: {DARK_TEXT};
    content-align: left middle;
    padding: 0 1;
    background: #c8e0d0;
    margin-top: 1;
}}

GuideBar {{
    height: 1;
    min-height: 1;
    max-height: 1;
    background: #efefef;
    color: {DARK_TEXT};
    padding: 0 1;
    margin: 0;
    overflow: hidden hidden;
}}

StatusBar {{
    height: 1;
    min-height: 1;
    max-height: 1;
    background: #d9d9d9;
    color: {DARK_TEXT};
    padding: 0 1;
    margin: 0;
    overflow: hidden hidden;
}}
"""


# ─── TuiApp ──────────────────────────────────────────────────────────────────
class TuiApp(App):
    CSS = _CSS

    BINDINGS = [
        Binding("up,left",       "prev_focus",    show=False),
        Binding("down,right",    "next_focus",    show=False),
        Binding("enter",         "activate",      show=False),
        Binding("escape",        "cancel_edit",   show=False),
        Binding("tab",           "next_section",  show=False),
        Binding("shift+tab",     "prev_section",  show=False),
        Binding("q",             "quit_app",      show=False),
        Binding("w", "prev_focus", show=False),
        Binding("a", "prev_focus", show=False),
        Binding("s", "next_focus", show=False),
        Binding("d", "next_focus", show=False),
        Binding("h", "prev_focus", show=False),
        Binding("j", "next_focus", show=False),
        Binding("k", "prev_focus", show=False),
        Binding("l", "next_focus", show=False),
        Binding("r", "send_reset",  show=False),
        Binding("L", "send_load",   show=False),
        Binding("u", "send_unload", show=False),
    ]

    def __init__(self, socket_path: str = SOCKET_PATH) -> None:
        super().__init__()
        self.socket_path = socket_path
        self.state = UiState(socket_path=socket_path)
        self.equipment_name = "equipment"
        self.poll_client: Optional[TuiClient] = None

        self.root_col     = Vertical(id="root")
        self.title_bar    = TitleBar()
        self.tab_bar      = SectionTabBar()
        self.power_wrap   = Horizontal(id="power-wrap")
        self.power_track  = PowerTrack()
        self.sp_grid      = Grid(id="sp-grid")
        self.sp_cards: dict[str, SpCard] = {}

        self.alarm_panel   = Vertical(id="alarm-panel")
        self.counter_panel = Vertical(id="counter-panel")
        self.fault_panel   = Vertical(id="fault-panel")
        self.status_panel  = Vertical(id="status-panel")

        self.guide_bar  = GuideBar()
        self.status_bar = StatusBar()

        self.box_width = BASE_BOX_W
        self._last_layout_sig: tuple[int, int] | None = None

    # ── 컴포즈 ────────────────────────────────────────────────────────────────
    def compose(self) -> ComposeResult:
        with self.root_col:
            yield self.title_bar
            yield self.tab_bar
            with self.power_wrap:
                yield self.power_track
            yield self.sp_grid
            with self.alarm_panel:
                yield Static("── ALARM ──", classes="panel-title")
            with self.counter_panel:
                yield Static("── COUNTER ──", classes="panel-title")
            with self.fault_panel:
                yield Static("── FAULT INJECT ──", classes="panel-title")
            with self.status_panel:
                yield Static("── STATUS ──", classes="panel-title")
            yield self.guide_bar
            yield self.status_bar

    async def on_mount(self) -> None:
        await self._boot_connect()
        self._configure_layout(force=True)
        self._ensure_sp_cards()
        self._refresh_title_only()
        self._apply_section_visibility()
        self._refresh_dynamic_parts()
        self.set_interval(1.0, self._poll_tick)
        self.set_interval(0.2, self._ui_tick)

    def on_resize(self) -> None:
        self._configure_layout()

    # ── 레이아웃 ──────────────────────────────────────────────────────────────
    def _compute_layout(self) -> tuple[int, int]:
        width = max(self.size.width, 40)
        max_columns = min(5, max(1,
            len(self.sp_cards) or
            len([i for i in self.state.focus_items if i.kind == "sp"]) or 1
        ))
        for columns in range(max_columns, 0, -1):
            available = width - ((columns - 1) * GRID_GUTTER)
            card_width = available // columns
            usable = card_width - CARD_CONTENT_EXTRA
            box_width = usable // 2
            if box_width >= TARGET_BOX_W:
                return columns, TARGET_BOX_W
        for columns in range(max_columns, 0, -1):
            available = width - ((columns - 1) * GRID_GUTTER)
            card_width = available // columns
            usable = card_width - CARD_CONTENT_EXTRA
            box_width = usable // 2
            if box_width >= MIN_BOX_W:
                return columns, box_width
        return 1, MIN_BOX_W

    def _configure_layout(self, force: bool = False) -> None:
        cols, box_width = self._compute_layout()
        sig = (cols, box_width)
        if not force and sig == self._last_layout_sig:
            return
        self._last_layout_sig = sig
        self.sp_grid.styles.grid_size_columns = cols
        self.box_width = box_width
        for card in self.sp_cards.values():
            card.set_sizes(self.box_width)
        self._refresh_dynamic_parts()

    def _apply_section_visibility(self) -> None:
        sec = self.state.section
        self.power_wrap.display = (sec == SEC_POWER)
        self.sp_grid.display    = (sec == SEC_POWER)
        self.alarm_panel.display   = (sec == SEC_ALARM)
        self.counter_panel.display = (sec == SEC_ALARM)
        self.fault_panel.display   = (sec == SEC_ALARM)
        self.status_panel.display  = (sec == SEC_STATUS)

    # ── 연결 ──────────────────────────────────────────────────────────────────
    async def _boot_connect(self) -> None:
        try:
            boot = TuiClient(socket_path=self.socket_path)
            boot.connect()
            if not boot.ping():
                self.exit(message=f"TUI wrapper 서버에 ping 실패: {self.socket_path}")
                return
            self.equipment_name = self._detect_equipment_name()
            tags = await asyncio.to_thread(boot.read)
            self.state.refresh_tags(tags)
            self.state.connected = True
            boot.close()
        except FileNotFoundError:
            self.exit(message=f"소켓이 없어요: {self.socket_path}")
            return
        except Exception as e:
            self.exit(message=f"초기 연결 실패: {e}")
            return
        self.poll_client = TuiClient(socket_path=self.socket_path)
        self.poll_client.connect()

    def _detect_equipment_name(self) -> str:
        name = os.environ.get("EQUIPMENT_NAME")
        if name:
            return name
        line = os.environ.get("LINE_ID", "")
        cfg  = os.environ.get("SIM_CONFIG", "")
        if cfg:
            from pathlib import Path
            eq = Path(cfg).stem
            return f"{line}_{eq}" if line else eq
        return "equipment"

    # ── 폴링 / UI 틱 ─────────────────────────────────────────────────────────
    async def _poll_tick(self) -> None:
        if self.poll_client is None:
            return
        try:
            # ① 태그 전체 읽기
            tags = await asyncio.to_thread(self.poll_client.read)
            self.state.refresh_tags(tags)
            self.state.connected = True
            self.state.last_error = ""
            self._ensure_sp_cards()

            # ② cycle_time 통계 별도 조회 (server.py에 OP_CYCLE_TIME 핸들러 필요)
            ct_resp = await asyncio.to_thread(
                self.poll_client.request, req_cycle_time()
            )
            if isinstance(ct_resp, dict) and ct_resp.get("status") == "ok":
                self.state.cycle_info = parse_cycle_time(ct_resp)

        except Exception as e:
            self.state.connected = False
            self.state.last_error = f"{type(e).__name__}: {e}"
        self._refresh_dynamic_parts()

    def _ui_tick(self) -> None:
        self.state.clear_expired_notice()
        self._refresh_bars_only()

    # ── SP 카드 관리 ──────────────────────────────────────────────────────────
    def _ensure_sp_cards(self) -> None:
        current_names = [
            item.name for item in self.state.focus_items if item.kind == "sp"
        ]
        current_set = set(current_names)
        mounted_set = set(self.sp_cards.keys())
        for name in current_names:
            if name not in self.sp_cards:
                card = SpCard(name, self.box_width)
                self.sp_cards[name] = card
                self.sp_grid.mount(card)
            else:
                self.sp_cards[name].set_sizes(self.box_width)
        for name in mounted_set - current_set:
            card = self.sp_cards.pop(name)
            card.remove()
        for name, card in self.sp_cards.items():
            card.display = name in current_set
            card.set_sizes(self.box_width)

    # ── 전체 화면 갱신 ────────────────────────────────────────────────────────
    def _refresh_title_only(self) -> None:
        self.title_bar.update(f"[bold {DARK_TEXT}]{self.equipment_name[:40]}[/]")

    def _refresh_dynamic_parts(self) -> None:
        self._apply_section_visibility()
        self._refresh_tab_bar()
        self._refresh_power()
        self._update_sp_cards()
        self._update_alarm_panel()
        self._update_counter_panel()
        self._update_fault_panel()
        self._update_status_panel()
        self._refresh_bars_only()

    def _refresh_tab_bar(self) -> None:
        parts = []
        for sec in SECTION_ORDER:
            label = SECTION_LABEL[sec]
            if sec == self.state.section:
                parts.append(f"[bold white on #444444] {label} [/]")
            else:
                parts.append(f"[{DARK_TEXT} on #c0c0c0] {label} [/]")
        self.tab_bar.update("  ".join(parts))

    def _refresh_power(self) -> None:
        power = next((t for t in self.state.tags if t.role == "power"), None)
        self.power_track.powered = (
            bool(power.value) if power and power.value is not None else False
        )
        cur = self.state.current()
        self.power_track.focused = cur is not None and cur.kind == "power"

    def _update_sp_cards(self) -> None:
        sensors_by_sp: dict[str, TagInfo] = {}
        for t in self.state.tags:
            if t.role == "sensor" and t.source_sp:
                sensors_by_sp.setdefault(t.source_sp, t)
        current = self.state.current()
        for name, card in self.sp_cards.items():
            sp = self.state.by_name.get(name)
            if sp is None:
                card.display = False
                continue
            actual = sensors_by_sp.get(name)
            is_current = (
                current is not None
                and current.kind == "sp"
                and current.name == name
            )
            card.set_data(
                sp_label=sp.name,
                sp_value=(
                    self.state.edit_buf
                    if (self.state.editing and is_current)
                    else _format_value(sp.value, sp.data_type, sp.unit)
                ),
                actual_label=actual.name if actual else "",
                actual_value=(
                    _format_value(actual.value, actual.data_type, actual.unit)
                    if actual else ""
                ),
                has_actual=actual is not None,
                editing=self.state.editing and is_current,
                focused=is_current,
            )

    # ── Alarm 패널 ────────────────────────────────────────────────────────────
    def _update_alarm_panel(self) -> None:
        alarms = [t for t in self.state.tags if t.role == "alarm"]
        for w in list(self.alarm_panel.query(InfoRow)):
            w.remove()
        if not alarms:
            self.alarm_panel.mount(InfoRow("(alarm 태그 없음)"))
            return
        for t in alarms:
            active = bool(t.value)
            label = f"{'[●]' if active else '[ ]'} {t.name}"
            row = InfoRow(label)
            if active:
                row.add_class("-active-alarm")
            self.alarm_panel.mount(row)

    # ── Counter 패널 ─────────────────────────────────────────────────────────
    def _update_counter_panel(self) -> None:
        counters = [t for t in self.state.tags if t.role == "counter"]
        for w in list(self.counter_panel.query(InfoRow)):
            w.remove()
        if not counters:
            self.counter_panel.mount(InfoRow("(counter 태그 없음)"))
            return
        for t in counters:
            val = _format_value(t.value, t.data_type, t.unit)
            self.counter_panel.mount(InfoRow(f"{t.name:<24} {val}"))

    # ── Fault Inject 패널 ────────────────────────────────────────────────────
    def _update_fault_panel(self) -> None:
        faults = [
            t for t in self.state.tags
            if t.role == "fault_inject" and t.writable
        ]
        for w in list(self.fault_panel.query(InfoRow)):
            w.remove()
        if not faults:
            self.fault_panel.mount(InfoRow("(fault_inject 태그 없음)"))
            return
        current = self.state.current()
        for t in faults:
            active = bool(t.value)
            is_cur = (
                current is not None
                and current.kind == "fault_inject"
                and current.name == t.name
            )
            label = f"{'[ON] ' if active else '[OFF]'} {t.name}"
            row = InfoRow(label)
            if is_cur:
                row.add_class("-focused-fault")
            if active:
                row.add_class("-active-alarm")
            self.fault_panel.mount(row)

    # ── Status 패널 ──────────────────────────────────────────────────────────
    def _update_status_panel(self) -> None:
        """
        Status 섹션 렌더링.

        렌더 순서:
            1. status   — IDLE/RUNNING/WARNING/ERROR/COMPLETE 색상 레이블
            2. progress — 텍스트 프로그레스 바 + 퍼센트
            3. sensor   — threshold 3단계 색상 (err=red / warn=yellow / ok=green)
                          + warn_hi / err_hi 힌트 표시
            4. cycle_time 태그 (raw 현재값)
            5. ── 구분선 + cycle_time 통계 블록 (last / mean / σ / n) ──
            6. event    — 최근 이벤트 태그 값
            7. 기타 RO  — 위에 해당하지 않는 나머지
        """
        status_roles = {"status", "progress", "cycle_time", "event", "sensor"}
        targets = [t for t in self.state.tags if t.role in status_roles]

        # 기존 동적 위젯 전부 제거
        for w in list(self.status_panel.query(InfoRow)):
            w.remove()
        for w in list(self.status_panel.query(".cycle-stat-row")):
            w.remove()

        if not targets and self.state.cycle_info.count == 0:
            self.status_panel.mount(InfoRow("(status 태그 없음)"))
            return

        # 1. status 태그
        for t in [x for x in targets if x.role == "status"]:
            markup = _status_markup(t.value)
            self.status_panel.mount(InfoRow(f"{t.name:<20} {markup}"))

        # 2. progress 태그
        for t in [x for x in targets if x.role == "progress"]:
            try:
                pct = float(t.value or 0)
                bar_len = 20
                filled = int(pct / 100 * bar_len)
                bar = "█" * filled + "░" * (bar_len - filled)
                self.status_panel.mount(
                    InfoRow(f"{t.name:<20} [{bar}] {pct:5.1f}%")
                )
            except Exception:
                self.status_panel.mount(InfoRow(f"{t.name:<20} {t.value}"))

        # 3. sensor 태그 — threshold 기반 3단계 색상
        for t in [x for x in targets if x.role == "sensor"]:
            style = _threshold_style(t.value, t)
            val_str = _format_value(t.value, t.data_type, t.unit)
            # warn_hi / err_hi 힌트 (있는 것만)
            hint_parts = []
            if t.warn_hi is not None:
                hint_parts.append(f"W:{t.warn_hi}")
            if t.err_hi is not None:
                hint_parts.append(f"E:{t.err_hi}")
            hint = f" [dim]({' '.join(hint_parts)})[/]" if hint_parts else ""
            self.status_panel.mount(
                InfoRow(f"{t.name:<22} [{style}]{val_str}[/]{hint}")
            )

        # 4. cycle_time 태그 (raw 현재값)
        for t in [x for x in targets if x.role == "cycle_time"]:
            val_str = _format_value(t.value, t.data_type, t.unit or "s")
            self.status_panel.mount(InfoRow(f"{t.name:<22} [cyan]{val_str}[/]"))

        # 5. cycle_time 통계 블록
        ct = self.state.cycle_info
        if ct.count > 0 or ct.last > 0.0:
            self.status_panel.mount(
                Static(
                    f"[bold]CycleTime Stat[/]  "
                    f"last=[cyan]{ct.last:.2f}s[/]  "
                    f"mean=[cyan]{ct.mean:.2f}s[/]  "
                    f"σ=[cyan]{ct.stddev:.2f}s[/]  "
                    f"n=[dim]{ct.count}[/]",
                    classes="cycle-stat-row",
                )
            )

        # 6. event 태그
        for t in [x for x in targets if x.role == "event"]:
            val_str = _format_value(t.value, t.data_type, t.unit)
            active = bool(t.value)
            row = InfoRow(
                f"[bold]EVT[/] {t.name:<18} "
                f"[{'bold white on green' if active else 'dim'}]{val_str}[/]"
            )
            if active:
                row.add_class("-active-alarm")
            self.status_panel.mount(row)

        # 7. 나머지 RO 태그 (status_roles 에 없는 non-writable)
        skip_roles = status_roles | {"power", "setpoint", "alarm", "counter", "fault_inject"}
        for t in self.state.tags:
            if t.role not in skip_roles and not t.writable:
                val_str = _format_value(t.value, t.data_type, t.unit)
                self.status_panel.mount(InfoRow(f"{t.name:<24} {val_str}"))

    def _refresh_bars_only(self) -> None:
        self.guide_bar.update(
            GUIDE_EDIT if self.state.editing else GUIDE_NORMAL
        )
        if self.state.notice:
            text = f"★ {self.state.notice}"
        elif not self.state.connected:
            text = "연결끊김"
        elif self.state.last_error:
            text = f"✗ {self.state.last_error[:40]}"
        else:
            text = " "
        self.status_bar.update(text[:100])

    # ── 바인딩 액션 ───────────────────────────────────────────────────────────
    def action_prev_focus(self) -> None:
        if self.state.editing:
            return
        self.state.prev_in_section()
        self._refresh_dynamic_parts()

    def action_next_focus(self) -> None:
        if self.state.editing:
            return
        self.state.next_in_section()
        self._refresh_dynamic_parts()

    def action_next_section(self) -> None:
        if self.state.editing:
            return
        self.state.next_section()
        self._refresh_dynamic_parts()

    def action_prev_section(self) -> None:
        if self.state.editing:
            return
        self.state.prev_section()
        self._refresh_dynamic_parts()

    def action_activate(self) -> None:
        cur = self.state.current()
        if cur is None:
            return
        if self.state.editing:
            self._commit_edit()
            return
        if cur.kind == "power":
            self._toggle_power(cur.name)
        elif cur.kind == "sp":
            self.state.editing = True
            self.state.edit_buf = ""
        elif cur.kind == "fault_inject":
            self._toggle_bool_tag(cur.name)
        self._refresh_dynamic_parts()

    def action_cancel_edit(self) -> None:
        if self.state.editing:
            self.state.editing = False
            self.state.edit_buf = ""
            self.state.set_notice("편집 취소")
            self._refresh_dynamic_parts()

    def action_quit_app(self) -> None:
        self.exit()

    def action_send_reset(self) -> None:
        self._fire_event("reset_error")

    def action_send_load(self) -> None:
        self._fire_event("load_request")

    def action_send_unload(self) -> None:
        self._fire_event("unload_request")

    def _fire_event(self, tag_name: str) -> None:
        tag = self.state.by_name.get(tag_name)
        if tag is None:
            self.state.set_notice(f"태그 없음: {tag_name}")
            return
        ok, msg = self._safe_write(tag_name, True)
        self.state.set_notice(f"{tag_name} " + ("전송" if ok else f"실패: {msg}"))
        self._refresh_dynamic_parts()

    # ── 키 입력 (편집 모드) ───────────────────────────────────────────────────
    async def on_key(self, event) -> None:
        if not self.state.editing:
            return
        if event.key == "backspace":
            self.state.edit_buf = self.state.edit_buf[:-1]
            self._refresh_dynamic_parts()
            event.prevent_default()
            return
        ch = event.character or ""
        if len(ch) == 1 and (ch.isdigit() or ch in ("-", ".")):
            self.state.edit_buf += ch
            self._refresh_dynamic_parts()
            event.prevent_default()

    # ── 내부 write 헬퍼 ──────────────────────────────────────────────────────
    def _toggle_power(self, name: str) -> None:
        tag = self.state.by_name.get(name)
        if tag is None:
            return
        new = not bool(tag.value)
        ok, msg = self._safe_write(name, new)
        self.state.set_notice(
            (f"POWER {'ON' if new else 'OFF'}") if ok else f"power 실패: {msg}"
        )

    def _toggle_bool_tag(self, name: str) -> None:
        tag = self.state.by_name.get(name)
        if tag is None:
            return
        new = not bool(tag.value)
        ok, msg = self._safe_write(name, new)
        self.state.set_notice(
            f"{name} → {'ON' if new else 'OFF'}" if ok else f"write 실패: {msg}"
        )

    def _commit_edit(self) -> None:
        cur = self.state.current()
        if cur is None:
            return
        tag = self.state.by_name.get(cur.name)
        if tag is None:
            self.state.editing = False
            self.state.edit_buf = ""
            return
        try:
            if tag.data_type == "float":
                val: Any = float(self.state.edit_buf)
            elif tag.data_type == "int":
                val = int(float(self.state.edit_buf))
            else:
                val = self.state.edit_buf
        except ValueError:
            self.state.set_notice(f"숫자 형식 오류: '{self.state.edit_buf}'")
            self.state.editing = False
            self.state.edit_buf = ""
            self._refresh_dynamic_parts()
            return
        ok, msg = self._safe_write(cur.name, val)
        self.state.set_notice(msg if ok else f"write 실패: {msg}")
        self.state.editing = False
        self.state.edit_buf = ""
        self._refresh_dynamic_parts()

    def _safe_write(self, name: str, value: Any) -> tuple[bool, str]:
        try:
            client = TuiClient(socket_path=self.socket_path)
            client.connect()
            try:
                return client.write(name, value)
            finally:
                client.close()
        except Exception as e:
            return False, f"{type(e).__name__}: {e}"


def run(socket_path: str = SOCKET_PATH) -> int:
    app = TuiApp(socket_path=socket_path)
    app.run()
    return 0