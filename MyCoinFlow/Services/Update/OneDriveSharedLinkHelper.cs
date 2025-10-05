using System;
using System.Web;

namespace MyCoinFlow.Services.Update
{
    public static class OneDriveSharedLinkHelper
    {
        /// <summary>
        /// Wandelt übliche OneDrive-Share-Links in echte "download"-Links um.
        /// Unterstützt:
        /// - 1drv.ms/*  (erzwingt download=1)
        /// - onedrive.live.com/?id=...&cid=...  ->  onedrive.live.com/download?cid=...&resid=...&authkey=...
        /// - onedrive.live.com/?resid=...&cid=...
        /// Alle anderen Links: hängt ?download=1 bzw. &download=1 an (idempotent).
        /// </summary>
        public static string EnsureDirectDownload(string sharedUrl)
        {
            if (string.IsNullOrWhiteSpace(sharedUrl)) return sharedUrl;

            // 1drv.ms-Kurzlinks NICHT anfassen – sie leiten selbst korrekt auf live.com um
            try
            {
                var host = new Uri(sharedUrl).Host.ToLowerInvariant();
                if (host.Contains("1drv.ms"))
                    return sharedUrl;
            }
            catch
            {
                // Falls Uri-Parsing scheitert, lieber nichts anhängen
                return sharedUrl;
            }

            // Für alle anderen: download=1 anhängen, wenn nicht vorhanden
            return sharedUrl.IndexOf("download=", StringComparison.OrdinalIgnoreCase) >= 0
                ? sharedUrl
                : (sharedUrl.Contains("?") ? sharedUrl + "&download=1" : sharedUrl + "?download=1");
        }


        private static string AppendDownloadParam(string url)
        {
            if (url.IndexOf("download=", StringComparison.OrdinalIgnoreCase) >= 0)
                return url; // bereits vorhanden

            return url.Contains("?") ? url + "&download=1" : url + "?download=1";
        }

        public static string RewriteFromFinalUri(Uri finalUri)
        {
            var abs = finalUri.AbsoluteUri;
            var host = finalUri.Host.ToLowerInvariant();

            // Schon "echter" Download-Endpunkt?
            if (abs.Contains("onedrive.live.com/download", StringComparison.OrdinalIgnoreCase))
                return abs;

            // OneDrive (Consumer): zwei Varianten
            if (host.Contains("onedrive.live.com"))
            {
                try
                {
                    var q = System.Web.HttpUtility.ParseQueryString(finalUri.Query);
                    var cid = q.Get("cid");
                    var resid = q.Get("resid");
                    var id = q.Get("id");       // kann ein Pfad sein: "/personal/.../Documents/.../version.json"
                    var auth = q.Get("authkey");

                    // Variante A: klassische "cid/resid" – dann auf /download umbauen
                    if (!string.IsNullOrEmpty(cid) && !string.IsNullOrEmpty(resid))
                    {
                        var b = new UriBuilder("https://onedrive.live.com/download");
                        var qq = System.Web.HttpUtility.ParseQueryString(string.Empty);
                        qq["cid"] = cid;
                        qq["resid"] = resid;
                        if (!string.IsNullOrEmpty(auth)) qq["authkey"] = auth;
                        b.Query = qq.ToString()!;
                        return b.Uri.ToString();
                    }

                    // Variante B: "id=/personal/…&parent=…" (pfadbasierte Links)
                    // -> NICHT auf "/download" umbiegen, sondern nur "download=1" an den aktuellen Link hängen.
                    if (!string.IsNullOrEmpty(id) && (id.StartsWith("/personal/", StringComparison.OrdinalIgnoreCase) ||
                                                      id.Contains("%2Fpersonal%2F", StringComparison.OrdinalIgnoreCase)))
                    {
                        return AppendDownloadParam(abs);
                    }
                }
                catch
                {
                    // Fallback unten
                }

                // Generischer Fallback für live.com
                return AppendDownloadParam(abs);
            }

            // 1drv.ms und SharePoint: einfach "download=1" anhängen
            if (host.Contains("1drv.ms")) return AppendDownloadParam(abs);
            if (host.Contains(".sharepoint.com")) return AppendDownloadParam(abs);

            // Generischer Fallback
            return AppendDownloadParam(abs);
        }

        public static string BuildSharesApiContentUrl(string sharedUrl)
        {
            // OneDrive Shares API: https://api.onedrive.com/v1.0/shares/u!<base64url(sharedUrl)>/root/content
            var bytes = System.Text.Encoding.UTF8.GetBytes(sharedUrl);
            var b64 = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return $"https://api.onedrive.com/v1.0/shares/u!{b64}/root/content";
        }


    }
}
