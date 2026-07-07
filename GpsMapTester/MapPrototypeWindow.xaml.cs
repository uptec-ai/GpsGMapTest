using DevExpress.Xpf.Map;
using System;
using System.IO;
using System.Windows;

namespace GpsMapTester
{
    /// <summary>
    /// DevExpress Map + Shapefile(벡터) 오프라인 지도 프로토타입.
    /// GMap+MBTiles+Docker 스택 없이, 무료 한국 shapefile 하나로 완전 오프라인 지도를 표출한다.
    /// </summary>
    public partial class MapPrototypeWindow : Window
    {
        public MapPrototypeWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            string shpDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Maps", "shp");
            AdSigungu.FileUri = new Uri(Path.Combine(shpDir, "gadm41_KOR_2.shp"));

            // 서울시청 중심 + 현재 위치 마커 (GPS 연동 시 이 좌표만 갱신하면 follow)
            var seoul = new GeoPoint(37.5666, 126.9784);
            Map.CenterPoint = seoul;
            Map.ZoomLevel   = 14;

            MarkerStorage.Items.Add(new MapPushpin
            {
                Location = seoul,
                Text = "서울시청"
            });
        }
    }
}
