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
            // ---- Failsafe-Logging ---------------------------------------------------
            var attempts = new List<(string Url, string Stage, int? Status, string? MediaType, string? Note)>();
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyCoinFlow");
            Directory.CreateDirectory(appDataDir);
            string logPath = Path.Combine(appDataDir, $"update_attempts_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            static bool IsBinary(string? media) =>
                !string.IsNullOrWhiteSpace(media) &&
                !media.Contains("text/html", StringComparison.OrdinalIgnoreCase) &&
                !media.Contains("text/plain", StringComparison.OrdinalIgnoreCase);

            // ------------------------------------------------------------------------

            Directory.CreateDirectory(AppReleaseConfig.LocalDownloadFolder);
            string target = Path.Combine(AppReleaseConfig.LocalDownloadFolder, AppReleaseConfig.DefaultSetupFileName);

            // Browserähnliche Header (einige Hoster verlangen das)
            try
            {
                if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome Safari")) { }
                _http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
            }
            catch { /* ignore */ }

            try
            {
                // (A) Fallback: lokale OneDrive-Datei
                if (string.IsNullOrWhiteSpace(fileUrl))
                {
                    var local = OneDriveLocalResolver.TryGetSetupLocalPath(AppReleaseConfig.DefaultSetupFileName);
                    if (local == null)
                        throw new InvalidOperationException("Keine Setup-Quelle angegeben und keine lokale Setup-Datei gefunden.");
                    File.Copy(local, target, overwrite: true);
                    return target;
                }

                // (B) lokale Pfade
                if (Uri.TryCreate(fileUrl, UriKind.Absolute, out var abs) && abs.IsFile)
                {
                    var src = abs.LocalPath;
                    if (!File.Exists(src)) throw new FileNotFoundException("Lokale Setup-Datei nicht gefunden.", src);
                    File.Copy(src, target, overwrite: true);
                    return target;
                }
                if (File.Exists(fileUrl))
                {
                    File.Copy(fileUrl, target, overwrite: true);
                    return target;
                }

                // ===== HTTP/HTTPS =====
                async Task<string?> TryDownloadAsync(string url, string stage, bool allowHtmlScrape, int depth = 0)
                {
                    try
                    {
                        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                        var media = resp.Content.Headers.ContentType?.MediaType ?? "";
                        attempts.Add((url, stage, (int)resp.StatusCode, media, null));

                        if (!resp.IsSuccessStatusCode) return null;

                        // Binär -> direkt speichern
                        if (IsBinary(media) || string.IsNullOrWhiteSpace(media))
                        {
                            var disp = resp.Content.Headers.ContentDisposition;
                            var suggested = disp != null ? (disp.FileNameStar ?? disp.FileName) : null;
                            if (!string.IsNullOrWhiteSpace(suggested))
                                target = Path.Combine(AppReleaseConfig.LocalDownloadFolder, suggested!.Trim('"'));

                            var total = resp.Content.Headers.ContentLength ?? -1L;
                            await using var input = await resp.Content.ReadAsStreamAsync(ct);
                            await using var output = File.Create(target);

                            var buf = new byte[81920];
                            long read = 0;
                            int n;
                            while ((n = await input.ReadAsync(buf.AsMemory(0, buf.Length), ct)) > 0)
                            {
                                await output.WriteAsync(buf.AsMemory(0, n), ct);
                                read += n;
                                if (total > 0 && progress != null)
                                    progress.Report(read / (double)total);
                            }
                            return target;
                        }

                        // HTML-Scrape (nur für Nicht-mscontent; für mscontent kommt direkt Binär)
                        if (allowHtmlScrape && media.Contains("text/html", StringComparison.OrdinalIgnoreCase) && depth < 2)
                        {
                            var html = await resp.Content.ReadAsStringAsync(ct);

                            string? candidate = null;

                            // FilesConfig.si → ReturnUrl
                            var mSi = System.Text.RegularExpressions.Regex.Match(
                                html, @"""si""\s*:\s*""([^""]+)""",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                            if (mSi.Success)
                            {
                                try
                                {
                                    var siUrl = System.Net.WebUtility.HtmlDecode(mSi.Groups[1].Value);
                                    var ub = new UriBuilder(siUrl);
                                    var qs = System.Web.HttpUtility.ParseQueryString(ub.Query);
                                    var ret = qs["ReturnUrl"];
                                    if (!string.IsNullOrWhiteSpace(ret))
                                    {
                                        var retDec = System.Web.HttpUtility.UrlDecode(ret);
                                        if (retDec.StartsWith("/")) retDec = "https://onedrive.live.com" + retDec;
                                        candidate = retDec;
                                    }
                                }
                                catch { }
                            }

                            // downloadUrl":"..."
                            if (string.IsNullOrWhiteSpace(candidate))
                            {
                                var mJson = System.Text.RegularExpressions.Regex.Match(
                                    html, @"""downloadUrl""\s*:\s*""([^""]+)""",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                                if (mJson.Success)
                                {
                                    candidate = mJson.Groups[1].Value
                                        .Replace("\\u0026", "&").Replace("\\/", "/")
                                        .Replace("\\u003d", "=").Replace("\\u003f", "?");
                                }
                            }

                            // absoluter /download?...
                            if (string.IsNullOrWhiteSpace(candidate))
                            {
                                var mAbs = System.Text.RegularExpressions.Regex.Match(
                                    html, @"https?://onedrive\.live\.com/download\?[^""'>\s]+",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (mAbs.Success) candidate = mAbs.Value;
                            }

                            // relativer /download?...
                            if (string.IsNullOrWhiteSpace(candidate))
                            {
                                var mRel = System.Text.RegularExpressions.Regex.Match(
                                    html, @"href\s*=\s*[""'](/download\?[^""'>\s]+)[""']",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (mRel.Success) candidate = "https://onedrive.live.com" + mRel.Groups[1].Value;
                            }

                            // Meta-Refresh
                            if (string.IsNullOrWhiteSpace(candidate))
                            {
                                var mMeta = System.Text.RegularExpressions.Regex.Match(
                                    html, @"http-equiv\s*=\s*[""']refresh[""']\s+content\s*=\s*[""'][^""']*url=([^""'>\s]+)[""']",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (mMeta.Success) candidate = System.Net.WebUtility.HtmlDecode(mMeta.Groups[1].Value.Trim('\'', '"'));
                            }

                            if (!string.IsNullOrWhiteSpace(candidate))
                            {
                                if (!candidate.Contains("download=", StringComparison.OrdinalIgnoreCase))
                                    candidate += (candidate.Contains("?") ? "&" : "?") + "download=1";
                                if (!candidate.Contains("em=", StringComparison.OrdinalIgnoreCase))
                                    candidate += "&em=2";

                                var rHtml = await TryDownloadAsync(candidate, stage + "+html-scrape", allowHtmlScrape: true, depth: depth + 1);
                                if (!string.IsNullOrWhiteSpace(rHtml)) return rHtml;
                            }
                            else
                            {
                                try { File.WriteAllText(Path.Combine(appDataDir, "update_preview.html"), html, System.Text.Encoding.UTF8); } catch { }
                            }
                        }

                        return null;
                    }
                    catch (Exception ex)
                    {
                        attempts.Add((url, stage, null, null, ex.GetType().Name + ": " + ex.Message));
                        return null;
                    }
                }

                // === SPEZIALFALL: mscontent (Direkt-Download) ===
                if (Uri.TryCreate(fileUrl, UriKind.Absolute, out var u) &&
                    u.Host.IndexOf("my.microsoftpersonalcontent.com", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Referrer setzen (manche mscontent-Links wollen eine Herkunft)
                    try { _http.DefaultRequestHeaders.Referrer = new Uri("https://onedrive.live.com/"); } catch { }
                    var r0 = await TryDownloadAsync(fileUrl, "mscontent-direct", allowHtmlScrape: false);
                    if (!string.IsNullOrWhiteSpace(r0)) return r0;
                    // kein Fallback auf Shares/Graph – diese Links sind bereits „final“
                }

                // 1) direct (download=1 auch für 1drv)
                var url1 = OneDriveSharedLinkHelper.EnsureDirectDownload(fileUrl);
                var r1 = await TryDownloadAsync(url1, "direct", allowHtmlScrape: true, depth: 0);
                if (!string.IsNullOrWhiteSpace(r1)) return r1;

                // 2) finale Redirect-URL → /download...
                try
                {
                    using var probe = await _http.GetAsync(url1, ct);
                    var finalUri = probe.RequestMessage?.RequestUri;
                    if (finalUri != null)
                    {
                        var url2 = OneDriveSharedLinkHelper.RewriteFromFinalUri(finalUri);
                        var r2 = await TryDownloadAsync(url2, "rewrite", allowHtmlScrape: true, depth: 0);
                        if (!string.IsNullOrWhiteSpace(r2)) return r2;
                    }
                }
                catch (Exception ex)
                {
                    attempts.Add((url1, "probe", null, null, ex.GetType().Name + ": " + ex.Message));
                }

                // 3) Shares-API
                var apiUrl = OneDriveSharedLinkHelper.BuildSharesApiContentUrl(fileUrl);
                var r3 = await TryDownloadAsync(apiUrl, "shares-api", allowHtmlScrape: false, depth: 0);
                if (!string.IsNullOrWhiteSpace(r3)) return r3;

                // 4) Graph-API
                var graphUrl = OneDriveSharedLinkHelper.BuildGraphSharesContentUrl(fileUrl);
                var r4 = await TryDownloadAsync(graphUrl, "graph-api", allowHtmlScrape: false, depth: 0);
                if (!string.IsNullOrWhiteSpace(r4)) return r4;

                // Diagnose + Pfad anzeigen
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Setup-Datei konnte nicht heruntergeladen werden. Prüfen Sie den öffentlichen Link in der version.json.");
                sb.AppendLine(); sb.AppendLine("Versuche:");
                foreach (var t in attempts)
                    sb.AppendLine($"[{t.Stage}] {t.Url}  status={(t.Status?.ToString() ?? "-")}  media={t.MediaType ?? "-"}  note={t.Note ?? "-"}");
                sb.AppendLine(); sb.AppendLine("Log: " + logPath);

                throw new InvalidOperationException(sb.ToString());
            }
            finally
            {
                // Log immer schreiben
                try
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var t in attempts)
                        sb.AppendLine($"[{t.Stage}] {t.Url}  status={(t.Status?.ToString() ?? "-")}  media={t.MediaType ?? "-"}  note={t.Note ?? "-"}");
                    File.WriteAllText(logPath, sb.ToString(), System.Text.Encoding.UTF8);
                }
                catch { }
            }
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
