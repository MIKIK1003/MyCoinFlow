using ExcelDataReader;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MyCoinFlow.Importing
{
    /// <summary>
    /// Liest Kontenplan aus Excel (Spalten: ArtN, Art, Gruppe, Untergruppe, Konto, Detail, BudgetJ).
    /// Bietet Preview/Analyse (inkl. Validierung) und Import.
    /// - Art-Bezeichnung: "{ArtN} - {Art}" (z. B. "1 - Einnahmen")
    /// - Gruppen/Untergruppen ohne Präfix
    /// - BudgetJ (optional) wird – falls ZeitraumId übergeben – nach BudgetDetail upsertet
    /// </summary>
    public class KontenplanExcelImporter
    {
        private readonly DatabaseService _db = new();

        public class PreviewRow
        {
            public int RowNo { get; set; }
            public string? ArtN { get; set; }
            public string? Art { get; set; }
            public string? Gruppe { get; set; }
            public string? Untergruppe { get; set; }
            public int? Konto { get; set; }
            public string? Detail { get; set; }

            // Budget (aus Excel-Spalte "BudgetJ")
            public decimal? BudgetJ { get; set; }

            // abgeleitet
            public string ArtBezeichnung => BuildArtBezeichnung(ArtN, Art);

            // Validierung / Hinweise
            public bool HasError { get; set; }
            public string? Warning { get; set; }
            public bool DuplicateKontoInFile { get; set; }

            // Existenz / Aktion
            public bool ExistsArt { get; set; }
            public bool ExistsGruppe { get; set; }
            public bool ExistsUntergruppe { get; set; }
            /// <summary>Existiert der exakte Kontenplan-Eintrag (gleiche 5 Felder)?</summary>
            public bool ExistsKonto { get; set; }

            public string ActionArt => ExistsArt ? "skip" : "create";
            public string ActionGruppe => ExistsGruppe ? "skip" : "create";
            public string ActionUntergruppe => string.IsNullOrWhiteSpace(Untergruppe) ? "-" : (ExistsUntergruppe ? "skip" : "create");
            public string ActionKonto => ExistsKonto ? "skip" : "create";
            public string BudgetInfo => BudgetJ.HasValue ? BudgetJ.Value.ToString("N2") : "";
        }

        public record AnalyzeResult(List<PreviewRow> Rows);

        public record ImportResult(
            int ArtenNeu, int GruppenNeu, int UntergruppenNeu, int KontenNeu,
            int RowsProcessed, int RowsSkipped, int RowsWithErrors,
            int BudgetsGesetzt);

        public AnalyzeResult Analyze(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("Datei nicht gefunden.", filePath);

            // ExcelDataReader braucht CodePages (sollte im App-Startup registriert sein)
            try { System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); } catch { }

            using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelReaderFactory.CreateReader(fs);
            var ds = reader.AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
            });
            if (ds.Tables.Count == 0) throw new InvalidOperationException("Excel enthält kein Arbeitsblatt.");

            var table = ds.Tables[0];

            // Stammdaten für Existenz-Checks
            var bekannteArten = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var a in _db.LadeKontenArten()) bekannteArten.Add(a.Bezeichnung);

            var bekannteGrupp = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var g in _db.LadeKontenGruppen()) bekannteGrupp.Add(g.Bezeichnung);

            var bekannteUGr = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var ug in _db.LadeKontenUnterGruppen()) bekannteUGr.Add(ug.Bezeichnung);

            var vorhandeneKonten = _db.LadeKontenplan(); // für exakten ExistsKonto-Check

            // Duplicates im File (Konto-Nr)
            var kontoCount = new Dictionary<int, int>();
            foreach (DataRow row in table.Rows)
            {
                var konto = GetInt(row, "Konto");
                if (konto.HasValue)
                {
                    kontoCount.TryGetValue(konto.Value, out int c);
                    kontoCount[konto.Value] = c + 1;
                }
            }

            var list = new List<PreviewRow>();
            int rowNo = 1;

            foreach (DataRow row in table.Rows)
            {
                var pr = new PreviewRow
                {
                    RowNo = rowNo++,
                    ArtN = GetStr(row, "ArtN"),
                    Art = GetStr(row, "Art"),
                    Gruppe = GetStr(row, "Gruppe"),
                    Untergruppe = GetStr(row, "Untergruppe"),
                    Konto = GetInt(row, "Konto"),
                    Detail = GetStr(row, "Detail"),
                    BudgetJ = GetDecimal(row, "BudgetJ")
                };

                // Duplikate im File
                pr.DuplicateKontoInFile = pr.Konto.HasValue && kontoCount.TryGetValue(pr.Konto.Value, out int c) && c > 1;

                // Exists-Flags
                pr.ExistsArt = !string.IsNullOrWhiteSpace(pr.ArtBezeichnung) && bekannteArten.Contains(pr.ArtBezeichnung);
                pr.ExistsGruppe = !string.IsNullOrWhiteSpace(pr.Gruppe) && bekannteGrupp.Contains(pr.Gruppe);
                pr.ExistsUntergruppe = !string.IsNullOrWhiteSpace(pr.Untergruppe) && bekannteUGr.Contains(pr.Untergruppe);

                pr.ExistsKonto = vorhandeneKonten.Any(k =>
                    k.Kontonummer == (pr.Konto ?? -1) &&
                    string.Equals(k.Art ?? "", pr.ArtBezeichnung ?? "", StringComparison.CurrentCultureIgnoreCase) &&
                    string.Equals(k.Gruppe ?? "", pr.Gruppe ?? "", StringComparison.CurrentCultureIgnoreCase) &&
                    string.Equals(k.Untergruppe ?? "", pr.Untergruppe ?? "", StringComparison.CurrentCultureIgnoreCase) &&
                    string.Equals(k.Detail ?? "", pr.Detail ?? "", StringComparison.CurrentCultureIgnoreCase));

                // Validierung
                var warn = new List<string>();
                if (pr.Konto == null || pr.Konto <= 0) { pr.HasError = true; warn.Add("Konto fehlt/ungültig"); }
                if (string.IsNullOrWhiteSpace(pr.Art) && string.IsNullOrWhiteSpace(pr.ArtN))
                    warn.Add("Art leer (OK, wenn bewusst)");
                if (string.IsNullOrWhiteSpace(pr.Detail))
                    warn.Add("Detail leer (OK, wenn bewusst)");
                if (pr.DuplicateKontoInFile)
                    warn.Add("Konto doppelt in Datei");
                if (pr.BudgetJ.HasValue && pr.BudgetJ.Value < 0)
                    warn.Add("BudgetJ < 0 (prüfen)");

                pr.Warning = warn.Count == 0 ? null : string.Join("; ", warn);

                list.Add(pr);
            }

            return new AnalyzeResult(list);
        }

        /// <summary>
        /// Importiert basierend auf Preview-Zeilen.
        /// - onlyNew: legt nur neue Stammdaten/Konten an, vorhandene werden übersprungen
        /// - zeitraumId: wenn gesetzt, wird BudgetJ upsertet (BudgetDetail) pro Konto/Zeitraum
        /// </summary>
        public ImportResult ImportFromPreview(IEnumerable<PreviewRow> rows, bool onlyNew, int? zeitraumId)
        {
            int artenNeu = 0, gruppenNeu = 0, ugruNeu = 0, kontenNeu = 0;
            int processed = 0, skipped = 0, errors = 0, budgetsGesetzt = 0;

            // Lookup für schnelle Konto-Id-Ermittlung
            var konten = _db.LadeKontenplan();
            var kontoKeyMap = BuildKontoKeyMap(konten);

            // Puffer aktualisieren, um innerhalb des Imports Duplikate zu vermeiden
            var bekannteArten = new HashSet<string>(_db.LadeKontenArten().Select(a => a.Bezeichnung),
                                                    StringComparer.CurrentCultureIgnoreCase);
            var bekannteGrupp = new HashSet<string>(_db.LadeKontenGruppen().Select(g => g.Bezeichnung),
                                                    StringComparer.CurrentCultureIgnoreCase);
            var bekannteUGr = new HashSet<string>(_db.LadeKontenUnterGruppen().Select(ug => ug.Bezeichnung),
                                                  StringComparer.CurrentCultureIgnoreCase);

            foreach (var pr in rows)
            {
                if (pr.HasError) { errors++; continue; }
                processed++;

                var artBez = pr.ArtBezeichnung;
                var gruppe = pr.Gruppe ?? "";
                var ugrp = string.IsNullOrWhiteSpace(pr.Untergruppe) ? null : pr.Untergruppe;
                var detail = string.IsNullOrWhiteSpace(pr.Detail) ? null : pr.Detail;

                // Stammdaten
                if (!string.IsNullOrWhiteSpace(artBez) && !bekannteArten.Contains(artBez))
                {
                    if (!onlyNew || (onlyNew && pr.ActionArt == "create"))
                    {
                        _db.SpeichereKontenArt(artBez);
                        bekannteArten.Add(artBez);
                        artenNeu++;
                    }
                }
                if (!string.IsNullOrWhiteSpace(gruppe) && !bekannteGrupp.Contains(gruppe))
                {
                    if (!onlyNew || (onlyNew && pr.ActionGruppe == "create"))
                    {
                        _db.SpeichereKontenGruppe(gruppe);
                        bekannteGrupp.Add(gruppe);
                        gruppenNeu++;
                    }
                }
                if (!string.IsNullOrWhiteSpace(ugrp) && !bekannteUGr.Contains(ugrp))
                {
                    if (!onlyNew || (onlyNew && pr.ActionUntergruppe == "create"))
                    {
                        _db.SpeichereKontenUnterGruppe(ugrp);
                        bekannteUGr.Add(ugrp);
                        ugruNeu++;
                    }
                }

                // Konto
                if (onlyNew && pr.ExistsKonto) { skipped++; }
                else
                {
                    if (pr.Konto.HasValue)
                    {
                        _db.NeuenKontoplanEintragSpeichern(
                            pr.Konto.Value, artBez, gruppe, ugrp, detail);

                        // KontoId ermitteln (Lookup ggf. refreshen)
                        var key = MakeKey(pr.Konto.Value, artBez, gruppe, ugrp, detail);
                        if (!kontoKeyMap.TryGetValue(key, out int kontoId))
                        {
                            // einmal frisch laden und nochmal versuchen
                            konten = _db.LadeKontenplan();
                            kontoKeyMap = BuildKontoKeyMap(konten);
                            if (!kontoKeyMap.TryGetValue(key, out kontoId))
                            {
                                // sollte nicht passieren – dann nächste Zeile
                                skipped++;
                                continue;
                            }
                        }

                        if (!pr.ExistsKonto) kontenNeu++; else skipped++;

                        // Budget setzen (falls Zeitraum gewählt + Wert vorhanden)
                        if (zeitraumId.HasValue && pr.BudgetJ.HasValue)
                        {
                            _db.UpsertBudgetwert(zeitraumId.Value, kontoId, pr.BudgetJ.Value);
                            budgetsGesetzt++;
                        }
                    }
                    else
                    {
                        skipped++;
                    }
                }
            }

            return new ImportResult(artenNeu, gruppenNeu, ugruNeu, kontenNeu, processed, skipped, errors, budgetsGesetzt);
        }

        // --------- Helpers ---------

        public static string BuildArtBezeichnung(string? artN, string? art)
        {
            string n = artN?.Trim() ?? "";
            string t = art?.Trim() ?? "";
            if (string.IsNullOrEmpty(n) && string.IsNullOrEmpty(t)) return "";
            if (string.IsNullOrEmpty(n)) return t;
            if (string.IsNullOrEmpty(t)) return n;
            return $"{n} - {t}";
        }

        private static string? GetStr(DataRow r, string col)
        {
            var colRef = FindColumn(r.Table, col);
            if (colRef == null) return null;
            var v = r[colRef];
            if (v == null || v == DBNull.Value) return null;
            return Convert.ToString(v)?.Trim();
        }

        private static int? GetInt(DataRow r, string col)
        {
            var colRef = FindColumn(r.Table, col);
            if (colRef == null) return null;
            var v = r[colRef];
            if (v == null || v == DBNull.Value) return null;

            if (v is double d) return (int)Math.Round(d, MidpointRounding.AwayFromZero);
            if (v is float f) return (int)Math.Round(f, MidpointRounding.AwayFromZero);
            if (int.TryParse(Convert.ToString(v), NumberStyles.Integer, CultureInfo.CurrentCulture, out var i)) return i;
            if (int.TryParse(Convert.ToString(v), NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;
            return null;
        }

        private static decimal? GetDecimal(DataRow r, string col)
        {
            var colRef = FindColumn(r.Table, col);
            if (colRef == null) return null;
            var v = r[colRef];
            if (v == null || v == DBNull.Value) return null;

            if (v is double d) return Convert.ToDecimal(d);
            if (v is float f) return Convert.ToDecimal(f);
            if (v is decimal m) return m;

            // versuchen mit CH/Invariant
            var s = Convert.ToString(v)?.Trim();
            if (string.IsNullOrEmpty(s)) return null;

            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out var dm)) return dm;
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out dm)) return dm;

            return null;
        }

        private static DataColumn? FindColumn(DataTable table, string wanted)
        {
            foreach (DataColumn c in table.Columns)
                if (string.Equals(c.ColumnName.Trim(), wanted, StringComparison.CurrentCultureIgnoreCase))
                    return c;
            return null;
        }

        private static string MakeKey(int kontoNr, string? artBez, string gruppe, string? ugrp, string? detail)
        {
            return $"{kontoNr}|{(artBez ?? "").Trim().ToUpperInvariant()}|{(gruppe ?? "").Trim().ToUpperInvariant()}|{(ugrp ?? "").Trim().ToUpperInvariant()}|{(detail ?? "").Trim().ToUpperInvariant()}";
        }

        private static Dictionary<string, int> BuildKontoKeyMap(IEnumerable<KontoplanEintrag> konten)
        {
            var dict = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var k in konten)
            {
                var key = MakeKey(k.Kontonummer, k.Art, k.Gruppe ?? "", k.Untergruppe, k.Detail);
                if (!dict.ContainsKey(key)) dict[key] = k.Id;
            }
            return dict;
        }
    }
}
