using MyCoinFlow.WinUI.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyCoinFlow.WinUI.Services;

public static class DashboardPrintDocumentBuilder
{
    private const int BudgetRowsPerPage = 6;
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private static readonly Brush Ink = Brush(34, 34, 40);
    private static readonly Brush MutedInk = Brush(70, 70, 80);
    private static readonly Brush Purple = Brush(91, 45, 169);
    private static readonly Brush PurplePastel = Brush(218, 201, 242);
    private static readonly Brush BluePastel = Brush(197, 225, 240);
    private static readonly Brush GreenPastel = Brush(202, 230, 200);
    private static readonly Brush YellowPastel = Brush(248, 224, 157);
    private static readonly Brush RosePastel = Brush(244, 199, 207);
    private static readonly Brush GrayPastel = Brush(238, 239, 243);
    private static readonly Brush Rule = Brush(188, 188, 198);
    private static readonly Brush[] ChartPalette =
    [
        Brush(151, 112, 207), Brush(74, 158, 194), Brush(104, 168, 88), Brush(225, 158, 58),
        Brush(209, 92, 112), Brush(168, 117, 195), Brush(54, 157, 146), Brush(185, 127, 53),
        Brush(77, 132, 185), Brush(133, 133, 145)
    ];

    private readonly record struct SignedScale(
        double AxisX,
        double NegativeMaximum,
        double PositiveMaximum,
        double NegativeWidth,
        double PositiveWidth);

    public static DocumentPaginator Build(DashboardPrintModel model, double printableWidth, double printableHeight)
    {
        var width = double.IsFinite(printableWidth) && printableWidth > 500 ? printableWidth : 793d;
        var height = double.IsFinite(printableHeight) && printableHeight > 700 ? printableHeight : 1122d;
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 9.5,
            Foreground = Ink,
            PageWidth = width,
            PageHeight = height,
            PagePadding = new Thickness(42, 58, 42, 52),
            ColumnWidth = double.PositiveInfinity
        };

