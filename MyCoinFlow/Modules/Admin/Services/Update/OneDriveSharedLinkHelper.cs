using System;
using System.Text;

namespace MyCoinFlow.Services.Update
{
    /// <summary>
    /// Hilfsfunktionen zur robusten Auflösung von öffentlichen OneDrive-Share-Links
    /// auf echte Download-Endpunkte (ohne HTML/Anmeldung).
    /// </summary>
    public static class OneDriveSharedLinkHelper
    {
        /// <summary>
        /// Für Nicht-1drv-Links "download=1" anhängen; 1drv.ms lassen wir unverändert (die Umleitung erledigt OneDrive).
        /// </summary>
        public static string EnsureDirectDownload(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;

            // 1drv.ms: erfahrungsgemäß Vorschau-HTML → direkt download=1 anhängen
            if (url.IndexOf("1drv.ms/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return url.Contains("download=", StringComparison.OrdinalIgnoreCase)
                    ? url
                    : (url.Contains("?") ? url + "&download=1" : url + "?download=1");
            }

            // onedrive.live.com & andere: ebenfalls download=1, falls nicht vorhanden
            return url.Contains("download=", StringComparison.OrdinalIgnoreCase)
                ? url
                : (url.Contains("?") ? url + "&download=1" : url + "?download=1");
        }


        /// <summary>
        /// Finale Redirect-URI von onedrive.live.com etc. in eine Download-URL umschreiben (download=1 erzwingen).
        /// </summary>
        public static string RewriteFromFinalUri(Uri finalUri)
        {
            if (finalUri == null) return string.Empty;
            var u = finalUri.ToString();

            // Falls schon eine saubere /download-URL → download=1 sicherstellen
            if (u.Contains("onedrive.live.com", StringComparison.OrdinalIgnoreCase) &&
                u.Contains("/download", StringComparison.OrdinalIgnoreCase))
            {
                if (!u.Contains("download=", StringComparison.OrdinalIgnoreCase))
                    u += (u.Contains("?") ? "&" : "?") + "download=1";
                return u;
            }

            // redir-/view-Links in /download umschreiben und unnötige Parameter entsorgen
            if (u.Contains("onedrive.live.com", StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(u);
                var q = System.Web.HttpUtility.ParseQueryString(builder.Query);

                var resid = q["resid"] ?? q["id"];
                var authkey = q["authkey"];
                var cid = q["cid"];

                var clean = "https://onedrive.live.com/download?";
                if (!string.IsNullOrWhiteSpace(resid)) clean += "resid=" + Uri.EscapeDataString(resid);
                if (!string.IsNullOrWhiteSpace(authkey)) clean += (clean.EndsWith("?") ? "" : "&") + "authkey=" + Uri.EscapeDataString(authkey);
                if (!string.IsNullOrWhiteSpace(cid)) clean += (clean.EndsWith("?") ? "" : "&") + "cid=" + Uri.EscapeDataString(cid);
                // Anonymer Download-Hinweis
                clean += (clean.EndsWith("?") ? "" : "&") + "em=2";
                // sicherheitshalber download=1
                clean += "&download=1";
                return clean;
            }

            // 1drv.ms lassen wir unverändert (wird separat behandelt)
            return u;
        }


        /// <summary>
        /// Shares-API Content-URL (api.onedrive.com) bauen: https://api.onedrive.com/v1.0/shares/u!{b64}/root/content
        /// </summary>
        public static string BuildSharesApiContentUrl(string sharedUrl)
        {
            var token = BuildSharesApiToken(sharedUrl);
            return $"https://api.onedrive.com/v1.0/shares/{token}/root/content";
        }

        /// <summary>
        /// Graph-API Content-URL bauen (zweites Standbein):
        /// https://graph.microsoft.com/v1.0/shares/u!{b64}/driveItem/content
        /// Für öffentliche Consumer-Shares funktioniert in der Praxis api.onedrive.com häufiger,
        /// aber wir probieren Graph als zusätzlichen Fallback.
        /// </summary>
        public static string BuildGraphSharesContentUrl(string sharedUrl)
        {
            var token = BuildSharesApiToken(sharedUrl);
            return $"https://graph.microsoft.com/v1.0/shares/{token}/driveItem/content";
        }

        /// <summary>
        /// Aus einem OneDrive-Share-Link das "u!{base64url(originalUrl)}" bauen.
        /// </summary>
        private static string BuildSharesApiToken(string sharedUrl)
        {
            if (string.IsNullOrWhiteSpace(sharedUrl))
                throw new ArgumentException(nameof(sharedUrl));

            // Original-URL als Base64URL (RFC 4648 ohne Padding) kodieren
            var b64 = Base64UrlEncode(sharedUrl);
            return "u!" + b64;
        }

        private static string Base64UrlEncode(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var b64 = Convert.ToBase64String(bytes);

            // URL-safe machen: '+' -> '-', '/' -> '_', '=' entfernen
            return b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static bool Is1Drv(string url)
        {
            return url.Contains("1drv.ms/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
