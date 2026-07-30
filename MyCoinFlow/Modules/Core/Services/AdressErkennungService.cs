using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MyCoinFlow.Models;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Erkennung von Adressen via IBAN, Name oder Text-Alias.
    /// </summary>
    public class AdressErkennungService
    {
        private readonly DatabaseService _db = new();

        // Caches
        private readonly Dictionary<string, int> _iban2adr = new();   // IBAN (normiert) -> AdresseId
        private readonly Dictionary<string, int> _name2adr = new();   // Name (normiert) -> AdresseId
        private List<(int AdresseId, string TextNorm, string Modus)> _aliases = new(); // alle Aliase

        public AdressErkennungService() => Reload();

        public void Reload()
        {
            _iban2adr.Clear();
            _name2adr.Clear();

            var adressen = _db.LadeAdressen();
            foreach (var a in adressen)
            {
                if (!string.IsNullOrWhiteSpace(a.IBAN))
                {
                    var ibanN = NormalizeIban(a.IBAN!);
                    if (!_iban2adr.ContainsKey(ibanN))
                        _iban2adr.Add(ibanN, a.Id);
                }

                var nameN = NormalizeText(a.Name);
                if (!_name2adr.ContainsKey(nameN))
                    _name2adr.Add(nameN, a.Id);
            }

            // Aliase nachziehen
            _aliases = _db.LadeAdressAliase()
                          .Select(al => (al.AdresseId, NormalizeText(al.Text), string.IsNullOrWhiteSpace(al.Modus) ? "Exact" : al.Modus.Trim()))
                          .ToList();

            // NEU: Adressname selbst als impliziter PrefixSeq-Kandidat.
            // Ohne das würde z. B. "TWINT Post CH AG" die bereits erfasste
            // Adresse "Post AG" nie finden, solange dafür noch kein Alias
            // manuell angelernt wurde - der Name selbst wurde bisher nur
            // für den exakten Vergleich (Schritt 2 in TryMatch) genutzt.
            // Mindestlänge als Schutz gegen zu kurze/generische Namen.
            foreach (var a in adressen)
            {
                var nameN = NormalizeText(a.Name);
                if (nameN.Length >= 4)
                    _aliases.Add((a.Id, nameN, "PrefixSeq"));
            }
        }

        /// <summary>
        /// Versucht eine Adresse zu finden.
        /// Priorität: IBAN -> Name (exakt) -> Alias (gegen Name und Text; längster Treffer gewinnt).
        /// </summary>
        public int? TryMatch(string? ibanRawOrNorm, string? nameRaw, string? textRaw)
        {
            // 1) IBAN
            if (!string.IsNullOrWhiteSpace(ibanRawOrNorm))
            {
                var ibanN = NormalizeIban(ibanRawOrNorm!);
                if (_iban2adr.TryGetValue(ibanN, out var adrIdIban))
                    return adrIdIban;
            }

            // 2) Name exakt
            var nameN = string.IsNullOrWhiteSpace(nameRaw) ? null : NormalizeText(nameRaw!);
            if (!string.IsNullOrWhiteSpace(nameN) && _name2adr.TryGetValue(nameN!, out var adrIdName))
                return adrIdName;

            // 3) Alias (gegen Name/Text) – längster Treffer gewinnt
            var textN = string.IsNullOrWhiteSpace(textRaw) ? null : NormalizeText(textRaw!);
            int? bestAdr = null;
            int bestLen = 0;

            foreach (var (adrId, alias, modus) in _aliases)
            {
                if (!string.IsNullOrWhiteSpace(nameN) && AliasMatches(alias, modus, nameN!) && alias.Length > bestLen)
                {
                    bestLen = alias.Length; bestAdr = adrId;
                }
                if (!string.IsNullOrWhiteSpace(textN) && AliasMatches(alias, modus, textN!) && alias.Length > bestLen)
                {
                    bestLen = alias.Length; bestAdr = adrId;
                }
            }
            return bestAdr;
        }

        public int? TryMatch(string? ibanRawOrNorm, string? nameRaw) => TryMatch(ibanRawOrNorm, nameRaw, null);

        private static bool AliasMatches(string aliasNorm, string modus, string hayNorm) =>
            modus switch
            {
                "Exact" => string.Equals(hayNorm, aliasNorm, StringComparison.Ordinal),
                "StartsWith" => hayNorm.StartsWith(aliasNorm, StringComparison.Ordinal),
                "EndsWith" => hayNorm.EndsWith(aliasNorm, StringComparison.Ordinal),
                "PrefixSeq" => PrefixSeqMatch(aliasNorm, hayNorm),
                _ => hayNorm.Contains(aliasNorm, StringComparison.Ordinal) // Default: Contains
            };

        // Matcht eine Sequenz von Wort-Prefixen in Reihenfolge (Wörter dazwischen sind erlaubt)
        private static bool PrefixSeqMatch(string aliasNorm, string hayNorm)
        {
            var parts = aliasNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            var tokens = hayNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int pos = 0;
            foreach (var p in parts)
            {
                bool found = false;
                while (pos < tokens.Length)
                {
                    if (tokens[pos].StartsWith(p, StringComparison.Ordinal))
                    {
                        found = true; pos++; break;
                    }
                    pos++;
                }
                if (!found) return false;
            }
            return true;
        }

        private static string NormalizeIban(string raw) =>
            new string(raw.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();

        private static string NormalizeText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var up = raw.ToUpperInvariant();
            var cleaned = Regex.Replace(up, @"[^A-Z0-9]+", " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned;
        }

        // ---------- Alias API ----------

        /// <summary>Fügt einen Alias (Exact/StartsWith/EndsWith/Contains/PrefixSeq) hinzu und aktualisiert Caches.</summary>
        public void AddAlias(int adresseId, string aliasText, string modus = "Exact")
        {
            if (string.IsNullOrWhiteSpace(aliasText)) return;
            var norm = NormalizeText(aliasText);
            if (string.IsNullOrWhiteSpace(norm)) return;

            _db.SpeichereAdressAlias(adresseId, norm, string.IsNullOrWhiteSpace(modus) ? "Exact" : modus);

            _aliases.Add((adresseId, norm, modus));
            if (modus.Equals("Exact", StringComparison.Ordinal))
                _name2adr[norm] = adresseId;
        }

        /// <summary>
        /// Erzeugt aus Buchungstext automatisch eine prägnante Präfix-Sequenz
        /// (z. B. "EINK TWIN BRAC") und speichert sie als Modus "PrefixSeq", sofern neu.
        /// </summary>
        public void AddAliasFromTextIfNew(int adresseId, string? bookingText)
        {
            var cand = ExtractPrefixSeqAlias(bookingText);
            if (string.IsNullOrWhiteSpace(cand)) return;

            var norm = NormalizeText(cand!);
            if (_aliases.Any(a => a.AdresseId == adresseId && a.TextNorm == norm && a.Modus == "PrefixSeq"))
                return;

            _db.SpeichereAdressAlias(adresseId, norm, "PrefixSeq");
            _aliases.Add((adresseId, norm, "PrefixSeq"));
        }

        // --------- Alias-Erzeugung: längere Präfix-Sequenz aus mehr Wörtern ---------
        private static string? ExtractPrefixSeqAlias(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // Rauschen entfernen (IBAN/UETR/Nummern)
            var t = Regex.Replace(text, @"[A-Z]{2}\d{2}[A-Z0-9]{4,}", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b\d{6,}\b", " ");
            t = Regex.Replace(t, @"UETR[:\sA-Z0-9\-]+", " ", RegexOptions.IgnoreCase);

            // Tokenisieren: >=3 Zeichen, alles Upper/space-normalisiert
            var tokens = Regex.Matches(t.ToUpperInvariant(), @"[A-Z0-9]{3,}")
                              .Cast<Match>()
                              .Select(m => m.Value)
                              .ToList();
            if (tokens.Count == 0) return null;

            // Optional: ganz leichte Ent-Raus chung; KEINE harten Stopwords mehr
            // (Wir behalten "EINKAUF", "ZAHLUNG", etc., damit die Kette insgesamt spezifischer wird.)

            // Präfix-Länge je Token erhöhen (z. B. 5)
            const int prefixLen = 5;

            // Aus ALLEN Wörtern eine Sequenz bilden – zur Sicherheit begrenzen wir auf maxParts
            const int maxParts = 8; // genug spezifisch, aber nicht zu lang
            var selected = tokens.Count <= maxParts ? tokens : tokens.Take(maxParts).ToList();

            // Präfixe bauen, gleiche aufeinander folgende Präfixe deduplizieren
            var parts = new List<string>(selected.Count);
            string? last = null;
            foreach (var tk in selected)
            {
                var p = tk.Length <= prefixLen ? tk : tk.Substring(0, prefixLen);
                if (p != last) parts.Add(p);
                last = p;
            }

            // Ergebnis: z. B. "EINKA TWIN TWIN? BRACK ELECT" (bis zu 8 Teile)
            var alias = string.Join(" ", parts);
            // Mindestlänge, damit zu kurze Aliase verworfen werden
            return alias.Length >= 12 ? alias : null;
        }

    }
}
