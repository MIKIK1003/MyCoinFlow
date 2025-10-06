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
            // 0) Lokaler Test zuerst
            var local = MyCoinFlow.Services.Update.AppReleaseConfig.LocalVersionJsonPath;
            if (!string.IsNullOrWhiteSpace(local) && File.Exists(local))
            {
                var rawLocal = await File.ReadAllTextAsync(local, ct);
                return JsonSerializer.Deserialize<AppVersionInfo>(rawLocal);
            }

            // 1) Versuch: deinen Link minimal vorbereiten
            var url1 = OneDriveSharedLinkHelper.EnsureDirectDownload(AppReleaseConfig.VersionFeedUrl);
            using (var resp1 = await _http.GetAsync(url1, ct))
            {
                var final1 = resp1.RequestMessage?.RequestUri;

                if (resp1.IsSuccessStatusCode)
                {
                    var raw1 = await resp1.Content.ReadAsStringAsync(ct);
                    var isHtml1 = !string.IsNullOrWhiteSpace(raw1) && raw1.TrimStart().StartsWith("<");
                    if (!isHtml1)
                        return JsonSerializer.Deserialize<AppVersionInfo>(raw1);
                    // bei HTML -> weiter zu Versuch 2
                }

                // 2) Versuch: finale Redirect-URL in einen Download-Link umschreiben
                var fallbackUrl = final1 != null
                    ? OneDriveSharedLinkHelper.RewriteFromFinalUri(final1)
                    : OneDriveSharedLinkHelper.EnsureDirectDownload(AppReleaseConfig.VersionFeedUrl);

                using (var resp2 = await _http.GetAsync(fallbackUrl, ct))
                {
                    var raw2 = await resp2.Content.ReadAsStringAsync(ct);
                    var ok2 = resp2.IsSuccessStatusCode && !(raw2?.TrimStart().StartsWith("<") ?? false);
                    if (ok2)
                        return JsonSerializer.Deserialize<AppVersionInfo>(raw2);

                    // 3) Fallback: OneDrive Shares API mit dem Original-Share-Link (funktioniert für 1drv.ms & Co.)
                    var apiUrl = OneDriveSharedLinkHelper.BuildSharesApiContentUrl(AppReleaseConfig.VersionFeedUrl);
                    using var resp3 = await _http.GetAsync(apiUrl, ct);
                    if (!resp3.IsSuccessStatusCode)
                    {
                        var final3 = resp3.RequestMessage?.RequestUri?.ToString() ?? apiUrl;
                        throw new InvalidOperationException(
                            $"Abruf fehlgeschlagen: {(int)resp3.StatusCode} {resp3.ReasonPhrase}\nURL: {final3}");
                    }

                    var raw3 = await resp3.Content.ReadAsStringAsync(ct);
                    if (!string.IsNullOrWhiteSpace(raw3) && raw3.TrimStart().StartsWith("<"))
                    {
                        var final3 = resp3.RequestMessage?.RequestUri?.ToString() ?? apiUrl;
                        throw new InvalidOperationException(
                            "Der Link zur version.json liefert HTML statt JSON (auch per Shares API).\n" +
                            $"URL: {final3}\nBitte einen direkten Datei-Link verwenden.");
                    }

                    return JsonSerializer.Deserialize<AppVersionInfo>(raw3);
                }
            }
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

            // (A) Wenn fileUrl leer ist: versuche lokale Setup-Datei im OneDrive-Update-Ordner
            if (string.IsNullOrWhiteSpace(fileUrl))
            {
                var local = OneDriveLocalResolver.TryGetSetupLocalPath(AppReleaseConfig.DefaultSetupFileName);
                if (local == null)
                    throw new InvalidOperationException("Keine Setup-Quelle angegeben und keine lokale Setup-Datei gefunden.");
                File.Copy(local, target, overwrite: true);
                return target;
            }

            // (B) Wenn fileUrl ein lokaler Pfad ist (oder file://)
            if (Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                var src = uri.LocalPath;
                if (!File.Exists(src)) throw new FileNotFoundException("Lokale Setup-Datei nicht gefunden.", src);
                File.Copy(src, target, overwrite: true);
                return target;
            }
            if (File.Exists(fileUrl)) // plain Pfad
            {
                File.Copy(fileUrl, target, overwrite: true);
                return target;
            }

            // (C) HTTP/HTTPS – wie gehabt herunterladen
            using var resp = await _http.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, ct);
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
