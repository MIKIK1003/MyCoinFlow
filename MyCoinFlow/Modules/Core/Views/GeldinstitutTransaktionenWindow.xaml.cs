using System;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MyCoinFlow.ViewModels;
using MyCoinFlow.UI.Base; // NEU
using MessageBox = System.Windows.MessageBox; // Fix Mehrdeutigkeit

namespace MyCoinFlow.Views
{
    public partial class GeldinstitutTransaktionenWindow : BaseWindow // NEU
    {
        public GeldinstitutTransaktionenWindow(int geldinstitutId, string geldinstitutName)
        {
            InitializeComponent();
            DataContext = new GeldinstitutTransaktionenViewModel(geldinstitutId, geldinstitutName);
        }

        private GeldinstitutTransaktionenViewModel VM => (GeldinstitutTransaktionenViewModel)DataContext;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new PrintDialog();
                if (dlg.ShowDialog() != true) return;

                if (dlg.PrintTicket != null)
                    dlg.PrintTicket.PageOrientation = PageOrientation.Landscape;

                var doc = BuildFlowDocumentForPrint(
                    dlg.PrintableAreaWidth,
                    dlg.PrintableAreaHeight
                );

                IDocumentPaginatorSource dps = doc;
                dlg.PrintDocument(dps.DocumentPaginator, "Geldinstitut-Transaktionen");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Druck fehlgeschlagen:\n" + ex.Message, "Drucken",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private FlowDocument BuildFlowDocumentForPrint(double printableWidth, double printableHeight)
        {
            var culture = new System.Globalization.CultureInfo("de-CH");

            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 9.5,                 // ggf. 9.0 setzen
                PagePadding = new Thickness(24),// ggf. 20 setzen
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

            doc.Blocks.Add(P("Transaktionen – Geldinstitut", bold: true));
            doc.Blocks.Add(P(BuildFilterLine(), align: TextAlignment.Left));

            var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
            doc.Blocks.Add(table);

            // ─────────────────────────────────────────────────────────
            // SPALTENBREITEN → HIER FEINJUSTIEREN
            // Ziel: kleine fixe Breiten für die ersten 5 Spalten, Notiz füllt Rest.
            var wKonto = 190d;   // vorher 110
            var wDatum = 50d;   // vorher 66
            var wEin = 100d;   // vorher 120
            var wAus = 100d;   // vorher 120
            var wAdresse = 240d;   // vorher 110
            var wNotiz = 380;
            // ─────────────────────────────────────────────────────────

            table.Columns.Add(new TableColumn { Width = new GridLength(wKonto) }); // Konto
            table.Columns.Add(new TableColumn { Width = new GridLength(wDatum) }); // Datum
            table.Columns.Add(new TableColumn { Width = new GridLength(wEin) }); // Einnahmen
            table.Columns.Add(new TableColumn { Width = new GridLength(wAus) }); // Ausgaben
            table.Columns.Add(new TableColumn { Width = new GridLength(wAdresse) }); // Adresse
            table.Columns.Add(new TableColumn { Width = new GridLength(wNotiz) }); // Notiz (rest)

            var header = new TableRowGroup();
            var headerRow = new TableRow(); header.Rows.Add(headerRow);
            AddHeaderCell(headerRow, "Konto");
            AddHeaderCell(headerRow, "Datum");
            AddHeaderCell(headerRow, "Einnahmen");
            AddHeaderCell(headerRow, "Ausgaben");
            AddHeaderCell(headerRow, "Adresse");
            AddHeaderCell(headerRow, "Notiz");
            table.RowGroups.Add(header);

            var data = new TableRowGroup();
            foreach (var r in VM.Rows.OrderByDescending(x => x.Datum).ThenByDescending(x => x.Id))
            {
                var row = new TableRow(); data.Rows.Add(row);

                AddCell(row, Trunc(r.Konto, 50));  //Anzahl Zeichen Konto
                AddCell(row, r.Datum.ToString("yy-MM-dd"));
                AddCellRight(row, r.Einnahmen.ToString("N2", culture));
                AddCellRight(row, r.Ausgaben.ToString("N2", culture));

                // Adresse & Notiz als UI-TextBlock mit Ellipsis (kein Überlappen!)
                AddUiCell(row, Trunc(r.AdresseName ?? "", 60), wAdresse, TextAlignment.Left);
                AddUiCellStar(row, Trunc(r.Notiz ?? "", 100), TextAlignment.Left); // Notiz Anzahl Zeichen
            }
            table.RowGroups.Add(data);

            decimal sumEin = VM.Rows.Sum(x => x.Einnahmen);
            decimal sumAus = VM.Rows.Sum(x => x.Ausgaben);
            decimal saldo = sumEin - sumAus;

            var totals = new TableRowGroup();
            var tr = new TableRow(); totals.Rows.Add(tr);
            AddCellBold(tr, "Summen / Saldo");
            AddCell(tr, "");
            AddCellRightBold(tr, sumEin.ToString("N2", culture));
            AddCellRightBold(tr, sumAus.ToString("N2", culture));
            AddCell(tr, "");
            AddCellRightBold(tr, $"Saldo: {saldo.ToString("N2", culture)}");
            table.RowGroups.Add(totals);

            return doc;

            // ───────────────────── Helper ─────────────────────

            string Trunc(string s, int max)
                => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, Math.Max(0, max - 1)) + "…";

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
                    Padding = new Thickness(2, 2, 8, 2),   // <— rechts 8–12 px geben
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

