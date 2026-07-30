using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Öffnet eine Websuche zum Buchungstext einer Transaktion im Standardbrowser,
    /// um kryptische Händler-/Buchungstexte zu recherchieren (welcher Artikel /
    /// welche Dienstleistung steckt dahinter?).
    /// Der Text wird vorher bereinigt: IBANs, lange Referenznummern, UETR,
    /// Datums- und Betragsangaben verschlechtern die Suche massiv und werden entfernt.
    /// </summary>
    public static class WebRechercheService
    {
        public static string? BuildQuery(string? buchungstext, string? gegenpartei = null)
        {
            var src = string.Join(" ",
                new[] { buchungstext, gegenpartei }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));

            if (string.IsNullOrWhiteSpace(src)) return null;

            // IBANs, UETR, lange Ziffernfolgen (Referenzen), Datumsangaben, Beträge
            var t = Regex.Replace(src, @"[A-Z]{2}\d{2}[A-Z0-9]{4,}", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"UETR[:\s A-Z0-9\-]+", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b\d{1,2}\.\d{1,2}\.\d{2,4}\b", " ");
            t = Regex.Replace(t, @"\b\d+[.,]\d{2}\b", " ");
            t = Regex.Replace(t, @"\b\d{5,}\b", " ");

            // Generische Zahlungs-Floskeln, die jede Suche verwässern
            var stop = new[]
            {
                "EINKAUF","ZAHLUNG","KARTENZAHLUNG","BELASTUNG","GUTSCHRIFT","TWINT",
                "DEBIT","KREDIT","VALUTA","REFERENZ","MITTEILUNG","CHF","EUR","USD","VOM"
            };

            var words = Regex.Matches(t, @"[\p{L}\p{Nd}][\p{L}\p{Nd}\.\*\-&']*")
                             .Select(m => m.Value.Trim('-', '.', '\''))
                             .Where(w => w.Length >= 2)
                             .Where(w => !stop.Contains(w.ToUpperInvariant()))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .Take(8)
                             .ToList();

            if (words.Count == 0) return null;

            return string.Join(" ", words);
        }

        public static bool OpenSearch(string? buchungstext, string? gegenpartei = null)
        {
            var query = BuildQuery(buchungstext, gegenpartei);
            if (string.IsNullOrWhiteSpace(query)) return false;

            var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query);

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            return true;
        }
    }
}
