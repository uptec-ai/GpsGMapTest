<<<<<<< HEAD
# GpsGMapTest
=======
# GpsMapTester

BU-353N GPS 장비와 GMap.NET(OpenStreetMap)을 이용한 **실시간 위치 표출 WPF 앱**.
지도는 GPS 위치를 따라 움직이는 **follow 지도**이며, **오프라인(타일 캐시)** 로 동작합니다.

## 개발 환경
- Visual Studio 2019/2022, **.NET Framework 4.8**, WPF
- DevExpress WPF 23.1.5 (ThemedWindow, DevExpress.Mvvm)
- GMap.NET 2.1.7 (`GMap.NET.WinPresentation` 패키지 / 네임스페이스 `GMap.NET.WindowsPresentation`)

## 주요 기능
### 1) GPS (BU-353N)
- 앱 시작과 동시에 **백그라운드 자동 연결**: COM 포트를 스캔하고 baud(9600/4800/115200/38400)를 자동 감지.
- 유효 NMEA 문장이 오는 포트를 찾으면 연결 유지, 끊기면 **자동 재탐색/재연결**.
- 실시간 표시: **연결 상태 / 위도(Lat) / 경도(Lng)** + Fix / 위성 수 / 속도.
- 코드: [Services/GpsAutoConnectService.cs](GpsMapTester/Services/GpsAutoConnectService.cs),
  [Services/GpsService.cs](GpsMapTester/Services/GpsService.cs),
  [Helpers/NmeaParser.cs](GpsMapTester/Helpers/NmeaParser.cs)

### 2) 지도 (GMap follow)
- OpenStreetMap **라스터 타일**, **고정 배율 z15**.
- GPS(또는 기본값)의 lat/lng가 **지도 중심 + 포인트 마커** 위치가 되고, 값이 바뀌면 지도가 따라 이동.
- 마커 위 텍스트 = **시/군/구 라벨**. 현재는 좌표를 표시하며, 역지오코딩 훅으로 교체 가능(아래 참조).
- 기본 중심: **서울시청(37.5666, 126.9784)** — GPS 미연결/초기 상태.
- 코드: [Views/MainView.xaml](GpsMapTester/Views/MainView.xaml) / [MainView.xaml.cs](GpsMapTester/Views/MainView.xaml.cs),
  [ViewModels/MainViewModel.cs](GpsMapTester/ViewModels/MainViewModel.cs)

## 오프라인 지도 (타일 캐시)
- 지도 접근 모드는 `AccessMode.ServerAndCache`: **캐시에 있으면 캐시로 즉시 표출**, 없고 온라인이면 서버 보충.
- 완전 오프라인이어도 미리 받아둔 영역은 정상 표출됩니다.
- 캐시 위치: `%LOCALAPPDATA%\GMap.NET\TileDBv5\en\Data.gmdb` (앱/프리페치 도구 공용).

### 프리페치(캐시 다운로드) 도구
z15 타일을 미리 받아 캐시에 저장합니다. 대상 영역: **서울 / 경기 / 내포신도시 / 대전**.

```powershell
# 전체(4개 영역)
dotnet run -c Release --project "tools\TilePrefetch\TilePrefetch.csproj"

# 특정 영역만 (soul|gyeonggi|naepo|daejeon)
dotnet run -c Release --project "tools\TilePrefetch\TilePrefetch.csproj" -- naepo daejeon
```
- 이미 캐시에 있는 타일은 **건너뜁니다(이어받기/재실행 안전)**.
- OSM 정책 배려: 낮은 동시성(3) + 식별 User-Agent + 429 백오프.
- 코드: [tools/TilePrefetch/Program.cs](tools/TilePrefetch/Program.cs)

> 영역/배율을 바꾸려면 `Program.cs`의 `AllRegions` bbox와 `Zoom` 상수를 수정하세요.
> 앱의 고정 배율은 [MainViewModel.FixedZoom](GpsMapTester/ViewModels/MainViewModel.cs) 과 맞춰야 합니다(현재 15).

## 시/군/구 역지오코딩(선택)
기본은 좌표 표시(no-op). 온라인 시/군/구 명칭이 필요하면
[MainViewModel](GpsMapTester/ViewModels/MainViewModel.cs)의
```csharp
private readonly IReverseGeocoder _geocoder = new NullReverseGeocoder();
```
를 `new NominatimReverseGeocoder()` 로 교체하세요(인터넷 필요, Nominatim 호출 정책 준수).
훅 정의: [Services/IReverseGeocoder.cs](GpsMapTester/Services/IReverseGeocoder.cs)

## 빌드/실행
```powershell
dotnet build "GpsMapTester.sln" -c Debug
# 또는 Visual Studio에서 F5
```
> 참고: .NET Framework에서 `System.IO.Ports.SerialPort`는 기본 참조 `System.dll`에 포함되어 별도 참조가 필요 없습니다.
>>>>>>> 332f810 (chore:gps and map)
