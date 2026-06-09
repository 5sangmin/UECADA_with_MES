"""TUI wrapper application — Textual 기반 시뮬레이터 제어 패널.

상단 Header (타이틀 + 상태 뱃지)  →  DeviceTable (설비 목록)
→  하단 GuideBar (키 안내) 구조.

REVISION 2026-06-09
  - Tab / Shift+Tab 대신 t / [ / ] 로 섹션 전환 (포커스 충돌 해소)
"""
from __future__ import annotations

import asyncio
import os
from dataclasses import dataclass, field
from enum import Enum, auto
from pathlib import Path
from typing import Any

from textual.app import App, ComposeResult
from textual.binding import Binding
from textual.color import Color
from textual.containers import Container, Horizontal, Vertical
from textual.reactive import reactive
from textual.screen import Screen
from textual.widget import Widget
from textual.widgets import (
    DataTable,
    Footer,
    Header,
    Input,
    Label,
    ListItem,
    ListView,
    Static,
)

# ---------------------------------------------------------------------------
# 가이드 바 텍스트
# ---------------------------------------------------------------------------

GUIDE_NORMAL = (
    "[bold cyan]↑↓[/] 이동  "
    "[bold red][[/] [bold red]][/] 섹션전환  "
    "[bold yellow]Enter[/] 선택  "
    "[bold]Space[/] 전원  "
    "[bold]e[/] 편집  "
    "[bold]r[/] 새로고침  "
    "[bold]q[/] 종료"
)

GUIDE_EDIT = (
    "[bold cyan]↑↓[/] 태그선택  "
    "[bold yellow]Enter[/] 값입력  "
    "[bold]Esc[/] 편집종료"
)


# ---------------------------------------------------------------------------
# 상태 열거형 / 데이터클래스
# ---------------------------------------------------------------------------


class EditState(Enum):
    NONE = auto()
    TAG_SELECT = auto()
    VALUE_INPUT = auto()


@dataclass
class AppState:
    editing: bool = False
    edit_state: EditState = EditState.NONE
    selected_row: int = 0
    selected_section: int = 0
    section_count: int = 1


# ---------------------------------------------------------------------------
# 더미 설비 데이터 (실제 구현 시 SimCore 와 연결)
# ---------------------------------------------------------------------------


@dataclass
class TagInfo:
    name: str
    role: str
    value: Any
    writable: bool = False


@dataclass
class EquipmentInfo:
    eq_id: str
    line: str
    protocol: str
    power: bool
    status: str
    tags: list[TagInfo] = field(default_factory=list)


# ---------------------------------------------------------------------------
# 위젯: GuideBar
# ---------------------------------------------------------------------------


class GuideBar(Static):
    """화면 하단 키 안내 바."""

    def __init__(self, text: str = GUIDE_NORMAL) -> None:
        super().__init__(text)
        self._text = text

    def update_text(self, text: str) -> None:
        self._text = text
        self.update(text)


# ---------------------------------------------------------------------------
# 위젯: EquipmentCard  (설비 카드 — 상세 뷰)
# ---------------------------------------------------------------------------


class EquipmentCard(Widget):
    """단일 설비 상세 정보 카드."""

    DEFAULT_CSS = """
    EquipmentCard {
        border: solid $primary;
        padding: 1 2;
        margin: 0 1;
        height: auto;
    }
    EquipmentCard .card-title {
        text-style: bold;
        color: $accent;
    }
    EquipmentCard .tag-row {
        height: 1;
    }
    """

    def __init__(self, eq: EquipmentInfo) -> None:
        super().__init__()
        self.eq = eq

    def compose(self) -> ComposeResult:
        power_str = "[green]ON[/]" if self.eq.power else "[red]OFF[/]"
        yield Static(
            f"[bold]{self.eq.eq_id}[/]  {power_str}  "
            f"[dim]{self.eq.protocol}[/]  status=[yellow]{self.eq.status}[/]",
            classes="card-title",
        )
        for tag in self.eq.tags:
            writable_mark = "[cyan](W)[/]" if tag.writable else "     "
            yield Static(
                f"  {writable_mark} {tag.name:<32} = {tag.value}",
                classes="tag-row",
            )


# ---------------------------------------------------------------------------
# 위젯: DeviceTable  (설비 목록 테이블)
# ---------------------------------------------------------------------------


