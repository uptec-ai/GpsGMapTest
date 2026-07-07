using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GpsMapTester.Services
{
    /// <summary>
    /// 좌표(lat/lng) → "시도 시군구"(한글) 오프라인 조회.
    /// KOSTAT 시군구 GeoJSON(Maps\skorea_muni.json)을 읽어 point-in-polygon 으로 판별한다.
    /// - name: 시군구 한글명(예: 종로구)
    /// - code 앞 2자리: 시도 코드 → 시도 한글명
    /// 네트워크/역지오코딩 API 불필요(완전 오프라인). IReverseGeocoder 로 뷰모델에 주입.
    /// </summary>
    public sealed class KoreaRegionLookup : IReverseGeocoder
    {
        private sealed class Poly
        {
            public double[] OX, OY;               // 외곽 링
            public List<double[]> HX, HY;          // 홀(구멍) 링들
        }
        private sealed class Region
        {
            public string Sido, Sigungu;
            public double MinX, MinY, MaxX, MaxY;
            public List<Poly> Polys;
        }

        // 이 KOSTAT 시군구 GeoJSON 의 시도 코드 체계(비표준, code 앞 2자리)
        private static readonly Dictionary<string, string> SidoByCode = new Dictionary<string, string>
        {
            {"11","서울특별시"}, {"21","부산광역시"}, {"22","대구광역시"}, {"23","인천광역시"},
            {"24","광주광역시"}, {"25","대전광역시"}, {"26","울산광역시"}, {"29","세종특별자치시"},
            {"31","경기도"},     {"32","강원특별자치도"}, {"33","충청북도"}, {"34","충청남도"},
            {"35","전북특별자치도"}, {"36","전라남도"}, {"37","경상북도"}, {"38","경상남도"}, {"39","제주특별자치도"},
        };

        private readonly string _path;
        private readonly object _lock = new object();
        private volatile List<Region> _regions;
        private bool _loadStarted;

        public KoreaRegionLookup(string dataPath = null)
        {
            _path = dataPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Maps", "skorea_muni.json");
            // 시작과 동시에 백그라운드 로드(첫 조회 지연 방지)
            Task.Run(() => EnsureLoaded());
        }

        /// <summary>IReverseGeocoder: 백그라운드 스레드에서 조회(무거운 파싱이 UI를 막지 않도록).</summary>
        public Task<string> GetRegionNameAsync(double latitude, double longitude, CancellationToken ct)
            => Task.Run(() => Lookup(latitude, longitude), ct);

        /// <summary>좌표 → "시도 시군구". 미로드/미매칭 시 null.</summary>
        public string Lookup(double lat, double lng)
        {
            EnsureLoaded();
            var regs = _regions;
            if (regs == null) return null;

            double x = lng, y = lat; // GeoJSON 좌표는 [lng, lat]
            foreach (var r in regs)
            {
                if (x < r.MinX || x > r.MaxX || y < r.MinY || y > r.MaxY) continue;
                foreach (var p in r.Polys)
                {
                    if (!InRing(p.OX, p.OY, x, y)) continue;
                    bool inHole = false;
                    if (p.HX != null)
                        for (int i = 0; i < p.HX.Count; i++)
                            if (InRing(p.HX[i], p.HY[i], x, y)) { inHole = true; break; }
                    if (!inHole)
                        return string.IsNullOrEmpty(r.Sido) ? r.Sigungu : r.Sido + " " + r.Sigungu;
                }
            }
            return null;
        }

        // 광선 투사(ray casting) point-in-polygon
        private static bool InRing(double[] xs, double[] ys, double x, double y)
        {
            bool inside = false;
            int n = xs.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if (((ys[i] > y) != (ys[j] > y)) &&
                    (x < (xs[j] - xs[i]) * (y - ys[i]) / (ys[j] - ys[i]) + xs[i]))
                    inside = !inside;
            }
            return inside;
        }

        private void EnsureLoaded()
        {
            if (_regions != null) return;
            lock (_lock)
            {
                if (_regions != null || _loadStarted) return;
                _loadStarted = true;
            }
            try
            {
                if (!File.Exists(_path)) return;
                var root = JObject.Parse(File.ReadAllText(_path));
                var feats = (JArray)root["features"];
                var list = new List<Region>(feats.Count);

                foreach (var f in feats)
                {
                    var props = f["properties"];
                    string name = (string)props?["name"];
                    string code = (string)props?["code"];
                    string sido = null;
                    if (!string.IsNullOrEmpty(code) && code.Length >= 2)
                        SidoByCode.TryGetValue(code.Substring(0, 2), out sido);

                    var geom = f["geometry"];
                    string gtype = (string)geom?["type"];
                    var coords = geom?["coordinates"] as JArray;
                    if (coords == null) continue;

                    var polys = new List<Poly>();
                    if (gtype == "Polygon") AddPolygon(coords, polys);
                    else if (gtype == "MultiPolygon")
                        foreach (var pc in coords) AddPolygon((JArray)pc, polys);
                    if (polys.Count == 0) continue;

                    var reg = new Region
                    {
                        Sido = sido, Sigungu = name, Polys = polys,
                        MinX = double.MaxValue, MinY = double.MaxValue,
                        MaxX = double.MinValue, MaxY = double.MinValue
                    };
                    foreach (var p in polys)
                        for (int i = 0; i < p.OX.Length; i++)
                        {
                            double xx = p.OX[i], yy = p.OY[i];
                            if (xx < reg.MinX) reg.MinX = xx;
                            if (xx > reg.MaxX) reg.MaxX = xx;
                            if (yy < reg.MinY) reg.MinY = yy;
                            if (yy > reg.MaxY) reg.MaxY = yy;
                        }
                    list.Add(reg);
                }
                _regions = list;
            }
            catch
            {
                _regions = null;
            }
        }

        // polygon = [ ring0(외곽), ring1(홀), ... ], ring = [[lng,lat], ...]
        private static void AddPolygon(JArray polygon, List<Poly> outp)
        {
            if (polygon == null || polygon.Count == 0) return;
            var poly = new Poly();
            for (int r = 0; r < polygon.Count; r++)
            {
                var ring = (JArray)polygon[r];
                int n = ring.Count;
                var xs = new double[n];
                var ys = new double[n];
                for (int i = 0; i < n; i++)
                {
                    var pt = (JArray)ring[i];
                    xs[i] = (double)pt[0];
                    ys[i] = (double)pt[1];
                }
                if (r == 0) { poly.OX = xs; poly.OY = ys; }
                else
                {
                    if (poly.HX == null) { poly.HX = new List<double[]>(); poly.HY = new List<double[]>(); }
                    poly.HX.Add(xs); poly.HY.Add(ys);
                }
            }
            if (poly.OX != null) outp.Add(poly);
        }
    }
}
