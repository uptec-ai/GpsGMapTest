using System;
using System.Collections.Generic;

namespace GpsMapTester.Models
{
    /// <summary>
    /// NMEA 파싱 결과로 채워지는 GPS 데이터 모델
    /// </summary>
    public class GpsData
    {
        // ─── 위치 ─────────────────────────────────────────────────────────
        /// <summary>위도 (십진도, 남위 음수)</summary>
        public double Latitude { get; set; }

        /// <summary>경도 (십진도, 서경 음수)</summary>
        public double Longitude { get; set; }

        /// <summary>해발 고도 (m)</summary>
        public double Altitude { get; set; }

        // ─── 이동 ─────────────────────────────────────────────────────────
        /// <summary>속도 (km/h)</summary>
        public double SpeedKmh { get; set; }

        /// <summary>속도 (knots)</summary>
        public double SpeedKnots { get; set; }

        /// <summary>진북 기준 방위각 (도)</summary>
        public double CourseDeg { get; set; }

        // ─── 수신 품질 ────────────────────────────────────────────────────
        /// <summary>GGA Fix 품질: 0=No Fix, 1=GPS, 2=DGPS</summary>
        public int FixQuality { get; set; }

        /// <summary>GSA Fix 유형 문자열: "No Fix" / "2D Fix" / "3D Fix"</summary>
        public string FixType { get; set; } = "No Fix";

        /// <summary>사용 중인 위성 수</summary>
        public int SatelliteCount { get; set; }

        /// <summary>수평 정밀도 저하 지수 (Horizontal Dilution of Precision)</summary>
        public double Hdop { get; set; }

        /// <summary>3D 위치 정밀도 저하 지수 (Position Dilution of Precision)</summary>
        public double Pdop { get; set; }

        /// <summary>수직 정밀도 저하 지수 (Vertical Dilution of Precision)</summary>
        public double Vdop { get; set; }

        // ─── 시각 ─────────────────────────────────────────────────────────
        /// <summary>GPS UTC 시각</summary>
        public DateTime UtcTime { get; set; } = DateTime.MinValue;

        // ─── 상태 ─────────────────────────────────────────────────────────
        /// <summary>위치 데이터 유효 여부 (RMC 상태 필드 기준)</summary>
        public bool IsValid { get; set; }

        // ─── 위성 목록 ────────────────────────────────────────────────────
        /// <summary>GSV 파싱으로 수집한 위성 정보 목록</summary>
        public List<SatelliteInfo> Satellites { get; set; } = new List<SatelliteInfo>();
    }
}
