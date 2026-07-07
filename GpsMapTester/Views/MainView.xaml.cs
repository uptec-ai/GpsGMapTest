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
        private MapPushpin _marker;

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
            // 배율 12 고정 + 사용자 조작 잠금(좌표 중앙 고정 follow 전용)
            Map.MinZoomLevel = FixedZoom;
            Map.MaxZoomLevel = FixedZoom;
            Map.ZoomLevel    = FixedZoom;
            Map.EnableZooming  = false;
            Map.EnableScrolling = false;
            Map.EnableRotation  = false;

            var center = new GeoPoint(_vm.CenterLatitude, _vm.CenterLongitude);
            Map.CenterPoint = center;

            _marker = new MapPushpin
            {
                Location  = center,
                Text      = _vm.RegionLabel,
                Brush     = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)),
                TextBrush = Brushes.White
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
                _marker.Text = _vm.RegionLabel; // 마커 텍스트 = 시/도/군
        }
    }
}
