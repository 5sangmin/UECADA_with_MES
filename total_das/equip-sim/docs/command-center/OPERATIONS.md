# Command Center 운영 가이드 (skeleton)

> PR11 단계 — 인프라/컨테이너만 구성. OPC UA Server flow / Picker / Ack Handler / Timeout Sweeper 는 후속 PR(PR12~PR15) 에서 추가.

Command Center 는 BeApi(.NET 9) 가 `command_request` 테이블에 적재한 명령을 OPC UA Server 를 통해 line-das 에 전달하고, 응답을 받아 DB 를 갱신하는 Node-RED 기반 워커.

---

## 1. 토폴로지

```
┌──────────────┐  REST   ┌────────────────────┐  PostgreSQL ┌─────────────┐
│ 운영자 UI    │ ──────▶ │ BeApi (.NET 9)     │ ──────────▶ │ CommandDB   │
│ (cshtml)     │         │ localhost:5082     │             │ :55433      │
└──────────────┘         └────────────────────┘             └──────┬──────┘
                                                                   │ polling
                                                                   ▼
   factory-net (docker network)
   ┌─────────────────────────────────────────────────────────────────────┐
   │                                                                     │
   │  ┌─────────────────────────┐         ┌──────────────────────────┐  │
   │  │ command-center          │  OPC UA │ nodered-line01           │  │
   │  │ Node-RED 4.1.10-22      │ ◀──────▶│ Node-RED 4.1.10-22       │  │
   │  │ OPC UA Server :5160     │  Ack    │ (Request 노드 구독)      │  │
   │  │ - Line-01.Request       │ ──────▶ │                          │  │
   │  │ - Line-01.Ack  (write)  │         │ (Ack 노드 write)         │  │
   │  │ - Line-02.* / Line-03.* │         └──────────────────────────┘  │
   │  └─────────────────────────┘         (line02, line03 동일)         │
   └─────────────────────────────────────────────────────────────────────┘
```

---

## 2. 시스템 요구사항

| 항목 | 값 |
|------|------|
| OS | Windows 10/11 (Docker Desktop) 또는 Linux + Docker Engine |
| Docker | factory-net 외부 네트워크 사전 생성 (스크립트가 자동 생성) |
| CommandDB | PostgreSQL `localhost:55433` (호스트 OS, BeApi 와 공유) |
| 포트 | 호스트 5888 (Node-RED UI), 5160 (OPC UA Server) |

> Command Center 는 **factory-net 만 있으면 단독 기동 가능**.
> line-das (nodered-line01/02/03) 가 떠있지 않아도 OPC UA Server 자체는 정상 동작.
> 응답이 안 오는 명령은 후속 PR 의 Timeout Sweeper 가 처리.

---

## 3. 디렉터리 구조

```
equip-sim/
├─ Dockerfile.command-center             # 베이스 + opcua + postgresql 노드
├─ Dockerfile.command-center.dockerignore
├─ docker-compose.command-center.yml
├─ .env.command-center                   # 포트/DB 커넥션 (CHANGE_ME 교체 필수)
├─ patch-opcua-server.js                 # (재사용) OPC UA Server getter 패치
├─ scripts/
│  └─ up-command-center.ps1              # 기동/중지/로그
├─ data/
│  └─ command-center/                    # Node-RED user dir (볼륨 마운트)
│     ├─ flows.json                      # 빈 배열(PR11) → PR12부터 채움
│     ├─ package.json
│     ├─ settings.js
│     └─ .gitignore                      # runtime 부산물 무시
└─ docs/
   └─ command-center/
      └─ OPERATIONS.md                   # 이 문서
```

---

## 4. 환경변수 (`.env.command-center`)

| 키 | 기본값 | 설명 |
|------|------|------|
| `COMPOSE_PROJECT_NAME` | `command-center` | docker compose 프로젝트 이름 |
| `PORT_NODERED_UI` | `5888` | 호스트 UI 포트 |
| `OPCUA_SERVER_PORT` | `5160` | OPC UA Server 포트 (호스트/컨테이너 동일) |
| `FLOWS` | `flows.json` | data/command-center 안 flow 파일명 |
| `COMMANDDB_HOST` | `host.docker.internal` | DB 호스트 (Windows/Linux 모두 매핑됨) |
| `COMMANDDB_PORT` | `55433` | DB 포트 |
| `COMMANDDB_NAME` | `total_das_command` | DB 이름 |
| `COMMANDDB_USER` | `total_das_cmd_user` | DB 사용자 |
| `COMMANDDB_PASSWORD` | `__CHANGE_ME__` | **운영 전 반드시 실제 비밀로 교체** |
| `WORKER_ID` | `command-center` | 워커 식별자 (graceful recovery / 동시 인스턴스 구분용) |

