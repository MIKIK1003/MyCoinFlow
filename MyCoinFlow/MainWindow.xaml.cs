using MyCoinFlow.Services;
using MyCoinFlow.Services.Update;
using MyCoinFlow.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace MyCoinFlow
{
    public partial class MainWindow : Window
    {
        public string VersionText { get; private set; } = "v0.0.0.0";

        public MainWindow()
        {
            try { PostInstallSyncFromJson(); } catch { }

            VersionText = "v" + ReadInstalledVersionFromDbOrFallback();

            InitializeComponent();

            if (DataContext is null) DataContext = new MyCoinFlow.ViewModels.MainViewModel();
            if (DataContext is null) DataContext = new MainViewModel();

            try
            {
                if (ExitButton != null)
                    ExitButton.Click += (_, __) => Application.Current?.Shutdown();
            }
            catch { }

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
            var db = new DatabaseService();

            var dbRaw = db.GetAppSetting("InstalledVersion");
            var dbVer = ParseVerOrZero(dbRaw);

            var jsonVerRaw = TryReadLocalJsonVersion();
            if (string.IsNullOrWhiteSpace(jsonVerRaw))
                return;

            var jsonVer = ParseVerOrZero(jsonVerRaw);

            if (jsonVer > dbVer)
            {
                db.SetAppSetting("InstalledVersion", Normalize4(jsonVerRaw));
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

                var fvi = FileVersionInfo.GetVersionInfo(asm.Location);
                if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                    return Normalize4(fvi.FileVersion);

                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                              ?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info))
                    return Normalize4(info);

                var asmVer = asm.GetName()?.Version?.ToString();
                if (!string.IsNullOrWhiteSpace(asmVer))
                    return Normalize4(asmVer);
            }
            catch { }

            return "0.0.0.0";
        }
    }
}