class DeviceTable(Widget):
    """설비 목록 DataTable 래퍼."""

    COLUMNS = ("Line", "Equipment", "Protocol", "Power", "Status")

    DEFAULT_CSS = """
    DeviceTable {
        height: 1fr;
    }
    """

    def __init__(self, equipments: list[EquipmentInfo]) -> None:
        super().__init__()
        self.equipments = equipments

    def compose(self) -> ComposeResult:
        table: DataTable = DataTable(id="eq-table")
        yield table

    def on_mount(self) -> None:
        table = self.query_one("#eq-table", DataTable)
        for col in self.COLUMNS:
            table.add_column(col)
        for eq in self.equipments:
            power_cell = "[green]ON[/]" if eq.power else "[red]OFF[/]"
            table.add_row(eq.line, eq.eq_id, eq.protocol, power_cell, eq.status)


# ---------------------------------------------------------------------------
# 메인 애플리케이션
# ---------------------------------------------------------------------------


SAMPLE_EQUIPMENTS: list[EquipmentInfo] = [
    EquipmentInfo(
        eq_id="CAST-01",
        line="LINE-01",
        protocol="mcprotocol",
        power=True,
        status="RUNNING",
        tags=[
            TagInfo("power",                 "power",    True,   True),
            TagInfo("load_request",          "event",    False,  True),
            TagInfo("unload_request",        "event",    False,  True),
            TagInfo("reset_error",           "event",    False,  True),
            TagInfo("injection_pressure_sp", "setpoint", 80.0,   True),
            TagInfo("mold_temperature_sp",   "setpoint", 215.0,  True),
            TagInfo("cooling_flow_sp",       "setpoint", 40.0,   True),
            TagInfo("injection_pressure",    "sensor",   81.2,   False),
            TagInfo("mold_temperature",      "sensor",   214.3,  False),
            TagInfo("cooling_flow",          "sensor",   39.8,   False),
            TagInfo("progress",              "sensor",   0.0167, False),
        ],
    ),
    EquipmentInfo(
        eq_id="CNC-01",
        line="LINE-01",
        protocol="modbus-rtu-tcp",
        power=True,
        status="RUNNING",
        tags=[
            TagInfo("power",           "power",    True,  True),
            TagInfo("load_request",    "event",    False, True),
            TagInfo("unload_request",  "event",    False, True),
            TagInfo("reset_error",     "event",    False, True),
            TagInfo("spindle_speed_sp","setpoint", 600,   True),
            TagInfo("tool_usage_sp",   "setpoint", 30.0,  True),
            TagInfo("coolant_flow_sp", "setpoint", 18.0,  True),
            TagInfo("spindle_speed",   "sensor",   598,   False),
            TagInfo("tool_usage",      "sensor",   29.5,  False),
            TagInfo("coolant_flow",    "sensor",   18.1,  False),
            TagInfo("progress",        "sensor",   0.0056,False),
        ],
    ),
    EquipmentInfo(
        eq_id="WASH-01",
        line="LINE-01",
        protocol="modbus",
        power=False,
        status="IDLE",
        tags=[
            TagInfo("power",                     "power",    False, True),
            TagInfo("load_request",              "event",    False, True),
            TagInfo("unload_request",            "event",    False, True),
            TagInfo("reset_error",               "event",    False, True),
            TagInfo("cleaning_concentration_sp", "setpoint", 3.2,   True),
            TagInfo("cleaning_temperature_sp",   "setpoint", 62.0,  True),
            TagInfo("cleaning_pressure_sp",      "setpoint", 4.0,   True),
            TagInfo("cleaning_concentration",    "sensor",   3.2,   False),
            TagInfo("cleaning_temperature",      "sensor",   62.0,  False),
            TagInfo("cleaning_pressure",         "sensor",   4.0,   False),
            TagInfo("progress",                  "sensor",   0.0,   False),
        ],
    ),
]