> `COMMANDDB_PASSWORD` 가 `__CHANGE_ME__` 인 상태로 `up` 하면 스크립트가 경고 출력. 빌드는 됨 (네트워크/노드만 보기 위해서) 하지만 PR12 이후 DB 쓰는 flow 에선 인증 실패.

---

## 5. 기동 / 중지

### 기동
```powershell
cd C:\smart factory\UECADA\with_MES\total_das\equip-sim
.\scripts\up-command-center.ps1
```

성공 출력 예:
```
==> factory-net already exists
==> docker compose --env-file .env.command-center -f docker-compose.command-center.yml up -d --build
...
 Command Center UP
   Node-RED UI       : http://localhost:5888
   OPC UA Server     : opc.tcp://localhost:5160
```

### 중지
```powershell
.\scripts\up-command-center.ps1 down
```
> `factory-net` 은 보존됨 (라인 DAS 도 사용 중이므로 의도적으로 남김).

### 로그
```powershell
.\scripts\up-command-center.ps1 logs
```

### 상태
```powershell
.\scripts\up-command-center.ps1 ps
```

---

## 6. 시작 후 검증 (PR11 범위)

1. **컨테이너 상태**
   ```powershell
   docker ps --filter "name=command-center" --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
   ```
   `Up` 상태이고 포트 매핑이 `5888->1880`, `5160->5160` 으로 보이면 OK.

2. **Node-RED UI**
   브라우저로 http://localhost:5888 접근.
   빈 flow 가 보임 (PR12 에서 채워질 예정).

3. **factory-net 멤버십**
   ```powershell
   docker network inspect factory-net --format "{{range .Containers}}{{.Name}} {{end}}"
   ```
   `command-center` 와 (라인 DAS 가 떠있다면) `nodered-line01/02/03` 가 같이 출력되어야 함.

4. **컨테이너에서 호스트 DB 도달성 (DB 비밀 채운 뒤)**
   ```powershell
   docker exec -it command-center sh -c "nc -zv host.docker.internal 55433"
   ```
   `succeeded` 가 나오면 OK. (PR13 의 PostgreSQL 노드 동작 사전 확인)

5. **OPC UA Server endpoint listening (PR12 이후)**
   ```powershell
   # 호스트 검증 (TCP listen 여부)
   Test-NetConnection -ComputerName 127.0.0.1 -Port 5160
   # 컨테이너 내부 검증 (factory-net hostname resolve)
   docker exec -it command-center sh -c "nc -zv command-center 5160"
   ```
   운영 경로는 `opc.tcp://command-center:5160/UA/CommandCenter` (line-das 용), 검증 경로는
   `opc.tcp://127.0.0.1:5160/UA/CommandCenter` (UaExpert 등 호스트 도구용).

6. **Ack 수신 검증 (PR12 flow)** — 외부 OPC UA 클라이언트(UaExpert / opcua-commander)로
   `ns=1;s=Line-01.Ack` 에 임의 JSON 문자열을 write 해 본다:
   ```powershell
   docker logs --tail 20 command-center
   ```
   에 `[ACK] line=Line-01 ...` 이 찍히면 OK. Node-RED UI 의 debug sidebar 에도 파싱된 payload 가 보임.

---

## 7. 트러블슈팅

### 7.1 `factory-net` 없음
스크립트가 자동 생성하지만 수동으로 만들고 싶다면:
```powershell
docker network create factory-net
```

### 7.2 포트 5888 / 5160 점유
`netstat -ano | findstr :5888` 로 PID 확인 후 종료, 또는 `.env.command-center` 의 포트 값 변경.

### 7.3 host.docker.internal 안 됨 (Linux)
compose 의 `extra_hosts: host.docker.internal:host-gateway` 가 처리하지만,
구버전 Docker 에선 안 될 수 있음. 그 땐 호스트 IP 를 직접 `.env.command-center` 의 `COMMANDDB_HOST` 에 박을 것.

### 7.4 이미지 재빌드
```powershell
docker compose --env-file .env.command-center -f docker-compose.command-center.yml build --no-cache
```

---

## 8. 후속 PR 계획

| PR | 범위 |
|----|------|
| **PR12** | OPC UA Server flow + Line-01/02/03 Request/Ack 노드 카탈로그 + JSON 스키마 문서 |
| **PR13** | Picker flow (CommandDB polling, `FOR UPDATE SKIP LOCKED`, Request write, status→SENT) |
| **PR14** | Ack Handler flow (onWrite → 3-table 트랜잭션) |
| **PR15** | Timeout Sweeper + worker_id 기반 graceful recovery + 최종 운영 문서 |
