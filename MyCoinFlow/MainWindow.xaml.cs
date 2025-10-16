using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using MyCoinFlow.ViewModels;
using MyCoinFlow.Services;

namespace MyCoinFlow
{
    public partial class MainWindow : Window
    {
        // Für XAML-Bindung (ElementName=RootWindow)
        public string VersionText { get; private set; } = "v0.0.0";
        public string? VersionSourcePath { get; private set; }

        public MainWindow()
        {
            // Version VOR InitializeComponent ermitteln, damit das Binding sofort greift
            var (v, src, _) = TryGetVersionFromJsonWithDiagnostics();
            VersionText = "v" + (string.IsNullOrWhiteSpace(v) ? GetAssemblyVersion() : v);
            VersionSourcePath = src;

            InitializeComponent();

            // DataContext beibehalten wie im Bestand
            if (DataContext is null) DataContext = new MyCoinFlow.ViewModels.MainViewModel();
            if (DataContext is null) DataContext = new MainViewModel();

            // Beenden-Button
            try { if (ExitButton != null) ExitButton.Click += ExitButton_Click; } catch { }
        }

        private void ExitButton_Click(object? sender, RoutedEventArgs e)
        {
            try { Application.Current?.Shutdown(); }
            catch (Exception ex)
            {
                MessageBox.Show("Anwendung konnte nicht beendet werden:\n" + ex.Message,
                    "Beenden", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== VERSION (wie zuvor, nur ohne DWM/Chrome) =====
        private (string? version, string? sourcePath, string log) TryGetVersionFromJsonWithDiagnostics()
        {
            var sb = new StringBuilder();
            void Log(string line) => sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}");

            try
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var baseDir = AppContext.BaseDirectory;

                var userExact = @"C:\Users\miche\OneDrive\Dokumente\MyCoinFlowUpdate\version.json";

                string? configured = null;
                try { configured = new DatabaseService().GetAppSetting("VersionJsonPath"); } catch { }

                var envPath = Environment.GetEnvironmentVariable("MYCOINFLOW_VERSION_JSON");

                string[] oneDriveRoots;
                try { oneDriveRoots = Directory.GetDirectories(userProfile, "OneDrive*"); }
                catch { oneDriveRoots = Array.Empty<string>(); }

                var candidates = new System.Collections.Generic.List<string?>();
                candidates.Add(userExact);
                candidates.Add(configured);
                candidates.Add(envPath);
                foreach (var root in oneDriveRoots)
                {
                    candidates.Add(Path.Combine(root, "Dokumente", "MyCoinFlowUpdate", "version.json"));
                    candidates.Add(Path.Combine(root, "Documents", "MyCoinFlowUpdate", "version.json"));
                }
                candidates.Add(Path.Combine(docs, "MyCoinFlowUpdate", "version.json"));
                candidates.Add(Path.Combine(docs, "MyCoinFlow", "Update", "version.json"));
                candidates.Add(Path.Combine(baseDir, "version.json"));
                candidates.Add(Path.Combine(baseDir, "AppReleaseConfig.json"));
                candidates.Add(Path.Combine(baseDir, "Service", "Update", "AppReleaseConfig.json"));

                foreach (var path in candidates.Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    try
                    {
                        if (!File.Exists(path!)) { Log($"check: {path}  exists=False"); continue; }

                        Log($"check: {path}  exists=True");
                        var json = File.ReadAllText(path!);
                        if (string.IsNullOrWhiteSpace(json)) { Log(" -> empty file"); continue; }

                        if (TryParseVersion(json, out var v))
                        {
                            var nv = NormalizeVersion(v);
                            Log($" -> parsed OK: {nv}");
                            return (nv, path, sb.ToString());
                        }
                        else
                        {
                            Log(" -> parse FAILED");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($" -> EXCEPTION: {ex.GetType().Name} {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"FATAL EXCEPTION: {ex.GetType().Name} {ex.Message}");
            }

            return (null, null, sb.ToString());
        }

        private static bool TryParseVersion(string json, out string? version)
        {
            version = null;
            try
            {
                var opts = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
                using var doc = JsonDocument.Parse(json, opts);
                var root = doc.RootElement;

                string[] keys = { "version", "fileVersion", "informationalVersion", "appVersion", "semver", "Version" };

                foreach (var k in keys)
                    if (TryGetString(root, k, out version)) return true;

                if (root.TryGetProperty("app", out var appNode))
                    foreach (var k in keys)
                        if (TryGetString(appNode, k, out version)) return true;
            }
            catch { }
            return false;

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

        private static string NormalizeVersion(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "0.0.0";
            var v = raw.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase)) v = v[1..].Trim();
            v = v.Replace(" ", "");
            var parts = v.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 4) v = string.Join(".", parts[..4]);
            return v;
        }

        private static string GetAssemblyVersion()
        {
            try
            {
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var fvi = FileVersionInfo.GetVersionInfo(asm.Location);
                if (!string.IsNullOrWhiteSpace(fvi.FileVersion)) return fvi.FileVersion;

                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info)) return info;

                var asmVer = asm.GetName()?.Version?.ToString();
                if (!string.IsNullOrWhiteSpace(asmVer)) return asmVer;
            }
            catch { }
            return "0.0.0";
        }
    }
}
