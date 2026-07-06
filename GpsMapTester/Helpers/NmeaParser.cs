using System;
using System.Collections.Generic;
using System.Globalization;
using GpsMapTester.Models;

namespace GpsMapTester.Helpers
{
    /// <summary>
    /// NMEA 0183 문장 파서
    /// 지원 문장: GGA, RMC, GSA, GSV, VTG
    /// Talker ID: GP (GPS), GN (복합), GL (GLONASS), GA (Galileo) 모두 처리
    /// </summary>
    public class NmeaParser
    {
        private readonly GpsData _data = new GpsData();

        // GSV 문장은 여러 개가 세트를 이루므로 누적 처리
        private readonly List<SatelliteInfo> _pendingSatellites = new List<SatelliteInfo>();
        private int _gsvTotalMessages = 0;

        /// <summary>현재 파싱된 GPS 데이터 (최신 상태)</summary>
        public GpsData CurrentData => _data;

        /// <summary>
        /// NMEA 문장 하나를 파싱하여 CurrentData를 갱신합니다.
        /// 알 수 없는 문장 타입은 무시됩니다.
        /// </summary>
        public void Parse(string sentence)
        {
            if (string.IsNullOrEmpty(sentence) || !sentence.StartsWith("$"))
                return;

            // 체크섬 제거 후 파싱
            int starIdx = sentence.LastIndexOf('*');
            string body = starIdx > 0 ? sentence.Substring(0, starIdx) : sentence;
            string[] fields = body.Split(',');

            if (fields.Length < 1 || fields[0].Length < 6)
                return;

            // Talker ID(2자) 이후 문장 타입(3자) 추출
            // 예) $GPGGA → GGA,  $GNGSA → GSA
            string type = fields[0].Substring(3);

            switch (type)
            {
                case "GGA": ParseGGA(fields); break;
                case "RMC": ParseRMC(fields); break;
                case "GSA": ParseGSA(fields); break;
                case "GSV": ParseGSV(fields); break;
                case "VTG": ParseVTG(fields); break;
                // GLL, ZDA 등 기타 문장은 무시
            }
        }

        // ─── GGA: Global Positioning System Fix Data ──────────────────────
        // $G?GGA,hhmmss.ss,Lat,N/S,Lon,E/W,quality,numSV,HDOP,Alt,M,Sep,M,,*cs
        private void ParseGGA(string[] f)
        {
            if (f.Length < 10) return;

            _data.UtcTime       = ParseTime(f[1]);
            _data.Latitude      = ParseCoord(f[2], f[3]);
            _data.Longitude     = ParseCoord(f[4], f[5]);
            _data.FixQuality    = ParseInt(f[6]);
            _data.SatelliteCount = ParseInt(f[7]);
            _data.Hdop          = ParseDouble(f[8]);
            _data.Altitude      = ParseDouble(f[9]);
            _data.IsValid       = _data.FixQuality > 0;
        }

        // ─── RMC: Recommended Minimum Specific GNSS Data ──────────────────
        // $G?RMC,hhmmss.ss,A/V,Lat,N/S,Lon,E/W,spd,cog,ddmmyy,mv,mvEW,posMode*cs
        private void ParseRMC(string[] f)
        {
            if (f.Length < 9) return;

            if (f.Length > 1) _data.UtcTime    = ParseTime(f[1]);
            _data.IsValid                        = f.Length > 2 && f[2] == "A";
            if (f.Length > 4) _data.Latitude    = ParseCoord(f[3], f[4]);
            if (f.Length > 6) _data.Longitude   = ParseCoord(f[5], f[6]);
            if (f.Length > 7) _data.SpeedKnots  = ParseDouble(f[7]);
            if (f.Length > 8) _data.CourseDeg   = ParseDouble(f[8]);

            // knots → km/h 변환
            _data.SpeedKmh = _data.SpeedKnots * 1.852;
        }

