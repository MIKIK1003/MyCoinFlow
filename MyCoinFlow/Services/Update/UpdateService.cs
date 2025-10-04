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
            var url = OneDriveSharedLinkHelper.EnsureDirectDownload(AppReleaseConfig.VersionFeedUrl);
            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();

            await using var s = await resp.Content.ReadAsStreamAsync(ct);
            var info = await JsonSerializer.DeserializeAsync<AppVersionInfo>(s, cancellationToken: ct);
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
            Directory.CreateDirectory(AppReleaseConfig.LocalDownloadFolder);
            var target = Path.Combine(AppReleaseConfig.LocalDownloadFolder, AppReleaseConfig.DefaultSetupFileName);

            var url = OneDriveSharedLinkHelper.EnsureDirectDownload(fileUrl);
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? -1L;
            await using var input = await resp.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(target);

            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await input.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
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
