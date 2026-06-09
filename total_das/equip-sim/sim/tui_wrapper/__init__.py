"""시뮬 컨테이너 내부 TUI wrapper.

목적:
    같은 컨테이너 안에서 `docker exec -it <eq> python -m sim.tui_wrapper` 로
    띄울 수 있는 제어반 TUI. 외부 산업 프로토콜(OPC UA/Modbus/MC) 을 거치지 않고
    **시뮬 내부 EquipmentState 에 직접 접근**해 read/write.

설계 결정 (자세한 배경은 docs/tui_wrapper_design.md 참조):
    - docker attach 는 부적합 (PID 1 의 TTY 충돌, SIGWINCH 안 옴). exec 채택.
    - exec 는 새 프로세스라 state 직접 접근 불가 → UDS + JSON line 으로 IPC.
    - UDS 소켓은 /tmp/sim-tui.sock (컨테이너 내부에만 보임).
    - TUI 서버는 daemon thread → 시뮬 본체에 영향 없음.

구성:
    protocol.py      JSON line 메시지 정의 (TagInfo unit 필드 포함)
    server.py        시뮬 PID 1 측. EquipmentState 와 UDS 연결.
    client.py        exec 측. UDS 로 read/write 요청.
    application.py   Textual 기반 TUI 본체 (client 사용).
                     섹션: [P]ower/Setpoint · [A]larm/Counter · [S]tatus
    __main__.py      `python -m sim.tui_wrapper` 진입점.

단축키 (application.py):
    ↑↓←→ / hjkl / wasd  포커스 이동
    Enter                선택 / 편집 확정
    Esc                  편집 취소
    Tab / Shift-Tab      섹션 전환 (P → A → S)
    R                    reset_error 이벤트 전송
    L                    load_request 이벤트 전송
    U                    unload_request 이벤트 전송
    Q                    종료
"""
