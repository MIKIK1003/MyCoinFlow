using System.Linq;
using ClosedXML.Excel;
using MyCoinFlow.Services;

namespace MyCoinFlow.Importing
{
    /// <summary>
    /// Exportiert den Kontenplan als Excel mit den Spalten:
    /// ArtN, Art, Gruppe, Untergruppe, Konto, Detail
    /// </summary>
    public class KontenplanExcelExporter
    {
        private readonly DatabaseService _db = new();

        public void Export(string filePath)
        {
            var daten = _db.LadeKontenplan()
                           .OrderBy(k => k.Art)
                           .ThenBy(k => k.Gruppe)
                           .ThenBy(k => k.Untergruppe)
                           .ThenBy(k => k.Kontonummer)
                           .ThenBy(k => k.Detail)
                           .ToList();

            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Kontenplan");

            // Header
            ws.Cell(1, 1).Value = "ArtN";
            ws.Cell(1, 2).Value = "Art";
            ws.Cell(1, 3).Value = "Gruppe";
            ws.Cell(1, 4).Value = "Untergruppe";
            ws.Cell(1, 5).Value = "Konto";
            ws.Cell(1, 6).Value = "Detail";
            ws.Range(1, 1, 1, 6).Style.Font.Bold = true;

            int r = 2;
            foreach (var e in daten)
            {
                var (artN, artText) = SplitArt(e.Art);

                ws.Cell(r, 1).Value = artN ?? "";
                ws.Cell(r, 2).Value = artText ?? "";
                ws.Cell(r, 3).Value = e.Gruppe ?? "";
                ws.Cell(r, 4).Value = e.Untergruppe ?? "";
                ws.Cell(r, 5).Value = e.Kontonummer;         // als Zahl
                ws.Cell(r, 6).Value = e.Detail ?? "";
                r++;
            }

            // Formatierung
            ws.Column(5).Style.NumberFormat.Format = "0";    // Konto ohne Nachkommastellen
            ws.Columns(1, 6).AdjustToContents();
            ws.SheetView.FreezeRows(1);

            wb.SaveAs(filePath);
        }

        /// <summary>
        /// Erwartet "N - Text" und splittet in (N, Text). Fällt sonst auf (null, Gesamt) zurück.
        /// </summary>
        private static (string? artN, string? artText) SplitArt(string? artCombined)
        {
            if (string.IsNullOrWhiteSpace(artCombined))
                return (null, null);

            var s = artCombined.Trim();

            // bevorzugt "N - Text"
            int idx = s.IndexOf(" - ");
            if (idx >= 0)
            {
                var left = s[..idx].Trim();
                var right = s[(idx + 3)..].Trim();
                if (left.All(char.IsDigit)) return (left, right);
                return (null, s);
            }

            // toleranter: "N-Text" / "N- Text"
            idx = s.IndexOf('-');
            if (idx > 0)
            {
                var left = s[..idx].Trim();
                var right = s[(idx + 1)..].Trim();
                if (left.All(char.IsDigit)) return (left, right);
            }

            return (null, s);
        }
    }
}
