using ClosedXML.Excel;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MyCoinFlow.Importing
{
    public class VermoegenPositionExcelImporter
    {
        private readonly DatabaseService _db = new();
        private static readonly CultureInfo ChCulture = CultureInfo.GetCultureInfo("de-CH");

        public ImportResult Import(string path, int depotId)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Kein Dateipfad angegeben.", nameof(path));

            if (depotId <= 0)
                throw new ArgumentException("Kein gültiges Depot gewählt.", nameof(depotId));

            _db.EnsureVermoegenSchema();

            var result = new ImportResult();

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.First();

            var headerRow = ws.FirstRowUsed();
            if (headerRow == null)
                throw new InvalidOperationException("Die Excel-Datei enthält keine Daten.");

            var headerMap = BuildHeaderMap(headerRow);

            int titelCol = FindRequired(headerMap, "Titel");
            int waehrungCol = FindRequired(headerMap, "Währung", "Waehrung");
            int anzahlCol = FindRequired(headerMap, "Anzahl /Nominalbetrag");
            int einstandCol = FindRequired(headerMap, "Einstandspreis");
            int kursCol = FindRequired(headerMap, "Aktueller Kurs");

            var existing = _db.VermoegenPositionenGetAll()
                .Where(p => p.DepotId == depotId)
                .ToList();

            foreach (var row in ws.RowsUsed().Skip(1))
            {
                result.RowsRead++;

                var titel = ReadString(row, titelCol);
                if (string.IsNullOrWhiteSpace(titel))
                {
                    result.RowsSkipped++;
                    continue;
                }

                if (IsIgnoredTitle(titel))
                {
                    result.RowsSkipped++;
                    continue;
                }

                var valor = "";
                var waehrung = waehrungCol > 0 ? ReadString(row, waehrungCol) : "CHF";

                if (!TryReadDecimal(row, anzahlCol, out var anzahl) || anzahl <= 0)
                {
                    result.RowsWithErrors++;
                    result.Errors.Add($"Zeile {row.RowNumber()}: Ungültige Anzahl.");
                    continue;
                }

                if (!TryReadDecimal(row, einstandCol, out var einstandspreis) || einstandspreis <= 0)
                {
                    result.RowsWithErrors++;
                    result.Errors.Add($"Zeile {row.RowNumber()}: Ungültiger Einstandspreis.");
                    continue;
                }

                if (!TryReadDecimal(row, kursCol, out var kurs) || kurs <= 0)
                {
                    result.RowsWithErrors++;
                    result.Errors.Add($"Zeile {row.RowNumber()}: Ungültiger aktueller Kurs.");
                    continue;
                }

                bool duplicate = existing.Any(p =>
    string.Equals(
        p.Titel?.Trim(),
        titel.Trim(),
        StringComparison.OrdinalIgnoreCase));

                if (duplicate)
                {
                    result.RowsSkipped++;
                    continue;
                }

                var model = new VermoegenPosition
                {
                    DepotId = depotId,
                    Titel = titel.Trim(),
                    Valor = valor.Trim().ToUpperInvariant(),
                    Waehrung = NormalizeWaehrung(waehrung),
                    Anlageklasse = GuessAnlageklasse(titel),
                    Anzahl = anzahl,
                    Einstandspreis = einstandspreis,
                    EinstandDatum = null,
                    AktuellerKurs = kurs,
                    KursDatum = DateTime.Today,
                    IstAktiv = true
                };

                int id = _db.VermoegenPositionInsert(model);

                _db.VermoegenPositionKursUpdate(id, kurs, DateTime.Today);

                _db.VermoegenKursHistorieInsertIfMissing(
                    id,
                    DateTime.Today,
                    kurs,
                    "Import");

                result.RowsImported++;
            }

            return result;
        }

        private static Dictionary<string, int> BuildHeaderMap(IXLRow headerRow)
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var cell in headerRow.CellsUsed())
            {
                var text = cell.GetString().Trim();
                if (!string.IsNullOrWhiteSpace(text) && !dict.ContainsKey(text))
                    dict[text] = cell.Address.ColumnNumber;
            }

            return dict;
        }

        private static int FindRequired(Dictionary<string, int> map, params string[] names)
        {
            var col = FindOptional(map, names);
            if (col <= 0)
                throw new InvalidOperationException("Pflichtspalte fehlt: " + string.Join(" / ", names));

            return col;
        }

        private static int FindOptional(Dictionary<string, int> map, params string[] names)
        {
            foreach (var name in names)
            {
                if (map.TryGetValue(name, out var col))
                    return col;
            }

            return -1;
        }

        private static string ReadString(IXLRow row, int col)
        {
            if (col <= 0)
                return "";

            return row.Cell(col).GetString().Trim();
        }

        private static bool TryReadDecimal(IXLRow row, int col, out decimal value)
        {
            value = 0;

            if (col <= 0)
                return false;

            var cell = row.Cell(col);

            if (cell.TryGetValue<double>(out var dbl))
            {
                value = Convert.ToDecimal(dbl);
                return true;
            }

            var text = cell.GetString()
                .Trim()
                .Replace("'", "")
                .Replace(" ", "");

            return decimal.TryParse(text, NumberStyles.Number, ChCulture, out value)
                || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }

        private static bool IsIgnoredTitle(string titel)
        {
            var t = titel.Trim();

            return t.Equals("Total", StringComparison.OrdinalIgnoreCase)
                || t.Contains("Investitionskonto", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeWaehrung(string value)
        {
            var w = string.IsNullOrWhiteSpace(value)
                ? "CHF"
                : value.Trim().ToUpperInvariant();

            return w switch
            {
                "CHF" => "CHF",
                "EUR" => "EUR",
                "USD" => "USD",
                "GBP" => "GBP",
                _ => w
            };
        }

        private static string GuessAnlageklasse(string titel)
        {
            if (titel.Contains("Fonds", StringComparison.OrdinalIgnoreCase))
                return "Fonds";

            if (titel.Contains("ETF", StringComparison.OrdinalIgnoreCase))
                return "ETF";

            return "Aktie";
        }

        public class ImportResult
        {
            public int RowsRead { get; set; }
            public int RowsImported { get; set; }
            public int RowsSkipped { get; set; }
            public int RowsWithErrors { get; set; }
            public List<string> Errors { get; } = new();
        }
    }
}