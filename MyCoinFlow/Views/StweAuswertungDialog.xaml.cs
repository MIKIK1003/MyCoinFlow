using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MyCoinFlow.Views
{
    public partial class StweAuswertungDialog : Window
    {
        private readonly DatabaseService _db = new();
        private readonly StweLiegenschaft _liegenschaft;

        public string TitleText => $"Auswertung – {_liegenschaft.Name}";
        public string DetailTitle => SelectedOwnerRow == null
            ? "Details"
            : $"Details – {SelectedOwnerRow.EigentuemerName}";

        public DateTime? Von { get; set; }
        public DateTime? Bis { get; set; }

        public ObservableCollection<StweOwnerSummaryRow> OwnerRows { get; } = new();
        public ObservableCollection<StweOwnerDetailRow> DetailRows { get; } = new();

        public StweOwnerSummaryRow? SelectedOwnerRow { get; set; }

        public StweAuswertungDialog(StweLiegenschaft liegenschaft)
        {
            InitializeComponent();
            _liegenschaft = liegenschaft ?? throw new ArgumentNullException(nameof(liegenschaft));

            DataContext = this;

            LoadOwnerSummary();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadOwnerSummary();

        private void OwnerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedOwnerRow != null)
                LoadOwnerDetails(SelectedOwnerRow.EigentuemerId);
            else
                DetailRows.Clear();

            try { Title = Title; } catch { }
        }

        private void LoadOwnerSummary()
        {
            OwnerRows.Clear();
            DetailRows.Clear();
            SelectedOwnerRow = null;

            var rows = _db.StweReportOwnerSummary(_liegenschaft.Id, Von, Bis);
            foreach (var r in rows) OwnerRows.Add(r);

            if (OwnerRows.Count > 0)
            {
                SelectedOwnerRow = OwnerRows[0];
                LoadOwnerDetails(SelectedOwnerRow.EigentuemerId);
            }
        }

        private void LoadOwnerDetails(int eigentuemerId)
        {
            DetailRows.Clear();
            var rows = _db.StweReportOwnerDetails(_liegenschaft.Id, eigentuemerId, Von, Bis);
            foreach (var r in rows) DetailRows.Add(r);
        }

        // ==========================================================
        // DRUCK / PDF (PrintDialog -> z.B. "Microsoft Print to PDF")
        // ==========================================================
        private void Print_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1) Optionen abfragen
                var optDlg = new StweReportPrintOptionsDialog();
                if (optDlg.ShowDialog() != true)
                    return;

                var options = optDlg.Options;

                // 2) PrintDialog (PDF über Druckerwahl)
                var dlg = new PrintDialog();
                if (dlg.ShowDialog() != true) return;

                if (dlg.PrintTicket != null)
                {
                    dlg.PrintTicket.PageOrientation = PageOrientation.Portrait;
                    dlg.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
                }

                // 3) Report ERST nach Kenntnis der druckbaren Fläche bauen
                var doc = BuildFlowDocumentForPrint(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight, options);

                IDocumentPaginatorSource dps = doc;
                dlg.PrintDocument(dps.DocumentPaginator, $"STWE-Auswertung – {_liegenschaft.Name}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Druck fehlgeschlagen:\n" + ex.Message, "Drucken",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private FlowDocument BuildFlowDocumentForPrint(double printableWidth, double printableHeight, StweReportPrintOptions options)
        {
            var culture = new CultureInfo("de-CH");

            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 10.0,
                PagePadding = new Thickness(24),
                ColumnWidth = double.PositiveInfinity,
                PageWidth = printableWidth,
                PageHeight = printableHeight
            };

            Paragraph P(string text, bool bold = false, double? fontSize = null, Brush? color = null)
            {
                var run = new Run(text);
                Inline inline = bold ? new Bold(run) : run;

                var para = new Paragraph(inline) { Margin = new Thickness(0) };
                if (fontSize.HasValue) para.FontSize = fontSize.Value;
                if (color != null) para.Foreground = color;
                return para;
            }

            void PageBreak()
            {
                doc.Blocks.Add(new Paragraph { BreakPageBefore = true, Margin = new Thickness(0) });
            }

            // ─────────────────────────────────────────────
            // Deckblatt (optional)
            if (options.MitDeckblatt)
            {
                doc.Blocks.Add(P("STWE Bericht", bold: true, fontSize: 22));
                doc.Blocks.Add(P(_liegenschaft.Name, bold: true, fontSize: 16));
                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 10, 0, 0) });

                doc.Blocks.Add(P($"Zeitraum: {FormatVonBis(Von, Bis)}", color: Brushes.DimGray));
                doc.Blocks.Add(P($"Erstellt: {DateTime.Now:dd.MM.yyyy HH:mm}", color: Brushes.DimGray));

                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 18, 0, 0) });

                doc.Blocks.Add(P("Inhalt", bold: true, fontSize: 13));
                doc.Blocks.Add(new Paragraph(new Run("• Übersicht der Summen pro Eigentümer")) { Margin = new Thickness(0, 6, 0, 0) });
                if (options.MitOriginalTransaktionen)
                    doc.Blocks.Add(new Paragraph(new Run("• Liste der Original-Transaktionen (Totalbetrag), die in STWE-Sets aufgeteilt wurden")) { Margin = new Thickness(0, 2, 0, 0) });
                doc.Blocks.Add(new Paragraph(new Run("• Detailauflistung je Eigentümer (aufgeteilte Positionen)")) { Margin = new Thickness(0, 2, 0, 0) });

                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 20, 0, 0) });
                doc.Blocks.Add(P("Hinweis: Der Bericht zeigt die STWE-Aufteilung basierend auf den erfassten Sets.", color: Brushes.DimGray));

                PageBreak();
            }

            // ─────────────────────────────────────────────
            // Kopf (immer)
            doc.Blocks.Add(P(TitleText, bold: true, fontSize: 16));
            doc.Blocks.Add(P($"Zeitraum: {FormatVonBis(Von, Bis)}   |   Erstellt: {DateTime.Now:dd.MM.yyyy HH:mm}", color: Brushes.DimGray));
            doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 10, 0, 0) });


            // ─────────────────────────────────────────────
            // Original-Transaktionen (optional) -> 1. Seite nach Summen
            if (options.MitOriginalTransaktionen)
            {
                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 16, 0, 0) });
                doc.Blocks.Add(P("Original-Transaktionen (Totalbetrag)", bold: true, fontSize: 13));
                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 6, 0, 0) });

                var originals = _db.StweReportOriginalTransaktionen(_liegenschaft.Id, Von, Bis);
                if (originals.Count == 0)
                {
                    doc.Blocks.Add(P("Keine Original-Transaktionen gefunden.", color: Brushes.DimGray));
                }
                else
                {
                    doc.Blocks.Add(BuildOriginalTransaktionenTable(doc, originals, culture));
                }

                // ✅ WICHTIG: Eigentümer sollen immer auf neuer Seite beginnen
                PageBreak();
            }


            // ─────────────────────────────────────────────
            // Details für ALLE Eigentümer
            if (OwnerRows.Count == 0)
            {
                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 16, 0, 0) });
                doc.Blocks.Add(P("Keine Daten vorhanden.", color: Brushes.DimGray));
                return doc;
            }

            bool firstOwner = true;

            foreach (var owner in OwnerRows)
            {
                if (options.NeueSeiteProEigentuemer)
                {
                    // Für den ersten Eigentümer NICHT zwingend umbrechen,
                    // ausser es gab vorher schon viel Inhalt (Deckblatt/Originals).
                    if (!firstOwner)
                        PageBreak();
                }
                else
                {
                    doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 14, 0, 0) });
                }

                doc.Blocks.Add(P($"Details – {owner.EigentuemerName}", bold: true, fontSize: 13));
                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 6, 0, 0) });

                var details = _db.StweReportOwnerDetails(_liegenschaft.Id, owner.EigentuemerId, Von, Bis);
                if (details == null || details.Count == 0)
                {
                    doc.Blocks.Add(P("Keine Detailzeilen vorhanden.", color: Brushes.DimGray));
                }
                else
                {
                    doc.Blocks.Add(BuildDetailsTable(doc, details, culture));
                }

                firstOwner = false;
            }

            return doc;
        }

        // ───────────────────────────── Tables ─────────────────────────────

        private Table BuildOwnerSummaryTable(FlowDocument doc)
        {
            var t = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };

            double inner = Math.Max(300, doc.PageWidth - doc.PagePadding.Left - doc.PagePadding.Right - 8);

            var wSum = 120d;
            var wName = Math.Max(180, inner - wSum);

            t.Columns.Add(new TableColumn { Width = new GridLength(wName) });
            t.Columns.Add(new TableColumn { Width = new GridLength(wSum) });

            var header = new TableRowGroup();
            var hr = new TableRow(); header.Rows.Add(hr);
            AddHeaderCell(hr, "Eigentümer");
            AddHeaderCell(hr, "Summe");
            t.RowGroups.Add(header);

            var data = new TableRowGroup();
            foreach (var r in OwnerRows)
            {
                var row = new TableRow(); data.Rows.Add(row);
                AddCell(row, r.EigentuemerName ?? "");
                AddCellRight(row, r.Summe.ToString("N2", CultureInfo.GetCultureInfo("de-CH")));
            }
            t.RowGroups.Add(data);

            return t;
        }

        private Table BuildOriginalTransaktionenTable(FlowDocument doc, System.Collections.Generic.IList<StweOriginalTransaktionRow> rows, CultureInfo culture)
        {
            var t = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };

            double inner = Math.Max(450, doc.PageWidth - doc.PagePadding.Left - doc.PagePadding.Right - 8);

            var wDatum = 80d;
            var wId = 70d;
            var wBetrag = 110d;

            var used = wDatum + wId + wBetrag;
            var wNotiz = Math.Max(180, inner - used);

            t.Columns.Add(new TableColumn { Width = new GridLength(wDatum) });
            t.Columns.Add(new TableColumn { Width = new GridLength(wId) });
            t.Columns.Add(new TableColumn { Width = new GridLength(wBetrag) });
            t.Columns.Add(new TableColumn { Width = new GridLength(wNotiz) });

            var header = new TableRowGroup();
            var hr = new TableRow(); header.Rows.Add(hr);
            AddHeaderCell(hr, "Datum");
            AddHeaderCell(hr, "ID");
            AddHeaderCell(hr, "Total");
            AddHeaderCell(hr, "Notiz");
            t.RowGroups.Add(header);

            var data = new TableRowGroup();
            decimal sum = 0m;

            foreach (var r in rows.OrderByDescending(x => x.Datum).ThenByDescending(x => x.TransaktionsId))
            {
                sum += r.Betrag;

                var row = new TableRow(); data.Rows.Add(row);
                AddCell(row, r.Datum.ToString("dd.MM.yyyy"));
                AddCellRight(row, r.TransaktionsId.ToString());
                AddCellRight(row, r.Betrag.ToString("N2", culture));
                AddUiCellStar(row, Trunc(r.Notiz ?? "", 200), TextAlignment.Left);
            }

            // Summenzeile
            var sumRow = new TableRow(); data.Rows.Add(sumRow);
            AddSumCell(sumRow, "Summe", colSpan: 2);
            AddSumCellRight(sumRow, sum.ToString("N2", culture));
            AddSumCell(sumRow, "", colSpan: 1);

            t.RowGroups.Add(data);
            return t;
        }

        private Table BuildDetailsTable(FlowDocument doc, System.Collections.Generic.IList<StweOwnerDetailRow> details, CultureInfo culture)
        {
            var t = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };

            double inner = Math.Max(450, doc.PageWidth - doc.PagePadding.Left - doc.PagePadding.Right - 8);

            var wDatum = 80d;
            var wTitel = 210d;
            var wSchluessel = 90d;
            var wBetrag = 100d;

            var used = wDatum + wTitel + wSchluessel + wBetrag;
            var wNotiz = Math.Max(180, inner - used);

            t.Columns.Add(new TableColumn { Width = new GridLength(wDatum) });
            t.Columns.Add(new TableColumn { Width = new GridLength(wTitel) });
            t.Columns.Add(new TableColumn { Width = new GridLength(wSchluessel) });
            t.Columns.Add(new TableColumn { Width = new GridLength(wBetrag) });
            t.Columns.Add(new TableColumn { Width = new GridLength(wNotiz) });

            var header = new TableRowGroup();
            var hr = new TableRow(); header.Rows.Add(hr);
            AddHeaderCell(hr, "Datum");
            AddHeaderCell(hr, "Titel");
            AddHeaderCell(hr, "Schlüssel");
            AddHeaderCell(hr, "Betrag");
            AddHeaderCell(hr, "Notiz");
            t.RowGroups.Add(header);

            var data = new TableRowGroup();
            decimal sum = 0m;

            foreach (var r in details)
            {
                decimal amount = r.Betrag;
                sum += amount;

                var row = new TableRow(); data.Rows.Add(row);

                AddCell(row, FormatDateObj(r.Datum));
                AddUiCell(row, Trunc(r.Titel ?? "", 120), wTitel, TextAlignment.Left);
                AddCell(row, r.Schluessel ?? "");
                AddCellRight(row, amount.ToString("N2", culture));
                AddUiCellStar(row, Trunc(r.Notiz ?? "", 240), TextAlignment.Left);
            }

            // Summenzeile
            var sumRow = new TableRow(); data.Rows.Add(sumRow);
            AddSumCell(sumRow, "Summe", colSpan: 3);
            AddSumCellRight(sumRow, sum.ToString("N2", culture));
            AddSumCell(sumRow, "", colSpan: 1);

            t.RowGroups.Add(data);
            return t;
        }


        // ───────────────────────────── Helpers ─────────────────────────────

        private static string Trunc(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, Math.Max(0, max - 1)) + "…";

        private static void AddHeaderCell(TableRow row, string text)
        {
            var p = new Paragraph(new Bold(new Run(text))) { Margin = new Thickness(0) };
            var cell = new TableCell(p)
            {
                Padding = new Thickness(2),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5),
                Background = Brushes.LightGray
            };
            row.Cells.Add(cell);
        }

        private static void AddCell(TableRow row, string text)
        {
            var p = new Paragraph(new Run(text)) { Margin = new Thickness(0) };
            var cell = new TableCell(p)
            {
                Padding = new Thickness(2),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5)
            };
            row.Cells.Add(cell);
        }

        private static void AddCellRight(TableRow row, string text)
        {
            var p = new Paragraph(new Run(text)) { Margin = new Thickness(0), TextAlignment = TextAlignment.Right };
            var cell = new TableCell(p)
            {
                Padding = new Thickness(2, 2, 8, 2),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5)
            };
            row.Cells.Add(cell);
        }
        private static void AddSumCell(TableRow row, string text, int colSpan = 1)
        {
            var p = new Paragraph(new Bold(new Run(text))) { Margin = new Thickness(0) };
            var cell = new TableCell(p)
            {
                ColumnSpan = colSpan,
                Padding = new Thickness(2),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5),
                Background = Brushes.WhiteSmoke
            };
            row.Cells.Add(cell);
        }

        private static void AddSumCellRight(TableRow row, string text)
        {
            var p = new Paragraph(new Bold(new Run(text))) { Margin = new Thickness(0), TextAlignment = TextAlignment.Right };
            var cell = new TableCell(p)
            {
                Padding = new Thickness(2, 2, 8, 2),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5),
                Background = Brushes.WhiteSmoke
            };
            row.Cells.Add(cell);
        }



        // Feste Breite + Wrap (für Titel)
        private static void AddUiCell(TableRow row, string text, double widthPx, TextAlignment align)
        {
            var tb = new TextBlock
            {
                Text = text ?? "",
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.None,
                Width = Math.Max(40, widthPx - 12),
                HorizontalAlignment = align == TextAlignment.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left
            };
            var b = new BlockUIContainer(tb) { Margin = new Thickness(0) };
            var cell = new TableCell(b) { Padding = new Thickness(2), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
            row.Cells.Add(cell);
        }

        // Restbreite + Wrap (für Notiz) – stabil wie im Geldinstitut-Print
        private static void AddUiCellStar(TableRow row, string text, TextAlignment align)
        {
            var tb = new TextBlock
            {
                Text = text ?? "",
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.None,
                HorizontalAlignment = align == TextAlignment.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left
            };
            var b = new BlockUIContainer(tb) { Margin = new Thickness(0) };
            var cell = new TableCell(b) { Padding = new Thickness(2), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
            row.Cells.Add(cell);
        }

        private static string FormatVonBis(DateTime? v, DateTime? b)
        {
            if (v == null && b == null) return "—";
            if (v != null && b == null) return $"ab {v:dd.MM.yyyy}";
            if (v == null && b != null) return $"bis {b:dd.MM.yyyy}";
            return $"{v:dd.MM.yyyy} – {b:dd.MM.yyyy}";
        }

        private static string FormatDateObj(object? dtObj)
        {
            if (dtObj == null) return "—";
            if (dtObj is DateTime dt) return dt.ToString("dd.MM.yyyy");

            if (dtObj is string s)
            {
                if (DateTime.TryParse(s, out var parsed))
                    return parsed.ToString("dd.MM.yyyy");
                return string.IsNullOrWhiteSpace(s) ? "—" : s;
            }

            try
            {
                var converted = Convert.ToDateTime(dtObj);
                return converted.ToString("dd.MM.yyyy");
            }
            catch
            {
                return dtObj.ToString() ?? "—";
            }
        }

        private static string FormatMoneyObj(object? amountObj, CultureInfo culture)
        {
            if (amountObj == null) return "0.00";
            if (amountObj is decimal d) return d.ToString("N2", culture);
            if (amountObj is double db) return db.ToString("N2", culture);
            if (amountObj is float f) return f.ToString("N2", culture);

            if (amountObj is string s && decimal.TryParse(s, NumberStyles.Any, culture, out var parsed))
                return parsed.ToString("N2", culture);

            try
            {
                var converted = Convert.ToDecimal(amountObj);
                return converted.ToString("N2", culture);
            }
            catch
            {
                return amountObj.ToString() ?? "0.00";
            }
        }
    }
}