        // ─── GSA: GNSS DOP and Active Satellites ──────────────────────────
        // $G?GSA,opMode,navMode,sv1..sv12,PDOP,HDOP,VDOP[,systemId]*cs
        private void ParseGSA(string[] f)
        {
            if (f.Length < 18) return;

            int navMode = ParseInt(f[2]);
            _data.FixType = navMode == 3 ? "3D Fix"
                          : navMode == 2 ? "2D Fix"
                          : "No Fix";

            _data.Pdop = ParseDouble(f[15]);
            _data.Hdop = ParseDouble(f[16]);

            // VDOP 필드에 체크섬이 붙을 수 있으므로 제거
            string vdopStr = f[17];
            int si = vdopStr.IndexOf('*');
            if (si >= 0) vdopStr = vdopStr.Substring(0, si);
            _data.Vdop = ParseDouble(vdopStr);
        }

        // ─── GSV: GNSS Satellites in View ────────────────────────────────
        // $G?GSV,totalMsg,msgNum,totalSV,[svid,elev,azim,snr]*4,...*cs
        private void ParseGSV(string[] f)
        {
            if (f.Length < 4) return;

            int totalMsg = ParseInt(f[1]);
            int msgNum   = ParseInt(f[2]);

            // 첫 번째 문장이면 위성 목록 초기화
            if (msgNum == 1)
            {
                _pendingSatellites.Clear();
                _gsvTotalMessages = totalMsg;
            }

            // 위성 데이터: 4필드씩 반복 (PRN, 앙각, 방위, SNR)
            int startIdx = 4;
            while (startIdx + 3 < f.Length)
            {
                // 마지막 필드에 체크섬이 붙을 수 있음
                string snrStr = f[startIdx + 3];
                int si = snrStr.IndexOf('*');
                if (si >= 0) snrStr = snrStr.Substring(0, si);

                int prn = ParseInt(f[startIdx]);
                if (prn > 0)
                {
                    _pendingSatellites.Add(new SatelliteInfo
                    {
                        PrnNumber = prn,
                        Elevation = ParseInt(f[startIdx + 1]),
                        Azimuth   = ParseInt(f[startIdx + 2]),
                        Snr       = ParseInt(snrStr)
                    });
                }
                startIdx += 4;
            }

            // 마지막 GSV 문장이면 위성 목록 확정
            if (msgNum == _gsvTotalMessages)
                _data.Satellites = new List<SatelliteInfo>(_pendingSatellites);
        }

        // ─── VTG: Course Over Ground and Ground Speed ─────────────────────
        // $G?VTG,cogt,T,cogm,M,knots,N,kph,K[,posMode]*cs
        private void ParseVTG(string[] f)
        {
            if (f.Length < 8) return;

            if (!string.IsNullOrEmpty(f[1]))
                _data.CourseDeg = ParseDouble(f[1]);

            _data.SpeedKmh   = ParseDouble(f[7]);
            _data.SpeedKnots = _data.SpeedKmh / 1.852;
        }

        // ─── 변환 헬퍼 ───────────────────────────────────────────────────
        /// <summary>NMEA DDMM.MMMM 형식을 십진도(Decimal Degrees)로 변환</summary>
        private static double ParseCoord(string value, string direction)
        {
            if (string.IsNullOrEmpty(value)) return 0.0;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double raw))
                return 0.0;

            int degrees    = (int)(raw / 100);
            double minutes = raw - degrees * 100;
            double result  = degrees + minutes / 60.0;

            return (direction == "S" || direction == "W") ? -result : result;
        }

        /// <summary>NMEA hhmmss.ss 형식을 DateTime(UTC)으로 변환</summary>
        private static DateTime ParseTime(string timeStr)
        {
            if (string.IsNullOrEmpty(timeStr) || timeStr.Length < 6)
                return DateTime.MinValue;
            if (!int.TryParse(timeStr.Substring(0, 2), out int hh)) return DateTime.MinValue;
            if (!int.TryParse(timeStr.Substring(2, 2), out int mm)) return DateTime.MinValue;
            if (!double.TryParse(timeStr.Substring(4), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double ss)) return DateTime.MinValue;

            var now = DateTime.UtcNow;
            return new DateTime(now.Year, now.Month, now.Day, hh, mm, (int)ss, DateTimeKind.Utc);
        }

        private static int ParseInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return int.TryParse(s, out int v) ? v : 0;
        }

        private static double ParseDouble(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0.0;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? v : 0.0;
        }
    }
}
