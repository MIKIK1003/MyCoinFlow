using MyCoinFlow.Models;
using MyCoinFlow.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MyCoinFlow.WinUI.Services;

public static class SubscriptionReportDocumentBuilder
{
    private static readonly CultureInfo Swiss = CultureInfo.GetCultureInfo("de-CH");
    private static readonly Brush Ink = Brush(34, 35, 42);
    private static readonly Brush Muted = Brush(78, 78, 91);
    private static readonly Brush Purple = Brush(91, 45, 169);
    private static readonly Brush PurpleDark = Brush(61, 31, 112);
    private static readonly Brush PurpleSoft = Brush(230, 220, 246);
    private static readonly Brush Teal = Brush(22, 126, 145);
    private static readonly Brush TealSoft = Brush(220, 241, 241);
    private static readonly Brush GreenSoft = Brush(222, 241, 225);
    private static readonly Brush Amber = Brush(176, 108, 31);
    private static readonly Brush AmberSoft = Brush(250, 235, 214);
    private static readonly Brush GraySoft = Brush(241, 241, 245);
    private static readonly Brush Rule = Brush(196, 194, 204);

    public static DocumentPaginator Build(
        IReadOnlyList<AboRow> source,
        IReadOnlyList<AboKategorie> categories,
        double printableWidth,
        double printableHeight)
    {
        var categoryByCode = categories.ToDictionary(value => value.Code, StringComparer.OrdinalIgnoreCase);
        var rows = source
            .Where(row => AboKategorien.IstZahlungsserie(row.Abo.Kategorie)
                          && row.Abo.Status != AboStatus.Beendet)
            .OrderBy(row => DirectionOrder(row.Abo.Richtung))
            .ThenBy(row => CategoryOrder(row.Abo.Kategorie, categoryByCode))
            .ThenBy(row => row.KuendigenBis ?? DateTime.MaxValue)
            .ThenBy(row => row.Name)
            .ToList();
        var width = double.IsFinite(printableWidth) && printableWidth > 500 ? printableWidth : 793d;
        var height = double.IsFinite(printableHeight) && printableHeight > 700 ? printableHeight : 1122d;
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 9.2,
            Foreground = Ink,
            PagePadding = new Thickness(42, 58, 42, 52),
            ColumnWidth = double.PositiveInfinity,
            PageWidth = width,
            PageHeight = height
        };

