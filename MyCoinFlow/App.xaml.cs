using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using MyCoinFlow.Views;   // LoginWindow
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

public partial class App : Application
{
    public App()
    {
        LiveCharts.Configure(config =>
            config.AddSkiaSharp());
    }
}


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

            RegisterGlobalExceptionHandlers(); // NEU

            base.OnStartup(e);
        }

        // NEU
        private void RegisterGlobalExceptionHandlers()
        {
            // WPF-UI
            this.DispatcherUnhandledException += (s, e) =>
            {
                try
                {
                    var msg = $"Unerwarteter Fehler (UI): {e.Exception.GetType().Name}\n{e.Exception.Message}";
                    System.Windows.MessageBox.Show(msg, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    try { System.IO.File.AppendAllText(GetLogPath(), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UI: {e.Exception}\r\n"); } catch { }
                }
                finally { e.Handled = true; } // App NICHT beenden
            };

            // Background-Tasks
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                try
                {
                    try { System.IO.File.AppendAllText(GetLogPath(), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Task: {e.Exception}\r\n"); } catch { }
                }
                finally { e.SetObserved(); }
            };

            // Nicht-UI
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception;
                    var msg = ex == null ? "Unbekannter Fehler (AppDomain)." : $"Unerwarteter Fehler (AppDomain): {ex.GetType().Name}\n{ex.Message}";
                    System.Windows.MessageBox.Show(msg, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    try { System.IO.File.AppendAllText(GetLogPath(), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] AppDomain: {ex}\r\n"); } catch { }
                }
                catch { /* last resort */ }
            };

            static string GetLogPath()
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MyCoinFlow");
                System.IO.Directory.CreateDirectory(dir);
                return System.IO.Path.Combine(dir, "error.log");
            }
        }

    }
}
