using GMap.NET;
using GMap.NET.MapProviders;
using System;
using System.Data.SQLite;
using System.Reflection;

namespace GpsMapTester.Services
{
    /// <summary>
    /// 로컬 MBTiles(SQLite) 파일에서 래스터 타일을 읽는 오프라인 전용 GMap 프로바이더.
    /// - 네트워크/외부 서버를 전혀 사용하지 않음 → OSM 정책·차단과 무관.
    /// - MBTiles 스펙: 테이블 tiles(zoom_level, tile_column, tile_row, tile_data),
    ///   좌표는 TMS(원점이 남서쪽, Y가 뒤집힘)이므로 XYZ→TMS 변환 필요.
    /// </summary>
    public class LocalMBTilesProvider : GMapProvider
    {
        private static readonly Guid ProviderId = new Guid("9E0D1C2A-1111-4A2B-9C3D-0F1E2D3C4B5A");

        private readonly SQLiteConnection _con;
        private readonly object _lock = new object();
        private GMapProvider[] _overlays;

        public string DisplayName { get; }

        public LocalMBTilesProvider(string mbtilesPath)
        {
            DisplayName = "LocalMBTiles";
            _con = new SQLiteConnection($"Data Source={mbtilesPath};Version=3;Read Only=True;");
            _con.Open();
            MaxZoom = null;               // MBTiles 안에 있는 줌만 표출
            Copyright = "© OpenStreetMap contributors";
        }

        public override Guid Id => ProviderId;
        public override string Name => "LocalMBTiles";
        public override PureProjection Projection => GMapProviders.OpenStreetMap.Projection; // 표준 Web Mercator
        public override GMapProvider[] Overlays => _overlays ?? (_overlays = new GMapProvider[] { this });

        // GMapProvider.TileImageProxy 는 internal static 이라 앱 어셈블리에서 직접 접근 불가.
        // GMapImageProxy.Enable() 이 채워둔 프록시를 리플렉션으로 얻어 사용한다.
        private static readonly FieldInfo ProxyField = typeof(GMapProvider).GetField(
            "TileImageProxy", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        private static MethodInfo _fromArray;

        public override PureImage GetTileImage(GPoint pos, int zoom)
        {
            byte[] data = ReadTile(zoom, (int)pos.X, (int)pos.Y);
            if (data == null || data.Length == 0) return null;

            object proxy = ProxyField?.GetValue(null);
            if (proxy == null) return null;
            if (_fromArray == null)
                _fromArray = proxy.GetType().GetMethod("FromArray", new[] { typeof(byte[]) });
            return _fromArray?.Invoke(proxy, new object[] { data }) as PureImage;
        }

        private byte[] ReadTile(int zoom, int x, int y)
        {
            // XYZ(Google/OSM) → TMS(MBTiles) : row 뒤집기
            int tmsY = (1 << zoom) - 1 - y;
            try
            {
                lock (_lock)
                {
                    using (var cmd = _con.CreateCommand())
                    {
                        cmd.CommandText =
                            "SELECT tile_data FROM tiles WHERE zoom_level=@z AND tile_column=@x AND tile_row=@y LIMIT 1";
                        cmd.Parameters.AddWithValue("@z", zoom);
                        cmd.Parameters.AddWithValue("@x", x);
                        cmd.Parameters.AddWithValue("@y", tmsY);
                        return cmd.ExecuteScalar() as byte[];
                    }
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
