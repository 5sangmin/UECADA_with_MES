# `uecada` — one-stop CLI

UECADA 스택 전체를 한 번에 켜고 끄기 위한 PowerShell wrapper.

- 진입점: repo root 의 `uecada.cmd` (cmd 창에서 그대로 호출 가능)
- 실제 로직: `scripts/uecada.ps1`
- Windows 전용 (PowerShell + docker compose + dotnet/npm)

## 컴포넌트

| 이름             | 위치                                 | 기동 방식                                  |
|------------------|--------------------------------------|--------------------------------------------|
| `infra`          | `./` (repo root)                     | `docker compose up -d`                     |
| `das`            | `total_das/DAS`                      | `docker compose up -d --build`             |
| `equip-sim`      | `total_das/equip-sim`                | `scripts/up-all.ps1 up` (line-01/02/03)    |
| `command-center` | `total_das/equip-sim`                | `scripts/up-command-center.ps1`            |
| `xdas`           | `total_das/X_DAS`                    | `docker compose up -d --build`             |
| `backend-api`    | `unreal-backend/BeApi`               | `dotnet run` — **Start-Process (Windows local)** |
| `frontend`       | `frontend/UECADA_3`                  | `npm run dev` — **Start-Process (Windows local)** |

`backend-api` 와 `frontend` 는 UDP 통신 / 핫리로드 편의를 위해 docker 가 아닌 Windows 로컬 프로세스로 동작한다. `Start-Process -WindowStyle Hidden` 으로 백그라운드 기동하고 (터미널을 닫아도 살아있음), PID 는 `~/.uecada/state.json` (즉 `%USERPROFILE%\.uecada\state.json`) 에 저장된다.

stdout / stderr 는 repo 아래 `logs/` 폴더의 파일로 저장된다:

- `logs/backend-api.log` (stdout) / `logs/backend-api.err.log` (stderr)
- `logs/frontend.log` / `logs/frontend.err.log`
- 재기동 시 기존 파일은 `.1` 서픽스로 아카이브된다 (이전 세션 1회분만 보존).

## 사용법

```powershell
uecada start                # 전체 기동 (default 순서)
uecada start backend-api    # 특정 컴포넌트만
uecada stop                 # 전체 종료 (역순)
uecada stop frontend
uecada restart              # 전체 재기동
uecada status               # docker compose ps + Job state
uecada logs frontend        # Receive-Job -Wait
uecada logs das             # docker compose logs -f
uecada help
```

기본 기동 순서:

```
(Ensure total-das-net, factory-net) → das → infra → equip-sim → command-center → xdas → backend-api → frontend
```

기본 종료 순서는 역순 — 단 das 가 제일 마지막에 내려간다 (`das_das-internal` 을 infra 가 참조하기 때문).

**외부 네트워크 의존성:**
- `total-das-net` (external) — `uecada start` 가 미리 생성
- `factory-net` (external) — `uecada start` 가 미리 생성
- `das_das-internal` — `das` compose 가 첫 기동시 생성. `infra` 의 backend 가 이를 참조하므로 **das 가 infra 보다 먼저 떠야 한다**.

## 동작 메모

- `start <component>` 호출 시 이미 같은 PID 가 살아있으면 중복 기동하지 않고 안내만 출력한다.
- `stop <component>` 는 docker 컴포넌트는 `docker compose down`, 로컬 프로세스 컴포넌트는 `taskkill /PID <pid> /T /F` (자식 트리 종료) 로 정리한 뒤 state.json 에서 해당 키를 삭제한다.
- `status` 는 컴포넌트별로 한 줄씩 컨테이너 / PID 상태를 출력한다. 로컬 프로세스는 `Get-Process -Id <pid>` 로 존재 여부만 확인한다.
- `logs <docker-component>` 는 `docker compose logs -f`. Ctrl+C 로 빠져나와도 컨테이너는 그대로 동작한다.
- `logs backend-api` / `logs frontend` 는 `Get-Content -Wait -Tail 50` 으로 stdout 파일을 follow 한다. stderr 가 궁금하면 별도 창에서 `Get-Content -Wait logs\<name>.err.log` 로 볼 것.

## 빠른 점검

```powershell
# 모든 게 떠있는지 한 화면에 확인
uecada status

# 특정 라인만 재기동
uecada restart equip-sim

# BeApi 로그 follow
uecada logs backend-api
```

## 주의 사항

- 이 CLI 는 DB 마이그레이션이나 스키마 변경을 하지 않는다. 단순히 기동/종료/상태/로그만 다룬다.
- `command-center` 의 `down` 은 `factory-net` 을 제거하지 않는다 (line-das 와 공유 네트워크). 수동 제거가 필요하면 `docker network rm factory-net`.
- 처음 사용 전 `unreal-backend/BeApi` 에는 `dotnet restore`, `frontend/UECADA_3` 에는 `npm install` 이 한 번씩 필요하다.
