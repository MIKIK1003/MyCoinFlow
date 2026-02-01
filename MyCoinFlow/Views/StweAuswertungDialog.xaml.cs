using MaterialDesignThemes.Wpf;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MyCoinFlow.Views
{
    public partial class StweAuswertungDialog : Window, INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new();
        private readonly StweLiegenschaft _liegenschaft;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public string TitleText => $"Auswertung – {_liegenschaft.Name}";

        public string ZeitraumText
        {
            get
            {
                if (Von == null && Bis == null)
                    return "Zeitraum: —";

                if (Von != null && Bis == null)
                    return $"Zeitraum: ab {Von:dd.MM.yyyy}";

                if (Von == null && Bis != null)
                    return $"Zeitraum: bis {Bis:dd.MM.yyyy}";

                return $"Zeitraum: {Von:dd.MM.yyyy} – {Bis:dd.MM.yyyy}";
            }
        }

        public string DetailTitle => SelectedOwnerRow == null
            ? "Details"
            : $"Details – {SelectedOwnerRow.EigentuemerName}";

        private DateTime? _von;
        public DateTime? Von
        {
            get => _von;
            set
            {
                if (_von != value)
                {
                    _von = value;
                    Notify();
                    Notify(nameof(ZeitraumText));
                }
            }
        }

        private DateTime? _bis;
        public DateTime? Bis
        {
            get => _bis;
            set
            {
                if (_bis != value)
                {
                    _bis = value;
                    Notify();
                    Notify(nameof(ZeitraumText));
                }
            }
        }

        public ObservableCollection<StweOwnerSummaryRow> OwnerRows { get; } = new();
        public ObservableCollection<StweOwnerDetailRow> DetailRows { get; } = new();
        public StweOwnerSummaryRow? SelectedOwnerRow { get; set; }

        public StweAuswertungDialog(StweLiegenschaft liegenschaft)
        {
            InitializeComponent();
            _liegenschaft = liegenschaft ?? throw new ArgumentNullException(nameof(liegenschaft));

            DataContext = this;

            // Zeitraum vorbelegen: aktiver Budgetzeitraum (falls vorhanden)
            try
            {
                if (Von == null && Bis == null)
                {
                    var activeId = _db.HoleAktivenBudgetzeitraumId();
                    if (activeId.HasValue)
                    {
                        var bz = _db.HoleBudgetzeitraum(activeId.Value);
                        if (bz != null)
                        {
                            Von = bz.Startdatum.Date;
                            Bis = bz.Enddatum.Date;
                        }
                    }
                }
            }
            catch
            {
                // bewusst still: Dialog soll trotzdem funktionieren
            }

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

            Paragraph P(string text, bool bold = false, double? fontSize = null, TextAlignment? align = null, bool dim = false)
            {
                var run = new Run(text);
                Inline inline = bold ? new Bold(run) : run;

                var para = new Paragraph(inline) { Margin = new Thickness(0) };
                if (fontSize.HasValue) para.FontSize = fontSize.Value;
                if (align.HasValue) para.TextAlignment = align.Value;
                if (dim) para.Foreground = Brushes.DimGray;
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

                doc.Blocks.Add(P($"{ZeitraumText}", dim: true));
                doc.Blocks.Add(P($"Erstellt: {DateTime.Now:dd.MM.yyyy HH:mm}", dim: true));

                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 18, 0, 0) });

                doc.Blocks.Add(P("Inhalt", bold: true, fontSize: 13));
                if (options.MitOriginalTransaktionen)
                    doc.Blocks.Add(new Paragraph(new Run("• Liste der Original-Transaktionen (Totalbetrag), die in STWE-Sets aufgeteilt wurden")) { Margin = new Thickness(0, 6, 0, 0) });
                doc.Blocks.Add(new Paragraph(new Run("• Detailauflistung je Eigentümer (aufgeteilte Positionen)")) { Margin = new Thickness(0, 2, 0, 0) });

                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 20, 0, 0) });
                doc.Blocks.Add(P("Hinweis: Der Bericht zeigt die STWE-Aufteilung basierend auf den erfassten Sets.", dim: true));

                PageBreak();
            }

            // ─────────────────────────────────────────────
            // Kopf (immer)
            doc.Blocks.Add(P(TitleText, bold: true, fontSize: 16));
            doc.Blocks.Add(P($"{ZeitraumText}   |   Erstellt: {DateTime.Now:dd.MM.yyyy HH:mm}", dim: true));
            doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 10, 0, 0) });

            // ─────────────────────────────────────────────
            // Energie-Grundlagen (neu, nur wenn im Zeitraum ENERGIE vorkommt)
            // Wir listen jede Energie-Grundlage (Zählerdaten-Set) im Zeitraum einmal auf.
            try
            {
                var sets = _db.StweSetsGetByLiegenschaft(_liegenschaft.Id, Von, Bis);

                // Nur Sets, die mindestens eine ENERGIE-Zeile haben
                var energieSets = sets
                    .Where(s =>
                    {
                        var lines = _db.StweSetLinesGet(s.Id);
                        return lines.Any(l => string.Equals(l.Schluessel, "ENERGIE", StringComparison.OrdinalIgnoreCase));
                    })
                    .ToList();

                if (energieSets.Count > 0)
                {
                    doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 10, 0, 0) });
                    doc.Blocks.Add(P("Energie – Grundlagen", bold: true, fontSize: 13));
                    doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 6, 0, 0) });

                    // De-dup: gleiche Zaehlerdaten-SetId nur einmal anzeigen
                    var seen = new HashSet<int>();

                    foreach (var s in energieSets.OrderBy(x => x.Datum).ThenBy(x => x.Id))
                    {
                        // Betrag in StweSetRow ist bei dir bereits SIGNED (Belastung + / Gutschrift -)
                        var info = _db.StweEnergieReportInfoGet(_liegenschaft.Id, s.Datum, s.Betrag);
                        if (info == null) continue;

                        if (seen.Contains(info.ZaehlerdatenSetId))
                            continue;
                        seen.Add(info.ZaehlerdatenSetId);

                        var notiz = string.IsNullOrWhiteSpace(info.ZaehlerdatenSetNotiz) ? "" : $" – {info.ZaehlerdatenSetNotiz}";
                        doc.Blocks.Add(P($"Zählerdaten-Set: {info.ZaehlerdatenSetDatum:dd.MM.yyyy}{notiz}", bold: true));

                        if (info.VorherigesZaehlerdatenSetDatum.HasValue)
                            doc.Blocks.Add(P($"Zeitraum: {info.VorherigesZaehlerdatenSetDatum:dd.MM.yyyy} – {info.ZaehlerdatenSetDatum:dd.MM.yyyy}", dim: true));
                        else
                            doc.Blocks.Add(P($"Zeitraum: (erstes Set) – {info.ZaehlerdatenSetDatum:dd.MM.yyyy}", dim: true));

                        doc.Blocks.Add(P($"Rechnung kWh total: {info.RechnungKwhTotal:0.###}    |    Preis / kWh: {info.PreisProKwh:0.####}", dim: true));

                        if (info.GutschriftChf.HasValue)
                            doc.Blocks.Add(P($"Gutschrift (Info): {info.GutschriftChf.Value:0.00}", dim: true));

                        doc.Blocks.Add(P($"Interne kWh (Diff, ohne EVU): {info.InterneKwhTotal:0.###}", dim: true));
                        doc.Blocks.Add(P($"Solar direkt (kWh): {info.SolarDirektKwh:0.###}", dim: true));


                        if (Math.Abs(info.Scale - 1m) > 0.0001m)
                            doc.Blocks.Add(P($"Scale: {info.Scale:0.######}", dim: true));

                        doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 6, 0, 0) });
                    }

                    PageBreak();
                }
            }
            catch
            {
                // bewusst still: Bericht soll trotzdem druckbar bleiben
            }

            // ─────────────────────────────────────────────
            // Original-Transaktionen (optional)
            if (options.MitOriginalTransaktionen)
            {
                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 16, 0, 0) });
                doc.Blocks.Add(P("Original-Transaktionen (Totalbetrag)", bold: true, fontSize: 13));
                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 6, 0, 0) });

                var originals = _db.StweReportOriginalTransaktionen(_liegenschaft.Id, Von, Bis);
                if (originals.Count == 0)
                {
                    doc.Blocks.Add(P("Keine Original-Transaktionen gefunden.", dim: true));
                }
                else
                {
                    doc.Blocks.Add(BuildOriginalTransaktionenTable(doc, originals, culture));
                }

                PageBreak(); // Eigentümer sollen immer auf neuer Seite beginnen
            }

            // ─────────────────────────────────────────────
            // Details für ALLE Eigentümer
            if (OwnerRows.Count == 0)
            {
                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 16, 0, 0) });
                doc.Blocks.Add(P("Keine Daten vorhanden.", dim: true));
                return doc;
            }

            bool firstOwner = true;

            foreach (var owner in OwnerRows)
            {
                if (options.NeueSeiteProEigentuemer)
                {
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
                    doc.Blocks.Add(P("Keine Detailzeilen vorhanden.", dim: true));
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

        private Table BuildOriginalTransaktionenTable(FlowDocument doc, System.Collections.Generic.IList<StweOriginalTransaktionRow> rows, CultureInfo culture)
        {
            var t = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };

            double inner = Math.Max(450, doc.PageWidth - doc.PagePadding.Left - doc.PagePadding.Right - 8);

            var wDatum = 80d;
            var wTyp = 40d;        // Icon
            var wId = 70d;
            var wBetrag = 110d;

            var used = wDatum + wTyp + wId + wBetrag;
            var wNotiz = Math.Max(180, inner - used);

            t.Columns.Add(new TableColumn { Width = new GridLength(wDatum) });
            t.Columns.Add(new TableColumn { Width = new GridLength(wTyp) });
            t.Columns.Add(new TableColumn { Width = new GridLength(wId) });
            t.Columns.Add(new TableColumn { Width = new GridLength(wBetrag) });
            t.Columns.Add(new TableColumn { Width = new GridLength(wNotiz) });

            var header = new TableRowGroup();
            var hr = new TableRow(); header.Rows.Add(hr);
            AddHeaderCell(hr, "Datum");
            AddHeaderCell(hr, "Typ");
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
                AddIconCell(row, r.Betrag < 0);
                AddCellRight(row, r.TransaktionsId.ToString());
                AddCellRight(row, r.Betrag.ToString("N2", culture));
                AddUiCellStar(row, Trunc(r.Notiz ?? "", 200), TextAlignment.Left);
            }

            var sumRow = new TableRow(); data.Rows.Add(sumRow);
            AddSumCell(sumRow, "Summe", colSpan: 3);
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

            var sumRow = new TableRow(); data.Rows.Add(sumRow);
            AddSumCell(sumRow, "Summe", colSpan: 3);
            AddSumCellRight(sumRow, sum.ToString("N2", culture));

            string hint;
            if (sum < 0m)
                hint = "Eigentümerguthaben";
            else if (sum > 0m)
                hint = "Fehlbetrag";
            else
                hint = "Ist ausgeglichen";

            AddSumUiCell(sumRow, hint);

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
            var cell = new TableCell(b)
            {
                Padding = new Thickness(2),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5)
            };
            row.Cells.Add(cell);
        }

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
            var cell = new TableCell(b)
            {
                Padding = new Thickness(2),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5)
            };
            row.Cells.Add(cell);
        }

        private static void AddIconCell(TableRow row, bool isCredit)
        {
            var icon = new PackIcon
            {
                Kind = isCredit ? PackIconKind.ArrowDownBoldCircleOutline
                                : PackIconKind.ArrowUpBoldCircleOutline,
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var container = new BlockUIContainer(icon) { Margin = new Thickness(0) };
            var cell = new TableCell(container)
            {
                Padding = new Thickness(2),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5)
            };
            row.Cells.Add(cell);
        }

        private static void AddSumUiCell(TableRow row, string text)
        {
            var tb = new TextBlock
            {
                Text = text ?? "",
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            var b = new BlockUIContainer(tb) { Margin = new Thickness(0) };

            var cell = new TableCell(b)
            {
                Padding = new Thickness(2),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5),
                Background = Brushes.WhiteSmoke
            };

            row.Cells.Add(cell);
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
    }
}
