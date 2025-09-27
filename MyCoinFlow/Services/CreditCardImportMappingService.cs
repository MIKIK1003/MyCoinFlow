using ExcelDataReader;
using Microsoft.Win32;
using MyCoinFlow.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FieldMapping = MyCoinFlow.Models.FieldMapping;
using ImportSchema = MyCoinFlow.Models.ImportSchema;


namespace MyCoinFlow.Services
{
    
    /// <summary>
    /// Kapselt: Master-Header, Schemas, Feld-Mappings und die eigentliche Umbenennung der DataTable.
    /// Greift NICHT in deinen nachfolgenden Workflow ein.
    /// </summary>
    public class CreditCardImportMappingService
    {
        
        
        private readonly ICreditCardImportRepository _db; // Repository nur für Mapping-Tabellen
        private readonly List<string> _masterHeaders; // aus deinem funktionierenden Excel (in exakt der Schreibweise)



        // oben: private readonly ICreditCardImportRepository _db;
        public CreditCardImportMappingService(ICreditCardImportRepository db)
        {
            _db = db;
            _masterHeaders = LoadMasterHeaders();
            EnsureMasterSchemaExists();
        }


        // === Öffentliche API ===

        public IReadOnlyList<string> GetMasterHeaders() => _masterHeaders;

        public IList<ImportSchema> GetSchemas() => _db.ImportSchemasGetAll();

        public ImportSchema CreateSchema(string name) => _db.ImportSchemaInsert(new ImportSchema { Name = name, IsMaster = false });

        public void DeleteSchema(int schemaId)
        {
            _db.FieldMappingsDeleteBySchema(schemaId);
            _db.ImportSchemaDelete(schemaId);
        }

        public void UpdateSchemaName(int schemaId, string name) => _db.ImportSchemaUpdateName(schemaId, name);


        public IList<FieldMapping> GetFieldMappings(int schemaId) => _db.FieldMappingsGetBySchema(schemaId);

        public void SaveMappings(int schemaId, IList<FieldMapping> items)
        {
            // defensiv: gültige MasterHeader / SourceHeader
            foreach (var m in items)
            {
                if (!_masterHeaders.Contains(m.MasterHeader))
                    throw new InvalidOperationException($"Unbekannter Master-Header: {m.MasterHeader}");
                if (string.IsNullOrWhiteSpace(m.SourceHeader))
                    throw new InvalidOperationException("SourceHeader darf nicht leer sein.");
            }
            _db.FieldMappingsReplace(schemaId, items.ToList());
        }

