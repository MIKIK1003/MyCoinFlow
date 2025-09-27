using System;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MyCoinFlow.ViewModels;

namespace MyCoinFlow.Views
{
    public partial class KontoTransaktionenWindow : Window
    {
        public KontoTransaktionenWindow(int kontoId, string kontoName)
        {
            InitializeComponent();
            DataContext = new KontoTransaktionenViewModel(kontoId, kontoName);
        }

        private KontoTransaktionenViewModel VM => (KontoTransaktionenViewModel)DataContext;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new PrintDialog();
                if (dlg.ShowDialog() != true) return;

                if (dlg.PrintTicket != null)
                    dlg.PrintTicket.PageOrientation = PageOrientation.Landscape;

                var doc = BuildFlowDocumentForPrint(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);
                IDocumentPaginatorSource dps = doc;
                dlg.PrintDocument(dps.DocumentPaginator, "Konto-Transaktionen");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Druck fehlgeschlagen:\n" + ex.Message, "Drucken",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private FlowDocument BuildFlowDocumentForPrint(double printableWidth, double printableHeight)
        {
            var culture = new CultureInfo("de-CH");

            // === fixe Spaltenbreiten (wie in der Grid-Ansicht, bei Bedarf frei anpassen) ===
            double wGI = 190; // Geldinstitut
            double wDatum = 50;  // Datum
            double wEin = 100; // Einnahmen
            double wAus = 100; // Ausgaben
            double wAdr = 240; // Adresse
            double wNotiz = 380; // Notiz

            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 9.5,
                PagePadding = new Thickness(24),
                ColumnWidth = double.PositiveInfinity,
                PageWidth = printableWidth,
                PageHeight = printableHeight
            };

            Paragraph P(string text, bool bold = false, TextAlignment? align = null)
            {
                var run = new Run(text);
                var para = new Paragraph(bold ? new Bold(run) : (Inline)run) { Margin = new Thickness(0) };
                if (align.HasValue) para.TextAlignment = align.Value;
                return para;
            }

            doc.Blocks.Add(P("Transaktionen – Konto", bold: true));
            doc.Blocks.Add(P(BuildFilterLine(), align: TextAlignment.Left));

            var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
            doc.Blocks.Add(table);

            table.Columns.Add(new TableColumn { Width = new GridLength(wGI) }); // Geldinstitut
            table.Columns.Add(new TableColumn { Width = new GridLength(wDatum) }); // Datum
            table.Columns.Add(new TableColumn { Width = new GridLength(wEin) }); // Einnahmen
            table.Columns.Add(new TableColumn { Width = new GridLength(wAus) }); // Ausgaben
            table.Columns.Add(new TableColumn { Width = new GridLength(wAdr) }); // Adresse
            table.Columns.Add(new TableColumn { Width = new GridLength(wNotiz) }); // Notiz

            // Header
            var header = new TableRowGroup();
            var headerRow = new TableRow(); header.Rows.Add(headerRow);
            AddHeaderCell(headerRow, "Geldinstitut");
            AddHeaderCell(headerRow, "Datum");
            AddHeaderCell(headerRow, "Einnahmen");
            AddHeaderCell(headerRow, "Ausgaben");
            AddHeaderCell(headerRow, "Adresse");
            AddHeaderCell(headerRow, "Notiz");
            table.RowGroups.Add(header);

            // Daten
            var data = new TableRowGroup();
            foreach (var r in VM.Rows.OrderByDescending(x => x.Datum).ThenByDescending(x => x.Id))
            {
                var row = new TableRow(); data.Rows.Add(row);

                AddCell(row, Trunc(r.GeldinstitutName ?? "", 50));
                AddCell(row, r.Datum.ToString("yy-MM-dd"));
                AddCellRight(row, r.Einnahmen.ToString("N2", culture));
                AddCellRight(row, r.Ausgaben.ToString("N2", culture));
                AddUiCell(row, Trunc(r.AdresseName ?? "", 60), wAdr, TextAlignment.Left);
                AddUiCell(row, Trunc(r.Notiz ?? "", 100), wNotiz, TextAlignment.Left);
            }
            table.RowGroups.Add(data);

            // Summen / Saldo / Budget / Delta (rechts)
            var totals = new TableRowGroup();
            var tr = new TableRow(); totals.Rows.Add(tr);

            // linke Zellen leer bis auf "Summen"
            AddCellBold(tr, "Summen");
            AddCell(tr, ""); // Datum
            AddCellRightBold(tr, VM.SumEinnahmen.ToString("N2", culture));
            AddCellRightBold(tr, VM.SumAusgaben.ToString("N2", culture));
            AddCell(tr, ""); // Adresse

            // rechts: Saldo | Budget | Delta in EINER Zelle (mit etwas Innenabstand)
            AddCellRightBold(tr, $"Saldo: {VM.Saldo.ToString("N2", culture)}   |   Budget: {VM.Budget.ToString("N2", culture)}   |   Δ: {VM.Delta.ToString("N2", culture)}");
            table.RowGroups.Add(totals);

            return doc;

            // ===== Helper =====
            string Trunc(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..Math.Max(0, max - 1)] + "…";

            void AddHeaderCell(TableRow row, string text)
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

            void AddCell(TableRow row, string text)
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

            void AddCellRight(TableRow row, string text)
            {
                var p = new Paragraph(new Run(text)) { Margin = new Thickness(0), TextAlignment = TextAlignment.Right };
                var cell = new TableCell(p)
                {
                    Padding = new Thickness(2),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(0.5)
                };
                row.Cells.Add(cell);
            }

            void AddCellRightBold(TableRow row, string text)
            {
                var p = new Paragraph(new Bold(new Run(text))) { Margin = new Thickness(0), TextAlignment = TextAlignment.Right };
                var cell = new TableCell(p)
                {
                    Padding = new Thickness(2, 2, 10, 2), // rechts etwas Luft
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(0.5)
                };
                row.Cells.Add(cell);
            }

            void AddCellBold(TableRow row, string text)
            {
                var p = new Paragraph(new Bold(new Run(text))) { Margin = new Thickness(0) };
                var cell = new TableCell(p)
                {
                    Padding = new Thickness(2),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(0.5)
                };
                row.Cells.Add(cell);
            }

            // UI-Cell mit Wrap (Adresse/Notiz)
            void AddUiCell(TableRow row, string text, double widthPx, TextAlignment align)
            {
                var tb = new TextBlock
                {
                    Text = text ?? "",
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.None,
                    Width = widthPx,
                    HorizontalAlignment = align == TextAlignment.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left
                };
                var b = new BlockUIContainer(tb) { Margin = new Thickness(0) };
                var cell = new TableCell(b)
                {
                    Padding = new Thickness(2),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(0.5)
                };
                row.Cells.Add(cell);
            }
        }

        private string BuildFilterLine()
        {
            string VonBis(DateTime? v, DateTime? b)
            {
                if (v == null && b == null) return "alle Daten";
                if (v != null && b == null) return $"ab {v:yyyy-MM-dd}";
                if (v == null && b != null) return $"bis {b:yyyy-MM-dd}";
                return $"{v:yyyy-MM-dd} bis {b:yyyy-MM-dd}";
            }

            return $"{VM.Titel}   |   Zeitraum: {VonBis(VM.FilterVon, VM.FilterBis)}   |   " +
                   $"Einnahmen {VM.SumEinnahmen:N2}   |   Ausgaben {VM.SumAusgaben:N2}   |   Saldo {VM.Saldo:N2}   |   Budget {VM.Budget:N2}   |   Δ {VM.Delta:N2}";
        }
    }
}
