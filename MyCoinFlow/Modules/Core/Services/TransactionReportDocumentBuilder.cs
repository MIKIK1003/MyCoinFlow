using MyCoinFlow.Models;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MyCoinFlow.Services
{
    public static class TransactionReportDocumentBuilder
    {
        private static readonly Brush Primary = NeueFarbe(91, 61, 145);
        private static readonly Brush PrimaryDark = NeueFarbe(65, 42, 105);
        private static readonly Brush PrimaryLight = NeueFarbe(241, 237, 248);
        private static readonly Brush HeaderBackground = NeueFarbe(225, 218, 239);
        private static readonly Brush ZebraBackground = NeueFarbe(249, 247, 252);
        private static readonly Brush TableBorder = NeueFarbe(204, 197, 216);
        private static readonly Brush MutedText = NeueFarbe(91, 88, 98);

        public static FlowDocument Build(
            TransactionReportResult result,
            double printableWidth,
            double printableHeight)
        {
            var culture = CultureInfo.GetCultureInfo("de-CH");
            var document = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 9,
                PagePadding = new Thickness(24),
                ColumnWidth = double.PositiveInfinity,
                PageWidth = printableWidth,
                PageHeight = printableHeight
            };

            var titel = new Paragraph
            {
                Background = Primary,
                Foreground = Brushes.White,
                Padding = new Thickness(12, 8, 12, 9),
                Margin = new Thickness(0)
            };
            titel.Inlines.Add(new Run("MyCoinFlow") { FontSize = 9, FontWeight = FontWeights.SemiBold });
            titel.Inlines.Add(new LineBreak());
            titel.Inlines.Add(new Bold(new Run(result.Optionen.Titel) { FontSize = 19 }));

            var metadaten = new Paragraph
            {
                Background = PrimaryLight,
                Foreground = PrimaryDark,
                BorderBrush = HeaderBackground,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 7, 10, 8),
                Margin = new Thickness(0, 0, 0, 12)
            };
            metadaten.Inlines.Add(new Bold(new Run($"Mandant: {ConnectionStrings.ActiveDatabaseName}")));
            metadaten.Inlines.Add(new Run(
                $"   |   Budget: {result.Optionen.BudgetzeitraumBezeichnung} " +
                $"({result.Optionen.BudgetVon:dd.MM.yyyy}-{result.Optionen.BudgetBis:dd.MM.yyyy})   |   " +
                $"Auswertung: {result.Optionen.AuswertungVon:dd.MM.yyyy}-{result.Optionen.AuswertungBis:dd.MM.yyyy}"));
            metadaten.Inlines.Add(new LineBreak());
            metadaten.Inlines.Add(new Run(
                $"Berichtsart: {ModusText(result.Optionen.Modus)}   |   " +
                $"Gruppierung: {GruppierungText(result.Optionen.Gruppierung)}   |   " +
                $"Konten: {result.AusgewaehlteKonten}   |   " +
                $"Erstellt: {result.ErstelltAm:dd.MM.yyyy HH:mm}"));

            document.Blocks.Add(AbschnittOhneTitel(true, titel, metadaten));
            document.Blocks.Add(Abschnitt("Zusammenfassung", true, BuildSummaryTable(result, culture)));
            document.Blocks.Add(Abschnitt("Auswertung", false, BuildMainTable(result, culture)));

            if (result.GroessteAusgaben.Count > 0)
            {
                document.Blocks.Add(Abschnitt(
                    SpotlightTitel("Top 5 Ausgaben", result),
                    true,
                    BuildSpotlightTable(result.GroessteAusgaben, culture, result.Optionen.Modus)));
            }

            if (result.GroessteEinnahmen.Count > 0)
            {
                document.Blocks.Add(Abschnitt(
                    SpotlightTitel("Top 5 Einnahmen", result),
                    true,
                    BuildSpotlightTable(result.GroessteEinnahmen, culture, result.Optionen.Modus)));
            }

            if (result.GroessteAbweichungen.Count > 0)
            {
                document.Blocks.Add(Abschnitt(
                    "Grösste ungünstige Jahresabweichungen",
                    true,
                    BuildDeviationTable(result, culture)));
            }

            var hinweis = new Paragraph(new Run(
                $"Berechnungsbasis: {result.Auswertungstage} Auswertungstage von {result.Budgettage} Tagen im Budgetzeitraum. " +
                "Hochrechnungen sind grössenabhängig auf ganze Einheiten, Zehner, Hunderter oder Tausender gerundet. " +
                "Positive Deltas sind günstig, negative Deltas ungünstig. " +
                $"Budgetabdeckung: {result.BudgetabdeckungProzent:N1} %. " +
                $"Ausgewählte Konten ohne Budgetwert: {result.KontenOhneBudget}."))
            {
                FontSize = 8,
                Foreground = MutedText,
                Background = PrimaryLight,
                BorderBrush = Primary,
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 7, 0, 0)
            };
            document.Blocks.Add(AbschnittOhneTitel(true, hinweis));

            return document;
        }

        private static Table BuildSummaryTable(TransactionReportResult result, CultureInfo culture)
        {
            var table = NeueTabelle(6);
            var group = new TableRowGroup();
            table.RowGroups.Add(group);

            AddHeader(group, "Kategorie", "Budget Jahr", "Soll Zeitraum", "Ist Zeitraum", "Hochrechnung", "Δ Jahr");
            AddSummaryRow(group, "Einnahmen", result.Einnahmen, culture);
            AddSummaryRow(group, "Ausgaben", result.Ausgaben, culture);

            var netto = new TransactionReportDirectionSummary
            {
                BudgetJahr = Differenz(result.Einnahmen.BudgetJahr, result.Ausgaben.BudgetJahr),
                SollZeitraum = Differenz(result.Einnahmen.SollZeitraum, result.Ausgaben.SollZeitraum),
                IstZeitraum = Differenz(result.Einnahmen.IstZeitraum, result.Ausgaben.IstZeitraum),
                HochrechnungJahr = Differenz(result.Einnahmen.HochrechnungJahr, result.Ausgaben.HochrechnungJahr),
                DeltaJahr = null
            };
            AddSummaryRow(group, "Nettoergebnis", netto, culture, bold: true);

            return table;
        }

        private static Table BuildSpotlightTable(
            System.Collections.Generic.IReadOnlyCollection<TransactionReportSpotlightRow> zeilen,
            CultureInfo culture,
            TransactionReportMode modus)
        {
            var zeigtHochrechnung = modus != TransactionReportMode.NurBudget;
            var table = NeueTabelle(zeigtHochrechnung ? 7 : 5);
            var group = new TableRowGroup();
            table.RowGroups.Add(group);
            if (zeigtHochrechnung)
            {
                AddHeader(group, "Rang", "Konto", "Bezeichnung", "Ist Zeitraum", "Ist-Anteil", "Hochrechnung Jahr", "Jahresanteil");
            }
            else
            {
                AddHeader(group, "Rang", "Konto", "Bezeichnung", "Jahresbudget", "Anteil");
            }

            var zeilenIndex = 0;
            foreach (var zeile in zeilen)
            {
                var row = new TableRow
                {
                    Background = zeilenIndex++ % 2 == 1 ? ZebraBackground : Brushes.White
                };
                group.Rows.Add(row);
                AddCell(row, zeile.Rang.ToString(culture));
                AddCell(row, zeile.Konto);
                AddCell(row, zeile.Bezeichnung);
                AddCell(row, zeile.Betrag.ToString("N2", culture), right: true);
                AddCell(row, zeile.AnteilProzent.ToString("N1", culture) + " %", right: true);
                if (zeigtHochrechnung)
                {
                    AddCell(row, Ganzbetrag(zeile.HochrechnungJahr, culture), right: true);
                    AddCell(row, Prozent(zeile.HochrechnungAnteilProzent, culture), right: true);
                }
            }

            return table;
        }

        private static Table BuildDeviationTable(TransactionReportResult result, CultureInfo culture)
        {
            var table = NeueTabelle(4);
            var group = new TableRowGroup();
            table.RowGroups.Add(group);
            AddHeader(group, "Rang", "Konto", "Bezeichnung", "Abweichung Jahr");

            var index = 0;
            foreach (var zeile in result.GroessteAbweichungen)
            {
                var row = new TableRow
                {
                    Background = index % 2 == 1 ? ZebraBackground : Brushes.White
                };
                group.Rows.Add(row);
                AddCell(row, (++index).ToString(culture));
                AddCell(row, zeile.Konto);
                AddCell(row, zeile.Bezeichnung);
                AddCell(row, Betrag(zeile.DeltaJahr, culture), right: true, foreground: DeltaBrush(zeile.DeltaJahr));
            }

            return table;
        }

        private static Table BuildMainTable(TransactionReportResult result, CultureInfo culture)
        {
            var table = NeueTabelle(9);
            var group = new TableRowGroup();
            table.RowGroups.Add(group);

            var kontoTitel = result.Optionen.Gruppierung == TransactionReportGrouping.Einzelkonto
                ? "Konto"
                : "Konto ab";
            AddHeader(group, kontoTitel, "Bezeichnung", "Richtung", "Budget Jahr", "Soll", "Ist", "Hochrechnung", "Δ Jahr", "Erfüllung");

            var zeilenIndex = 0;
            foreach (var row in result.Zeilen)
            {
                var tableRow = new TableRow
                {
                    Background = zeilenIndex++ % 2 == 1 ? ZebraBackground : Brushes.White
                };
                group.Rows.Add(tableRow);
                AddCell(tableRow, row.Konto);
                AddCell(tableRow, row.Bezeichnung);
                AddCell(tableRow, row.Richtung);
                AddCell(tableRow, Betrag(row.BudgetJahr, culture), right: true);
                AddCell(tableRow, Betrag(row.SollZeitraum, culture), right: true);
                AddCell(tableRow, Betrag(row.IstZeitraum, culture), right: true);
                AddCell(tableRow, Ganzbetrag(row.HochrechnungJahr, culture), right: true);
                AddCell(tableRow, Betrag(row.DeltaJahr, culture), right: true, foreground: DeltaBrush(row.DeltaJahr));
                AddCell(tableRow, Prozent(row.ErfuellungProzent, culture), right: true);
            }

            return table;
        }

        private static Table NeueTabelle(int spalten)
        {
            var table = new Table
            {
                CellSpacing = 0,
                BorderBrush = TableBorder,
                BorderThickness = new Thickness(0.5),
                Margin = new Thickness(0, 0, 0, 4)
            };

            for (var index = 0; index < spalten; index++)
                table.Columns.Add(new TableColumn());

            return table;
        }

        private static void AddHeader(TableRowGroup group, params string[] texte)
        {
            var row = new TableRow { Background = HeaderBackground };
            group.Rows.Add(row);
            foreach (var text in texte)
                AddCell(row, text, bold: true, foreground: PrimaryDark);
        }

        private static void AddSummaryRow(
            TableRowGroup group,
            string label,
            TransactionReportDirectionSummary summary,
            CultureInfo culture,
            bool bold = false)
        {
            var row = new TableRow
            {
                Background = bold ? PrimaryLight : Brushes.White
            };
            group.Rows.Add(row);
            AddCell(row, label, bold: bold);
            AddCell(row, Betrag(summary.BudgetJahr, culture), right: true, bold: bold);
            AddCell(row, Betrag(summary.SollZeitraum, culture), right: true, bold: bold);
            AddCell(row, Betrag(summary.IstZeitraum, culture), right: true, bold: bold);
            AddCell(row, Ganzbetrag(summary.HochrechnungJahr, culture), right: true, bold: bold);
            AddCell(row, Betrag(summary.DeltaJahr, culture), right: true, bold: bold, foreground: DeltaBrush(summary.DeltaJahr));
        }

        private static void AddCell(
            TableRow row,
            string text,
            bool right = false,
            bool bold = false,
            Brush? foreground = null)
        {
            Inline inline = bold ? new Bold(new Run(text)) : new Run(text);
            var paragraph = new Paragraph(inline)
            {
                Margin = new Thickness(0),
                TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
                Foreground = foreground ?? Brushes.Black
            };
            row.Cells.Add(new TableCell(paragraph)
            {
                Padding = new Thickness(4, 3, 4, 3),
                BorderBrush = TableBorder,
                BorderThickness = new Thickness(0.5)
            });
        }

        private static Paragraph Ueberschrift(string text) => new(new Bold(new Run(text)))
        {
            FontSize = 11.5,
            Foreground = PrimaryDark,
            Background = PrimaryLight,
            BorderBrush = Primary,
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(7, 4, 7, 4),
            Margin = new Thickness(0, 9, 0, 5),
            KeepWithNext = true
        };

        private static Block Abschnitt(string titel, bool zusammenhalten, params Block[] inhalt)
        {
            var blocks = new Block[inhalt.Length + 1];
            blocks[0] = Ueberschrift(titel);
            Array.Copy(inhalt, 0, blocks, 1, inhalt.Length);

            if (zusammenhalten)
                return UnteilbarerBereich(blocks);

            var section = new Section
            {
                Margin = new Thickness(0)
            };
            foreach (var block in blocks)
                section.Blocks.Add(block);
            return section;
        }

        private static Block AbschnittOhneTitel(bool zusammenhalten, params Block[] inhalt)
        {
            if (zusammenhalten)
                return UnteilbarerBereich(inhalt);

            var section = new Section
            {
                Margin = new Thickness(0)
            };
            foreach (var block in inhalt)
                section.Blocks.Add(block);
            return section;
        }

        private static Table UnteilbarerBereich(params Block[] inhalt)
        {
            var table = new Table
            {
                CellSpacing = 0,
                Margin = new Thickness(0),
                BorderThickness = new Thickness(0)
            };
            table.Columns.Add(new TableColumn());

            var group = new TableRowGroup();
            var row = new TableRow();
            var cell = new TableCell
            {
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0)
            };
            foreach (var block in inhalt)
                cell.Blocks.Add(block);

            row.Cells.Add(cell);
            group.Rows.Add(row);
            table.RowGroups.Add(group);
            return table;
        }

        private static Brush NeueFarbe(byte rot, byte gruen, byte blau)
        {
            var brush = new SolidColorBrush(Color.FromRgb(rot, gruen, blau));
            brush.Freeze();
            return brush;
        }

        private static decimal? Differenz(decimal? links, decimal? rechts)
        {
            if (!links.HasValue && !rechts.HasValue)
                return null;
            return (links ?? 0m) - (rechts ?? 0m);
        }

        private static string Betrag(decimal? value, CultureInfo culture) =>
            value.HasValue ? value.Value.ToString("N2", culture) : "–";

        private static string Ganzbetrag(decimal? value, CultureInfo culture) =>
            value.HasValue ? value.Value.ToString("N0", culture) : "–";

        private static string Prozent(decimal? value, CultureInfo culture) =>
            value.HasValue ? value.Value.ToString("N1", culture) + " %" : "–";

        private static Brush DeltaBrush(decimal? value) => value switch
        {
            < 0m => Brushes.Firebrick,
            > 0m => Brushes.DarkGreen,
            _ => Brushes.Black
        };

        private static string KontoUndBezeichnung(TransactionReportRow row) =>
            string.IsNullOrWhiteSpace(row.Konto) ? row.Bezeichnung : $"{row.Konto} {row.Bezeichnung}";

        private static string SpotlightTitel(string titel, TransactionReportResult result) =>
            result.Optionen.Modus == TransactionReportMode.NurBudget
                ? titel + " · Jahresbudget"
                : titel + " · nach Jahreshochrechnung sortiert";

        private static string ModusText(TransactionReportMode mode) => mode switch
        {
            TransactionReportMode.NurBudget => "Nur Jahresbudget",
            TransactionReportMode.IstMitHochrechnung => "Nur Ist mit Jahreshochrechnung",
            _ => "Soll/Ist mit Jahreshochrechnung"
        };

        private static string GruppierungText(TransactionReportGrouping grouping) => grouping switch
        {
            TransactionReportGrouping.Einzelkonto => "Einzelkonto",
            TransactionReportGrouping.Art => "Art",
            TransactionReportGrouping.Gruppe => "Gruppe",
            _ => "Untergruppe"
        };
    }
}
