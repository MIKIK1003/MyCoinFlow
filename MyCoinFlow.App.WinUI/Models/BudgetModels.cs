using MyCoinFlow.Models;
using System.Globalization;

namespace MyCoinFlow.WinUI.Models;

public sealed class BudgetPeriodDisplayRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public BudgetPeriodDisplayRow(Budgetzeitraum period) => Period = period;

    public Budgetzeitraum Period { get; }
    public int Id => Period.Id;
    public string Name => Period.Bezeichnung;
    public string StartText => Period.Startdatum.ToString("dd.MM.yyyy", SwissCulture);
    public string EndText => Period.Enddatum.ToString("dd.MM.yyyy", SwissCulture);
    public string DurationText => $"{StartText} – {EndText}";
    public string ActiveText => Period.IstAktiv ? "Aktiv" : "Inaktiv";
}

public sealed class BudgetAccountDisplayRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public BudgetAccountDisplayRow(BudgetKontoRow row)
    {
        Row = row;
        ResetText();
    }

    public BudgetKontoRow Row { get; }
    public int AccountNumber => Row.Kontonummer;
    public string Art => Row.Art;
    public string Group => Row.Gruppe;
    public string Subgroup => Row.Untergruppe;
    public string Detail => Row.Detail;
    public string BudgetText { get; set; } = string.Empty;

    public void ResetText() =>
        BudgetText = Row.Budgetwert?.ToString("N2", SwissCulture) ?? string.Empty;
}
