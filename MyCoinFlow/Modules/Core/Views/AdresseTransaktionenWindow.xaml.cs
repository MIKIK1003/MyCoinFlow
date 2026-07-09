using MyCoinFlow.ViewModels;
using System;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;   // <— wichtig für FontFamily, Brushes
using MyCoinFlow.Services;


namespace MyCoinFlow.Views
{
    public partial class AdresseTransaktionenWindow : Window

    {

        public AdresseTransaktionenWindow(int adresseId, string adresseName)
        {
            InitializeComponent();
            DataContext = new AdresseTransaktionenViewModel(adresseId, adresseName);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new PrintDialog();
                if (dlg.ShowDialog() != true) return;

                if (dlg.PrintTicket != null)
                    dlg.PrintTicket.PageOrientation = PageOrientation.Landscape;

                FlowDocument BuildFlowDocument(double printableWidth, double printableHeight)
                {
                    var culture = new System.Globalization.CultureInfo("de-CH");

                    // Ausgangsbreiten (wie zuvor)
                    double wKonto = 190;
                    double wDatum = 70;
                    double wEin = 110;
                    double wAus = 110;
                    double wBank = 220;
                    double wNotiz = 360;   // wird dynamisch reduziert, falls zu breit

                    // Dokument-Layout
                    var doc = new FlowDocument
                    {
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize = 10.0,
                        PagePadding = new Thickness(24),
                        ColumnWidth = double.PositiveInfinity,
                        PageWidth = printableWidth,
                        PageHeight = printableHeight
                    };

                    // Verfügbare Breite = Seite minus Padding
                    double available = printableWidth - doc.PagePadding.Left - doc.PagePadding.Right;
                    double total = wKonto + wDatum + wEin + wAus + wBank + wNotiz;

                    // 1) Zuerst nur die letzte Spalte verkleinern (untere Grenze 180)
                    if (total > available)
                    {
                        double overshoot = total - available;
                        double newNotiz = Math.Max(180, wNotiz - overshoot);
                        if (newNotiz < wNotiz)
                        {
                            wNotiz = newNotiz;
                            total = wKonto + wDatum + wEin + wAus + wBank + wNotiz;
                        }
                    }

                    // 2) Falls immer noch zu breit: alles proportional skalieren
                    if (total > available && total > 0)
                    {
                        double scale = available / total;
                        wKonto *= scale;
                        wDatum *= scale;
                        wEin *= scale;
                        wAus *= scale;
                        wBank *= scale;
                        wNotiz *= scale;
                    }

                    var vm = (AdresseTransaktionenViewModel)DataContext;

                    Paragraph P(string text, bool bold = false, TextAlignment? align = null)
                    {
                        var run = new Run(text);
                        var para = new Paragraph(bold ? new Bold(run) : (Inline)run) { Margin = new Thickness(0, 0, 0, 4) };
                        if (align.HasValue) para.TextAlignment = align.Value;
                        return para;
                    }

                    // Titel + Summary
                    doc.Blocks.Add(P(vm.Titel, bold: true));
                    doc.Blocks.Add(P(vm.SummaryText));

                    // Filterzeile
                    string VonBis(DateTime? v, DateTime? b)
                    {
                        if (v == null && b == null) return "alle Daten";
                        if (v != null && b == null) return $"ab {v:yyyy-MM-dd}";
                        if (v == null && b != null) return $"bis {b:yyyy-MM-dd}";
                        return $"{v:yyyy-MM-dd} bis {b:yyyy-MM-dd}";
                    }
                    var filterLine =
                        $"Zeitraum: {VonBis(vm.FilterVon, vm.FilterBis)}" +
                        (vm.FilterKontoId.HasValue ? $"  |  Konto-ID: {vm.FilterKontoId.Value}" : "") +
                        (vm.FilterGeldinstitutId.HasValue ? $"  |  Bank-ID: {vm.FilterGeldinstitutId.Value}" : "") +
                        (vm.FilterMinBetrag.HasValue ? $"  |  Betrag ≥ {vm.FilterMinBetrag.Value.ToString("N2", culture)}" : "") +
                        (vm.FilterMaxBetrag.HasValue ? $"  |  Betrag ≤ {vm.FilterMaxBetrag.Value.ToString("N2", culture)}" : "");
                    doc.Blocks.Add(P(filterLine));

                    // Tabelle
                    var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
                    doc.Blocks.Add(table);

                    table.Columns.Add(new TableColumn { Width = new GridLength(wKonto) }); // Konto
                    table.Columns.Add(new TableColumn { Width = new GridLength(wDatum) }); // Datum
                    table.Columns.Add(new TableColumn { Width = new GridLength(wEin) }); // Einnahmen
                    table.Columns.Add(new TableColumn { Width = new GridLength(wAus) }); // Ausgaben
                    table.Columns.Add(new TableColumn { Width = new GridLength(wBank) }); // Geldinstitut
                    table.Columns.Add(new TableColumn { Width = new GridLength(wNotiz) }); // Notiz

                    // Header
                    var header = new TableRowGroup();
                    var hr = new TableRow(); header.Rows.Add(hr);
                    AddHeaderCell(hr, "Konto");
                    AddHeaderCell(hr, "Datum");
                    AddHeaderCell(hr, "Einnahmen");
                    AddHeaderCell(hr, "Ausgaben");
                    AddHeaderCell(hr, "Geldinstitut");
                    AddHeaderCell(hr, "Notiz");
                    table.RowGroups.Add(header);

                    // Daten
                    var data = new TableRowGroup();
                    foreach (var r in vm.Rows.OrderByDescending(x => x.Datum).ThenByDescending(x => x.Id))
                    {
                        var row = new TableRow(); data.Rows.Add(row);
                        AddCell(row, Trunc(r.Konto, 50));
                        AddCell(row, r.Datum.ToString("yyyy-MM-dd"));
                        AddCellRight(row, r.Einnahmen == 0m ? "" : r.Einnahmen.ToString("N2", culture));
                        AddCellRight(row, r.Ausgaben == 0m ? "" : r.Ausgaben.ToString("N2", culture));
                        AddCell(row, Trunc(r.GeldinstitutName ?? "", 40));
                        AddUiCellWrap(row, Trunc(r.Notiz ?? "", 140), TextAlignment.Left);
                    }
                    table.RowGroups.Add(data);

                    // Summen / Saldo
                    decimal sumEin = vm.Rows.Sum(x => x.Einnahmen);
                    decimal sumAus = vm.Rows.Sum(x => x.Ausgaben);
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

                    // --- Helpers ---
                    static string Trunc(string s, int max)
                        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, Math.Max(0, max - 1)) + "…";

                    static void AddHeaderCell(TableRow row, string text)
                    {
                        var p = new Paragraph(new Bold(new Run(text))) { Margin = new Thickness(0) };
                        var cell = new TableCell(p)
                        {
                            Padding = new Thickness(3),
                            BorderBrush = Brushes.Gray,
                            BorderThickness = new Thickness(0.5),
                            Background = Brushes.LightGray
                        };
                        row.Cells.Add(cell);
                    }

                    static void AddCell(TableRow row, string text)
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

                    static void AddCellRight(TableRow row, string text)
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

                    static void AddCellRightBold(TableRow row, string text)
                    {
                        var p = new Paragraph(new Bold(new Run(text))) { Margin = new Thickness(0), TextAlignment = TextAlignment.Right };
                        var cell = new TableCell(p)
                        {
                            Padding = new Thickness(2, 2, 10, 2),
                            BorderBrush = Brushes.Gray,
                            BorderThickness = new Thickness(0.5)
                        };
                        row.Cells.Add(cell);
                    }

                    static void AddCellBold(TableRow row, string text)
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

                    static void AddUiCellWrap(TableRow row, string text, TextAlignment align)
                    {
                        var tb = new System.Windows.Controls.TextBlock
                        {
                            Text = text ?? "",
                            TextWrapping = TextWrapping.Wrap,
                            TextTrimming = TextTrimming.None,
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

                var doc = BuildFlowDocument(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);
                IDocumentPaginatorSource dps = doc;
                dlg.PrintDocument(dps.DocumentPaginator, "Adresse-Transaktionen");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Druck fehlgeschlagen:\n" + ex.Message, "Drucken",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1) Selektion prüfen
                if (TransGrid == null || TransGrid.SelectedItem is not AdresseTransaktionenViewModel.Row row)
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
                catch { /* still */ }

                if (owner != null && !ReferenceEquals(owner, dlg))
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                // 4) Nach erfolgreichem Speichern neu laden
                if (dlg.ShowDialog() == true)
                {
                    (DataContext as AdresseTransaktionenViewModel)?
                        .ApplyFilterCommand?
                        .Execute(null);

                    // Optional: vorherige Auswahl wiederherstellen
                    foreach (var it in TransGrid.Items)
                    {
                        if (it is AdresseTransaktionenViewModel.Row r && r.Id == row.Id)
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
