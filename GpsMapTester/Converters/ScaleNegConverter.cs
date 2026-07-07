using System;
using System.Globalization;
using System.Windows.Data;

namespace GpsMapTester.Converters
{
    /// <summary>
    /// 값에 -scale 을 곱해 반환 (ConverterParameter = scale, 기본 1).
    /// 마커 콘텐츠를 하단중앙 기준으로 좌표에 앵커링할 때 사용:
    ///   X = ActualWidth * -0.5, Y = ActualHeight * -1
    /// </summary>
    public sealed class ScaleNegConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double v = 0;
            if (value is double d) v = d;
            else double.TryParse(System.Convert.ToString(value, CultureInfo.InvariantCulture),
                                 NumberStyles.Any, CultureInfo.InvariantCulture, out v);

            double scale = 1.0;
            if (parameter is string ps)
                double.TryParse(ps, NumberStyles.Any, CultureInfo.InvariantCulture, out scale);

            return -v * scale;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
