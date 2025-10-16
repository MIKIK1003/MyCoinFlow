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
        // Für XAML-Bindung (ElementName=RootWindow, Path=VersionText)
        public string VersionText { get; private set; } = "v0.0.0.0";

        public MainWindow()
        {
            // 1) Post-Install-Sync: JSON > DB? -> DB anheben (einmal pro Installation)
            try { PostInstallSyncFromJson(); } catch { /* still */ }

            // 2) Anzeige ausschließlich aus der DB (Fallback: Assembly)
            VersionText = "v" + ReadInstalledVersionFromDbOrFallback();

            InitializeComponent();

            // 3) DataContext wie gehabt
            if (DataContext is null) DataContext = new MyCoinFlow.ViewModels.MainViewModel();
            if (DataContext is null) DataContext = new MainViewModel();

            // 4) Beenden-Button
            try { if (ExitButton != null) ExitButton.Click += (_, __) => Application.Current?.Shutdown(); } catch { }
        }

        // === Post-Install-Sync: hebt DB-Version auf JSON-Version an, wenn JSON > DB ===
        private static void PostInstallSyncFromJson()
        {
            var db = new DatabaseService();

            // DB-Wert lesen (kann null/leer sein)
            var dbRaw = db.GetAppSetting("InstalledVersion");
            var dbVer = ParseVerOrZero(dbRaw);

            // JSON aus OneDrive lesen (AppReleaseConfig.LocalVersionJsonPath)
            var jsonVerRaw = TryReadLocalJsonVersion();  // z. B. "1.2.5"
            if (string.IsNullOrWhiteSpace(jsonVerRaw))
                return; // keine JSON erreichbar -> nichts zu tun

            var jsonVer = ParseVerOrZero(jsonVerRaw!);

            // Nur anheben, wenn JSON wirklich größer ist
            if (jsonVer > dbVer)
            {
                db.SetAppSetting("InstalledVersion", Normalize4(jsonVerRaw!));
            }
        }

        private static string? TryReadLocalJsonVersion()
        {
            try
            {
                var path = AppReleaseConfig.LocalVersionJsonPath; // OneDrive\...\MyCoinFlowUpdate\version.json
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var raw = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { AllowTrailingCommas = true });
                    var root = doc.RootElement;

                    // erlaubt: version / fileVersion / informationalVersion / appVersion / semver (case-insensitiv)
                    string[] keys = { "version", "fileVersion", "informationalVersion", "appVersion", "semver", "Version" };
                    foreach (var k in keys)
                    {
                        if (TryGetString(root, k, out var v) && !string.IsNullOrWhiteSpace(v))
                            return v;
                    }
                    // optional verschachtelt: "app": { "version": "..." }
                    if (root.TryGetProperty("app", out var appNode))
                    {
                        foreach (var k in keys)
                            if (TryGetString(appNode, k, out var v) && !string.IsNullOrWhiteSpace(v))
                                return v;
                    }
                }
            }
            catch { /* still */ }
            return null;

            static bool TryGetString(JsonElement elem, string name, out string? value)
            {
                value = null;
                foreach (var p in elem.EnumerateObject())
                {
                    if (p.NameEquals(name) || string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (p.Value.ValueKind == JsonValueKind.String) { value = p.Value.GetString(); return true; }
                        if (p.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) { value = p.Value.ToString(); return true; }
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
            catch { /* still */ }

            // Fallback (nur Anzeige, falls DB leer): Assembly-Version
            return GetAssemblyVersion();
        }

        private static Version ParseVerOrZero(string? raw)
        {
            var n = Normalize4(raw ?? "0.0.0.0");
            return Version.TryParse(n, out var v) ? v : new Version(0, 0, 0, 0);
        }

        private static string Normalize4(string v)
        {
            var cut = (v ?? "").Trim().TrimStart('v', 'V');
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
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

                var fvi = FileVersionInfo.GetVersionInfo(asm.Location);
                if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                    return Normalize4(fvi.FileVersion);

                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info))
                    return Normalize4(info);

                var asmVer = asm.GetName()?.Version?.ToString();
                if (!string.IsNullOrWhiteSpace(asmVer))
                    return Normalize4(asmVer);
            }
            catch { /* still */ }

            return "0.0.0.0";
        }
    }
}
