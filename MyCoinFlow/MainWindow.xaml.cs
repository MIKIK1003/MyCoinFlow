using MyCoinFlow.Services;
using MyCoinFlow.Services.Update;
using MyCoinFlow.UI.Base;
using MyCoinFlow.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace MyCoinFlow
{
    public partial class MainWindow : BaseWindow
    {

        protected override bool AllowEscapeClose => false;

        public string VersionText
        {
            get => (string)GetValue(VersionTextProperty);
            set => SetValue(VersionTextProperty, value);
        }

        public static readonly DependencyProperty VersionTextProperty =
            DependencyProperty.Register(
                nameof(VersionText),
                typeof(string),
                typeof(MainWindow),
                new PropertyMetadata("v0.0.0.0"));

        public MainWindow()
        {
            
            
            InitializeComponent();
                        
            Loaded += (s, e) =>
            {
                SyncInstalledVersionFromAssembly();

                VersionText = "v" + ReadInstalledVersionFromDbOrFallback();
            };

            UpdateVersionInUi();


            if (DataContext is null) DataContext = new MyCoinFlow.ViewModels.MainViewModel();
            if (DataContext is null) DataContext = new MainViewModel();

            try
            {
                if (ExitButton != null)
                    ExitButton.Click += (_, __) => Application.Current?.Shutdown();
            }
            catch { }

            // ✅ ESC im MainWindow hart unterdrücken (überschreibt BaseWindow!)
            this.AddHandler(
                System.Windows.UIElement.PreviewKeyDownEvent,
                new System.Windows.Input.KeyEventHandler((s, e) =>
                {
                    if (e.Key == System.Windows.Input.Key.Escape)
                    {
                        e.Handled = true;
                    }
                }),
                true // WICHTIG: handledEventsToo
            );

            // ✅ PLUS/BASIC anwenden (2 Buttons links)
            ApplyEditionVisibility();
        }

        private void ApplyEditionVisibility()
        {
            try
            {
                var isPlus = AppEdition.IsPlus;

                if (NavStweSetsButton != null)
                    NavStweSetsButton.Visibility = isPlus ? Visibility.Visible : Visibility.Collapsed;

                if (NavLiegenschaftenButton != null)
                    NavLiegenschaftenButton.Visibility = isPlus ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                // still
            }
        }

        private static void PostInstallSyncFromJson()
        {
            // DEAKTIVIERT
            // JSON darf NICHT die InstalledVersion überschreiben
        }

        private void SyncInstalledVersionFromAssembly()
        {
            try
            {
                var db = new DatabaseService();

                var current = GetAssemblyVersion();
                var dbValue = db.GetAppSetting("InstalledVersion");

                var currentVer = ParseVerOrZero(current);
                var dbVer = ParseVerOrZero(dbValue);

                
                if (currentVer > dbVer)
                {
                    db.SetAppSetting("InstalledVersion", Normalize4(current));

                    MessageBox.Show(
                        $"DB aktualisiert auf {current}",
                        "DEBUG UPDATE");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "SYNC ERROR");
            }
        }




        private static string? TryReadLocalJsonVersion()
        {
            try
            {
                var path = AppReleaseConfig.LocalVersionJsonPath;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var raw = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(
                        raw,
                        new JsonDocumentOptions { AllowTrailingCommas = true });

                    var root = doc.RootElement;

                    string[] keys =
                    {
                        "version", "fileVersion", "informationalVersion",
                        "appVersion", "semver", "Version"
                    };

                    foreach (var k in keys)
                    {
                        if (TryGetString(root, k, out var v) && !string.IsNullOrWhiteSpace(v))
                            return v;
                    }

                    if (root.TryGetProperty("app", out var appNode))
                    {
                        foreach (var k in keys)
                        {
                            if (TryGetString(appNode, k, out var v) && !string.IsNullOrWhiteSpace(v))
                                return v;
                        }
                    }
                }
            }
            catch { }

            return null;

            static bool TryGetString(JsonElement elem, string name, out string? value)
            {
                value = null;

                foreach (var p in elem.EnumerateObject())
                {
                    if (p.NameEquals(name) ||
                        string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (p.Value.ValueKind == JsonValueKind.String)
                        {
                            value = p.Value.GetString();
                            return true;
                        }

                        if (p.Value.ValueKind is JsonValueKind.Number
                            or JsonValueKind.True
                            or JsonValueKind.False)
                        {
                            value = p.Value.ToString();
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        private void UpdateVersionInUi()
        {
            try
            {
                var version = "v" + ReadInstalledVersionFromDbOrFallback();

                // Suche das TextBlock im Visual Tree
                var textBlocks = FindVisualChildren<System.Windows.Controls.TextBlock>(this);

                foreach (var tb in textBlocks)
                {
                    // wir suchen den mit dem aktuellen Wert v0.0.0.0
                    if (tb.Text != null && tb.Text.StartsWith("v0.0.0"))
                    {
                        tb.Text = version;
                        break;
                    }
                }
            }
            catch
            {
                // still
            }
        }

        private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject depObj)
    where T : System.Windows.DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);

                    if (child is T t)
                        yield return t;

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                        yield return childOfChild;
                }
            }
        }

        private static string ReadInstalledVersionFromDbOrFallback()
        {
            try
            {
                var db = new DatabaseService();
                var v = db.GetAppSetting("InstalledVersion");

                if (!string.IsNullOrWhiteSpace(v))
                    return Normalize4(v);
            }
            catch { }

            return GetAssemblyVersion();
        }

        private static Version ParseVerOrZero(string? raw)
        {
            var n = Normalize4(raw ?? "0.0.0.0");
            return Version.TryParse(n, out var v)
                ? v
                : new Version(0, 0, 0, 0);
        }

        private static string Normalize4(string v)
        {
            var cut = (v ?? string.Empty).Trim().TrimStart('v', 'V');
            cut = cut.Split('+', '-', ' ', '(')[0].Trim();

            var p = cut.Split('.', StringSplitOptions.RemoveEmptyEntries);

            return p.Length switch
            {
                >= 4 => string.Join('.', p[0], p[1], p[2], p[3]),
                3 => cut + ".0",
                2 => cut + ".0.0",
                1 => cut + ".0.0.0",
                _ => "0.0.0.0"
            };
        }

        private static string GetAssemblyVersion()
        {
            try
            {
                var asm = Assembly.GetEntryAssembly()
                          ?? Assembly.GetExecutingAssembly();

                var version = asm.GetName()?.Version;

                if (version != null)
                    return Normalize4(version.ToString());
            }
            catch { }

            return "0.0.0.0";
        }
    }
}
