using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Reine Textanalyse für DMS-Dokumente (kein DB-/UI-Zugriff):
    /// Dokumentdatum, sprechender Titel und Betragskandidaten aus dem OCR-/Textlayer-Inhalt.
    /// Wird von DmsWatcherService genutzt, um eingehende Dateien sinnvoll zu benennen
    /// und Transaktions-Matching-Kandidaten zu bewerten.
    /// </summary>
    public static class DmsDocumentAnalyzer
    {
        public const int TitleMaxLength = 40; // gleiche Konvention wie AttachmentService.AttachFreestanding

        private static readonly string[] DateKeywords =
        {
            "rechnungsdatum", "datum", "vom", "ausgestellt am", "rechnung vom", "belegdatum"
        };

        private static readonly Dictionary<string, int> GermanMonths = new(StringComparer.OrdinalIgnoreCase)
        {
            ["januar"] = 1, ["jan"] = 1,
            ["februar"] = 2, ["feb"] = 2,
            ["märz"] = 3, ["maerz"] = 3, ["mrz"] = 3, ["mär"] = 3,
            ["april"] = 4, ["apr"] = 4,
            ["mai"] = 5,
            ["juni"] = 6, ["jun"] = 6,
            ["juli"] = 7, ["jul"] = 7,
            ["august"] = 8, ["aug"] = 8,
            ["september"] = 9, ["sept"] = 9, ["sep"] = 9,
            ["oktober"] = 10, ["okt"] = 10,
            ["november"] = 11, ["nov"] = 11,
            ["dezember"] = 12, ["dez"] = 12,
        };

        // 31.12.2025 / 31.12.25
        private static readonly Regex DotDatePattern = new(@"\b(\d{1,2})\.(\d{1,2})\.(\d{2,4})\b", RegexOptions.Compiled);
        // 2025-12-31
        private static readonly Regex IsoDatePattern = new(@"\b(\d{4})-(\d{1,2})-(\d{1,2})\b", RegexOptions.Compiled);
        // 3. März 2026 / 3. Mrz. 2026 (übliches Format auf CH/DE-Rechnungen)
        private static readonly Regex GermanTextDatePattern = new(
            @"\b(\d{1,2})\.\s*([A-Za-zÀ-ÿ]+)\.?\s+(\d{4})\b", RegexOptions.Compiled);

        // Tausendertrennzeichen kommen je nach Rechnungssoftware unterschiedlich vor: Schweizer
        // Apostroph (6'400.00), aber auch Komma (6,400.00, international) oder Punkt
        // (6.400,00, deutsches Format). Alle drei müssen als Gruppierung erkannt werden, sonst
        // wird die Zahl an der falschen Stelle zerschnitten (siehe TryParseAmount).
        private static readonly Regex AmountPattern = new(
            @"(?:CHF|Fr\.?|EUR|€)?\s*(\d{1,3}(?:[.,'’]\d{3})*(?:[.,]\d{2})?)\s*(?:CHF|Fr\.?|EUR|€)?",
            RegexOptions.Compiled);

        private static readonly string[] AmountKeywords =
        {
            "total", "rechnungsbetrag", "zu zahlen", "zu bezahlen", "gesamtbetrag", "endbetrag", "betrag",
            "gesamt", "saldo"
        };

        // Nur Treffer, die überhaupt wie ein formatierter Geldbetrag aussehen (Dezimal- oder
        // Tausendertrennzeichen), dürfen von der Nähe zu einem AmountKeyword profitieren. Sonst
        // werden blosse Positions-/Zeilennummern (1, 2, 3...) fälschlich aufgewertet, wenn eine
        // Tabellenkopfzeile ("... Betrag") im Text direkt vor jeder Position wiederholt wird.
        private static readonly char[] AmountSeparators = { '.', ',', '\'', '’' };

        private static readonly char[] InvalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();

        /// <summary>
        /// Sucht im Text nach einem plausiblen Dokumentdatum (numerisch dd.mm.yyyy, ISO yyyy-mm-dd
        /// oder ausgeschrieben "3. März 2026"). Bevorzugt Treffer in der Nähe typischer
        /// Schlüsselwörter. Fällt auf <paramref name="fallback"/> zurück, wenn nichts Plausibles
        /// gefunden wird (z. B. kein Text, reines Bild ohne erkennbares Datum).
        /// </summary>
        public static DateTime ExtractDocumentDate(string? text, DateTime fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;

            var candidates = new List<(DateTime Date, int Score)>();
            var lowerText = text.ToLowerInvariant();

            void AddCandidate(Match m, DateTime? date)
            {
                if (!date.HasValue) return;
                var d = date.Value;

                // Unplausible Werte verwerfen: nicht mehr als 3 Tage in der Zukunft,
                // nicht älter als 10 Jahre (Scans alter Belege sind unwahrscheinlich, aber möglich).
                if (d > DateTime.Today.AddDays(3)) return;
                if (d < DateTime.Today.AddYears(-10)) return;

                int score = 0;
                int contextStart = Math.Max(0, m.Index - 30);
                int contextLen = Math.Min(60, lowerText.Length - contextStart);
                string context = lowerText.Substring(contextStart, contextLen);
                foreach (var kw in DateKeywords)
                {
                    if (context.Contains(kw)) { score += 10; break; }
                }

                candidates.Add((d, score));
            }

            foreach (Match m in DotDatePattern.Matches(text))
                AddCandidate(m, TryParseDotDate(m));

            foreach (Match m in IsoDatePattern.Matches(text))
                AddCandidate(m, TryParseIsoDate(m));

            foreach (Match m in GermanTextDatePattern.Matches(text))
                AddCandidate(m, TryParseGermanTextDate(m));

            if (candidates.Count == 0) return fallback;

            return candidates
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.Date) // bei Gleichstand: neueres Datum bevorzugen
                .First().Date;
        }

        private static DateTime? TryParseDotDate(Match m)
        {
            try
            {
                int day = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                int year = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                if (year < 100) year += 2000;
                return new DateTime(year, month, day);
            }
            catch { return null; } // ungültiges Datum (z. B. 31.02.2025) -> verwerfen
        }

        private static DateTime? TryParseIsoDate(Match m)
        {
            try
            {
                int year = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                int day = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                return new DateTime(year, month, day);
            }
            catch { return null; }
        }

        private static DateTime? TryParseGermanTextDate(Match m)
        {
            try
            {
                int day = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (!GermanMonths.TryGetValue(m.Groups[2].Value, out var month)) return null;
                int year = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                return new DateTime(year, month, day);
            }
            catch { return null; }
        }

        /// <summary>
        /// Ermittelt einen sprechenden Titel: bevorzugt einen bereits erkannten Adressnamen
        /// (Gegenpartei/Aussteller, vom Aufrufer per Adressbuch-Abgleich ermittelt – ein Firmenlogo
        /// im PDF ist meist nur Bild, kein Text, daher taugt "erste Textzeile" nicht als Heuristik).
        /// Ohne Treffer wird der ursprüngliche Dateiname beibehalten, statt beliebigen Fliesstext
        /// zu verwenden (der oft die Adresse des Empfängers statt des Ausstellers enthält).
        /// Wird auf <see cref="TitleMaxLength"/> Zeichen gekürzt, damit alle Dokumente die
        /// gleiche Namensstruktur haben.
        /// </summary>
        public static string ExtractTitle(string? text, string? matchedAdresseName, string fallbackFromFileName)
        {
            var raw = !string.IsNullOrWhiteSpace(matchedAdresseName) ? matchedAdresseName : fallbackFromFileName;
            return SanitizeAndTruncate(raw);
        }

        private static string SanitizeAndTruncate(string raw)
        {
            var sb = new StringBuilder(raw.Trim());
            foreach (var c in InvalidFileNameChars)
                sb.Replace(c, '-');
            sb.Replace(' ', '-');

            var result = sb.ToString().Trim('-');
            if (result.Length == 0) result = "Dokument";
            if (result.Length > TitleMaxLength) result = result.Substring(0, TitleMaxLength).TrimEnd('-');
            return result;
        }

        /// <summary>
        /// Liefert Betragskandidaten aus dem Text, beste Vermutung zuerst (Nähe zu
        /// Schlüsselwörtern wie "Total"/"Rechnungsbetrag" wird höher gewichtet). Der Aufrufer
        /// sollte bei der Transaktionssuche mehrere Kandidaten der Reihe nach probieren, falls
        /// die Gewichtung im Einzelfall daneben liegt.
        /// </summary>
        public static List<decimal> ExtractAmountCandidates(string? text) =>
            ExtractAmountCandidatesScored(text).Select(r => r.Amount).ToList();

        /// <summary>
        /// Wie <see cref="ExtractAmountCandidates"/>, liefert zusätzlich den Score je Kandidat
        /// (0 = keine Nähe zu einem Schlüsselwort erkannt, reine Fliesstext-Zahl; 10 = in der Nähe
        /// von "Total"/"Betrag"/... gefunden). Der Aufrufer kann damit einen Treffer über einen
        /// Score-0-Kandidaten bewusst vorsichtiger behandeln (z. B. Bestätigung statt stillem
        /// Auto-Link), weil ein zufälliger Fliesstext-Betrag auch zufällig zu einer unabhängigen
        /// Transaktion passen kann.
        /// </summary>
        public static List<(decimal Amount, int Score)> ExtractAmountCandidatesScored(string? text)
        {
            var result = new List<(decimal Amount, int Score)>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            var lowerText = text.ToLowerInvariant();

            foreach (Match m in AmountPattern.Matches(text))
            {
                var raw = m.Groups[1].Value;
                if (!TryParseAmount(raw, out var amount)) continue;
                if (amount <= 0 || amount > 1_000_000m) continue;

                int score = 0;
                if (raw.IndexOfAny(AmountSeparators) >= 0)
                {
                    int contextStart = Math.Max(0, m.Index - 25);
                    int contextLen = Math.Min(50, lowerText.Length - contextStart);
                    string context = lowerText.Substring(contextStart, contextLen);
                    foreach (var kw in AmountKeywords)
                    {
                        if (context.Contains(kw)) { score += 10; break; }
                    }
                }

                result.Add((amount, score));
            }

            return result
                .GroupBy(r => r.Amount)
                .Select(g => (Amount: g.Key, Score: g.Max(x => x.Score)))
                .OrderByDescending(r => r.Score)
                .ThenByDescending(r => r.Amount)
                .ToList();
        }

        private static bool TryParseAmount(string raw, out decimal amount)
        {
            amount = 0m;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            // Apostroph ist im Schweizer Format immer Tausendertrennzeichen, nie Dezimalzeichen.
            var cleaned = raw.Replace("'", "").Replace("’", "");

            bool hasDot = cleaned.Contains('.');
            bool hasComma = cleaned.Contains(',');

            if (hasDot && hasComma)
            {
                // Gemischtes Format: das SPÄTER stehende Zeichen ist der Dezimaltrenner
                // (z.B. "6.400,00" -> Komma dezimal; "6,400.00" -> Punkt dezimal), alle
                // vorherigen Vorkommen des jeweils anderen Zeichens sind Tausendergruppen.
                cleaned = cleaned.LastIndexOf(',') > cleaned.LastIndexOf('.')
                    ? cleaned.Replace(".", "").Replace(',', '.')
                    : cleaned.Replace(",", "");
            }
            else if (hasComma)
            {
                // Nur Komma: meist Dezimalkomma (13,50). Stehen aber genau 3 Ziffern danach,
                // ist es eher eine Tausendergruppe ohne Nachkommastellen (6,400 = sechstausend).
                int digitsAfter = cleaned.Length - cleaned.LastIndexOf(',') - 1;
                cleaned = digitsAfter == 3 ? cleaned.Replace(",", "") : cleaned.Replace(',', '.');
            }
            else if (hasDot)
            {
                int digitsAfter = cleaned.Length - cleaned.LastIndexOf('.') - 1;
                if (digitsAfter == 3) cleaned = cleaned.Replace(".", "");
            }

            return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
        }
    }
}