        AddTitle(document, model);
        AddKpis(document, model);
        var contentWidth = Math.Max(420d, width - document.PagePadding.Left - document.PagePadding.Right);
        if (model.IsStwe) AddStweReport(document, model, contentWidth);
        else AddBudgetReport(document, model, contentWidth);

        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.PageSize = new Size(width, height);
        paginator.ComputePageCount();
        return new HeaderFooterPaginator(paginator, model.IsStwe ? "Dashboard - STWE" : "Dashboard - Budget", model.Subtitle);
    }

    private static void AddTitle(FlowDocument document, DashboardPrintModel model)
    {
        var panel = new Grid { Background = PurplePastel, Margin = new Thickness(0, 0, 0, 16) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        title.Children.Add(new TextBlock { Text = "MyCoinFlow Dashboard", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Purple });
        title.Children.Add(new TextBlock { Text = model.IsStwe ? "STWE- und Liegenschaftsauswertung" : "Budget- und Finanzübersicht", FontSize = 12, Foreground = MutedInk, Margin = new Thickness(0, 3, 0, 0) });
        panel.Children.Add(title);
        var created = new StackPanel { Margin = new Thickness(18), VerticalAlignment = VerticalAlignment.Center };
        created.Children.Add(new TextBlock { Text = "ERSTELLT", FontSize = 8, FontWeight = FontWeights.SemiBold, Foreground = MutedInk, HorizontalAlignment = HorizontalAlignment.Right });
        created.Children.Add(new TextBlock { Text = DateTime.Now.ToString("dd.MM.yyyy HH:mm", SwissCulture), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Right });
        Grid.SetColumn(created, 1);
        panel.Children.Add(created);
        document.Blocks.Add(new BlockUIContainer(panel) { Margin = new Thickness(0) });
        document.Blocks.Add(Paragraph(model.Subtitle, 10, MutedInk, new Thickness(0, 0, 0, 14)));
    }

    private static void AddKpis(FlowDocument document, DashboardPrintModel model)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        for (var index = 0; index < 3; index++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddKpiCard(grid, 0, "KREDITKARTE (OFFEN)", model.CreditCardOpenCount.ToString("N0", SwissCulture), BluePastel);
        AddKpiCard(grid, 1, "BANK (OFFEN)", model.BankOpenCount.ToString("N0", SwissCulture), GreenPastel);
        AddKpiCard(grid, 2, model.IsStwe ? "BEREICH" : "GRUPPIERUNG", model.IsStwe ? "STWE" : model.Grouping, YellowPastel);
        document.Blocks.Add(new BlockUIContainer(grid) { Margin = new Thickness(0) });
    }

    private static void AddKpiCard(Grid grid, int column, string label, string value, Brush background)
    {
        var card = new Border { Background = background, CornerRadius = new CornerRadius(9), Padding = new Thickness(15, 12, 15, 12), Margin = new Thickness(column == 0 ? 0 : 5, 0, column == 2 ? 0 : 5, 0) };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = label, FontSize = 8.5, FontWeight = FontWeights.SemiBold, Foreground = MutedInk });
        content.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        card.Child = content;
        Grid.SetColumn(card, column);
        grid.Children.Add(card);
    }

    private static void AddBudgetReport(FlowDocument document, DashboardPrintModel model, double contentWidth)
    {
        if (model.RangeSections.Count == 0)
        {
            AddSectionHeading(document, "Keine auswertbaren Kontenkreise", "Bitte Einnahmen, Ausgaben, Anschaffungen oder Investitionen auswählen.");
        }
        else
        {
            for (var sectionIndex = 0; sectionIndex < model.RangeSections.Count; sectionIndex++)
            {
                var section = model.RangeSections[sectionIndex];
                var chunks = section.Comparison.Count == 0
                    ? new[] { Array.Empty<DashboardComparisonRow>() }
                    : section.Comparison.Chunk(BudgetRowsPerPage).ToArray();

                for (var chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
                {
                    var chunk = chunks[chunkIndex];
                    var firstPosition = section.Comparison.Count == 0 ? 0 : chunkIndex * BudgetRowsPerPage + 1;
                    var lastPosition = section.Comparison.Count == 0 ? 0 : firstPosition + chunk.Length - 1;
                    var continuation = chunkIndex > 0;
                    var subtitle = section.Comparison.Count == 0
                        ? $"Kontenkreis {section.RangeStart:N0}-{section.RangeEnd:N0} · Gruppierung: {model.Grouping} · keine Positionen"
                        : $"Kontenkreis {section.RangeStart:N0}-{section.RangeEnd:N0} · Gruppierung: {model.Grouping} · Positionen {firstPosition:N0}-{lastPosition:N0} von {section.Comparison.Count:N0}";
                    AddSectionHeading(
                        document,
                        continuation ? $"{section.Title} - Fortsetzung" : section.Title,
                        subtitle,
                        sectionIndex > 0 || continuation);
                    if (!continuation) AddRangeSummary(document, section.Comparison);
                    AddSubsectionHeading(document, $"Budget und IST nach {model.Grouping}");
                    document.Blocks.Add(BuildComparisonChartBlock(chunk, contentWidth));
                    var comparisonRows = chunk.Select(row => new[]
                    {
                        row.Label,
                        row.Budget.ToString("N2", SwissCulture),
                        row.Actual.ToString("N2", SwissCulture),
                        (row.Actual - row.Budget).ToString("N2", SwissCulture)
                    });
                    AddTable(
                        document,
                        new[] { string.IsNullOrWhiteSpace(model.Grouping) ? "Gruppierung" : model.Grouping, "Budget", "IST", "Abweichung" },
                        comparisonRows,
                        new[] { 2.5d, 1d, 1d, 1d },
                        new[] { false, true, true, true });
                }
            }
        }

        if (model.Banks.Any(row => Math.Abs(row.Value) > 0.0001))
        {
            AddSectionHeading(document, "Bestände der Geldinstitute", "Weitere Dashboardwerte · Saldo per aktuellem Stand", model.RangeSections.Count > 0);
            document.Blocks.Add(BuildSignedValueChartBlock(model.Banks, BluePastel, RosePastel, contentWidth: contentWidth));
            AddValueTable(document, model.Banks, "Bestand");
        }
    }

    private static void AddRangeSummary(FlowDocument document, IReadOnlyList<DashboardComparisonRow> rows)
    {
        var budget = rows.Sum(row => row.Budget);
        var actual = rows.Sum(row => row.Actual);
        var deviation = actual - budget;
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        for (var index = 0; index < 3; index++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddKpiCard(grid, 0, "BUDGET", budget.ToString("N2", SwissCulture), PurplePastel);
        AddKpiCard(grid, 1, "IST", actual.ToString("N2", SwissCulture), BluePastel);
        AddKpiCard(grid, 2, "ABWEICHUNG", deviation.ToString("N2", SwissCulture), deviation < 0 ? RosePastel : GreenPastel);
        document.Blocks.Add(new BlockUIContainer(grid) { Margin = new Thickness(0) });
    }

    private static void AddStweReport(FlowDocument document, DashboardPrintModel model, double contentWidth)
    {
        if (model.Energy.Any(row => Math.Abs(row.Invoice) > 0.0001 || Math.Abs(row.Internal) > 0.0001 || Math.Abs(row.Solar) > 0.0001))
        {
            AddSectionHeading(document, "Energie kWh", "Rechnung, interne Zähler und direkt genutzte Solarenergie nach Quartal");
            document.Blocks.Add(BuildEnergyChartBlock(model.Energy, contentWidth));
        }

        if (model.Solar.Any(row => Math.Abs(row.Percent) > 0.0001))
        {
            AddSectionHeading(document, "Solar-Anteil", "Direkt genutzte Solarenergie in Prozent");
            var solarRows = model.Solar.Select(row => new DashboardValueRow { Label = row.Label, Value = row.Percent }).ToList();
            document.Blocks.Add(BuildSignedValueChartBlock(solarRows, GreenPastel, RosePastel, " %", contentWidth));
        }

        if (model.OwnerKwh.Any(row => Math.Abs(row.Value) > 0.0001))
        {
            AddSectionHeading(document, "kWh pro Eigentümer", "Direkter und anteiliger Verbrauch ALLG + HEIZ");
            document.Blocks.Add(BuildSignedValueChartBlock(model.OwnerKwh, BluePastel, RosePastel, " kWh", contentWidth));
        }

        if (model.OwnerChf.Any(row => Math.Abs(row.Value) > 0.0001))
        {
            AddSectionHeading(document, "CHF pro Eigentümer", "Beträge aus STWE-Verteilungen und SetLines");
            document.Blocks.Add(BuildSignedValueChartBlock(model.OwnerChf, PurplePastel, RosePastel, " CHF", contentWidth));
        }
    }

    private static BlockUIContainer BuildPieBlock(IReadOnlyList<DashboardDistributionRow> rows)
    {
        var positive = rows.Where(row => row.Value > 0).ToList();
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var chart = new Image { Source = RenderPie(positive, 520, 420), Height = 245, Stretch = Stretch.Uniform };
        grid.Children.Add(chart);
        var legend = new StackPanel { Margin = new Thickness(14, 4, 0, 0) };
        if (positive.Count == 0) legend.Children.Add(new TextBlock { Text = "Keine IST-Werte vorhanden.", Foreground = MutedInk });
        else
        {
            var total = positive.Sum(row => row.Value);
            foreach (var item in positive.Take(10).Select((row, index) => (row, index)))
            {
                var line = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5), Background = ChartPalette[item.index % ChartPalette.Length], VerticalAlignment = VerticalAlignment.Center });
                var label = new TextBlock { Text = item.row.Label, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ink, FontSize = 9 };
                Grid.SetColumn(label, 1); line.Children.Add(label);
                var value = new TextBlock { Text = $"{item.row.Value:N2}  ·  {item.row.Value / total:P0}", Foreground = MutedInk, FontSize = 8.5, Margin = new Thickness(8, 0, 0, 0) };
                Grid.SetColumn(value, 2); line.Children.Add(value);
                legend.Children.Add(line);
            }
            if (positive.Count > 10) legend.Children.Add(new TextBlock { Text = $"+ {positive.Count - 10} weitere Positionen in der Detailtabelle", Foreground = MutedInk, FontSize = 8, Margin = new Thickness(15, 4, 0, 0) });
        }
        Grid.SetColumn(legend, 1); grid.Children.Add(legend);
        return new BlockUIContainer(grid) { Margin = new Thickness(0) };
    }

    private static Section BuildComparisonChartBlock(IReadOnlyList<DashboardComparisonRow> rows, double contentWidth)
    {
        var section = new Section { Margin = new Thickness(0) };
        if (rows.Count == 0) return section;
        foreach (var chunk in rows.Chunk(10))
        {
            var source = RenderComparisonChart(chunk, 1000);
            var image = CreateSizedChartImage(source, contentWidth, new Thickness(0, 2, 0, 8));
            section.Blocks.Add(new BlockUIContainer(image) { Margin = new Thickness(0) });
        }
        return section;
    }

    private static Section BuildSignedValueChartBlock(IReadOnlyList<DashboardValueRow> rows, Brush positiveBrush, Brush negativeBrush, string suffix = "", double contentWidth = 680d)
    {
        var section = new Section { Margin = new Thickness(0) };
        if (rows.Count == 0) return section;
        foreach (var chunk in rows.Chunk(12))
        {
            var source = RenderSignedValueChart(chunk, 1000, positiveBrush, negativeBrush, suffix);
            var image = CreateSizedChartImage(source, contentWidth, new Thickness(0, 2, 0, 8));
            section.Blocks.Add(new BlockUIContainer(image) { Margin = new Thickness(0) });
        }
        return section;
    }

    private static BlockUIContainer BuildEnergyChartBlock(IReadOnlyList<DashboardEnergyRow> rows, double contentWidth)
    {
        var source = RenderEnergyChart(rows, 1000);
        var image = CreateSizedChartImage(source, contentWidth, new Thickness(0, 2, 0, 8));
        return new BlockUIContainer(image) { Margin = new Thickness(0) };
    }

    private static Image CreateSizedChartImage(BitmapSource source, double contentWidth, Thickness margin)
    {
        var width = Math.Max(1d, contentWidth);
        var height = width * source.PixelHeight / Math.Max(1d, source.PixelWidth);
        return new Image
        {
            Source = source,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = margin
        };
    }

    private static BitmapSource RenderComparisonChart(IReadOnlyList<DashboardComparisonRow> rows, int width)
    {
        const int top = 42;
        const int rowHeight = 56;
        var height = top + Math.Max(1, rows.Count) * rowHeight + 22;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(GrayPastel, null, new Rect(0, 0, width, height), 16, 16);
            DrawLegend(context, 22, 20, [(PurplePastel, "Budget"), (BluePastel, "IST")]);
            var plotLeft = 245d;
            var valueWidth = 190d;
            var plotWidth = width - plotLeft - valueWidth;
            var scale = CreateSignedScale(rows.SelectMany(row => new[] { row.Budget, row.Actual }), plotLeft, plotWidth);
            context.DrawLine(new Pen(Rule, 1), new Point(scale.AxisX, top - 5), new Point(scale.AxisX, height - 16));
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var y = top + index * rowHeight + rowHeight / 2d;
                DrawText(context, row.Label, 18, y - 9, 210, 14, Ink, false);
                DrawSignedBar(context, row.Budget, scale, y - 13, 11, PurplePastel);
                DrawSignedBar(context, row.Actual, scale, y + 3, 11, BluePastel);
                DrawText(context, $"B {row.Budget:N2}  |  I {row.Actual:N2}", width - valueWidth + 8, y - 9, valueWidth - 20, 11.5, MutedInk, true);
            }
        }
        return ToBitmap(visual, width, height);
    }

    private static BitmapSource RenderSignedValueChart(IReadOnlyList<DashboardValueRow> rows, int width, Brush positiveBrush, Brush negativeBrush, string suffix)
    {
        const int top = 20;
        const int rowHeight = 45;
        var height = top + Math.Max(1, rows.Count) * rowHeight + 18;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(GrayPastel, null, new Rect(0, 0, width, height), 16, 16);
            var plotLeft = 245d;
            var valueWidth = 170d;
            var plotWidth = width - plotLeft - valueWidth;
            var scale = CreateSignedScale(rows.Select(row => row.Value), plotLeft, plotWidth);
            context.DrawLine(new Pen(Rule, 1), new Point(scale.AxisX, 12), new Point(scale.AxisX, height - 12));
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var y = top + index * rowHeight + rowHeight / 2d;
                DrawText(context, row.Label, 18, y - 8, 210, 14, Ink, false);
                DrawSignedBar(context, row.Value, scale, y - 8, 16, row.Value < 0 ? negativeBrush : positiveBrush);
                DrawText(context, row.Value.ToString("N2", SwissCulture) + suffix, width - valueWidth + 8, y - 8, valueWidth - 20, 12, MutedInk, true);
            }
        }
        return ToBitmap(visual, width, height);
    }

    private static BitmapSource RenderEnergyChart(IReadOnlyList<DashboardEnergyRow> rows, int width)
    {
        const int top = 45;
        const int rowHeight = 72;
        var height = top + Math.Max(1, rows.Count) * rowHeight + 20;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(GrayPastel, null, new Rect(0, 0, width, height), 16, 16);
            DrawLegend(context, 22, 20, [(PurplePastel, "Rechnung"), (BluePastel, "Intern"), (GreenPastel, "Solar direkt")]);
            var plotLeft = 140d;
            var valueWidth = 260d;
            var plotWidth = width - plotLeft - valueWidth;
            var scale = CreateSignedScale(rows.SelectMany(row => new[] { row.Invoice, row.Internal, row.Solar }), plotLeft, plotWidth);
            context.DrawLine(new Pen(Rule, 1), new Point(scale.AxisX, top - 4), new Point(scale.AxisX, height - 14));
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var y = top + index * rowHeight + rowHeight / 2d;
                DrawText(context, row.Quarter, 24, y - 8, 90, 14, Ink, false);
                DrawSignedBar(context, row.Invoice, scale, y - 20, 11, PurplePastel);
                DrawSignedBar(context, row.Internal, scale, y - 5, 11, BluePastel);
                DrawSignedBar(context, row.Solar, scale, y + 10, 11, GreenPastel);
                DrawText(context, $"R {row.InvoiceText}  |  I {row.InternalText}  |  S {row.SolarText}", width - valueWidth + 8, y - 8, valueWidth - 20, 11.5, MutedInk, true);
            }
        }
        return ToBitmap(visual, width, height);
    }

    private static SignedScale CreateSignedScale(IEnumerable<double> values, double plotLeft, double plotWidth)
    {
        var materialized = values.ToList();
        var negativeMaximum = materialized.Where(value => value < 0).Select(Math.Abs).DefaultIfEmpty(0d).Max();
        var positiveMaximum = materialized.Where(value => value > 0).DefaultIfEmpty(0d).Max();
        double axisX;
        if (negativeMaximum > 0 && positiveMaximum > 0)
            axisX = plotLeft + plotWidth * negativeMaximum / (negativeMaximum + positiveMaximum);
        else
            axisX = negativeMaximum > 0 ? plotLeft + plotWidth : plotLeft;

        return new SignedScale(
            axisX,
            negativeMaximum,
            positiveMaximum,
            Math.Max(0, axisX - plotLeft - 5),
            Math.Max(0, plotLeft + plotWidth - axisX - 5));
    }

    private static void DrawSignedBar(DrawingContext context, double value, SignedScale scale, double y, double height, Brush brush)
    {
        if (Math.Abs(value) <= 0.0001) return;
        var maximum = value < 0 ? scale.NegativeMaximum : scale.PositiveMaximum;
        var availableWidth = value < 0 ? scale.NegativeWidth : scale.PositiveWidth;
        if (maximum <= 0 || availableWidth <= 0) return;
        var length = Math.Max(2, Math.Abs(value) / maximum * availableWidth);
        var rect = value < 0 ? new Rect(scale.AxisX - length, y, length, height) : new Rect(scale.AxisX, y, length, height);
        context.DrawRoundedRectangle(brush, new Pen(Rule, 0.45), rect, height / 2d, height / 2d);
    }

    private static void DrawLegend(DrawingContext context, double x, double y, IReadOnlyList<(Brush Brush, string Label)> values)
    {
        var currentX = x;
        foreach (var value in values)
        {
            context.DrawRoundedRectangle(value.Brush, new Pen(Rule, 0.4), new Rect(currentX, y - 7, 13, 13), 3, 3);
            DrawText(context, value.Label, currentX + 19, y - 8, 100, 11.5, MutedInk, false);
            currentX += 125;
        }
    }

    private static void DrawText(DrawingContext context, string text, double x, double y, double width, double fontSize, Brush brush, bool rightAligned)
    {
        var formatted = new FormattedText(text ?? string.Empty, SwissCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), fontSize, brush, 1)
        {
            MaxTextWidth = Math.Max(10, width),
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = rightAligned ? TextAlignment.Right : TextAlignment.Left
        };
        context.DrawText(formatted, new Point(x, y));
    }

    private static BitmapSource ToBitmap(DrawingVisual visual, int width, int height)
    {
        const double dpi = 144d;
        var scale = dpi / 96d;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * scale),
            (int)Math.Ceiling(height * scale),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource RenderPie(IReadOnlyList<DashboardDistributionRow> rows, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(GrayPastel, null, new Rect(0, 0, width, height), 18, 18);
            var total = rows.Sum(row => row.Value);
            var center = new Point(width / 2d, height / 2d);
            var radius = Math.Min(width, height) * 0.38d;
            if (total <= 0)
            {
                context.DrawEllipse(Brush(232, 232, 236), null, center, radius, radius);
            }
            else
            {
                var start = -90d;
                foreach (var item in rows.Select((row, index) => (row, index)))
                {
                    var sweep = item.row.Value / total * 360d;
                    if (sweep >= 359.999)
                        context.DrawEllipse(ChartPalette[item.index % ChartPalette.Length], null, center, radius, radius);
                    else
                        context.DrawGeometry(ChartPalette[item.index % ChartPalette.Length], new Pen(Brush(255, 255, 255), 2), CreateWedge(center, radius, start, sweep));
                    start += sweep;
                }
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Geometry CreateWedge(Point center, double radius, double startAngle, double sweepAngle)
    {
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweepAngle);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(center, true, true);
            context.LineTo(start, true, false);
            context.ArcTo(end, new Size(radius, radius), 0, sweepAngle > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180d;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static void AddDistributionTable(FlowDocument document, IReadOnlyList<DashboardDistributionRow> rows)
    {
        var total = rows.Sum(row => row.Value);
        AddTable(document, new[] { "Gruppe", "IST-Wert", "Anteil" }, rows.Select(row => new[] { row.Label, row.Value.ToString("N2", SwissCulture), (total > 0 ? row.Value / total : 0).ToString("P1", SwissCulture) }), new[] { 2.5d, 1d, 1d }, new[] { false, true, true });
    }

    private static void AddValueTable(FlowDocument document, IReadOnlyList<DashboardValueRow> rows, string valueHeader)
        => AddTable(document, new[] { "Bezeichnung", valueHeader }, rows.Select(row => new[] { row.Label, row.Value.ToString("N2", SwissCulture) }), new[] { 2.7d, 1d }, new[] { false, true });

    private static void AddTable(FlowDocument document, string[] headers, IEnumerable<string[]> sourceRows, double[] widths, bool[] alignRight)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 16), BorderBrush = Rule, BorderThickness = new Thickness(0.6) };
        foreach (var width in widths) table.Columns.Add(new TableColumn { Width = new GridLength(width, GridUnitType.Star) });
        var headerGroup = new TableRowGroup(); table.RowGroups.Add(headerGroup);
        var header = new TableRow { Background = PurplePastel }; headerGroup.Rows.Add(header);
        for (var index = 0; index < headers.Length; index++) AddCell(header, headers[index], true, alignRight[index]);
        var body = new TableRowGroup(); table.RowGroups.Add(body);
        var rowIndex = 0;
        foreach (var values in sourceRows)
        {
            var row = new TableRow { Background = rowIndex++ % 2 == 0 ? Brushes.White : GrayPastel };
            body.Rows.Add(row);
            for (var index = 0; index < headers.Length; index++) AddCell(row, index < values.Length ? values[index] : string.Empty, false, alignRight[index]);
        }
        if (rowIndex == 0)
        {
            var empty = new TableRow { Background = GrayPastel }; body.Rows.Add(empty);
            var cell = new TableCell(Paragraph("Keine Daten vorhanden.", 9, MutedInk)) { ColumnSpan = headers.Length, Padding = new Thickness(7) };
            empty.Cells.Add(cell);
        }
        document.Blocks.Add(table);
    }

    private static void AddCell(TableRow row, string text, bool bold, bool right)
    {
        var paragraph = Paragraph(text, bold ? 9 : 8.8, bold ? Ink : MutedInk);
        paragraph.TextAlignment = right ? TextAlignment.Right : TextAlignment.Left;
        row.Cells.Add(new TableCell(paragraph) { Padding = new Thickness(7, 5, 7, 5), BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 0, 0.4) });
    }

    private static void AddSectionHeading(FlowDocument document, string title, string subtitle, bool breakBefore = false)
    {
        var heading = Paragraph(title, 14, Purple, new Thickness(0, breakBefore ? 0 : 10, 0, 2));
        heading.FontWeight = FontWeights.SemiBold;
        heading.BreakPageBefore = breakBefore;
        document.Blocks.Add(heading);
        document.Blocks.Add(Paragraph(subtitle, 9, MutedInk, new Thickness(0, 0, 0, 7)));
    }

    private static void AddSubsectionHeading(FlowDocument document, string title)
    {
        var heading = Paragraph(title, 11, Purple, new Thickness(0, 8, 0, 5));
        heading.FontWeight = FontWeights.SemiBold;
        document.Blocks.Add(heading);
    }

    private static Paragraph Paragraph(string text, double size, Brush brush, Thickness? margin = null) =>
        new(new Run(text)) { FontSize = size, Foreground = brush, Margin = margin ?? new Thickness(0), LineHeight = size * 1.35 };

    private static SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private sealed class HeaderFooterPaginator : DocumentPaginator
    {
        private readonly DocumentPaginator _inner;
        private readonly string _title;
        private readonly string _subtitle;

        public HeaderFooterPaginator(DocumentPaginator inner, string title, string subtitle)
        {
            _inner = inner;
            _title = title;
            _subtitle = subtitle;
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
                var header = new FormattedText(_title, SwissCulture, FlowDirection.LeftToRight, typeface, 8.5, MutedInk, 1);
                context.DrawText(header, new Point(42, 22));
                var subtitle = new FormattedText(_subtitle, SwissCulture, FlowDirection.LeftToRight, typeface, 7.5, MutedInk, 1) { MaxTextWidth = Math.Max(100, page.Size.Width - 240), Trimming = TextTrimming.CharacterEllipsis };
                context.DrawText(subtitle, new Point(145, 23));
                context.DrawLine(new Pen(Rule, 0.7), new Point(42, 42), new Point(page.Size.Width - 42, 42));
                context.DrawLine(new Pen(Rule, 0.7), new Point(42, page.Size.Height - 36), new Point(page.Size.Width - 42, page.Size.Height - 36));
                var footer = new FormattedText($"MyCoinFlow  ·  Seite {pageNumber + 1} von {PageCount}", SwissCulture, FlowDirection.LeftToRight, typeface, 8, MutedInk, 1);
                context.DrawText(footer, new Point(page.Size.Width - 42 - footer.Width, page.Size.Height - 26));
            }
            container.Children.Add(chrome);
            return new DocumentPage(container, page.Size, page.BleedBox, page.ContentBox);
        }
    }
}
