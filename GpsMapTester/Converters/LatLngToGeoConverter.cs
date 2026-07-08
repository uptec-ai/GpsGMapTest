using DevExpress.Xpf.Map;
using System;
using System.Globalization;
using System.Windows.Data;

namespace GpsMapTester.Converters
{
    /// <summary>
    /// (위도, 경도) 두 값을 DevExpress GeoPoint 로 변환. MapControl.CenterPoint 바인딩용.
    /// XAML 바인딩으로 초기 중심을 설정하면 (0,0)→목표 팬 애니메이션이 발생하지 않는다.
    /// </summary>
    public sealed class LatLngToGeoConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values != null && values.Length >= 2 &&
                values[0] is double lat && values[1] is double lng)
                return new GeoPoint(lat, lng);
            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }
}