        public DataTable ApplyMappingIfNeeded(DataTable raw)
        {
            // A) Harte Konvertierung TOP-Card → Master (inhaltlich, fix)
            if (TryConvertTopCardXlsx(raw, out var master))
                return master;

            // B) Master-Erkennung (Kartennummer optional)
            var headers = raw.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
            var isMaster = IsMaster(headers);  // prüft nur Pflichtfelder

            // C) Falls nicht Master: gespeichertes Schema anwenden (deine UI-Maps)
            if (!isMaster)
            {
                var candidates = _db.ImportSchemasGetAll().Where(s => !s.IsMaster).ToList();
                foreach (var schema in candidates)
                {
                    var maps = _db.FieldMappingsGetBySchema(schema.Id);
                    if (maps.Count == 0) continue;

                    // passt dieses Schema? -> alle SourceHeader vorhanden
                    if (maps.All(m => headers.Any(h => EqualsNormalized(h, m.SourceHeader))))
                    {
                        // 1) Spalten auf Master umbenennen
                        foreach (var m in maps)
                        {
                            var col = raw.Columns.Cast<DataColumn>()
                                .FirstOrDefault(c => EqualsNormalized(c.ColumnName, m.SourceHeader));
                            if (col != null) col.ColumnName = m.MasterHeader;
                        }

                        // 2) Fallback „DefaultValue“ anwenden, wenn Source leer/fehlend
                        foreach (var m in maps)
                        {
                            if (string.IsNullOrWhiteSpace(m.DefaultValue)) continue;

                            // Zielspalte (Master) finden – nach dem Umbenennen vorhanden
                            var destCol = raw.Columns.Cast<DataColumn>()
                                .FirstOrDefault(c => EqualsNormalized(c.ColumnName, m.MasterHeader));
                            if (destCol == null) continue;

                            // Quelle (ursprünglicher SourceHeader) existiert evtl. nicht mehr (umbenannt) – deshalb neu suchen:
                            var srcCol = raw.Columns.Cast<DataColumn>()
                                .FirstOrDefault(c => EqualsNormalized(c.ColumnName, m.SourceHeader));

                            foreach (DataRow rr in raw.Rows)
                            {
                                bool missing = srcCol == null || string.IsNullOrWhiteSpace(rr[srcCol]?.ToString());
                                if (missing)
                                    rr[destCol] = m.DefaultValue;
                            }
                        }

                        break; // Schema angewendet → raus
                    }
                }
            }

            // D) Pflichtspalten sicherstellen (für deinen bestehenden Reader)
            void EnsureColumn(string name, object? defaultValue = null)
            {
                var col = raw.Columns.Cast<DataColumn>().FirstOrDefault(c => EqualsNormalized(c.ColumnName, name));
                if (col == null)
                {
                    raw.Columns.Add(name);
                    if (defaultValue != null)
                        foreach (DataRow rr in raw.Rows) rr[name] = defaultValue;
                }
            }

            EnsureColumn("Transaktionsdatum");
            EnsureColumn("Beschreibung");
            EnsureColumn("Händler");
            EnsureColumn("Händlerkategorie");
            EnsureColumn("Betrag");
            EnsureColumn("Debit/Kredit");
            EnsureColumn("Kartennummer"); // optional, leer ist ok

            return raw;
        }





        // Hilfen für UI

        public List<string>? PickHeadersFromSampleFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Musterdatei wählen",
                Filter = "Excel|*.xlsx;*.xls|CSV|*.csv|Alle Dateien|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return null;

            var path = dlg.FileName;
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

            bool IsBanned(string? h)
            {
                if (string.IsNullOrWhiteSpace(h)) return true;
                var s = Normalize(h);
                return s.StartsWith("abgeschlossene zahlungen"); // TOP-Card Überschrift, keine echte Headerzeile
            }

            try
            {
                if (ext == ".csv")
                {
                    var first = System.IO.File.ReadLines(path).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(first)) return null;
                    var cols = first.Split(new[] { ';', ',', '\t' }, StringSplitOptions.None)
                                    .Select(h => h.Trim())
                                    .Where(h => !IsBanned(h))
                                    .ToList();
                    return cols.Count > 0 ? cols : null;
                }

                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                using var fs = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);

                // 1) Roh lesen (ohne Headerinterpretation), um die tatsächliche Header-Zeile zu finden
                using (var readerRaw = ExcelDataReader.ExcelReaderFactory.CreateReader(fs))
                {
                    var dsRaw = readerRaw.AsDataSet(); // keine Header
                    if (dsRaw.Tables.Count == 0 || dsRaw.Tables[0].Rows.Count == 0) return null;
                    var raw = dsRaw.Tables[0];

                    var knownTop = new HashSet<string>(new[]
                    {
                        "Ausführungsdatum","Auftragsnummer","Auftragsart","Belastungskonto",
                        "Status","Rubrik","IBAN","Begünstigter","Zahlungsgrund","Eigene Notiz",
                        "Währung","Betrag"
                        }.Select(Normalize));

                    int bestRow = -1, bestScore = -1;
                    int maxRow = Math.Min(10, raw.Rows.Count);
                    for (int r = 0; r < maxRow; r++)
                    {
                        int score = 0;
                        for (int c = 0; c < raw.Columns.Count; c++)
                        {
                            var val = raw.Rows[r][c]?.ToString()?.Trim();
                            if (string.IsNullOrWhiteSpace(val)) continue;
                            var n = Normalize(val);
                            if (knownTop.Contains(n)) score += 2;
                            if (n is "betrag" or "wahrung" or "iban") score += 1;
                        }
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestRow = r;
                        }
                    }

