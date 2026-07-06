using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
using GpsMapTester.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GpsMapTester.Views
{
    /// <summary>
    /// GMap follow 지도 뷰.
    /// - OSM 라스터 타일 + SQLite 캐시(오프라인)
    /// - 고정 배율(FixedZoom)
    /// - GPS 위치를 지도 중심 + 마커로 표시하고, 위치 변경 시 follow
    /// - 마커 위 텍스트 = 시/군/구(또는 좌표)
    /// </summary>
    public partial class MainView : UserControl
    {
        private MainViewModel _vm;
        private GMapMarker _marker;
        private FrameworkElement _markerShape;
        private TextBlock _markerLabel;

        public MainView()
        {
            InitializeComponent();
            Loaded   += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 디자이너/디자인타임에서는 지도·시리얼 초기화 금지
            if (DesignerProperties.GetIsInDesignMode(this))
                return;

            _vm = DataContext as MainViewModel;
            if (_vm == null) return;

            ConfigureMap();
            BuildMarker();

            _vm.PropertyChanged += OnViewModelPropertyChanged;

            // 초기 중심/라벨 반영 후 백그라운드 자동 연결 시작
            ApplyCenter();
            ApplyLabel();
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
            // 오프라인 우선: 캐시에 있으면 캐시, 없으면 서버(온라인 시). 완전 오프라인이면 캐시만 표출.
            GMaps.Instance.Mode = AccessMode.ServerAndCache;

            // OSM 정책상 식별용 User-Agent 지정
            GMapProvider.UserAgent =
                "GpsMapTester/1.0 (offline gps map tester; contact rnd1@uptec-netzeroai.com)";

            Map.MapProvider = GMapProviders.OpenStreetMap;

            // 고정 배율
            Map.MinZoom = MainViewModel.FixedZoom;
            Map.MaxZoom = MainViewModel.FixedZoom;
            Map.Zoom    = MainViewModel.FixedZoom;

            Map.MouseWheelZoomEnabled = false;
            Map.CanDragMap            = false; // follow 전용 (사용자 팬 잠금)

            Map.Position = new PointLatLng(_vm.CenterLatitude, _vm.CenterLongitude);

            if (ZoomLabel != null)
                ZoomLabel.Text = $"배율(Zoom) {MainViewModel.FixedZoom} · OSM";
        }

        // ─── 마커(위치 점 + 라벨) ────────────────────────────────────────
        private void BuildMarker()
        {
            // 라벨 (시/군/구 또는 좌표)
            _markerLabel = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 3, 8, 3),
                TextAlignment = TextAlignment.Center
            };
            var labelBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x24, 0x29)),
                CornerRadius = new CornerRadius(6),
                Child = _markerLabel,
                HorizontalAlignment = HorizontalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.5
                }
            };

            // 위치 핀 (teardrop, 팁이 하단 중앙 = 실제 좌표)
            var pin = new Path
            {
                Data = System.Windows.Media.Geometry.Parse(
                    "M 12,0 C 5.4,0 0,5.4 0,12 C 0,21 12,32 12,32 C 12,32 24,21 24,12 C 24,5.4 18.6,0 12,0 Z"),
                Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)),
                Stroke = Brushes.White,
                StrokeThickness = 2,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var pinDot = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var pinGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            pinGrid.Children.Add(pin);
            pinGrid.Children.Add(pinDot);

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(labelBorder);
            stack.Children.Add(new FrameworkElement { Height = 4 }); // 간격
            stack.Children.Add(pinGrid);

            _markerShape = stack;
            _markerShape.SizeChanged += (s, e) => UpdateMarkerOffset();

            _marker = new GMapMarker(new PointLatLng(_vm.CenterLatitude, _vm.CenterLongitude))
            {
                Shape = _markerShape,
                ZIndex = 100
            };
            Map.Markers.Add(_marker);
            UpdateMarkerOffset();
        }

        /// <summary>핀 팁(하단 중앙)이 좌표에 오도록 오프셋 조정</summary>
        private void UpdateMarkerOffset()
        {
            if (_marker == null || _markerShape == null) return;
            double w = _markerShape.ActualWidth;
            double h = _markerShape.ActualHeight;
            if (w <= 0 || h <= 0) return;
            _marker.Offset = new System.Windows.Point(-w / 2.0, -h);
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
            var p = new PointLatLng(_vm.CenterLatitude, _vm.CenterLongitude);
            Map.Position = p;          // follow: 지도 recenter
            if (_marker != null)
                _marker.Position = p;  // 마커 이동
        }

        private void ApplyLabel()
        {
            if (_vm != null && _markerLabel != null)
            {
                _markerLabel.Text = _vm.RegionLabel;
                UpdateMarkerOffset();
            }
        }
    }
}
