using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using MyCoinFlow.Models;
using MyCoinFlow.ViewModels;
using System.Globalization;

namespace MyCoinFlow.WinUI.Models;

public sealed class SubscriptionDisplayRow
{
    private static readonly CultureInfo Swiss = CultureInfo.GetCultureInfo("de-CH");

    public SubscriptionDisplayRow(AboRow value, AboKategorie? category = null)
    {
        Value = value;
        CategoryDefinition = category;
        var color = ParseColor(category?.FarbeHex);
        CategoryAccent = new SolidColorBrush(color);
        CategorySurface = new SolidColorBrush(ColorHelper.FromArgb(255,
            Blend(color.R, 238), Blend(color.G, 238), Blend(color.B, 238)));
        CategoryGlyph = value.Abo.Kategorie switch
        {
            AboKategorien.Streaming => "\uE768",
            AboKategorien.SoftwareLizenz => "\uE943",
            AboKategorien.Vertrag => "\uE8F1",
            _ => "\uE8D7"
        };
        (DirectionAccent, DirectionSurface, DirectionGlyph) = value.Abo.Richtung switch
        {
            Zahlungsrichtungen.Einnahme => (Brush(31, 122, 72), Brush(221, 242, 228), "\uE8C7"),
            Zahlungsrichtungen.Ausgabe => (Brush(177, 66, 59), Brush(247, 226, 224), "\uE74B"),
            _ => (Brush(176, 108, 31), Brush(250, 235, 214), "\uE7BA")
        };
        (StatusBackground, StatusForeground) = value.Abo.Status switch
        {
            AboStatus.Gekuendigt => (Brush(232, 239, 247), Brush(63, 87, 113)),
            AboStatus.Beendet => (Brush(238, 238, 242), Brush(93, 93, 103)),
            _ => (Brush(221, 242, 228), Brush(31, 112, 65))
        };
    }

    public AboRow Value { get; }
    public AboKategorie? CategoryDefinition { get; }
    public int Id => Value.Id;
    public string Name => Value.Name;
    public string Provider => Value.AdresseName ?? "Anbieter noch nicht zugeordnet";
    public string Category => Value.Abo.Kategorie;
    public string CategoryName => CategoryDefinition?.Bezeichnung ?? Value.Abo.KategorieBezeichnung ?? AboKategorien.Anzeige(Category);
    public int CategoryOrder => CategoryDefinition?.Sortierung ?? int.MaxValue;
    public string CategoryGlyph { get; }
    public SolidColorBrush CategoryAccent { get; }
    public SolidColorBrush CategorySurface { get; }
    public string Direction => Value.Abo.Richtung;
    public string DirectionName => Zahlungsrichtungen.Anzeige(Direction);
    public string DirectionGlyph { get; }
    public SolidColorBrush DirectionAccent { get; }
    public SolidColorBrush DirectionSurface { get; }
    public string Status => Value.StatusAnzeige;
    public SolidColorBrush StatusBackground { get; }
    public SolidColorBrush StatusForeground { get; }
    public string Period => Value.PeriodizitaetAnzeige;
    public string ExpectedAmount => Value.Abo.ErwarteterBetrag?.ToString("N2", Swiss) ?? "–";
    public string LastAmount => Value.LetzterBetrag?.ToString("N2", Swiss) ?? "–";
    public string LastDate => Value.LetzteZahlung?.ToString("dd.MM.yyyy") ?? "–";
    public string NextDate => Value.NaechsteZahlung?.ToString("dd.MM.yyyy") ?? "–";
    public string CancelBy => Value.KuendigenBis?.ToString("dd.MM.yyyy") ?? "Nicht geplant";
    public string CancelLine => $"Kündigen bis {CancelBy}";
    public string ContractEnd => Value.Abo.KuendigenZum?.ToString("dd.MM.yyyy") ?? "Nicht geplant";
    public string CancellationRoute => string.IsNullOrWhiteSpace(Value.Abo.Kuendigungsweg) ? "Noch nicht erfasst" : Value.Abo.Kuendigungsweg!;
    public int Payments => Value.AnzahlZahlungen;
    public int OneTimePayments => Value.AnzahlEinmaligeZahlungen;
    public string PaymentCount => OneTimePayments > 0
        ? $"{Payments:N0} · {OneTimePayments:N0} einmalig"
        : Payments.ToString("N0");
    public string HistoricalTotal => Currency(Value.HistorischesTotal);
    public decimal AnnualCostValue => Value.Jahreskosten ?? 0m;
    public string AnnualCost => Currency(AnnualCostValue);
    public string MonthlyCost => Currency(AnnualCostValue / 12m);
    public string Account => Value.KontoAnzeige ?? "Noch nicht festgelegt";
    public string Indicator => Value.AmpelText;
    public string Hint => Value.HinweisText ?? string.Empty;
    public string Website => Value.Abo.WebseiteUrl ?? string.Empty;
    public string Note => Value.Abo.Notiz ?? string.Empty;
    public bool IsReportable => AboKategorien.IstZahlungsserie(Category);

    public string SearchText =>
        $"{Name} {Provider} {DirectionName} {CategoryName} {Status} {Account} {CancellationRoute} {Note}";

    private static string Currency(decimal value) => $"CHF {value.ToString("N2", Swiss)}";
    private static SolidColorBrush Brush(byte r, byte g, byte b) =>
        new(ColorHelper.FromArgb(255, r, g, b));
    private static Windows.UI.Color ParseColor(string? hex)
    {
        var value = string.IsNullOrWhiteSpace(hex) ? "536274" : hex.Trim().TrimStart('#');
        if (value.Length == 6
            && byte.TryParse(value[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r)
            && byte.TryParse(value[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g)
            && byte.TryParse(value[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
            return ColorHelper.FromArgb(255, r, g, b);
        return ColorHelper.FromArgb(255, 83, 98, 116);
    }
    private static byte Blend(byte accent, byte surface) => (byte)Math.Round(accent * 0.18 + surface * 0.82);
}

public sealed class SubscriptionGroup
{
    public SubscriptionGroup(
        string title,
        string direction,
        string summary,
        SolidColorBrush directionSurface,
        SolidColorBrush directionAccent,
        IReadOnlyList<SubscriptionDisplayRow> entries)
    {
        Title = title;
        Direction = direction;
        Summary = summary;
        DirectionSurface = directionSurface;
        DirectionAccent = directionAccent;
        Entries = entries;
    }

    public string Title { get; }
    public string Direction { get; }
    public string Summary { get; }
    public SolidColorBrush DirectionSurface { get; }
    public SolidColorBrush DirectionAccent { get; }
    public IReadOnlyList<SubscriptionDisplayRow> Entries { get; }
}

public sealed record SubscriptionCategoryFilterOption(string Code, string Display);

public sealed class SubscriptionPaymentDisplayRow
{
    private static readonly CultureInfo Swiss = CultureInfo.GetCultureInfo("de-CH");
    public SubscriptionPaymentDisplayRow(AboZahlungRow value) => Value = value;
    public AboZahlungRow Value { get; }
    public string Date => Value.Datum.ToString("dd.MM.yyyy");
    public string Amount => Value.Betrag.ToString("N2", Swiss);
    public string Account => Value.KontoAnzeige ?? string.Empty;
    public string Bank => Value.BankName ?? string.Empty;
    public string Assignment => Value.ZuordnungAnzeige;
    public bool IsOneTime => Value.IstEinmalig;
    public string PaymentType => Value.ZahlungsartAnzeige;
    public string Note => Value.Notiz ?? string.Empty;
}
