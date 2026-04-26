using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyCoinFlow.Services.Update
{
    /// <summary>
    /// Lädt version.json, vergleicht, lädt Setup.exe und startet das Update (/passive).
    /// </summary>
    public sealed class UpdateService
    {
        private readonly HttpClient _http;

        public UpdateService(HttpClient? httpClient = null)
        {
            _http = httpClient ?? new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = true
            });
            _http.Timeout = TimeSpan.FromSeconds(20);
        }

        public Version GetCurrentVersion()
        {
            // Nutzt AssemblyInformationalVersion; fallback auf AssemblyVersion
            var infoAttr = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (!Version.TryParse(NormalizeSemVer(infoAttr), out var v))
            {
                var f = Assembly.GetEntryAssembly()?.GetName()?.Version ?? new Version(1, 0, 0, 0);
                return f;
            }
            return v;
        }

        private static string NormalizeSemVer(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "1.0.0.0";
            // Entferne evtl. Suffixe wie "+commit" etc.
            var cut = raw.Split('+', '-', ' ', '(')[0].Trim();
            // Erzeuge 4-teilige Version
            var parts = cut.Split('.');
            if (parts.Length == 3) return cut + ".0";
            if (parts.Length == 2) return cut + ".0.0";
            if (parts.Length == 1) return cut + ".0.0.0";
            return cut;
        }

        public async Task<AppVersionInfo?> TryFetchLatestAsync(CancellationToken ct = default)
        {
            // TEMP: lokale Datei zum Testen
            var path = @"D:\Michel\OneDrive\Dokumente\MyCoinFlowUpdate\version.json";

            if (!File.Exists(path))
                throw new InvalidOperationException("Lokale version.json nicht gefunden.");

            var json = await File.ReadAllTextAsync(path, ct);

            if (string.IsNullOrWhiteSpace(json) || json.TrimStart().StartsWith("<"))
                throw new InvalidOperationException("Ungültige Antwort: kein JSON.");

            var info = JsonSerializer.Deserialize<AppVersionInfo>(json);

            if (info == null || string.IsNullOrWhiteSpace(info.Version))
                throw new InvalidOperationException("Version.json ist ungültig.");

            return info;
        }



        public static bool IsNewer(Version current, string candidate)
        {
            if (!Version.TryParse(Normalize(candidate), out var c)) return false;
            return c > current;

            static string Normalize(string v)
            {
                var cut = v.Split('+', '-', ' ', '(')[0].Trim();
                var parts = cut.Split('.');
                if (parts.Length == 3) return cut + ".0";
                if (parts.Length == 2) return cut + ".0.0";
                if (parts.Length == 1) return cut + ".0.0.0";
                return cut;
            }
        }

        public async Task<string?> DownloadSetupAsync(string fileUrl, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(fileUrl))
                throw new InvalidOperationException("Keine Download-URL angegeben.");

            Directory.CreateDirectory(AppReleaseConfig.LocalDownloadFolder);

            string target = Path.Combine(
                AppReleaseConfig.LocalDownloadFolder,
                AppReleaseConfig.DefaultSetupFileName);

            // 1) Lokale Datei direkt kopieren
            if (File.Exists(fileUrl))
            {
                File.Copy(fileUrl, target, overwrite: true);
                return target;
            }

            // 2) HTTP Download (einfach, ohne Spezialfälle)
            using var response = await _http.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Download fehlgeschlagen: {(int)response.StatusCode}");

            var total = response.Content.Headers.ContentLength ?? -1L;

            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(target);

            var buffer = new byte[81920];
            long read = 0;
            int bytesRead;

            while ((bytesRead = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                read += bytesRead;

                if (total > 0 && progress != null)
                    progress.Report(read / (double)total);
            }

            return target;
        }



        public static void StartPassiveInstallerAndExit(string setupFullPath, string? postArgs = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = setupFullPath,
                Arguments = "/passive",
                UseShellExecute = true,
                Verb = "runas" // erhöhte Rechte für Install
            };
            Process.Start(psi);

            // App sauber schließen – der Installer übernimmt
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (System.Windows.Window w in System.Windows.Application.Current.Windows)
                    w.Close();
                System.Windows.Application.Current.Shutdown();
            });
        }
    }
}
