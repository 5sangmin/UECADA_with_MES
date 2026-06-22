# Videos Directory

PR7 (Step13) 비디오 스트리밍이 참조하는 디렉터리입니다.

## 환경변수

```
VIDEO_ROOT=C:\path\to\videos   # 미설정 시 ./videos (실행 디렉터리 기준)
```

## 파일 컨벤션

모든 (line, equipment) 가 status_code 만으로 영상을 공유합니다.

```
{VIDEO_ROOT}/
  status_0.webm           # status_code=0 일 때 사용 (예: idle)
  status_0.jpg            # 썸네일 (첫 프레임을 미리 추출해 둘 것)
  status_1.webm
  status_1.jpg
  status_2.webm
  status_2.jpg
  ...
  status_default.webm     # status 별 파일이 없을 때 fallback
  status_default.jpg
```

## 컨테이너 / 코덱

| 항목 | 값 | 비고 |
|---|---|---|
| Container | `.webm` | Unreal CEF (Chromium Embedded) 호환성 우선 |
| Codec | VP8 또는 VP9 | H.264 도 가능하나 CEF 빌드에 따라 비활성일 수 있음 |
| 썸네일 | `.jpg` | 첫 프레임을 ffmpeg 등으로 사전 추출 |

ffmpeg 로 mp4 → webm 변환 예시:

```bash
ffmpeg -i input.mp4 -c:v libvpx-vp9 -b:v 1M -c:a libopus status_1.webm
ffmpeg -i status_1.webm -ss 00:00:00 -vframes 1 status_1.jpg
```

## API 매핑

| URL | 동작 |
|---|---|
| `GET /api/video/{lineId}/{equipmentId}` | TSDB 최신 status_code → `status_{code}.webm` 스트림 (Range 지원) |
| `GET /api/video/{lineId}/{equipmentId}/thumbnail.jpg` | 동일 status 의 `status_{code}.jpg` 반환 |

파일이 없으면 `status_default.*` 로 fallback, 그것도 없으면 404.

응답 헤더에 디버그 정보 노출:
- `X-Status-Code`: 매칭된 status_code
- `X-Video-Source`: `status` (정확 매칭) / `default` (fallback)