class SimTuiApp(App):
    """설비 시뮬레이터 TUI."""

    CSS = """
    Screen {
        layout: vertical;
    }
    #main-container {
        height: 1fr;
        layout: horizontal;
    }
    #left-panel {
        width: 60%;
        height: 100%;
    }
    #right-panel {
        width: 40%;
        height: 100%;
        border: solid $accent;
        padding: 1;
        overflow-y: auto;
    }
    #guide-bar {
        height: 1;
        background: $surface;
        color: $text;
        padding: 0 1;
    }
    """

    BINDINGS = [
        Binding("q",             "quit",          "종료",        show=True),
        Binding("r",             "refresh",       "새로고침",    show=True),
        Binding("e",             "toggle_edit",   "편집",        show=True),
        Binding("space",         "toggle_power",  "전원",        show=True),
        Binding("t",             "next_section",  show=False),
        Binding("[",             "prev_section",  show=False),
        Binding("]",             "next_section",  show=False),
        Binding("up",            "cursor_up",     show=False),
        Binding("down",          "cursor_down",   show=False),
        Binding("enter",         "select",        show=False),
        Binding("escape",        "escape",        show=False),
    ]

    def __init__(self) -> None:
        super().__init__()
        self.state = AppState(section_count=2)
        self._equipments = SAMPLE_EQUIPMENTS
        self._selected_eq_idx: int = 0

    # ------------------------------------------------------------------
    # 레이아웃
    # ------------------------------------------------------------------

    def compose(self) -> ComposeResult:
        yield Header(show_clock=True)
        with Horizontal(id="main-container"):
            with Vertical(id="left-panel"):
                yield DeviceTable(self._equipments)
            with Vertical(id="right-panel"):
                yield EquipmentCard(self._equipments[self._selected_eq_idx])
        yield GuideBar(GUIDE_NORMAL)

    # ------------------------------------------------------------------
    # 섹션 전환 액션
    # ------------------------------------------------------------------

    def action_next_section(self) -> None:
        self.state.selected_section = (
            self.state.selected_section + 1
        ) % self.state.section_count
        self._focus_section(self.state.selected_section)

    def action_prev_section(self) -> None:
        self.state.selected_section = (
            self.state.selected_section - 1
        ) % self.state.section_count
        self._focus_section(self.state.selected_section)

    def _focus_section(self, idx: int) -> None:
        if idx == 0:
            try:
                self.query_one("#eq-table", DataTable).focus()
            except Exception:
                pass
        else:
            try:
                self.query_one("#right-panel").focus()
            except Exception:
                pass

    # ------------------------------------------------------------------
    # 기타 액션
    # ------------------------------------------------------------------

    def action_refresh(self) -> None:
        self.notify("새로고침 (미구현)")

    def action_toggle_edit(self) -> None:
        self.state.editing = not self.state.editing
        guide_bar = self.query_one(GuideBar)
        guide_bar.update_text(GUIDE_EDIT if self.state.editing else GUIDE_NORMAL)

    def action_toggle_power(self) -> None:
        eq = self._equipments[self._selected_eq_idx]
        eq.power = not eq.power
        self.notify(f"{eq.eq_id} 전원 {'ON' if eq.power else 'OFF'}")
        self._refresh_detail()

    def action_cursor_up(self) -> None:
        try:
            table = self.query_one("#eq-table", DataTable)
            table.move_cursor(row=max(0, table.cursor_row - 1))
            self._selected_eq_idx = table.cursor_row
            self._refresh_detail()
        except Exception:
            pass

    def action_cursor_down(self) -> None:
        try:
            table = self.query_one("#eq-table", DataTable)
            table.move_cursor(row=min(len(self._equipments) - 1, table.cursor_row + 1))
            self._selected_eq_idx = table.cursor_row
            self._refresh_detail()
        except Exception:
            pass

    def action_select(self) -> None:
        self.notify(f"선택: {self._equipments[self._selected_eq_idx].eq_id}")

    def action_escape(self) -> None:
        if self.state.editing:
            self.state.editing = False
            guide_bar = self.query_one(GuideBar)
            guide_bar.update_text(GUIDE_NORMAL)

    # ------------------------------------------------------------------
    # 내부 헬퍼
    # ------------------------------------------------------------------

    def _refresh_detail(self) -> None:
        right = self.query_one("#right-panel")
        right.remove_children()
        right.mount(EquipmentCard(self._equipments[self._selected_eq_idx]))


# ---------------------------------------------------------------------------
# 엔트리포인트
# ---------------------------------------------------------------------------


def main(socket_path: str | None = None) -> int:
    """TUI 앱 실행. 반환값 0 = 정상 종료."""
    SimTuiApp().run()
    return 0


# __main__.py 가 `from .application import run` 으로 호출하므로 별칭 유지.
run = main


if __name__ == "__main__":
    main()
