# Videos Directory

PR7 (Step13) 비디오 스트리밍이 참조하는 디렉터리입니다.

## 환경변수

```
VIDEO_ROOT=C:\path\to\videos   # 미설정 시 ./videos (실행 디렉터리 기준)
```

## 파일 컨벤션

영상은 **equipment 타입(백의 자리)** × **status_code** 조합으로 공유됩니다.
**line(1/2/3)은 영상 파일 선택에 영향을 주지 않습니다** — line 1/2/3 의 같은 타입은 모두 같은 영상을 사용합니다.

### Equipment 타입 매핑

| 백의 자리 | 타입 폴더 | 해당 equipmentId | 비고 |
|---|---|---|---|
| 1xx | `CAST` | 101 (CAST-01) | 1개 |
| 2xx | `CNC`  | 201, 202, 203 (CNC-01~03) | 3개가 같은 영상 |
| 3xx | `WASH` | 301 (WASH-01) | 1개 |
| 4xx | `ASSY` | 401, 402 (ASSY-01~02) | 2개가 같은 영상 |
| 5xx | `TEST` | 501, 502 (TEST-01~02) | 2개가 같은 영상 |

### Status code

| code | 의미 |
|---|---|
| 0 | IDLE |
| 1 | RUNNING |
| 2 | COMPLETED |
| 3 | WARNING |
| 4 | ERROR |

### Power off 정책

TSDB 의 `power` 컬럼이 `false` 이면 status_code 와 무관하게 항상 타입별 `status_default.webm` 으로 매핑됩니다.
(power-off 를 독립적인 status 로 추가하지 않고 default 영상으로 표현.)

### 디렉터리 구조

```
{VIDEO_ROOT}/
  CAST/
    status_0.webm        # CAST IDLE
    status_0.jpg         # 썸네일 (사전 생성)
    status_1.webm        # CAST RUNNING
    status_1.jpg
    status_2.webm
    status_2.jpg
    status_3.webm
    status_3.jpg
    status_4.webm
    status_4.jpg
    status_default.webm  # status_N.webm 미존재 시 fallback
    status_default.jpg
  CNC/
    status_0.webm
    status_0.jpg
    ...
  WASH/
    ...
  ASSY/
    ...
  TEST/
    ...
```

총 최대 **5 타입 × 5 상태 = 25개 영상 (+ 타입별 default)**.

## 컨테이너 / 코덱

| 항목 | 값 | 비고 |
|---|---|---|
| Container | `.webm` | Unreal CEF (Chromium Embedded) 호환성 우선 |
| Codec | VP8 또는 VP9 | H.264 도 가능하나 CEF 빌드에 따라 비활성일 수 있음 |
| 썸네일 | `.jpg` | 첫 프레임을 ffmpeg 등으로 사전 추출 |

ffmpeg 로 mp4 → webm 변환 + 썸네일 추출 예시:

```bash
ffmpeg -i input.mp4 -c:v libvpx-vp9 -b:v 1M -c:a libopus CNC/status_1.webm
ffmpeg -i CNC/status_1.webm -ss 00:00:00 -vframes 1 CNC/status_1.jpg
```

## API 매핑

| URL | 동작 |
|---|---|
| `GET /api/video/{lineId}/{equipmentId}` | equipmentId → 타입 변환 후 `{TYPE}/status_{code}.webm` 스트림 (Range 지원) |
| `GET /api/video/{lineId}/{equipmentId}/thumbnail.jpg` | 동일 (타입, status) 의 `{TYPE}/status_{code}.jpg` 반환 |

파일 매핑 우선순위:

1. **power == false** → status 고려 없이 바로 `{TYPE}/status_default.{ext}` (없으면 404)
2. **power == true** 이면 `{TYPE}/status_{statusCode}.{ext}` → `{TYPE}/status_default.{ext}` → 404
3. equipmentId 의 백의 자리가 1~5 가 아니면 즉시 404 (`UNKNOWN_EQUIPMENT_TYPE`)

응답 헤더에 디버그 정보 노출:
- `X-Equipment-Type`: 매칭된 타입 (CAST/CNC/WASH/ASSY/TEST)
- `X-Status-Code`: 원본 status_code (power-off 일 때도 동일)
- `X-Power`: `on` / `off`
- `X-Video-Source`: `status` (정확 매칭) / `default` (status 파일 없어 fallback) / `power_off` (power-off 이라 default)

응답에는 항상 `Cache-Control: no-cache, no-store, must-revalidate` 가 붙어 있으므로
브라우저가 이전 영상/썸네일을 캐싱해서 상태가 최신으로 보이지 않는 문제를 완화합니다.
그리드 페이지(`/video`)는 5초마다 썸네일에 cache-buster query 를 붙여 강제 갱신합니다.
