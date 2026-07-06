using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;

namespace TilePrefetch
{
    /// <summary>
    /// GpsMapTester 오프라인 지도용 타일 프리페치 도구.
    /// 앱과 동일한 GMap.NET 2.1.7 캐시(%LOCALAPPDATA%\GMap.NET\TileDBv5)에
    /// OpenStreetMap z15 타일을 받아 저장합니다.
    ///
    /// 사용:  dotnet run --project TilePrefetch [region ...]
    ///   region 미지정 → 전체(seoul, gyeonggi, naepo, daejeon)
    ///   예) dotnet run -- naepo         (소량 검증)
    ///       dotnet run -- seoul daejeon (일부만)
    /// 이미 캐시에 있는 타일은 건너뜁니다(재실행/이어받기 안전).
    /// </summary>
    internal static class Program
    {
        private const int Zoom = 15;            // 고정 배율
        private const int MaxConcurrency = 3;   // OSM 정책 배려: 낮은 동시성 + 429 백오프

        private sealed class Region
        {
            public string Key;
            public string Name;
            public double West, North, East, South;
        }

        // z15 오프라인 대상 영역 (bbox)
        private static readonly Region[] AllRegions =
        {
            new Region { Key = "seoul",    Name = "서울특별시", West = 126.734, North = 37.715, East = 127.269, South = 37.413 },
            new Region { Key = "gyeonggi", Name = "경기도",     West = 126.50,  North = 38.30,  East = 127.90,  South = 36.90  },
            new Region { Key = "naepo",    Name = "내포신도시", West = 126.58,  North = 36.73,  East = 126.75,  South = 36.58  },
            new Region { Key = "daejeon",  Name = "대전광역시", West = 127.25,  North = 36.50,  East = 127.56,  South = 36.18  },
        };

        private static readonly HttpClient Http = CreateHttp();

        private static HttpClient CreateHttp()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            // OSM 정책: 식별 가능한 User-Agent 필수
            h.DefaultRequestHeaders.UserAgent.ParseAdd(
                "GpsMapTester-TilePrefetch/1.0 (offline cache; contact rnd1@uptec-netzeroai.com)");
            return h;
        }

        [STAThread]
        private static int Main(string[] args) => MainAsync(args).GetAwaiter().GetResult();

