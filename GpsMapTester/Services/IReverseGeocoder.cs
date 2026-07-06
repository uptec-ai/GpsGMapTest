using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GpsMapTester.Services
{
    /// <summary>
    /// 좌표(lat/lng) → 행정구역명(시/군/구) 변환 훅.
    /// 기본 구현(<see cref="NullReverseGeocoder"/>)은 아무 것도 하지 않으며,
    /// 이 경우 뷰모델은 좌표 문자열을 마커 라벨로 사용합니다.
    /// 온라인 역지오코딩이 필요하면 <see cref="NominatimReverseGeocoder"/> 로 교체하세요.
    /// </summary>
    public interface IReverseGeocoder
    {
        /// <summary>
        /// 좌표에 해당하는 시/군/구 명칭을 반환합니다.
        /// 조회 실패 또는 미지원 시 null/빈 문자열을 반환하며,
        /// 호출측은 이때 좌표를 대신 표시합니다.
        /// </summary>
        Task<string> GetRegionNameAsync(double latitude, double longitude, CancellationToken ct);
    }

    /// <summary>
    /// 기본(no-op) 역지오코더. 항상 null을 반환하여 좌표 표시로 폴백시킵니다.
    /// 오프라인 환경에서 네트워크 호출 없이 동작하는 안전한 기본값입니다.
    /// </summary>
    public sealed class NullReverseGeocoder : IReverseGeocoder
    {
        public Task<string> GetRegionNameAsync(double latitude, double longitude, CancellationToken ct)
            => Task.FromResult<string>(null);
    }

    /// <summary>
    /// OSM Nominatim 역지오코딩 구현 (온라인 전용, 훅 교체용).
    /// 사용 시 뷰모델에서 <c>_geocoder = new NominatimReverseGeocoder();</c> 로 바꾸면 됩니다.
    /// 주의: Nominatim 이용 정책상 초당 1회 이하 호출, 식별용 User-Agent 필수.
    ///      오프라인(인터넷 없음) 환경에서는 조용히 null로 폴백합니다.
    /// </summary>
    public sealed class NominatimReverseGeocoder : IReverseGeocoder
    {
        public async Task<string> GetRegionNameAsync(double lat, double lng, CancellationToken ct)
        {
            try
            {
                string url =
                    $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=10&accept-language=ko" +
                    $"&lat={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                    $"&lon={lng.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

                string json;
                using (var wc = new WebClient())
                {
                    // Nominatim 정책: 식별 가능한 User-Agent 필수
                    wc.Headers.Add("User-Agent", "GpsMapTester/1.0 (offline gps map tester)");
                    wc.Headers.Add("Accept-Language", "ko");
                    wc.Encoding = Encoding.UTF8;
                    json = await wc.DownloadStringTaskAsync(new Uri(url)).ConfigureAwait(false);
                }

                // 의존성 최소화를 위해 정규식 대신 가벼운 필드 추출 (Newtonsoft은 GMap가 이미 참조)
                string city   = ExtractJsonField(json, "city");
                string county = ExtractJsonField(json, "county");
                string town   = ExtractJsonField(json, "town");
                string borough = ExtractJsonField(json, "borough");
                string province = ExtractJsonField(json, "province") ?? ExtractJsonField(json, "state");

                // 시/군/구 우선순위: city > county > town, 여기에 구(borough) 결합
                string sigungu = city ?? county ?? town;
                if (!string.IsNullOrEmpty(sigungu) && !string.IsNullOrEmpty(borough))
                    return $"{sigungu} {borough}";
                if (!string.IsNullOrEmpty(sigungu))
                    return string.IsNullOrEmpty(province) ? sigungu : $"{province} {sigungu}";

                return null;
            }
            catch
            {
                // 오프라인/타임아웃/파싱실패 → 좌표 표시로 폴백
                return null;
            }
        }

        private static string ExtractJsonField(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string token = "\"" + key + "\"";
            int i = json.IndexOf(token, StringComparison.Ordinal);
            if (i < 0) return null;
            i = json.IndexOf('"', json.IndexOf(':', i + token.Length) + 1);
            if (i < 0) return null;
            int end = json.IndexOf('"', i + 1);
            if (end < 0) return null;
            return json.Substring(i + 1, end - i - 1);
        }
    }
}