        AddCover(document, rows);
        document.Blocks.Add(new Paragraph { BreakPageBefore = true, FontSize = 1 });
        AddOverview(document, rows, categoryByCode);
        document.Blocks.Add(new Paragraph { BreakPageBefore = true, FontSize = 1 });
        AddCancellationPlanner(document, rows);

        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.PageSize = new Size(width, height);
        paginator.ComputePageCount();
        return new HeaderFooterPaginator(paginator);
    }

    private static void AddCover(FlowDocument document, IReadOnlyList<AboRow> rows)
    {
        var hero = new Border
        {
            Background = PurpleSoft,
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(28, 27, 28, 27),
            Margin = new Thickness(0, 26, 0, 28)
        };
        var stack = new StackPanel();
        stack.Children.Add(Text("MYCOINFLOW", 9, Purple, FontWeights.Bold));
        stack.Children.Add(Text("Zahlungsserien", 28, PurpleDark, FontWeights.SemiBold, new Thickness(0, 12, 0, 3)));
        stack.Children.Add(Text("Einnahmen, Ausgaben und Vertragsübersicht", 15, Ink, FontWeights.SemiBold));
        stack.Children.Add(Text(
            "Regelmässige Zahlungsfolgen, nach Richtung und Themenart geordnet.",
            9.5, Muted, FontWeights.Normal, new Thickness(0, 8, 0, 0)));
        hero.Child = stack;
        document.Blocks.Add(new BlockUIContainer(hero));

        document.Blocks.Add(Paragraph($"Stand {DateTime.Now:dd.MM.yyyy, HH:mm}", 10.5, Ink, new Thickness(0, 0, 0, 22), true));
        var active = rows.Where(row => row.Abo.Status == AboStatus.Aktiv).ToList();
        var incomeAnnual = active.Where(row => row.Abo.Richtung == Zahlungsrichtungen.Einnahme).Sum(row => row.Jahreskosten ?? 0m);
        var expenseAnnual = active.Where(row => row.Abo.Richtung == Zahlungsrichtungen.Ausgabe).Sum(row => row.Jahreskosten ?? 0m);
        document.Blocks.Add(CreateKpiRow(
            ("AKTIVE SERIEN", active.Count.ToString("N0", Swiss), PurpleSoft),
            ("EINNAHMEN PRO JAHR", Currency(incomeAnnual), GreenSoft),
            ("AUSGABEN PRO JAHR", Currency(expenseAnnual), AmberSoft)));

        var summary = new Border
        {
            Background = GraySoft,
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(18, 16, 18, 16),
            Margin = new Thickness(0, 30, 0, 0)
        };
        var summaryStack = new StackPanel();
        summaryStack.Children.Add(Text("Dieser Bericht beantwortet", 13, PurpleDark, FontWeights.SemiBold));
        summaryStack.Children.Add(Text("• Welche Einnahmen und Ausgaben laufen regelmässig?", 10, Ink, FontWeights.Normal, new Thickness(0, 10, 0, 0)));
        summaryStack.Children.Add(Text("• Welche Verträge, Lizenzen und Streamingdienste bestehen?", 10, Ink));
        summaryStack.Children.Add(Text("• Gibt es Zahlungslücken oder anstehende Kündigungstermine?", 10, Ink));
        summaryStack.Children.Add(Text(
            "Vertragsdokumente und Korrespondenz werden weiterhin im DMS verwaltet.",
            9.2, Muted, FontWeights.Normal, new Thickness(0, 12, 0, 0)));
        summary.Child = summaryStack;
        document.Blocks.Add(new BlockUIContainer(summary));
    }

    private static void AddOverview(FlowDocument document, IReadOnlyList<AboRow> rows, IReadOnlyDictionary<string, AboKategorie> categoryByCode)
    {
        AddTitle(document, "Übersicht nach Richtung und Art", "Aktive und bereits gekündigte Zahlungsserien");
        var active = rows.Where(row => row.Abo.Status == AboStatus.Aktiv).ToList();
        var incomeAnnual = active.Where(row => row.Abo.Richtung == Zahlungsrichtungen.Einnahme).Sum(row => row.Jahreskosten ?? 0m);
        var expenseAnnual = active.Where(row => row.Abo.Richtung == Zahlungsrichtungen.Ausgabe).Sum(row => row.Jahreskosten ?? 0m);
        document.Blocks.Add(CreateKpiRow(
            ("EINNAHMEN / JAHR", Currency(incomeAnnual), GreenSoft),
            ("AUSGABEN / JAHR", Currency(expenseAnnual), AmberSoft),
            ("SALDO / JAHR", Currency(incomeAnnual - expenseAnnual), PurpleSoft)));

        AddDirection(document, "Einnahmen", "Regelmässige Zahlungseingänge", rows, Zahlungsrichtungen.Einnahme, Brush(31, 122, 72), GreenSoft, categoryByCode);
        AddDirection(document, "Ausgaben", "Regelmässige Zahlungsausgänge", rows, Zahlungsrichtungen.Ausgabe, Brush(177, 66, 59), Brush(247, 226, 224), categoryByCode);
        AddDirection(document, "Richtung prüfen", "Noch nicht eindeutig zugeordnete Serien", rows, Zahlungsrichtungen.Unklar, Amber, AmberSoft, categoryByCode);
    }

    private static void AddDirection(
        FlowDocument document,
        string title,
        string subtitle,
        IReadOnlyList<AboRow> allRows,
        string direction,
        Brush accent,
        Brush surface,
        IReadOnlyDictionary<string, AboKategorie> categoryByCode)
    {
        var rows = allRows.Where(row => row.Abo.Richtung == direction).ToList();
        if (rows.Count == 0)
            return;

        var heading = new Border
        {
            Background = surface,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 24, 0, 2)
        };
        var stack = new StackPanel();
        stack.Children.Add(Text(title, 16, accent, FontWeights.SemiBold));
        stack.Children.Add(Text($"{subtitle} · {rows.Count} Serie(n)", 8.8, Muted));
        heading.Child = stack;
        document.Blocks.Add(new BlockUIContainer(heading));

        foreach (var group in rows.GroupBy(row => row.Abo.Kategorie)
                     .OrderBy(value => CategoryOrder(value.Key, categoryByCode))
                     .ThenBy(value => CategoryName(value.First(), categoryByCode)))
        {
            categoryByCode.TryGetValue(group.Key, out var definition);
            var categoryAccent = BrushFromHex(definition?.FarbeHex);
            AddCategory(document,
                definition?.Bezeichnung ?? CategoryName(group.First(), categoryByCode),
                definition?.Beschreibung ?? "Weitere regelmässige Zahlungsvorgänge",
                group.ToList(), categoryAccent, SoftBrush(categoryAccent));
        }
    }

    private static void AddCategory(
        FlowDocument document,
        string title,
        string subtitle,
        IReadOnlyList<AboRow> rows,
        Brush accent,
        Brush surface)
    {
        var header = new Border
        {
            Background = surface,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 22, 0, 7)
        };
        var panel = new StackPanel();
        panel.Children.Add(Text(title, 14, accent, FontWeights.SemiBold));
        panel.Children.Add(Text($"{subtitle} · {rows.Count} Serie(n)", 8.8, Muted));
        header.Child = panel;
        document.Blocks.Add(new BlockUIContainer(header) { BreakPageBefore = false });
        if (rows.Count == 0)
        {
            document.Blocks.Add(Paragraph("Keine Verträge in dieser Kategorie.", 9.5, Muted, new Thickness(8, 6, 0, 0)));
            return;
        }
        document.Blocks.Add(BuildOverviewTable(rows, accent));
    }

    private static Table BuildOverviewTable(IReadOnlyList<AboRow> rows, Brush accent)
    {
        var table = NewTable(150, 62, 74, 74, 82, 82, 128);
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        group.Rows.Add(Row(new[] { "Serie", "Status", "Rhythmus", "Monat", "Jahr", "Nächste", "Kündigen bis" }, accent, Brushes.White, true));
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var annual = row.Jahreskosten ?? 0m;
            group.Rows.Add(Row(new[]
            {
                row.Name,
                Status(row.Abo.Status),
                AboPerioden.Anzeige(row.Abo.Periodizitaet),
                Money(annual / 12m),
                Money(annual),
                row.NaechsteZahlung?.ToString("dd.MM.yyyy") ?? "–",
                row.KuendigenBis?.ToString("dd.MM.yyyy") ?? "Nicht geplant"
            }, index % 2 == 0 ? Brushes.White : GraySoft, Ink));
        }
        return table;
    }

    private static void AddCancellationPlanner(FlowDocument document, IReadOnlyList<AboRow> rows)
    {
        AddTitle(document, "Vertrags- und Terminplanung", "Kündigungswege und Endtermine aller relevanten Serien");
        var planned = rows.Where(row => row.Abo.Status == AboStatus.Aktiv)
            .OrderBy(row => row.KuendigenBis ?? DateTime.MaxValue).ToList();

        var complete = planned.Count(row => row.Abo.KuendigenZum.HasValue
                                            && !string.IsNullOrWhiteSpace(row.Abo.Kuendigungsweg));
        document.Blocks.Add(CreateKpiRow(
            ("AKTIVE SERIEN", planned.Count.ToString("N0", Swiss), PurpleSoft),
            ("PLANUNG VOLLSTÄNDIG", complete.ToString("N0", Swiss), GreenSoft),
            ("ANGABEN FEHLEN", (planned.Count - complete).ToString("N0", Swiss), AmberSoft)));

        document.Blocks.Add(Paragraph(
            "„Nicht erfasst“ bedeutet, dass Vertragsende oder Kündigungsweg in der Zahlungsserie noch ergänzt werden sollte.",
            9.2, Muted, new Thickness(0, 15, 0, 10)));
        var table = NewTable(142, 78, 86, 82, 197, 92);
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        group.Rows.Add(Row(new[] { "Serie", "Vertragsende", "Kündigen bis", "Frist", "Kündigungsweg", "Status" }, Amber, Brushes.White, true));
        for (var index = 0; index < planned.Count; index++)
        {
            var row = planned[index];
            var deadline = row.KuendigenBis;
            var deadlineStatus = deadline is null
                ? "Nicht geplant"
                : deadline.Value.Date < DateTime.Today
                    ? "Termin verpasst"
                    : (deadline.Value.Date - DateTime.Today).TotalDays <= 30
                        ? "Jetzt erledigen"
                        : "Geplant";
            group.Rows.Add(Row(new[]
            {
                row.Name,
                row.Abo.KuendigenZum?.ToString("dd.MM.yyyy") ?? "Nicht erfasst",
                deadline?.ToString("dd.MM.yyyy") ?? "Nicht erfasst",
                row.Abo.KuendigungsfristTage.HasValue ? $"{row.Abo.KuendigungsfristTage} Tage" : "–",
                string.IsNullOrWhiteSpace(row.Abo.Kuendigungsweg) ? "Nicht erfasst" : row.Abo.Kuendigungsweg!,
                deadlineStatus
            }, index % 2 == 0 ? Brushes.White : GraySoft, Ink));
        }
        document.Blocks.Add(table);

        var reminder = new Border
        {
            Background = TealSoft,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 13, 16, 13),
            Margin = new Thickness(0, 24, 0, 0)
        };
        reminder.Child = Text(
            "Tipp: Verwaltungs- oder Kündigungsseite und Kündigungsweg direkt bei der Zahlungsserie speichern. Die zugehörigen Vertragsunterlagen bleiben sauber im DMS.",
            9.5, Teal, FontWeights.SemiBold);
        document.Blocks.Add(new BlockUIContainer(reminder));
    }

    private static void AddTitle(FlowDocument document, string title, string subtitle)
    {
        document.Blocks.Add(Paragraph("MYCOINFLOW", 8.5, Purple, new Thickness(0, 0, 0, 7), true));
        document.Blocks.Add(Paragraph(title, 23, PurpleDark, new Thickness(0, 0, 0, 3), true));
        document.Blocks.Add(Paragraph(subtitle, 10.2, Muted, new Thickness(0, 0, 0, 20)));
    }

    private static BlockUIContainer CreateKpiRow(params (string Label, string Value, Brush Background)[] items)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        for (var index = 0; index < items.Length; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var card = new Border
            {
                Background = items[index].Background,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(index == 0 ? 0 : 5, 0, index == items.Length - 1 ? 0 : 5, 0)
            };
            var panel = new StackPanel();
            panel.Children.Add(Text(items[index].Label, 8.2, Muted, FontWeights.Bold));
            panel.Children.Add(Text(items[index].Value, 16, Ink, FontWeights.SemiBold, new Thickness(0, 4, 0, 0)));
            card.Child = panel;
            Grid.SetColumn(card, index);
            grid.Children.Add(card);
        }
        return new BlockUIContainer(grid);
    }

    private static Table NewTable(params double[] widths)
    {
        var table = new Table { CellSpacing = 0 };
        foreach (var width in widths)
            table.Columns.Add(new TableColumn { Width = new GridLength(width) });
        return table;
    }

    private static TableRow Row(string[] values, Brush background, Brush foreground, bool header = false)
    {
        var row = new TableRow { Background = background };
        foreach (var value in values)
        {
            row.Cells.Add(new TableCell(new Paragraph(new Run(value))
            {
                Margin = new Thickness(0),
                FontSize = header ? 8.3 : 8.5,
                FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = foreground,
                TextAlignment = TextAlignment.Left
            })
            {
                Padding = new Thickness(7, header ? 7 : 6, 7, header ? 7 : 6),
                BorderBrush = Rule,
                BorderThickness = new Thickness(0, 0, 0, 0.45)
            });
        }
        return row;
    }

    private static TextBlock Text(
        string value,
        double size,
        Brush color,
        FontWeight weight = default,
        Thickness margin = default) => new()
    {
        Text = value,
        FontSize = size,
        Foreground = color,
        FontWeight = weight == default ? FontWeights.Normal : weight,
        Margin = margin,
        TextWrapping = TextWrapping.Wrap
    };

    private static Paragraph Paragraph(string text, double size, Brush color, Thickness margin, bool bold = false) =>
        new(new Run(text)) { FontSize = size, Foreground = color, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = margin };

    private static string Money(decimal value) => value.ToString("N2", Swiss);
    private static string Currency(decimal value) => $"CHF {Money(value)}";
    private static string Status(string status) => status switch
    {
        AboStatus.Gekuendigt => "Gekündigt",
        AboStatus.Beendet => "Beendet",
        _ => "Aktiv"
    };
    private static int DirectionOrder(string direction) => direction switch
    {
        Zahlungsrichtungen.Einnahme => 0,
        Zahlungsrichtungen.Ausgabe => 1,
        _ => 2
    };
    private static int CategoryOrder(string category, IReadOnlyDictionary<string, AboKategorie> categories) =>
        categories.TryGetValue(category, out var value) ? value.Sortierung : int.MaxValue;
    private static string CategoryName(AboRow row, IReadOnlyDictionary<string, AboKategorie> categories) =>
        categories.TryGetValue(row.Abo.Kategorie, out var value)
            ? value.Bezeichnung
            : row.Abo.KategorieBezeichnung ?? AboKategorien.Anzeige(row.Abo.Kategorie);
    private static SolidColorBrush BrushFromHex(string? hex)
    {
        var value = string.IsNullOrWhiteSpace(hex) ? "536274" : hex.Trim().TrimStart('#');
        if (value.Length == 6
            && byte.TryParse(value[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return Brush(r, g, b);
        return Brush(83, 98, 116);
    }
    private static SolidColorBrush SoftBrush(Brush accent)
    {
        var color = accent is SolidColorBrush solid ? solid.Color : Color.FromRgb(83, 98, 116);
        static byte Blend(byte value) => (byte)Math.Round(value * 0.18 + 238 * 0.82);
        return Brush(Blend(color.R), Blend(color.G), Blend(color.B));
    }
    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    private sealed class HeaderFooterPaginator : DocumentPaginator
    {
        private readonly DocumentPaginator _inner;
        public HeaderFooterPaginator(DocumentPaginator inner) => _inner = inner;
        public override bool IsPageCountValid => _inner.IsPageCountValid;
        public override int PageCount => _inner.PageCount;
        public override Size PageSize { get => _inner.PageSize; set => _inner.PageSize = value; }
        public override IDocumentPaginatorSource Source => _inner.Source;

        public override DocumentPage GetPage(int pageNumber)
        {
            var page = _inner.GetPage(pageNumber);
            var canvas = new Canvas { Width = PageSize.Width, Height = PageSize.Height, Background = Brushes.White };
            var visual = new ContainerVisual();
            visual.Children.Add(page.Visual);
            canvas.Children.Add(new VisualHost(visual));

            var header = Text("MYCOINFLOW  /  ZAHLUNGSSERIEN", 7.7, Purple, FontWeights.Bold);
            Canvas.SetLeft(header, 42); Canvas.SetTop(header, 22); canvas.Children.Add(header);
            var footer = Text($"Erstellt {DateTime.Now:dd.MM.yyyy}     ·     Seite {pageNumber + 1} von {PageCount}", 7.5, Muted);
            Canvas.SetLeft(footer, 42); Canvas.SetTop(footer, PageSize.Height - 31); canvas.Children.Add(footer);
            canvas.Measure(PageSize); canvas.Arrange(new Rect(PageSize)); canvas.UpdateLayout();
            return new DocumentPage(canvas, PageSize, page.BleedBox, page.ContentBox);
        }
    }

    private sealed class VisualHost : FrameworkElement
    {
        private readonly Visual _visual;
        public VisualHost(Visual visual) => _visual = visual;
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => index == 0 ? _visual : throw new ArgumentOutOfRangeException(nameof(index));
    }
}
