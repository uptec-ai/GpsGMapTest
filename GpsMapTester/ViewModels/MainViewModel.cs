using DevExpress.Mvvm;
using GpsMapTester.Models;
using GpsMapTester.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace GpsMapTester.ViewModels
{
    /// <summary>
    /// 메인 뷰모델
    /// - BU-353N 자동 연결/재연결 상태 표시
    /// - 실시간 Lat/Lng 표시
    /// - 지도 중심(CenterLatitude/Longitude)을 GPS 위치로 갱신 → 뷰가 follow
    /// - 마커 라벨(RegionLabel): 기본은 좌표, IReverseGeocoder 교체 시 시/군/구
    /// </summary>
    public class MainViewModel : ViewModelBase, IDisposable
    {
        // ─── 고정 설정 ────────────────────────────────────────────────────
        /// <summary>기본 중심 좌표: 서울시청</summary>
        public const double DefaultLat = 37.5666;
        public const double DefaultLng = 126.9784;
        /// <summary>지도 고정 배율 (거리 수준)</summary>
        public const int FixedZoom = 15;
        /// <summary>이 거리(m) 이상 이동했을 때만 역지오코딩 재조회</summary>
        private const double GeocodeMoveThresholdM = 60.0;

        private readonly GpsAutoConnectService _gps;

        // 좌표 → 시/도/군(한글) 오프라인 조회 (KOSTAT 시군구 GeoJSON point-in-polygon).
        // 미로드/미매칭 시 좌표로 폴백. (온라인 명칭 원하면 NominatimReverseGeocoder 로 교체 가능)
        private readonly IReverseGeocoder _geocoder = new KoreaRegionLookup();

        private readonly Dispatcher _ui;
        private double _lastGeocodedLat, _lastGeocodedLng;
        private bool _hasGeocodeAnchor;
        private bool _disposed;

        public MainViewModel()
        {
            _ui = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            // 초기 표시값
            ConnectionState      = GpsConnectionState.Disconnected;
            ConnectionStatusText = "대기 중 · 시작하면 자동 연결";
            LatitudeText         = "--";
            LongitudeText        = "--";
            FixText              = "No Fix";
            SatelliteCountText   = "0";
            SpeedText            = "--";
            HasFix               = false;
            CenterLatitude       = DefaultLat;
            CenterLongitude      = DefaultLng;
            RegionLabel          = FormatCoordLabel(DefaultLat, DefaultLng);

            _gps = new GpsAutoConnectService();
            _gps.StateChanged += OnStateChanged;
            _gps.DataUpdated  += OnDataUpdated;
        }

        /// <summary>백그라운드 자동 연결 시작. (뷰 Loaded 에서 호출; 디자인타임에는 호출 금지)</summary>
        public void Start()
        {
            UpdateRegionLabel(CenterLatitude, CenterLongitude); // 기본 중심(서울시청)도 시/도/군 표시
            _gps.Start();
        }

        // ─── 바인딩 속성 ──────────────────────────────────────────────────
        /// <summary>연결 상태(enum) — 색상/트리거 바인딩용</summary>
        public GpsConnectionState ConnectionState
        {
            get => GetProperty(() => ConnectionState);
            set => SetProperty(() => ConnectionState, value);
        }

        /// <summary>연결 상태 사람이 읽는 메시지</summary>
        public string ConnectionStatusText
        {
            get => GetProperty(() => ConnectionStatusText);
            set => SetProperty(() => ConnectionStatusText, value);
        }

        /// <summary>연결됨 여부</summary>
        public bool IsConnected
        {
            get => GetProperty(() => IsConnected);
            set => SetProperty(() => IsConnected, value);
        }

        /// <summary>현재 위치 유효(Fix) 여부</summary>
        public bool HasFix
        {
            get => GetProperty(() => HasFix);
            set => SetProperty(() => HasFix, value);
        }

        public string LatitudeText
        {
            get => GetProperty(() => LatitudeText);
            set => SetProperty(() => LatitudeText, value);
        }

        public string LongitudeText
        {
            get => GetProperty(() => LongitudeText);
            set => SetProperty(() => LongitudeText, value);
        }

        public string FixText
        {
            get => GetProperty(() => FixText);
            set => SetProperty(() => FixText, value);
        }

        public string SatelliteCountText
        {
            get => GetProperty(() => SatelliteCountText);
            set => SetProperty(() => SatelliteCountText, value);
        }

        public string SpeedText
        {
            get => GetProperty(() => SpeedText);
            set => SetProperty(() => SpeedText, value);
        }

        /// <summary>지도 중심 위도 (GPS 위치로 갱신 → 뷰가 follow)</summary>
        public double CenterLatitude
        {
            get => GetProperty(() => CenterLatitude);
            set => SetProperty(() => CenterLatitude, value);
        }

        /// <summary>지도 중심 경도</summary>
        public double CenterLongitude
        {
            get => GetProperty(() => CenterLongitude);
            set => SetProperty(() => CenterLongitude, value);
        }

        /// <summary>마커 위에 표시할 텍스트 (시/군/구 또는 좌표)</summary>
        public string RegionLabel
        {
            get => GetProperty(() => RegionLabel);
            set => SetProperty(() => RegionLabel, value);
        }

        // ─── GPS 서비스 이벤트 (백그라운드 스레드 → UI) ──────────────────
        private void OnStateChanged(GpsConnectionState state, string detail)
        {
            _ui.InvokeAsync(() =>
            {
                ConnectionState      = state;
                IsConnected          = state == GpsConnectionState.Connected;
                ConnectionStatusText = detail;
                if (state != GpsConnectionState.Connected)
                    HasFix = false;
            });
        }

        private void OnDataUpdated(GpsData d)
        {
            _ui.InvokeAsync(() =>
            {
                HasFix             = d.IsValid;
                FixText            = string.IsNullOrEmpty(d.FixType) ? "No Fix" : d.FixType;
                SatelliteCountText = d.SatelliteCount.ToString();
                SpeedText          = d.IsValid ? $"{d.SpeedKmh:F1} km/h" : "--";

                if (IsUsablePosition(d.Latitude, d.Longitude))
                {
                    LatitudeText  = FormatLat(d.Latitude);
                    LongitudeText = FormatLng(d.Longitude);

                    // 지도 follow: 중심 갱신 → 뷰가 Position/마커 이동
                    CenterLatitude  = d.Latitude;
                    CenterLongitude = d.Longitude;

                    UpdateRegionLabel(d.Latitude, d.Longitude);
                }
            });
        }

        // ─── 마커 라벨(시/군/구/좌표) 갱신 ────────────────────────────────
        private void UpdateRegionLabel(double lat, double lng)
        {
            // 우선 좌표를 즉시 표시 (역지오코더가 이름을 주면 덮어씀)
            RegionLabel = FormatCoordLabel(lat, lng);

            // 충분히 이동했을 때만 역지오코딩 (Null 구현이면 즉시 null 반환 → 좌표 유지)
            if (_hasGeocodeAnchor &&
                Haversine(lat, lng, _lastGeocodedLat, _lastGeocodedLng) < GeocodeMoveThresholdM)
                return;

            _lastGeocodedLat = lat;
            _lastGeocodedLng = lng;
            _hasGeocodeAnchor = true;

            _ = GeocodeAsync(lat, lng);
        }

        private async Task GeocodeAsync(double lat, double lng)
        {
            try
            {
                string name = await _geocoder
                    .GetRegionNameAsync(lat, lng, CancellationToken.None)
                    .ConfigureAwait(false);

                if (!string.IsNullOrEmpty(name))
                    await _ui.InvokeAsync(() => RegionLabel = name);
            }
            catch { /* 역지오코딩 실패는 좌표 표시로 폴백 */ }
        }

        // ─── 포맷/계산 헬퍼 ──────────────────────────────────────────────
        private static bool IsUsablePosition(double lat, double lng)
            => !double.IsNaN(lat) && !double.IsNaN(lng)
               && !double.IsInfinity(lat) && !double.IsInfinity(lng)
               && !(Math.Abs(lat) < 1e-9 && Math.Abs(lng) < 1e-9)
               && Math.Abs(lat) <= 90.0 && Math.Abs(lng) <= 180.0;

        private static string FormatLat(double lat)
            => $"{Math.Abs(lat):F6}°  {(lat >= 0 ? "N" : "S")}";

        private static string FormatLng(double lng)
            => $"{Math.Abs(lng):F6}°  {(lng >= 0 ? "E" : "W")}";

        private static string FormatCoordLabel(double lat, double lng)
            => $"{lat:F5}, {lng:F5}";

        /// <summary>두 좌표 간 거리(m) — Haversine</summary>
        private static double Haversine(double lat1, double lng1, double lat2, double lng2)
        {
            const double R = 6371000.0;
            double dLat = ToRad(lat2 - lat1);
            double dLng = ToRad(lng2 - lng1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
                     * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static double ToRad(double deg) => deg * Math.PI / 180.0;

        // ─── IDisposable ──────────────────────────────────────────────────
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_gps != null)
            {
                _gps.StateChanged -= OnStateChanged;
                _gps.DataUpdated  -= OnDataUpdated;
                _gps.Dispose();
            }
        }
    }
}
