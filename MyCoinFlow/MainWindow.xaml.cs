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
        /// <summary>
        /// Versionsanzeige für die XAML-Bindung
        /// (ElementName=RootWindow, Path=VersionText).
        /// Wird beim Start aus DB oder Fallback ermittelt.
        /// </summary>
        public string VersionText { get; private set; } = "v0.0.0.0";

        /// <summary>
        /// Initialisiert das Hauptfenster.
        /// Ablauf:
        /// 1) Einmaliger Post-Install-Sync (JSON → DB, falls neuer)
        /// 2) Versionsanzeige aus DB (Fallback: Assembly)
        /// 3) UI initialisieren
        /// 4) DataContext setzen
        /// 5) Exit-Button verdrahten
        /// </summary>
        public MainWindow()
        {
            // 1) Post-Install-Sync: hebt DB-Version auf JSON-Version an
            // Fehler werden bewusst geschluckt, um den App-Start nie zu blockieren.
            try { PostInstallSyncFromJson(); } catch { /* still */ }

            // 2) Versionsanzeige ausschließlich aus DB (Fallback: Assembly)
            VersionText = "v" + ReadInstalledVersionFromDbOrFallback();

            // 3) UI initialisieren
            InitializeComponent();

            // 4) DataContext setzen (historisch gewachsen, bewusst defensiv)
            if (DataContext is null) DataContext = new MyCoinFlow.ViewModels.MainViewModel();
            if (DataContext is null) DataContext = new MainViewModel();

            // 5) Beenden-Button verdrahten
            // Fehler werden bewusst ignoriert (z. B. falls Button nicht existiert).
            try
            {
                if (ExitButton != null)
                    ExitButton.Click += (_, __) => Application.Current?.Shutdown();
            }
            catch { /* still */ }
        }

        /// <summary>
        /// Hebt die in der Datenbank gespeicherte App-Version an,
        /// wenn eine lokal verfügbare JSON-Version größer ist.
        /// Wird einmalig pro Installation beim Start ausgeführt.
        /// </summary>
        private static void PostInstallSyncFromJson()
        {
            var db = new DatabaseService();

            // Aktuelle DB-Version lesen (kann null/leer sein)
            var dbRaw = db.GetAppSetting("InstalledVersion");
            var dbVer = ParseVerOrZero(dbRaw);

            // JSON-Version aus lokalem Update-Pfad lesen
            var jsonVerRaw = TryReadLocalJsonVersion(); // z. B. "1.2.5"
            if (string.IsNullOrWhiteSpace(jsonVerRaw))
                return; // keine JSON erreichbar → nichts zu tun

            var jsonVer = ParseVerOrZero(jsonVerRaw);

            // Nur anheben, wenn JSON-Version tatsächlich größer ist
            if (jsonVer > dbVer)
            {
                db.SetAppSetting("InstalledVersion", Normalize4(jsonVerRaw));
            }
        }

        /// <summary>
        /// Liest die Versionsnummer aus einer lokalen JSON-Datei
        /// (z. B. OneDrive Update-Verzeichnis).
        /// Unterstützt mehrere Key-Namen und toleriert einfache Typen.
        /// </summary>
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

                    // Unterstützte Keys (case-insensitiv)
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

                    // Optional verschachtelt: "app": { "version": "..." }
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
            catch { /* still */ }

            return null;

            // Lokaler Helper: tolerant gegenüber Typen und Groß-/Kleinschreibung
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

        /// <summary>
        /// Liest die installierte Version aus der Datenbank.
        /// Fallback ausschließlich für Anzeigezwecke: Assembly-Version.
        /// </summary>
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

            return GetAssemblyVersion();
        }

        /// <summary>
        /// Parst eine Versionszeichenfolge in ein Version-Objekt.
        /// Ungültige oder leere Werte ergeben 0.0.0.0.
        /// </summary>
        private static Version ParseVerOrZero(string? raw)
        {
            var n = Normalize4(raw ?? "0.0.0.0");
            return Version.TryParse(n, out var v)
                ? v
                : new Version(0, 0, 0, 0);
        }

        /// <summary>
        /// Normalisiert eine Versionszeichenfolge auf vier Stellen (x.y.z.w).
        /// Entfernt Präfixe, Suffixe und Zusatzinformationen.
        /// </summary>
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

        /// <summary>
        /// Ermittelt die Assembly-Version als letzten Fallback.
        /// Reihenfolge: FileVersion → InformationalVersion → AssemblyVersion.
        /// </summary>
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
            catch { /* still */ }

            return "0.0.0.0";
        }
    }
}
