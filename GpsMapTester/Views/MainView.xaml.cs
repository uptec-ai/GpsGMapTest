using DevExpress.Xpf.Map;
using GpsMapTester.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpsMapTester.Views
{
    /// <summary>
    /// DevExpress Map 기반 지도 뷰.
    /// - OSM 래스터 타일
    /// - 배율 12 고정 (사용자 줌/스크롤 잠금) · 좌표 중앙 고정(follow)
    /// - GPS 위치를 지도 중심 + 마커로 표시, 마커 텍스트 = 시/도/군
    /// </summary>
    public partial class MainView : UserControl
    {
        private const double FixedZoom = 12;
        private MainViewModel _vm;
        private MapCustomElement _marker;

        public MainView()
        {
            InitializeComponent();
            Loaded   += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DesignerProperties.GetIsInDesignMode(this))
                return;

            _vm = DataContext as MainViewModel;
            if (_vm == null) return;

            _vm.PropertyChanged += OnViewModelPropertyChanged;

            ConfigureMap();
            _vm.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnViewModelPropertyChanged;
                _vm.Dispose();
                _vm = null;
            }
        }

        // ─── 지도 설정 ───────────────────────────────────────────────────
        private void ConfigureMap()
        {
            // OSM 래스터 레이어를 코드에서 생성 → VS 디자이너는 이 코드를 실행하지 않으므로
            // 디자인타임에 OSM 요청/차단 이미지가 뜨지 않는다(런타임에만 타일 로드).
            // OSM 정책상 유효 User-Agent 필수(없으면 "Access blocked" 반환).
            var osm = new OpenStreetMapDataProvider();
            osm.WebRequest += (s, e) =>
                e.UserAgent = "GpsMapTester/1.0 (gps map tester; contact rnd1@uptec-netzeroai.com)";
            Map.Layers.Insert(0, new ImageLayer { DataProvider = osm }); // 마커 레이어 아래(바탕)

            // 배율 12 고정 + 사용자 조작 잠금(좌표 중앙 고정 follow 전용)
            Map.MinZoomLevel = FixedZoom;
            Map.MaxZoomLevel = FixedZoom;
            Map.ZoomLevel    = FixedZoom;
            Map.EnableZooming  = false;
            Map.EnableScrolling = false;
            Map.EnableRotation  = false;

            var center = new GeoPoint(_vm.CenterLatitude, _vm.CenterLongitude);
            Map.CenterPoint = center;

            _marker = new MapCustomElement
            {
                Location        = center,
                Content         = _vm.RegionLabel,
                ContentTemplate = (System.Windows.DataTemplate)Resources["MarkerLabelTemplate"]
            };
            MarkerStorage.Items.Add(_marker);

            if (ZoomLabel != null)
                ZoomLabel.Text = $"배율(Zoom) {FixedZoom:0} 고정 · OSM";
        }

        // ─── 뷰모델 → 지도 반영 ──────────────────────────────────────────
        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.CenterLatitude):
                case nameof(MainViewModel.CenterLongitude):
                    ApplyCenter();
                    break;
                case nameof(MainViewModel.RegionLabel):
                    ApplyLabel();
                    break;
            }
        }

        private void ApplyCenter()
        {
            if (_vm == null) return;
            var p = new GeoPoint(_vm.CenterLatitude, _vm.CenterLongitude);
            Map.CenterPoint = p;          // follow: 지도 중앙 고정 이동
            if (_marker != null)
                _marker.Location = p;     // 마커 이동
        }

        private void ApplyLabel()
        {
            if (_vm != null && _marker != null)
                _marker.Content = _vm.RegionLabel; // 마커 라벨 = 시/도/군
        }
    }
}