                    if (bestRow >= 0 && bestScore >= 3)
                    {
                        var headers = new List<string>();
                        for (int c = 0; c < raw.Columns.Count; c++)
                        {
                            var val = raw.Rows[bestRow][c]?.ToString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(val) && !IsBanned(val))
                                headers.Add(val);
                        }
                        if (headers.Count > 0) return headers;
                    }
                }

                // 2) Fallback: normal mit UseHeaderRow
                fs.Position = 0;
                using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(fs);
                var ds = reader.AsDataSet(new ExcelDataReader.ExcelDataSetConfiguration
                {
                    ConfigureDataTable = _ => new ExcelDataReader.ExcelDataTableConfiguration { UseHeaderRow = true }
                });
                if (ds.Tables.Count == 0) return null;
                var t = ds.Tables[0];
                var excelHeaders = t.Columns.Cast<System.Data.DataColumn>()
                                    .Select(c => c.ColumnName.Trim())
                                    .Where(h => !IsBanned(h))
                                    .ToList();
                return excelHeaders.Count > 0 ? excelHeaders : null;
            }
            catch
            {
                return null;
            }
        }

        // Mappt eine TOP-Card CSV (Einkaufsdatum/Buchungstext/Branche/Belastung/Gutschrift)
        // robust auf die Master-Header. Gibt true zurück, wenn eine Umformung stattfand.
        private bool TryMapTopCardCsv(DataTable t)
        {
            bool Has(string name) => t.Columns.Cast<DataColumn>().Any(c => EqualsNormalized(c.ColumnName, name));
            string? Find(string name)
                => t.Columns.Cast<DataColumn>().FirstOrDefault(c => EqualsNormalized(c.ColumnName, name))?.ColumnName;

            // Erkennen: typische TOP-Card-Spalten vorhanden?
            var hasTopSignature = Has("Einkaufsdatum") && Has("Buchungstext") && (Has("Belastung") || Has("Gutschrift"));
            if (!hasTopSignature) return false;

            // 1) Umbenennen auf Master
            void RenameIfExists(string src, string dest)
            {
                var colName = Find(src);
                if (colName != null)
                    t.Columns[colName].ColumnName = dest;
            }

            RenameIfExists("Einkaufsdatum", "Transaktionsdatum");
            RenameIfExists("Buchungstext", "Beschreibung");
            RenameIfExists("Branche", "Händlerkategorie");
            RenameIfExists("Kartennummer", "Kartennummer");   // passt schon
            RenameIfExists("Währung", "Währung");
            RenameIfExists("Betrag", "Betrag");

            // Händler gibt es in der CSV nicht explizit -> leere Spalte anlegen,
            // (optional: aus Beschreibung heuristisch ziehen – lassen wir bewusst neutral)
            if (!Has("Händler")) t.Columns.Add("Händler");

            // 2) Debit/Kredit aus Belastung/Gutschrift ableiten
            if (!Has("Debit/Kredit")) t.Columns.Add("Debit/Kredit");

            var colBelastung = Find("Belastung");
            var colGutschrift = Find("Gutschrift");
            var colBetrag = Find("Betrag") ?? Find("Betrag"); // nach Umbenennung heißt sie "Betrag"

            foreach (DataRow r in t.Rows)
            {
                // Betrag: wenn CSV zwei Spalten hat, ist "Betrag" meist schon gesetzt.
                // Wichtig ist: Debit/Kredit bestimmen.
                string dk = r["Debit/Kredit"]?.ToString()?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(dk))
                {
                    decimal bel = 0m, gut = 0m;
                    if (colBelastung != null)
                        decimal.TryParse((r[colBelastung]?.ToString() ?? "").Replace("’", "").Replace("'", ""), NumberStyles.Any, CultureInfo.GetCultureInfo("de-CH"), out bel);
                    if (colGutschrift != null)
                        decimal.TryParse((r[colGutschrift]?.ToString() ?? "").Replace("’", "").Replace("'", ""), NumberStyles.Any, CultureInfo.GetCultureInfo("de-CH"), out gut);

                    // Regel: positiver Wert in "Belastung" => Debit; positiver Wert in "Gutschrift" => Kredit.
                    // Falls beides 0/leer: als Debit annehmen (defensiv); dein Import nimmt später Betrag.Abs().
                    if (bel > 0 && gut == 0) dk = "Debit";
                    else if (gut > 0 && bel == 0) dk = "Kredit";
                    else if (bel == 0 && gut == 0)
                    {
                        // fallback: Betrag-Vorzeichen auswerten (falls gesetzt)
                        if (colBetrag != null &&
                            decimal.TryParse((r[colBetrag]?.ToString() ?? ""), NumberStyles.Any, CultureInfo.GetCultureInfo("de-CH"), out var bet))
                            dk = bet < 0 ? "Debit" : "Kredit";
                        else
                            dk = "Debit";
                    }
                    else
                    {
                        // beides gesetzt (sollte nicht vorkommen) -> Debit bevorzugen
                        dk = "Debit";
                    }

                    r["Debit/Kredit"] = dk;
                }
            }

            // 3) Pflichtspalten, die dein Import erwartet, sicherstellen
            void EnsureColumn(string name)
            {
                if (!Has(name)) t.Columns.Add(name);
            }
            EnsureColumn("Transaktionsdatum");
            EnsureColumn("Beschreibung");
            EnsureColumn("Händler");
            EnsureColumn("Händlerkategorie");
            EnsureColumn("Betrag");
            EnsureColumn("Debit/Kredit");
            EnsureColumn("Kartennummer"); // optional, leer ok

            return true;
        }



        public string? SuggestSourceForMaster(string masterHeader, IEnumerable<string> sampleHeaders)
        {
            // Verbote
            bool IsBanned(string? h)
                => string.IsNullOrWhiteSpace(h) || Normalize(h!).StartsWith("abgeschlossene zahlungen");

            // Synonyme für bessere Treffer
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Transaktionsdatum"] = new[] { "Ausführungsdatum", "Buchungsdatum", "Datum" },
                ["Beschreibung"] = new[] { "Zahlungsgrund", "Verwendungszweck", "Text" },
                ["Händler"] = new[] { "Begünstigter", "Empfänger", "Name" },
                ["Betrag"] = new[] { "Betrag", "Amount" },
                ["Debit/Kredit"] = new[] { "Debit", "Kredit", "Soll/Haben", "Belastung", "Gutschrift" },
                ["Händlerkategorie"] = new[] { "Rubrik", "Kategorie" },
                ["Kartennummer"] = new[] { "Kartennummer", "Karte" }
            };

            var candidates = sampleHeaders.Where(h => !IsBanned(h)).ToList();
            if (candidates.Count == 0) return null;

            // 1) Exakt
            var exact = candidates.FirstOrDefault(h => EqualsNormalized(h, masterHeader));
            if (!string.IsNullOrWhiteSpace(exact)) return exact;

            // 2) Synonym
            if (map.TryGetValue(masterHeader, out var syns))
            {
                var synHit = candidates.FirstOrDefault(h => syns.Any(s => EqualsNormalized(h, s)));
                if (!string.IsNullOrWhiteSpace(synHit)) return synHit;
            }

            // 3) Ähnlichster via LCS
            string nMaster = Normalize(masterHeader);
            return candidates
                .Select(h => new { h, n = Normalize(h) })
                .OrderByDescending(x => LcsScore(nMaster, x.n))
                .FirstOrDefault()?.h;
        }



        public void NotifyLabelPropertiesChanged()
        {
            // Hook für deine bestehende UI-Konvention
            // Z.B.: _labelCache.Refresh(); _matcher.Reload(); NotifyLabelPropertiesChanged();
        }

        // === intern ===

        private bool IsMaster(IEnumerable<string> headers)
        {
            // Nur auf Pflicht-Header prüfen (Kartennummer ist optional)
            var required = new[] { "Transaktionsdatum", "Beschreibung", "Händler", "Händlerkategorie", "Betrag", "Debit/Kredit" };
            var set = headers.Select(Normalize).ToHashSet();
            return required.All(h => set.Contains(Normalize(h)));
        }


        private List<string> LoadMasterHeaders() => new()
{
        "Transaktionsdatum",
        "Beschreibung",
        "Händler",
        "Händlerkategorie",
        "Betrag",
        "Debit/Kredit",
        "Kartennummer"
};


        private void EnsureMasterSchemaExists()
        {
            var schemas = _db.ImportSchemasGetAll();
            if (!schemas.Any(s => s.IsMaster))
            {
                _db.ImportSchemaInsert(new ImportSchema { Name = "Master", IsMaster = true });
            }
        }

        private static bool EqualsNormalized(string a, string b) => Normalize(a) == Normalize(b);

        private static string Normalize(string s)
        {
            if (s == null) return "";
            s = s.Trim().ToLowerInvariant();
            s = s.Replace('\u00A0', ' '); // non-breaking space
            var normalized = s.Normalize(NormalizationForm.FormD);
            var chars = normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
            return new string(chars.ToArray()).Normalize(NormalizationForm.FormC);
        }

        // Liefert ein DefaultValue (Stellvertreter) aus irgendeinem Nicht-Master-Schema
        // für exakt (MasterHeader, SourceHeader). Nimmt den ersten Treffer.
        private string? GetDefaultValueFor(string masterHeader, string sourceHeader)
        {
            foreach (var s in _db.ImportSchemasGetAll().Where(x => !x.IsMaster))
            {
                var m = _db.FieldMappingsGetBySchema(s.Id)
                           .FirstOrDefault(x => EqualsNormalized(x.MasterHeader, masterHeader)
                                             && EqualsNormalized(x.SourceHeader, sourceHeader)
                                             && !string.IsNullOrWhiteSpace(x.DefaultValue));
                if (m != null) return m.DefaultValue!.Trim();
            }
            return null;
        }




        // TOP-Card XLSX → Master-Table (inhaltlich, nicht nach "ähnlichen" Überschriften)
        // TOP-Card XLSX → Master-Table (inhaltlich, deterministisch)
        private bool TryConvertTopCardXlsx(DataTable src, out DataTable dst)
        {
            dst = src;

            // Helpers
            bool Has(string name) => src.Columns.Cast<DataColumn>().Any(c => EqualsNormalized(c.ColumnName, name));
            string? Find(string name) => src.Columns.Cast<DataColumn>().FirstOrDefault(c => EqualsNormalized(c.ColumnName, name))?.ColumnName;

            // Signatur: TOP-Card hat "Buchung", "Buchungstext" und (Belastung oder Gutschrift)
            var looksTopCard = Has("Buchung") && Has("Buchungstext") && (Has("Belastung") || Has("Gutschrift"));
            if (!looksTopCard) return false;

            // Quellspalten (konkret aus deinem Muster)
            var colBuchung = Find("Buchung")!;            // Datum
            var colText = Find("Buchungstext")!;       // Beschreibung
            var colBranche = Find("Branche");             // Händlerkategorie (optional)
            var colKarte = Find("Kartennummer");        // optional
            var colWaehrung = Find("Währung");             // optional (für Master nicht zwingend)
            var colBelastung = Find("Belastung");           // optional
            var colGutschrift = Find("Gutschrift");          // optional
            var colBetrag = Find("Betrag");              // optional (manchmal zusätzliche Gesamtsumme)

            // Ziel: exakt die Master-Header, die dein Reader erwartet
            var master = new DataTable();
            master.Columns.Add("Transaktionsdatum");
            master.Columns.Add("Beschreibung");
            master.Columns.Add("Händler");
            master.Columns.Add("Händlerkategorie");
            master.Columns.Add("Betrag");
            master.Columns.Add("Debit/Kredit");
            master.Columns.Add("Kartennummer"); // optional

            var ciCH = CultureInfo.GetCultureInfo("de-CH");

            // Lokaler Amount-Parser (robust gegen CHF, Tausender, Apostroph)
            static decimal ParseAmount(object? v, CultureInfo ci)
            {
                if (v == null) return 0m;
                var s = v.ToString()?.Trim() ?? "";
                if (s.Length == 0) return 0m;
                s = s.Replace("’", "").Replace("'", "").Replace(" ", "").Replace("CHF", "", StringComparison.OrdinalIgnoreCase);
                if (decimal.TryParse(s, NumberStyles.Any, ci, out var ch)) return ch;
                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var inv)) return inv;
                return 0m;
            }

            foreach (DataRow s in src.Rows)
            {
                var r = master.NewRow();

                // Transaktionsdatum (aus "Buchung")
                r["Transaktionsdatum"] = s[colBuchung];

                // Beschreibung
                r["Beschreibung"] = s[colText]?.ToString()?.Trim() ?? "";

                // Händler (TOP-Card liefert keinen expliziten Händler) → leer lassen (dein Workflow füllt/erkennt später)
                r["Händler"] = "";

                // Händlerkategorie (aus "Branche")
                r["Händlerkategorie"] = colBranche != null ? s[colBranche]?.ToString()?.Trim() : "";

                // Fallback aus Mapping (z. B. Händlerkategorie ← Branche → "Gebühren")
                if (string.IsNullOrWhiteSpace(r["Händlerkategorie"]?.ToString()))
                {
                    var def = GetDefaultValueFor("Händlerkategorie", "Branche");
                    if (!string.IsNullOrWhiteSpace(def))
                        r["Händlerkategorie"] = def;
                }

                // Kartennummer (falls vorhanden)
                r["Kartennummer"] = colKarte != null ? s[colKarte]?.ToString()?.Trim() : "";

                // Betrag & Debit/Kredit
                var bel = colBelastung != null ? ParseAmount(s[colBelastung], ciCH) : 0m;
                var gut = colGutschrift != null ? ParseAmount(s[colGutschrift], ciCH) : 0m;
                var bet = colBetrag != null ? ParseAmount(s[colBetrag], ciCH) : 0m;

                string dk;
                decimal amount;

                if (bel > 0m && gut == 0m)
                {
                    dk = "Debit"; amount = bel;
                }
                else if (gut > 0m && bel == 0m)
                {
                    dk = "Kredit"; amount = gut;
                }
                else if (bel == 0m && gut == 0m && bet != 0m)
                {
                    // Fallback: Einzelspalte "Betrag"
                    dk = bet < 0m ? "Debit" : "Kredit";
                    amount = Math.Abs(bet);
                }
                else
                {
                    // Unklarer Fall → defensiv Debit, Betrag aus der ersten sinnvollen Quelle
                    dk = "Debit";
                    amount = bel != 0m ? bel : (gut != 0m ? gut : Math.Abs(bet));
                }

                r["Debit/Kredit"] = dk;
                r["Betrag"] = amount;

                master.Rows.Add(r);
            }

            dst = master;
            return true;
        }




        // sehr einfache Score-Funktion für "ähnlichste" Header (Longest Common Subsequence approximiert)
        private static int LcsScore(string a, string b)
        {
            int m = a.Length, n = b.Length;
            var dp = new int[m + 1, n + 1];
            for (int i = 1; i <= m; i++)
                for (int j = 1; j <= n; j++)
                    dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);
            return dp[m, n];
        }
    }


    // Minimaler Header-Reader (du kannst hier deine vorhandenen Excel/CSV-Leser nutzen)
    internal static class SimpleTableReader
    {
        public static DataTable? ReadHeadersOnly(string path)
        {
            // Falls du bereits EPPlus/ClosedXML einsetzt: dort einfach Sheet erste Zeile lesen
            // Hier Dummy: CSV nur als Beispiel
            if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                var first = File.ReadLines(path).FirstOrDefault();
                if (first == null) return null;
                var cols = first.Split(';', ',', '\t');
                var dt = new DataTable();
                foreach (var c in cols) dt.Columns.Add(c.Trim());
                return dt;
            }
            // Für .xlsx/.xls -> nimm deinen bestehenden Reader
            return null;
        }
    }
}
