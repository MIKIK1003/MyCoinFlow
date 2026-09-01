using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MyCoinFlow.WinUI.Services;

public static class StweReportDocumentBuilder
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private static readonly Brush Ink = NewBrush(35, 34, 42);
    private static readonly Brush MutedInk = NewBrush(78, 74, 88);
    private static readonly Brush Purple = NewBrush(91, 45, 169);
    private static readonly Brush PurpleDark = NewBrush(63, 30, 119);
    private static readonly Brush PurplePastel = NewBrush(220, 204, 241);
    private static readonly Brush BluePastel = NewBrush(198, 225, 239);
    private static readonly Brush GreenPastel = NewBrush(202, 230, 200);
    private static readonly Brush RosePastel = NewBrush(244, 199, 207);
    private static readonly Brush GrayPastel = NewBrush(241, 241, 245);
    private static readonly Brush Rule = NewBrush(194, 190, 202);

    public static DocumentPaginator Build(
        DatabaseService database,
        StweLiegenschaft property,
        DateTime? from,
        DateTime? to,
        double printableWidth,
        double printableHeight,
        StweReportPrintOptions options)
    {
        var width = double.IsFinite(printableWidth) && printableWidth > 500 ? printableWidth : 793d;
        var height = double.IsFinite(printableHeight) && printableHeight > 700 ? printableHeight : 1122d;
        var owners = database.StweReportOwnerSummary(property.Id, from, to);
        var details = owners.ToDictionary(
            owner => owner.EigentuemerId,
            owner => database.StweReportOwnerDetails(property.Id, owner.EigentuemerId, from, to));
        var originals = options.MitOriginalTransaktionen
            ? database.StweReportOriginalTransaktionen(property.Id, from, to)
            : new List<StweOriginalTransaktionRow>();

        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 9.4,
            Foreground = Ink,
            PagePadding = new Thickness(42, 58, 42, 52),
            ColumnWidth = double.PositiveInfinity,
            PageWidth = width,
            PageHeight = height
        };
        var contentWidth = Math.Max(440d, width - document.PagePadding.Left - document.PagePadding.Right);
        var period = PeriodText(from, to);

        if (options.MitDeckblatt)
        {
            AddCover(document, property, period, owners, details, originals, options);
            AddPageBreak(document);
        }

        AddReportTitle(document, property, period);
        AddOverview(document, owners, details);
        AddEnergyFoundation(document, database, property, from, to, contentWidth);

        if (options.MitOriginalTransaktionen)
        {
            AddSectionHeading(
                document,
                "Original-Transaktionen",
                "Ausgangsbeträge der Transaktionen, die in STWE-Sets aufgeteilt wurden");
            document.Blocks.Add(BuildOriginalTransactionsTable(originals));
        }

        if (owners.Count == 0)
        {
            AddEmptyState(document, "Keine aufgeteilten Positionen im gewählten Zeitraum vorhanden.");
        }
        else
        {
            for (var index = 0; index < owners.Count; index++)
            {
                var owner = owners[index];
                AddOwnerHeading(
                    document,
                    owner,
                    details[owner.EigentuemerId].Count,
                    breakBefore: options.NeueSeiteProEigentuemer || index == 0);
                document.Blocks.Add(BuildDetailsTable(details[owner.EigentuemerId]));
            }
        }

        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.PageSize = new Size(width, height);
        paginator.ComputePageCount();
        return new HeaderFooterPaginator(paginator, property.Name, period);
    }

    private static void AddCover(
        FlowDocument document,
        StweLiegenschaft property,
        string period,
        IReadOnlyList<StweOwnerSummaryRow> owners,
        IReadOnlyDictionary<int, List<StweOwnerDetailRow>> details,
        IReadOnlyList<StweOriginalTransaktionRow> originals,
        StweReportPrintOptions options)
    {
        var hero = new Border
        {
            Background = PurplePastel,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(28, 25, 28, 25),
            Margin = new Thickness(0, 22, 0, 26)
        };
        var heroContent = new StackPanel();
        heroContent.Children.Add(new TextBlock
        {
            Text = "MYCOINFLOW",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = Purple
        });
        heroContent.Children.Add(new TextBlock
        {
            Text = "STWE Bericht",
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = PurpleDark,
            Margin = new Thickness(0, 12, 0, 2)
        });
        heroContent.Children.Add(new TextBlock
        {
            Text = property.Name,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink
        });
        var location = PropertyLocation(property);
        if (!string.IsNullOrWhiteSpace(location))
        {
            heroContent.Children.Add(new TextBlock
            {
                Text = location,
                FontSize = 10,
                Foreground = MutedInk,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
        hero.Child = heroContent;
        document.Blocks.Add(new BlockUIContainer(hero) { Margin = new Thickness(0) });

        document.Blocks.Add(Paragraph(period, 12, Ink, new Thickness(0, 0, 0, 5), true));
        document.Blocks.Add(Paragraph(
            $"Erstellt am {DateTime.Now:dd.MM.yyyy} um {DateTime.Now:HH:mm}",
            9.5,
            MutedInk,
            new Thickness(0, 0, 0, 28)));

        var totalDetails = details.Values.Sum(rows => rows.Count);
        var total = owners.Sum(owner => owner.Summe);
        document.Blocks.Add(CreateKpiRow(
            ("EIGENTÜMER", owners.Count.ToString("N0", SwissCulture), BluePastel),
            ("DETAILPOSITIONEN", totalDetails.ToString("N0", SwissCulture), GreenPastel),
            ("GESAMTSALDO", Currency(total), total > 0 ? RosePastel : GreenPastel)));

        var contents = new Border
        {
            Background = GrayPastel,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 15, 18, 15),
            Margin = new Thickness(0, 30, 0, 0)
        };
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "Inhalt",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = PurpleDark,
            Margin = new Thickness(0, 0, 0, 8)
        });
        content.Children.Add(CoverLine("Eigentümerübersicht und Gesamtsalden"));
        content.Children.Add(CoverLine("Detailauflistung der aufgeteilten Positionen"));
        if (options.MitOriginalTransaktionen)
            content.Children.Add(CoverLine($"Original-Transaktionen mit Totalbetrag ({originals.Count:N0})"));
        content.Children.Add(CoverLine("Energie-Grundlagen und Diagramme, sofern Daten vorhanden"));
        contents.Child = content;
        document.Blocks.Add(new BlockUIContainer(contents) { Margin = new Thickness(0) });
    }

    private static TextBlock CoverLine(string text) => new()
    {
        Text = "-  " + text,
        FontSize = 10,
        Foreground = Ink,
        Margin = new Thickness(0, 3, 0, 3)
    };

    private static void AddReportTitle(FlowDocument document, StweLiegenschaft property, string period)
    {
        var panel = new Grid { Background = PurplePastel, Margin = new Thickness(0, 0, 0, 16) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Margin = new Thickness(20, 15, 20, 15) };
        title.Children.Add(new TextBlock
        {
            Text = "STWE-Auswertung",
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = PurpleDark
        });
        title.Children.Add(new TextBlock
        {
            Text = property.Name,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink,
            Margin = new Thickness(0, 3, 0, 0)
        });
        panel.Children.Add(title);
        var meta = new StackPanel { Margin = new Thickness(18), VerticalAlignment = VerticalAlignment.Center };
        meta.Children.Add(new TextBlock
        {
            Text = period.ToUpperInvariant(),
            FontSize = 8.3,
            FontWeight = FontWeights.SemiBold,
            Foreground = MutedInk,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        meta.Children.Add(new TextBlock
        {
            Text = DateTime.Now.ToString("dd.MM.yyyy HH:mm", SwissCulture),
            FontSize = 9.2,
            Foreground = Ink,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(meta, 1);
        panel.Children.Add(meta);
        document.Blocks.Add(new BlockUIContainer(panel) { Margin = new Thickness(0) });
    }

    private static void AddOverview(
        FlowDocument document,
        IReadOnlyList<StweOwnerSummaryRow> owners,
        IReadOnlyDictionary<int, List<StweOwnerDetailRow>> details)
    {
        AddSectionHeading(
            document,
            "Übersicht pro Eigentümer",
            "Salden und Anzahl der im Zeitraum zugeordneten Detailpositionen");
        var total = owners.Sum(owner => owner.Summe);
        var totalDetails = details.Values.Sum(rows => rows.Count);
        document.Blocks.Add(CreateKpiRow(
            ("EIGENTÜMER", owners.Count.ToString("N0", SwissCulture), BluePastel),
            ("POSITIONEN", totalDetails.ToString("N0", SwissCulture), GreenPastel),
            ("SALDO", Currency(total), total > 0 ? RosePastel : GreenPastel)));
        document.Blocks.Add(BuildOwnerSummaryTable(owners, details));
    }

    private static BlockUIContainer CreateKpiRow(params (string Label, string Value, Brush Background)[] values)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        foreach (var _ in values) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            var card = new Border
            {
                Background = value.Background,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(14, 11, 14, 11),
                Margin = new Thickness(index == 0 ? 0 : 5, 0, index == values.Length - 1 ? 0 : 5, 0)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = value.Label,
                FontSize = 8,
                FontWeight = FontWeights.SemiBold,
                Foreground = MutedInk
            });
            stack.Children.Add(new TextBlock
            {
                Text = value.Value,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ink,
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            card.Child = stack;
            Grid.SetColumn(card, index);
            grid.Children.Add(card);
        }
        return new BlockUIContainer(grid) { Margin = new Thickness(0) };
    }

    private static Table BuildOwnerSummaryTable(
        IReadOnlyList<StweOwnerSummaryRow> owners,
        IReadOnlyDictionary<int, List<StweOwnerDetailRow>> details)
    {
        var table = CreateTable(
            new[] { new GridLength(2.4, GridUnitType.Star), new GridLength(1, GridUnitType.Star), new GridLength(1.2, GridUnitType.Star) },
            new[] { "Eigentümer", "Positionen", "Saldo" },
            new[] { false, true, true });
        var body = new TableRowGroup();
        table.RowGroups.Add(body);
        var index = 0;
        foreach (var owner in owners)
        {
            var row = new TableRow { Background = index++ % 2 == 0 ? Brushes.White : GrayPastel };
            body.Rows.Add(row);
            AddCell(row, owner.EigentuemerName, bold: true);
            AddCell(row, details[owner.EigentuemerId].Count.ToString("N0", SwissCulture), right: true);
            AddCell(row, Currency(owner.Summe), right: true, foreground: BalanceBrush(owner.Summe));
        }
        if (owners.Count == 0) AddEmptyRow(body, 3, "Keine Daten vorhanden.");
        else AddSummaryRow(body, 3, 2, "Gesamtsaldo", Currency(owners.Sum(owner => owner.Summe)), string.Empty);
        return table;
    }

    private static void AddEnergyFoundation(
        FlowDocument document,
        DatabaseService database,
        StweLiegenschaft property,
        DateTime? from,
        DateTime? to,
        double contentWidth)
    {
        try
        {
            var dataSets = database.StweZaehlerdatenSetsGetByLiegenschaft(property.Id)
                .Where(value => !from.HasValue || value.ErfasstAm.Date >= from.Value.Date)
                .Where(value => !to.HasValue || value.ErfasstAm.Date <= to.Value.Date)
                .OrderBy(value => value.ErfasstAm)
                .ThenBy(value => value.Id)
                .ToList();
            if (dataSets.Count == 0) return;

            AddSectionHeading(
                document,
                "Energie-Grundlagen",
                "Zählerdaten-Sets und Berechnungsbasis der Energieverteilung");
            var table = CreateTable(
                new[] { new GridLength(1.1, GridUnitType.Star), new GridLength(1.1, GridUnitType.Star), new GridLength(1.1, GridUnitType.Star), new GridLength(1.7, GridUnitType.Star) },
                new[] { "Zählerdatum", "Rechnung kWh", "Preis / kWh", "Interne kWh / Solar" },
                new[] { false, true, true, true });
            var body = new TableRowGroup();
            table.RowGroups.Add(body);
            var rowIndex = 0;
            foreach (var dataSet in dataSets)
            {
                var amount = database.StweSetSumBetragByEnergieZaehlerdatenSetId(property.Id, dataSet.Id, from, to);
                var info = database.StweEnergieReportInfoGet(property.Id, dataSet.ErfasstAm, amount);
                var row = new TableRow { Background = rowIndex++ % 2 == 0 ? Brushes.White : GrayPastel };
                body.Rows.Add(row);
                AddCell(row, dataSet.ErfasstAm.ToString("dd.MM.yyyy", SwissCulture), bold: true);
                AddCell(row, info is null ? "-" : info.RechnungKwhTotal.ToString("N3", SwissCulture), right: true);
                AddCell(row, info is null ? "-" : info.PreisProKwh.ToString("N4", SwissCulture), right: true);
                AddCell(row, info is null
                    ? "Rechnung kWh fehlt"
                    : $"{info.InterneKwhTotal:N3} / {info.SolarDirektKwh:N3}", right: true);
            }
            document.Blocks.Add(table);

            try
            {
                document.Blocks.Add(BuildEnergyChartsBlock(database, property.Id, from, to, contentWidth));
            }
            catch (Exception exception)
            {
                document.Blocks.Add(InfoParagraph("Energie-Grafik konnte nicht gerendert werden: " + ExceptionText(exception)));
            }
        }
        catch (Exception exception)
        {
            document.Blocks.Add(InfoParagraph("Energie-Grundlagen konnten nicht geladen werden: " + ExceptionText(exception)));
        }
    }

    private static BlockUIContainer BuildEnergyChartsBlock(
        DatabaseService database,
        int propertyId,
        DateTime? from,
        DateTime? to,
        double contentWidth)
    {
        var data = database.StweEnergieChartGet(propertyId, from, to);
        const int chartWidth = 1000;
        const int kwhHeight = 260;
        const int solarHeight = 190;
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 12) };
        panel.Children.Add(new Image
        {
            Source = MyCoinFlow.Helpers.EnergiePrintChartRenderer.RenderKwhChart(data, chartWidth, kwhHeight),
            Stretch = Stretch.Uniform,
            Width = contentWidth,
            Height = contentWidth * kwhHeight / chartWidth
        });
        panel.Children.Add(new Image
        {
            Source = MyCoinFlow.Helpers.EnergiePrintChartRenderer.RenderSolarPctChart(data, chartWidth, solarHeight),
            Stretch = Stretch.Uniform,
            Width = contentWidth,
            Height = contentWidth * solarHeight / chartWidth,
            Margin = new Thickness(0, 8, 0, 0)
        });
        return new BlockUIContainer(panel) { Margin = new Thickness(0) };
    }

    private static Table BuildOriginalTransactionsTable(IReadOnlyList<StweOriginalTransaktionRow> rows)
    {
        var table = CreateTable(
            new[] { new GridLength(1.05, GridUnitType.Star), new GridLength(0.8, GridUnitType.Star), new GridLength(1.1, GridUnitType.Star), new GridLength(1.25, GridUnitType.Star), new GridLength(3.5, GridUnitType.Star) },
            new[] { "Datum", "Typ", "ID", "Total CHF", "Notiz" },
            new[] { false, false, true, true, false });
        var body = new TableRowGroup();
        table.RowGroups.Add(body);
        decimal sum = 0;
        var index = 0;
        foreach (var value in rows.OrderByDescending(value => value.Datum).ThenByDescending(value => value.TransaktionsId))
        {
            sum += value.Betrag;
            var row = new TableRow { Background = index++ % 2 == 0 ? Brushes.White : GrayPastel };
            body.Rows.Add(row);
            AddCell(row, value.Datum.ToString("dd.MM.yyyy", SwissCulture));
            AddCell(row, value.Betrag < 0 ? "Ausgabe" : "Einnahme");
            AddCell(row, value.TransaktionsId.ToString(SwissCulture), right: true);
            AddCell(row, Currency(value.Betrag), right: true, foreground: BalanceBrush(value.Betrag));
            AddCell(row, Truncate(value.Notiz ?? string.Empty, 180));
        }
        if (rows.Count == 0) AddEmptyRow(body, 5, "Keine Original-Transaktionen gefunden.");
        else AddSummaryRow(body, 5, 3, "Summe", Currency(sum), string.Empty);
        return table;
    }

    private static Table BuildDetailsTable(IReadOnlyList<StweOwnerDetailRow> rows)
    {
        var table = CreateTable(
            new[] { new GridLength(1.05, GridUnitType.Star), new GridLength(2.2, GridUnitType.Star), new GridLength(1.2, GridUnitType.Star), new GridLength(1.25, GridUnitType.Star), new GridLength(2.7, GridUnitType.Star) },
            new[] { "Datum", "Titel", "Schlüssel", "Betrag CHF", "Notiz" },
            new[] { false, false, false, true, false });
        var body = new TableRowGroup();
        table.RowGroups.Add(body);
        decimal sum = 0;
        var index = 0;
        foreach (var value in rows)
        {
            sum += value.Betrag;
            var row = new TableRow { Background = index++ % 2 == 0 ? Brushes.White : GrayPastel };
            body.Rows.Add(row);
            AddCell(row, value.Datum.ToString("dd.MM.yyyy", SwissCulture));
            AddCell(row, Truncate(value.Titel ?? string.Empty, 100), bold: true);
            AddCell(row, value.Schluessel ?? string.Empty);
            AddCell(row, Currency(value.Betrag), right: true, foreground: BalanceBrush(value.Betrag));
            AddCell(row, Truncate(value.Notiz ?? string.Empty, 180));
        }
        if (rows.Count == 0) AddEmptyRow(body, 5, "Keine Detailzeilen vorhanden.");
        else AddSummaryRow(body, 5, 3, "Summe", Currency(sum), BalanceHint(sum));
        return table;
    }

    private static void AddOwnerHeading(
        FlowDocument document,
        StweOwnerSummaryRow owner,
        int detailCount,
        bool breakBefore)
    {
        var table = new Table
        {
            CellSpacing = 0,
            Background = PurplePastel,
            Margin = new Thickness(0, breakBefore ? 0 : 18, 0, 8),
            BreakPageBefore = breakBefore
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(170) });
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        var row = new TableRow();
        group.Rows.Add(row);
        row.Cells.Add(new TableCell(Paragraph(owner.EigentuemerName, 13, PurpleDark, new Thickness(0), true))
        {
            Padding = new Thickness(14, 10, 14, 10)
        });
        var right = Paragraph(
            $"{detailCount:N0} Positionen  ·  {Currency(owner.Summe)}",
            9.4,
            BalanceBrush(owner.Summe),
            new Thickness(0),
            true);
        right.TextAlignment = TextAlignment.Right;
        row.Cells.Add(new TableCell(right) { Padding = new Thickness(14, 12, 14, 10) });
        document.Blocks.Add(table);
    }

    private static void AddSectionHeading(FlowDocument document, string title, string subtitle)
    {
        var heading = Paragraph(title, 14, PurpleDark, new Thickness(0, 14, 0, 2), true);
        heading.KeepWithNext = true;
        document.Blocks.Add(heading);
        var sub = Paragraph(subtitle, 9, MutedInk, new Thickness(0, 0, 0, 8));
        sub.KeepWithNext = true;
        document.Blocks.Add(sub);
    }

    private static void AddEmptyState(FlowDocument document, string text)
    {
        var paragraph = Paragraph(text, 9.5, MutedInk, new Thickness(0, 16, 0, 0));
        paragraph.Background = GrayPastel;
        paragraph.BorderBrush = PurplePastel;
        paragraph.BorderThickness = new Thickness(4, 0, 0, 0);
        paragraph.Padding = new Thickness(10, 8, 10, 8);
        document.Blocks.Add(paragraph);
    }

    private static Paragraph InfoParagraph(string text)
    {
        var paragraph = Paragraph(text, 8.8, MutedInk, new Thickness(0, 6, 0, 10));
        paragraph.Background = GrayPastel;
        paragraph.BorderBrush = PurplePastel;
        paragraph.BorderThickness = new Thickness(3, 0, 0, 0);
        paragraph.Padding = new Thickness(8, 6, 8, 6);
        return paragraph;
    }

    private static Table CreateTable(GridLength[] widths, string[] headers, bool[] rightAligned)
    {
        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = Rule,
            BorderThickness = new Thickness(0.6),
            Margin = new Thickness(0, 3, 0, 14)
        };
        foreach (var width in widths) table.Columns.Add(new TableColumn { Width = width });
        var headerGroup = new TableRowGroup();
        table.RowGroups.Add(headerGroup);
        var header = new TableRow { Background = PurplePastel };
        headerGroup.Rows.Add(header);
        for (var index = 0; index < headers.Length; index++)
            AddCell(header, headers[index], right: rightAligned[index], bold: true, foreground: PurpleDark);
        return table;
    }

    private static void AddCell(
        TableRow row,
        string text,
        bool right = false,
        bool bold = false,
        Brush? foreground = null)
    {
        var paragraph = Paragraph(text, bold ? 8.9 : 8.6, foreground ?? (bold ? Ink : MutedInk), new Thickness(0), bold);
        paragraph.TextAlignment = right ? TextAlignment.Right : TextAlignment.Left;
        row.Cells.Add(new TableCell(paragraph)
        {
            Padding = new Thickness(7, 5, 7, 5),
            BorderBrush = Rule,
            BorderThickness = new Thickness(0, 0, 0, 0.4)
        });
    }

    private static void AddEmptyRow(TableRowGroup group, int columns, string text)
    {
        var row = new TableRow { Background = GrayPastel };
        group.Rows.Add(row);
        row.Cells.Add(new TableCell(Paragraph(text, 8.8, MutedInk, new Thickness(0)))
        {
            ColumnSpan = columns,
            Padding = new Thickness(8, 7, 8, 7)
        });
    }

    private static void AddSummaryRow(
        TableRowGroup group,
        int totalColumns,
        int labelSpan,
        string label,
        string amount,
        string hint)
    {
        var row = new TableRow { Background = BluePastel };
        group.Rows.Add(row);
        row.Cells.Add(new TableCell(Paragraph(label, 8.9, Ink, new Thickness(0), true))
        {
            ColumnSpan = labelSpan,
            Padding = new Thickness(7, 6, 7, 6)
        });
        var amountParagraph = Paragraph(amount, 8.9, Ink, new Thickness(0), true);
        amountParagraph.TextAlignment = TextAlignment.Right;
        row.Cells.Add(new TableCell(amountParagraph) { Padding = new Thickness(7, 6, 7, 6) });
        var remaining = totalColumns - labelSpan - 1;
        if (remaining > 0)
        {
            row.Cells.Add(new TableCell(Paragraph(hint, 8.5, MutedInk, new Thickness(0), true))
            {
                ColumnSpan = remaining,
                Padding = new Thickness(7, 6, 7, 6)
            });
        }
    }

    private static Paragraph Paragraph(
        string text,
        double size,
        Brush brush,
        Thickness margin,
        bool bold = false)
    {
        Inline inline = bold ? new Bold(new Run(text)) : new Run(text);
        return new Paragraph(inline)
        {
            FontSize = size,
            Foreground = brush,
            Margin = margin,
            LineHeight = size * 1.35
        };
    }

    private static void AddPageBreak(FlowDocument document) => document.Blocks.Add(new Paragraph
    {
        BreakPageBefore = true,
        Margin = new Thickness(0)
    });

    private static string PeriodText(DateTime? from, DateTime? to) => from is null && to is null
        ? "Zeitraum nicht eingeschränkt"
        : from is not null && to is null
            ? $"Zeitraum ab {from:dd.MM.yyyy}"
            : from is null
                ? $"Zeitraum bis {to:dd.MM.yyyy}"
                : $"Zeitraum {from:dd.MM.yyyy} - {to:dd.MM.yyyy}";

    private static string PropertyLocation(StweLiegenschaft property) => string.Join(" · ", new[]
    {
        property.Strasse,
        string.Join(" ", new[] { property.PLZ, property.Ort }.Where(value => !string.IsNullOrWhiteSpace(value)))
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string Currency(decimal value) => "CHF " + value.ToString("N2", SwissCulture);
    private static string BalanceHint(decimal value) => value < 0 ? "Eigentümerguthaben" : value > 0 ? "Fehlbetrag" : "Ausgeglichen";
    private static Brush BalanceBrush(decimal value) => value > 0 ? NewBrush(157, 43, 60) : value < 0 ? NewBrush(37, 112, 68) : Ink;
    private static string Truncate(string value, int maximum) => string.IsNullOrEmpty(value) || value.Length <= maximum ? value : value[..Math.Max(0, maximum - 1)] + "…";
    private static string ExceptionText(Exception exception) => string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

    private static SolidColorBrush NewBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private sealed class HeaderFooterPaginator : DocumentPaginator
    {
        private readonly DocumentPaginator _inner;
        private readonly string _propertyName;
        private readonly string _period;

        public HeaderFooterPaginator(DocumentPaginator inner, string propertyName, string period)
        {
            _inner = inner;
            _propertyName = propertyName;
            _period = period;
        }

        public override bool IsPageCountValid => _inner.IsPageCountValid;
        public override int PageCount => _inner.PageCount;
        public override Size PageSize { get => _inner.PageSize; set => _inner.PageSize = value; }
        public override IDocumentPaginatorSource Source => _inner.Source;

        public override DocumentPage GetPage(int pageNumber)
        {
            var page = _inner.GetPage(pageNumber);
            var container = new ContainerVisual();
            if (page.Visual is Visual content) container.Children.Add(content);
            var chrome = new DrawingVisual();
            using (var context = chrome.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, page.Size.Width, 48));
                context.DrawRectangle(Brushes.White, null, new Rect(0, page.Size.Height - 42, page.Size.Width, 42));
                var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                var header = new FormattedText("MyCoinFlow · STWE Bericht", SwissCulture, FlowDirection.LeftToRight, typeface, 8.5, PurpleDark, 1);
                context.DrawText(header, new Point(42, 22));
                var property = new FormattedText(_propertyName, SwissCulture, FlowDirection.LeftToRight, typeface, 8.1, MutedInk, 1)
                {
                    MaxTextWidth = Math.Max(100, page.Size.Width - 330),
                    Trimming = TextTrimming.CharacterEllipsis
                };
                context.DrawText(property, new Point(180, 22));
                context.DrawLine(new Pen(Rule, 0.7), new Point(42, 42), new Point(page.Size.Width - 42, 42));
                context.DrawLine(new Pen(Rule, 0.7), new Point(42, page.Size.Height - 36), new Point(page.Size.Width - 42, page.Size.Height - 36));
                var period = new FormattedText(_period, SwissCulture, FlowDirection.LeftToRight, typeface, 7.8, MutedInk, 1);
                context.DrawText(period, new Point(42, page.Size.Height - 26));
                var footer = new FormattedText($"Seite {pageNumber + 1} von {PageCount}", SwissCulture, FlowDirection.LeftToRight, typeface, 8, MutedInk, 1);
                context.DrawText(footer, new Point(page.Size.Width - 42 - footer.Width, page.Size.Height - 26));
            }
            container.Children.Add(chrome);
            return new DocumentPage(container, page.Size, page.BleedBox, page.ContentBox);
        }
    }
}