            // Adresse: feste Breite (px) + Wrap (mehrzeilig)
            void AddUiCell(TableRow row, string text, double widthPx, TextAlignment align)
            {
                var tb = new TextBlock
                {
                    Text = text ?? "",
                    TextWrapping = TextWrapping.Wrap,        // <— vorher NoWrap
                    TextTrimming = TextTrimming.None,        // <— kein Ellipsis
                    Width = widthPx,
                    HorizontalAlignment = align == TextAlignment.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left
                };
                var b = new BlockUIContainer(tb) { Margin = new Thickness(0) };
                var cell = new TableCell(b) { Padding = new Thickness(2), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
                row.Cells.Add(cell);
            }

            // Notiz: füllt Restbreite + Wrap (mehrzeilig)
            void AddUiCellStar(TableRow row, string text, TextAlignment align)
            {
                var tb = new TextBlock
                {
                    Text = text ?? "",
                    TextWrapping = TextWrapping.Wrap,        // <— vorher NoWrap
                    TextTrimming = TextTrimming.None,
                    HorizontalAlignment = align == TextAlignment.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left
                };
                var b = new BlockUIContainer(tb) { Margin = new Thickness(0) };
                var cell = new TableCell(b) { Padding = new Thickness(2), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
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

            decimal ein = VM.Rows.Sum(x => x.Einnahmen);
            decimal aus = VM.Rows.Sum(x => x.Ausgaben);
            decimal sal = ein - aus;

            return $"{VM.Titel}   |   Zeitraum: {VonBis(VM.FilterVon, VM.FilterBis)}   |   " +
                   $"Einnahmen {ein:N2}   |   Ausgaben {aus:N2}   |   Saldo {sal:N2}";
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1) Selektion prüfen
                if (TransGrid == null || TransGrid.SelectedItem is not GeldinstitutTransaktionenViewModel.Row row)
                {
                    MessageBox.Show("Bitte zuerst eine Transaktion in der Liste auswählen.",
                        "Bearbeiten", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 2) Vollständige Transaktion laden
                var db = new MyCoinFlow.Services.DatabaseService();
                var t = db.HoleTransaktion(row.Id);
                if (t == null)
                {
                    MessageBox.Show("Die ausgewählte Transaktion konnte nicht geladen werden.",
                        "Bearbeiten", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 3) Dialog öffnen
                var dlg = new TransactionsDialog(t);

                Window? owner = null;
                try
                {
                    owner = Application.Current?.Windows?.OfType<Window>()?.FirstOrDefault(w => w.IsActive)
                         ?? Application.Current?.MainWindow;
                }
                catch { /* ignore */ }

                if (owner != null && !ReferenceEquals(owner, dlg))
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                // 4) Nach erfolgreichem Speichern neu laden + Auswahl halten
                if (dlg.ShowDialog() == true)
                {
                    (DataContext as GeldinstitutTransaktionenViewModel)?
                        .ApplyFilterCommand?
                        .Execute(null);

                    foreach (var it in TransGrid.Items)
                    {
                        if (it is GeldinstitutTransaktionenViewModel.Row r && r.Id == row.Id)
                        {
                            TransGrid.SelectedItem = it;
                            TransGrid.ScrollIntoView(it);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Bearbeiten fehlgeschlagen:\n" + ex.Message,
                    "Bearbeiten", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }




    }
}
