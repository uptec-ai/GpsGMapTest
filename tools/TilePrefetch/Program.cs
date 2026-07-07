using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
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
    /// ⚠ OSM 타일 이용정책: 대량/고속 다운로드 금지. 이 도구는 저속(낮은 동시성 + 요청 간격)으로
    ///   동작하며, OSM 이 반환하는 "Access blocked" 차단 이미지를 해시로 감지해 캐시하지 않고,
    ///   차단이 반복되면 자동 중단합니다.
    ///
    /// 사용:  dotnet run -c Release --project TilePrefetch [region ...]
    ///   region 미지정 → 전체(seoul, gyeonggi, naepo, daejeon)
    ///   test = 서울 도심 소량(검증용)
    ///   이미 캐시에 있는 타일은 건너뜁니다(재실행/이어받기 안전).
    /// </summary>
    internal static class Program
    {
        private const int Zoom = 15;                 // 고정 배율
        private const int MaxConcurrency = 2;        // OSM 배려: 낮은 동시성
        private const int ThrottleMs = 150;          // 각 요청 뒤 지연(worker당) → 전체 ~10 req/s 내외
        private const int BlockAbortThreshold = 15;  // 차단 이미지 이만큼 감지되면 전체 중단

        // OSM "Access blocked" 차단 이미지의 SHA-256 (감지용)
        private const string BlockSha256 = "b02c44252dac5a5e820ecef1e9bf9200e9407c042df668a466a1aa81a9ecca7a";

        private sealed class Region
        {
            public string Key;
            public string Name;
            public double West, North, East, South;
        }

        private static readonly Region[] AllRegions =
        {
            new Region { Key = "seoul",    Name = "서울특별시", West = 126.734, North = 37.715, East = 127.269, South = 37.413 },
            new Region { Key = "gyeonggi", Name = "경기도",     West = 126.50,  North = 38.30,  East = 127.90,  South = 36.90  },
            new Region { Key = "naepo",    Name = "내포신도시", West = 126.58,  North = 36.73,  East = 126.75,  South = 36.58  },
            new Region { Key = "daejeon",  Name = "대전광역시", West = 127.25,  North = 36.50,  East = 127.56,  South = 36.18  },
            // 검증용 소량(서울 도심, 시청 주변)
            new Region { Key = "test",     Name = "서울도심(테스트)", West = 126.94, North = 37.60, East = 127.02, South = 37.53 },
        };

        private static readonly HttpClient Http = CreateHttp();
        private static int _blocked;      // 감지된 차단 이미지 수
        private static volatile bool _abort;

        private static HttpClient CreateHttp()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd(
                "GpsMapTester-TilePrefetch/1.0 (offline cache; contact rnd1@uptec-netzeroai.com)");
            return h;
        }

        [STAThread]
        private static int Main(string[] args) => MainAsync(args).GetAwaiter().GetResult();

        private static async Task<int> MainAsync(string[] args)
        {
            GMapImageProxy.Enable();
            GMaps.Instance.Mode    = AccessMode.ServerAndCache;
            GMapProvider.UserAgent = "GpsMapTester-TilePrefetch/1.0 (offline cache)";

            var provider = GMapProviders.OpenStreetMap;
            var cache = GMaps.Instance.PrimaryCache;
            if (cache == null) { Console.Error.WriteLine("[FATAL] PrimaryCache null"); return 2; }

            IEnumerable<Region> targets = AllRegions.Where(r => r.Key != "test"); // 기본: 실지역 4개
            if (args != null && args.Length > 0)
            {
                var keys = new HashSet<string>(args.Select(a => a.ToLowerInvariant()));
                targets = AllRegions.Where(r => keys.Contains(r.Key));
            }
            var list = targets.ToList();
            if (list.Count == 0) { Console.Error.WriteLine("[ERR] no region (seoul|gyeonggi|naepo|daejeon|test)"); return 2; }

            Console.WriteLine($"provider={provider.Name}(DbId={provider.DbId}) zoom={Zoom} concurrency={MaxConcurrency} throttle={ThrottleMs}ms");
            Console.WriteLine($"regions : {string.Join(", ", list.Select(r => r.Key))}");
            Console.WriteLine(new string('-', 68));

            int grandOk = 0, grandSkip = 0, grandFail = 0;
            var swAll = Stopwatch.StartNew();

            foreach (var r in list)
            {
                if (_abort) break;
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
                        if (_abort) break;
                        await sem.WaitAsync().ConfigureAwait(false);
                        var tile = t;
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
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

                                var (bytes, blocked) = await DownloadTileAsync(Zoom, tile.X, tile.Y).ConfigureAwait(false);
                                if (blocked)
                                {
                                    int b = Interlocked.Increment(ref _blocked);
                                    if (b >= BlockAbortThreshold) _abort = true;
                                    Interlocked.Increment(ref fail);
                                }
                                else if (bytes != null && bytes.Length > 0)
                                {
                                    lock (cacheLock) { cache.PutImageToCache(bytes, provider.DbId, tile, Zoom); }
                                    Interlocked.Increment(ref ok);
                                }
                                else Interlocked.Increment(ref fail);

                                if (ThrottleMs > 0) await Task.Delay(ThrottleMs).ConfigureAwait(false);
                            }
                            catch { Interlocked.Increment(ref fail); }
                            finally
                            {
                                sem.Release();
                                int d = Interlocked.Increment(ref done);
                                if (d % 100 == 0 || d == total)
                                {
                                    double tps = d / Math.Max(1.0, sw.Elapsed.TotalSeconds);
                                    Console.WriteLine($"  [{r.Key}] {d}/{total} ({100.0*d/total:F1}%) ok={ok} skip={skip} fail={fail} blocked={_blocked} {tps:F0} t/s");
                                }
                            }
                        }));
                    }
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }

                Console.WriteLine($"[{r.Key}] DONE ok={ok} skip={skip} fail={fail} in {sw.Elapsed.TotalMinutes:F1} min");
                Console.WriteLine(new string('-', 68));
                grandOk += ok; grandSkip += skip; grandFail += fail;
                if (_abort) { Console.WriteLine("[ABORT] OSM 차단 이미지 반복 감지 → 중단. 잠시 후(수십 분) 저속으로 재시도하세요."); break; }
            }

            Console.WriteLine($"ALL DONE ok={grandOk} skip={grandSkip} fail={grandFail} blocked={_blocked} total {swAll.Elapsed.TotalMinutes:F1} min");
            if (_blocked > 0)
                Console.WriteLine($"[WARN] 차단 이미지 {_blocked}건은 캐시하지 않았습니다. OSM 정책상 대량 다운로드는 제한됩니다.");
            return _abort ? 3 : 0;
        }

        /// <summary>다운로드. 반환: (bytes, blocked). blocked=true 면 OSM 차단 이미지.</summary>
        private static async Task<(byte[] bytes, bool blocked)> DownloadTileAsync(int z, long x, long y)
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
                        {
                            var data = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            if (IsBlockImage(data)) return (null, true);   // 차단 이미지 → 캐시 금지
                            return (data, false);
                        }
                        if (code == 429 || code == 403 || code >= 500)
                        {
                            // 차단/과부하 → 백오프
                            await Task.Delay(800 * (attempt + 1) * (attempt + 1)).ConfigureAwait(false);
                            if (code == 403) return (null, true);
                            continue;
                        }
                        return (null, false); // 404 등
                    }
                }
                catch
                {
                    await Task.Delay(400 * (attempt + 1)).ConfigureAwait(false);
                }
            }
            return (null, false);
        }

        private static bool IsBlockImage(byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            using (var sha = SHA256.Create())
            {
                string hex = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
                return hex == BlockSha256;
            }
        }
    }
}
