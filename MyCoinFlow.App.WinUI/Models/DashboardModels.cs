using System.Globalization;
using Microsoft.UI.Xaml.Media;

namespace MyCoinFlow.WinUI.Models;

public sealed class DashboardComparisonRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public string Label { get; init; } = string.Empty;
    public double Budget { get; init; }
    public double Actual { get; init; }
    public double BudgetBarWidth { get; init; }
    public double ActualBarWidth { get; init; }
    public double BudgetNegativeBarWidth => Budget < 0 ? BudgetBarWidth : 0d;
    public double BudgetPositiveBarWidth => Budget >= 0 ? BudgetBarWidth : 0d;
    public double ActualNegativeBarWidth => Actual < 0 ? ActualBarWidth : 0d;
    public double ActualPositiveBarWidth => Actual >= 0 ? ActualBarWidth : 0d;
    public string BudgetText => Budget.ToString("N2", SwissCulture);
    public string ActualText => Actual.ToString("N2", SwissCulture);
}

public sealed class DashboardValueRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public string Label { get; init; } = string.Empty;
    public double Value { get; init; }
    public double BarWidth { get; init; }
    public double NegativeBarWidth => Value < 0 ? BarWidth : 0d;
    public double PositiveBarWidth => Value >= 0 ? BarWidth : 0d;
    public string ValueText => Value.ToString("N2", SwissCulture);
}

public sealed class DashboardDistributionRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public string Label { get; init; } = string.Empty;
    public double Value { get; init; }
    public double Share { get; init; }
    public Brush SliceBrush { get; init; } = new SolidColorBrush(Microsoft.UI.Colors.Gray);
    public string DisplayValueText { get; init; } = string.Empty;
    public string ValueText => Value.ToString("N2", SwissCulture);
    public string ShareText => Share.ToString("P0", SwissCulture);
}

public sealed class DashboardEnergyRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public string Quarter { get; init; } = string.Empty;
    public double Invoice { get; init; }
    public double Internal { get; init; }
    public double Solar { get; init; }
    public double InvoiceBarWidth { get; init; }
    public double InternalBarWidth { get; init; }
    public double SolarBarWidth { get; init; }
    public string InvoiceText => Invoice.ToString("N0", SwissCulture);
    public string InternalText => Internal.ToString("N0", SwissCulture);
    public string SolarText => Solar.ToString("N0", SwissCulture);
}

public sealed class DashboardPercentRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public string Label { get; init; } = string.Empty;
    public double Percent { get; init; }
    public string PercentText => Percent.ToString("N1", SwissCulture) + " %";
}

public sealed class DashboardPrintModel
{
    public bool IsStwe { get; init; }
    public string Subtitle { get; init; } = string.Empty;
    public string Grouping { get; init; } = string.Empty;
    public string StweStatus { get; init; } = string.Empty;
    public int CreditCardOpenCount { get; init; }
    public int BankOpenCount { get; init; }
    public IReadOnlyList<DashboardComparisonRow> Comparison { get; init; } = Array.Empty<DashboardComparisonRow>();
    public IReadOnlyList<DashboardDistributionRow> Distribution { get; init; } = Array.Empty<DashboardDistributionRow>();
    public IReadOnlyList<DashboardValueRow> Deviations { get; init; } = Array.Empty<DashboardValueRow>();
    public IReadOnlyList<DashboardValueRow> Banks { get; init; } = Array.Empty<DashboardValueRow>();
    public IReadOnlyList<string> SelectedNumberRanges { get; init; } = Array.Empty<string>();
    public IReadOnlyList<DashboardEnergyRow> Energy { get; init; } = Array.Empty<DashboardEnergyRow>();
    public IReadOnlyList<DashboardPercentRow> Solar { get; init; } = Array.Empty<DashboardPercentRow>();
    public IReadOnlyList<DashboardValueRow> OwnerKwh { get; init; } = Array.Empty<DashboardValueRow>();
    public IReadOnlyList<DashboardValueRow> OwnerChf { get; init; } = Array.Empty<DashboardValueRow>();
    public IReadOnlyList<DashboardRangePrintSection> RangeSections { get; init; } = Array.Empty<DashboardRangePrintSection>();
}

public sealed class DashboardRangePrintSection
{
    public string Title { get; init; } = string.Empty;
    public int RangeStart { get; init; }
    public int RangeEnd { get; init; }
    public IReadOnlyList<DashboardComparisonRow> Comparison { get; init; } = Array.Empty<DashboardComparisonRow>();
    public IReadOnlyList<DashboardDistributionRow> Distribution { get; init; } = Array.Empty<DashboardDistributionRow>();
    public IReadOnlyList<DashboardValueRow> Deviations { get; init; } = Array.Empty<DashboardValueRow>();
}
