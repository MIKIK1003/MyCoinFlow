using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using MyCoinFlow.Views;   // LoginWindow

namespace MyCoinFlow
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Für ExcelDataReader (XLS/XLSX mit CodePages)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Basis: Schweizerdeutsch
            var culture = (CultureInfo)CultureInfo.GetCultureInfo("de-CH").Clone();

            // Sicherstellen: Hochkomma als Tausender, Punkt als Dezimal
            culture.NumberFormat.NumberGroupSeparator = "'";
            culture.NumberFormat.NumberDecimalSeparator = ".";

            // Für alle (auch neu erstellten) Threads:
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            // WPF anweisen, dieselbe Kultur in Bindings/Strings zu verwenden
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

            // >>> NEU: Login als Startfenster öffnen
            var login = new LoginWindow();
            login.Show();

            base.OnStartup(e);
        }
    }
}
