# 오프라인 지도 타일 (MBTiles) 가이드

GpsMapTester는 **로컬 MBTiles 파일**에서 래스터 타일을 읽어 완전 오프라인으로 지도를 표출합니다.
(외부 타일 서버를 사용하지 않으므로 OSM 이용정책/차단과 무관합니다.)

## 동작 방식 (앱 쪽 — 이미 구현·검증 완료)
- [Services/LocalMBTilesProvider.cs](GpsMapTester/Services/LocalMBTilesProvider.cs): MBTiles(SQLite)에서 타일을 직접 읽는 커스텀 GMap 프로바이더. XYZ→TMS 변환 포함.
- [Views/MainView.xaml.cs](GpsMapTester/Views/MainView.xaml.cs) `ConfigureMap()`:
  - MBTiles 파일이 있으면 → `AccessMode.ServerOnly` + `LocalMBTilesProvider` (오프라인)
  - 없으면 → OSM 온라인(임시, 캐시 저장 안 함)
- **MBTiles 위치 해석 순서**
  1. `App.config` 의 `<appSettings><add key="MBTilesPath" value="..." /></appSettings>`
  2. 기본: `<실행폴더>\Maps\tiles.mbtiles`  (예: `bin\Debug\Maps\tiles.mbtiles`)

> 현재 `bin\Debug\Maps\tiles.mbtiles` 에는 **합성 테스트 타일**(서울시청 주변, 좌표 라벨)이 들어 있어
> 오프라인 표출이 검증된 상태입니다. 실제 지도로 쓰려면 이 파일을 아래 방법으로 만든 실제 MBTiles로 교체하세요.

## ⚠ 왜 OSM 공개 서버로 사전캐시를 못 하나
OSM 공개 타일 서버는 **대량/체계적 다운로드를 금지**합니다. 실제로 지역 단위(수만 타일) 다운로드를 시도하니
IP가 차단되어 "Access blocked" 이미지(6,987B)가 반환됐습니다. 따라서 오프라인 지역 타일은 **본인 소유의
소스**(자체 렌더 서버 등)에서 받아야 합니다.

## 실제 한국 타일 만들기 (권장: 자체 렌더 서버)

### 방법 A — Docker OSM 타일 서버 (표준)
```bash
# 1) Docker Desktop 설치 후, 한국 추출 데이터 준비 (Geofabrik)
#    https://download.geofabrik.de/asia/south-korea-latest.osm.pbf

# 2) 임포트 (PostGIS)
docker run -v osm-data:/data/database/ \
  -v /path/south-korea-latest.osm.pbf:/data/region.osm.pbf \
  overv/openstreetmap-tile-server import

# 3) 실행 → http://localhost:8080/tile/{z}/{x}/{y}.png (내 서버, 정책 무관)
docker run -p 8080:80 -v osm-data:/data/database/ \
  overv/openstreetmap-tile-server run
```
```powershell
# 4) 로컬 서버에서 4개 지역 z15 타일을 받아 바로 MBTiles로 저장 (내 서버라 대량 OK, 이어받기 지원)
python tools\mbtiles\render_to_mbtiles.py --out korea_z15.mbtiles
#   미리 타일 수만 확인: --dry-run  /  일부 지역만: --regions seoul daejeon
#   → 고유 약 22,115 타일 (z15, 서울·경기·내포·대전)

# 5) 앱에 넣기
copy korea_z15.mbtiles "GpsMapTester\bin\Debug\Maps\tiles.mbtiles"
```
[tools/mbtiles/render_to_mbtiles.py](tools/mbtiles/render_to_mbtiles.py) — localhost 타일서버 → MBTiles (표준 라이브러리만).

### 방법 B — MapTiler Engine 등 GUI 렌더러
`.osm.pbf` 추출을 불러와 **래스터 MBTiles로 바로 내보내기**. 무료 티어는 배율/워터마크 제한이 있을 수 있음.

### 공통 마지막 단계 — 폴더 → MBTiles 패키징
어떤 방법으로든 `{z}/{x}/{y}.png` 폴더가 준비되면:
```powershell
python tools\mbtiles\folder_to_mbtiles.py  .\rendered\  .\korea_z15.mbtiles
```
[tools/mbtiles/folder_to_mbtiles.py](tools/mbtiles/folder_to_mbtiles.py) — XYZ→TMS 변환 포함, 표준 라이브러리만 사용.

만든 `korea_z15.mbtiles` 를 `bin\Debug\Maps\tiles.mbtiles` 로 복사(또는 `App.config` 의 `MBTilesPath` 지정)하면 끝입니다.

## 배율/영역
- 앱 고정 배율 = **z15** ([MainViewModel.FixedZoom](GpsMapTester/ViewModels/MainViewModel.cs)).
  MBTiles에 최소한 z15 타일이 있어야 합니다(부드러운 표출을 원하면 z14~16 포함 권장).
- 대상 영역: 서울 / 경기 / 내포신도시 / 대전 (원하는 만큼 확장 가능).
