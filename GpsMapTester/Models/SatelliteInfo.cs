namespace GpsMapTester.Models
{
    /// <summary>
    /// $GPGSV/$GNGSV 문장에서 파싱한 개별 위성 정보
    /// </summary>
    public class SatelliteInfo
    {
        /// <summary>위성 PRN 번호 (Pseudo-Random Noise)</summary>
        public int PrnNumber { get; set; }

        /// <summary>앙각 (Elevation, 0~90°)</summary>
        public int Elevation { get; set; }

        /// <summary>방위각 (Azimuth, 0~359°)</summary>
        public int Azimuth { get; set; }

        /// <summary>신호 대 잡음비 (SNR, dBHz). 0이면 신호 없음</summary>
        public int Snr { get; set; }
    }
}