        private static async Task<int> MainAsync(string[] args)
        {
            GMapImageProxy.Enable();                             // GetImageFromCache 디코드용
            GMaps.Instance.Mode   = AccessMode.ServerAndCache;
            GMapProvider.UserAgent = "GpsMapTester-TilePrefetch/1.0 (offline cache)";

            var provider = GMapProviders.OpenStreetMap;
            var cache = GMaps.Instance.PrimaryCache;
            if (cache == null)
            {
                Console.Error.WriteLine("[FATAL] GMaps.Instance.PrimaryCache 가 null 입니다.");
                return 2;
            }

            // 대상 지역
            IEnumerable<Region> targets = AllRegions;
            if (args != null && args.Length > 0)
            {
                var keys = new HashSet<string>(args.Select(a => a.ToLowerInvariant()));
                targets = AllRegions.Where(r => keys.Contains(r.Key));
            }
            var list = targets.ToList();
            if (list.Count == 0)
            {
                Console.Error.WriteLine("[ERR] 일치하는 지역이 없습니다. (seoul|gyeonggi|naepo|daejeon)");
                return 2;
            }

            string cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GMap.NET");
            Console.WriteLine($"cache dir : {cacheDir}");
            Console.WriteLine($"provider  : {provider.Name} (DbId={provider.DbId})  zoom={Zoom}  concurrency={MaxConcurrency}");
            Console.WriteLine($"regions   : {string.Join(", ", list.Select(r => r.Key))}");
            Console.WriteLine(new string('-', 68));

            int grandOk = 0, grandSkip = 0, grandFail = 0;
            var swAll = Stopwatch.StartNew();

            foreach (var r in list)
            {
                var rect = RectLatLng.FromLTRB(r.West, r.North, r.East, r.South);
                var tiles = provider.Projection.GetAreaTileList(rect, Zoom, 0);
                int total = tiles.Count;
                Console.WriteLine($"[{r.Key}] {r.Name}: {total} tiles @ z{Zoom}");

                int ok = 0, skip = 0, fail = 0, done = 0;
                var cacheLock = new object();
                var sw = Stopwatch.StartNew();

                using (var sem = new SemaphoreSlim(MaxConcurrency))
                {
                    var tasks = new List<Task>(total);
                    foreach (var t in tiles)
                    {
                        await sem.WaitAsync().ConfigureAwait(false);
                        var tile = t;
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                // 이미 캐시에 있으면 skip (실패 시 안전하게 재다운로드)
                                bool cached = false;
                                try
                                {
                                    lock (cacheLock)
                                    {
                                        var have = cache.GetImageFromCache(provider.DbId, tile, Zoom);
                                        cached = have != null;
                                        (have as IDisposable)?.Dispose();
                                    }
                                }
                                catch { cached = false; }

                                if (cached) { Interlocked.Increment(ref skip); return; }

                                byte[] bytes = await DownloadTileAsync(Zoom, tile.X, tile.Y).ConfigureAwait(false);
                                if (bytes != null && bytes.Length > 0)
                                {
                                    lock (cacheLock) { cache.PutImageToCache(bytes, provider.DbId, tile, Zoom); }
                                    Interlocked.Increment(ref ok);
                                }
                                else
                                {
                                    Interlocked.Increment(ref fail);
                                }
                            }
                            catch { Interlocked.Increment(ref fail); }
                            finally
                            {
                                sem.Release();
                                int d = Interlocked.Increment(ref done);
                                if (d % 200 == 0 || d == total)
                                {
                                    double tps = d / Math.Max(1.0, sw.Elapsed.TotalSeconds);
                                    Console.WriteLine(
                                        $"  [{r.Key}] {d}/{total} ({100.0 * d / total:F1}%)  ok={ok} skip={skip} fail={fail}  {tps:F0} t/s");
                                }
                            }
                        }));
                    }
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }

                Console.WriteLine($"[{r.Key}] DONE  ok={ok} skip={skip} fail={fail}  in {sw.Elapsed.TotalMinutes:F1} min");
                Console.WriteLine(new string('-', 68));
                grandOk += ok; grandSkip += skip; grandFail += fail;
            }

            Console.WriteLine($"ALL DONE  ok={grandOk} skip={grandSkip} fail={grandFail}  total {swAll.Elapsed.TotalMinutes:F1} min");

            // 검증: 첫 지역의 중심 타일이 실제로 캐시에서 읽히는지 확인
            var verify = list[0];
            var vrect = RectLatLng.FromLTRB(verify.West, verify.North, verify.East, verify.South);
            var vtiles = provider.Projection.GetAreaTileList(vrect, Zoom, 0);
            var mid = vtiles[vtiles.Count / 2];
            var vimg = cache.GetImageFromCache(provider.DbId, mid, Zoom);
            Console.WriteLine($"verify    : [{verify.Key}] center tile ({mid.X},{mid.Y})@z{Zoom} in cache = {(vimg != null ? "YES" : "NO")}");
            (vimg as IDisposable)?.Dispose();

            return grandFail > 0 && grandOk == 0 ? 1 : 0;
        }

        private static async Task<byte[]> DownloadTileAsync(int z, long x, long y)
        {
            string url = $"https://tile.openstreetmap.org/{z}/{x}/{y}.png";
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    using (var resp = await Http.GetAsync(url).ConfigureAwait(false))
                    {
                        int code = (int)resp.StatusCode;
                        if (code == 200)
                            return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

                        if (code == 429 || code >= 500)
                        {
                            await Task.Delay(600 * (attempt + 1) * (attempt + 1)).ConfigureAwait(false); // 백오프
                            continue;
                        }
                        return null; // 404 등 → 더 이상 시도 안 함
                    }
                }
                catch
                {
                    await Task.Delay(400 * (attempt + 1)).ConfigureAwait(false);
                }
            }
            return null;
        }
    }
}
