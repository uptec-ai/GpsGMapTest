using DevExpress.Xpf.Map;
using GpsMapTester.Services;
using GpsMapTester.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace GpsMapTester.Views
{
    /// <summary>
    /// DevExpress Map 기반 지도 뷰 (완전 오프라인 래스터).
    /// - 로컬 타일 폴더(Maps\tiles\{z}\{x}\{y}.png)를 file:// 로 표출 (네트워크 사용 안 함)
    /// - 배율 고정(우하단 콤보에서 15/12/10/8 선택) · 좌표 중앙 고정(follow, XAML 바인딩)
    /// - GPS 위치를 지도 중심 + 마커로 표시, 마커 텍스트 = 시/도/군
    /// </summary>
    public partial class MainView : UserControl
    {
        private const double DefaultZoom = 12; // 시작 배율(콤보 기본 선택). 콤보에서 15/12/10/8 로 변경.
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
            // 완전 오프라인 래스터: 로컬 타일 폴더(Maps\tiles\{z}\{x}\{y}.png)를 file:// 로 읽는다.
            // 검증된 OSM 제공자 렌더 경로를 그대로 사용(네트워크 0). 코드에서 레이어 생성 → 디자이너엔 안 뜸.
            string tilesDir = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Maps", "tiles");
            if (System.IO.Directory.Exists(tilesDir))
            {
                string baseUri = new System.Uri(tilesDir + System.IO.Path.DirectorySeparatorChar).AbsoluteUri;
                var provider = new OpenStreetMapDataProvider
                {
                    TileUriTemplate = baseUri + "{tileLevel}/{tileX}/{tileY}.png"
                };
                Map.Layers.Insert(0, new ImageLayer { DataProvider = provider });
            }

            // 중심(초기 + follow)은 XAML CenterPoint 바인딩이 담당(애니메이션 방지). 여기선 설정하지 않는다.

            // 사용자 휠 줌은 잠금(배율은 콤보로만 변경) + 마우스 드래그(패닝) 허용
            Map.EnableZooming   = false; // 휠 줌 잠금
            Map.EnableScrolling = true;  // 마우스 드래그로 지도 이동
            Map.EnableRotation  = false;

            _marker = new MapCustomElement
            {
                Location        = new GeoPoint(_vm.CenterLatitude, _vm.CenterLongitude),
                Content         = _vm.RegionLabel,
                ContentTemplate = (System.Windows.DataTemplate)Resources["MarkerLabelTemplate"]
            };
            MarkerStorage.Items.Add(_marker);

            // 배율 콤보 기본 선택(=DefaultZoom) → SelectionChanged → ApplyZoom 호출
            SelectZoom(DefaultZoom);
        }

        // ─── 배율(줌) 선택 ───────────────────────────────────────────────
        private void ZoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ZoomCombo?.SelectedItem is ComboBoxItem item &&
                double.TryParse(item.Content?.ToString(), out double z))
                ApplyZoom(z);
        }

        /// <summary>콤보에서 지정 배율 항목을 선택. 선택되면 SelectionChanged→ApplyZoom 이 호출됨.</summary>
        private void SelectZoom(double zoom)
        {
            if (ZoomCombo == null) return;
            string target = ((int)zoom).ToString();
            foreach (ComboBoxItem it in ZoomCombo.Items)
            {
                if (it.Content?.ToString() != target) continue;
                if (ReferenceEquals(ZoomCombo.SelectedItem, it))
                    ApplyZoom(zoom);            // 이미 선택돼 SelectionChanged 안 뜰 때 대비
                else
                    ZoomCombo.SelectedItem = it; // 변경 → SelectionChanged → ApplyZoom
                return;
            }
        }

        /// <summary>
        /// 고정 배율 적용: Min=Max=Zoom 으로 잠그고 라벨 갱신.
        /// 해당 배율 타일 폴더(Maps\tiles\{z})가 출력폴더에 없으면 라벨에 경고 표시.
        /// </summary>
        private void ApplyZoom(double zoom)
        {
            if (Map == null) return;

            // Min>Max 충돌 없이 안전하게 변경: 범위를 넓힌 뒤 값 설정하고 다시 잠금.
            Map.MinZoomLevel = 1;
            Map.MaxZoomLevel = 25;
            Map.ZoomLevel    = zoom;
            Map.MinZoomLevel = zoom;
            Map.MaxZoomLevel = zoom;

            bool hasTiles = System.IO.Directory.Exists(System.IO.Path.Combine(
                System.AppDomain.CurrentDomain.BaseDirectory, "Maps", "tiles", ((int)zoom).ToString()));

            if (ZoomLabel != null)
                ZoomLabel.Text = hasTiles
                    ? $"고정 · 오프라인"
                    : $"⚠ z{zoom:0} 타일 없음";
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
            // 지도 중심은 XAML CenterPoint 바인딩이 자동 반영. 여기선 마커만 이동.
            if (_vm != null && _marker != null)
                _marker.Location = new GeoPoint(_vm.CenterLatitude, _vm.CenterLongitude);
        }

        private void ApplyLabel()
        {
            if (_vm != null && _marker != null)
                _marker.Content = _vm.RegionLabel; // 마커 라벨 = 시/도/군
        }

        // ─── 더블클릭 → 현재 마커(GPS 좌표)로 복귀 ───────────────────────
        private void Map_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return; // 더블클릭만 처리(단일 클릭 드래그는 그대로)

            // CenterPoint 은 VM(CenterLatitude/Longitude)에 OneWay 바인딩.
            // 값을 직접 대입하면 바인딩이 끊기므로, 바인딩을 강제 재평가해서
            // 드래그로 벗어난 지도를 현재 VM 좌표(=마커 위치)로 재중심한다. follow 유지.
            BindingOperations.GetBindingExpressionBase(Map, MapControl.CenterPointProperty)?.UpdateTarget();
            e.Handled = true; // 더블클릭 시 팬 시작 방지
        }
    }
}
