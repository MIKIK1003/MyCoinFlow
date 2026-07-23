using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using MyCoinFlow.Views;   // LoginWindow
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using MyCoinFlow.Services; // AppEdition

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
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var culture = (CultureInfo)CultureInfo.GetCultureInfo("de-CH").Clone();
            culture.NumberFormat.NumberGroupSeparator = "'";
            culture.NumberFormat.NumberDecimalSeparator = ".";

            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

            // ✅ NEU: Edition (Basic/Plus) laden
            AppEdition.Load();
            new MyCoinFlow.Services.LicenseService().TryLoadAndApply(out _);
            MyCoinFlow.Services.AdminMode.Load();

            var login = new LoginWindow();
            login.Show();

            RegisterGlobalExceptionHandlers();

            try { MyCoinFlow.Services.DmsWatcherService.Instance.Start(); } catch { /* Arbeitsordner evtl. nicht konfiguriert */ }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { MyCoinFlow.Services.DmsWatcherService.Instance.Stop(); } catch { /* sauberes Beenden hat Vorrang, still */ }
            base.OnExit(e);
        }

        private void RegisterGlobalExceptionHandlers()
        {
            this.DispatcherUnhandledException += (s, e) =>
            {
                try
                {
                    var msg = $"Unerwarteter Fehler (UI): {e.Exception.GetType().Name}\n{e.Exception.Message}";
                    System.Windows.MessageBox.Show(msg, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    try { System.IO.File.AppendAllText(GetLogPath(), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UI: {e.Exception}\r\n"); } catch { }
                }
                finally { e.Handled = true; }
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                try
                {
                    try { System.IO.File.AppendAllText(GetLogPath(), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Task: {e.Exception}\r\n"); } catch { }
                }
                finally { e.SetObserved(); }
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception;
                    var msg = ex == null ? "Unbekannter Fehler (AppDomain)." : $"Unerwarteter Fehler (AppDomain): {ex.GetType().Name}\n{ex.Message}";
                    System.Windows.MessageBox.Show(msg, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    try { System.IO.File.AppendAllText(GetLogPath(), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] AppDomain: {ex}\r\n"); } catch { }
                }
                catch { }
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
