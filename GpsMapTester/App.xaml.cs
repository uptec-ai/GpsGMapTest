using DevExpress.Xpf.Core;
using System.Windows;

namespace GpsMapTester
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        static App()
        {
            // DevExpress 경량 테마 사용
            CompatibilitySettings.UseLightweightThemes = true;
        }
    }
}
