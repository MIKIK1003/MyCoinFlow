using MyCoinFlow.Models;
using MyCoinFlow.ViewModels;
using System.Globalization;

namespace MyCoinFlow.WinUI.Models;

public sealed class InstitutionDisplayRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public InstitutionDisplayRow(GeldinstitutSaldo institution) => Institution = institution;

    public GeldinstitutSaldo Institution { get; }
    public int Id => Institution.Id;
    public string Name => Institution.Name;
    public string Bic => Institution.BIC ?? string.Empty;
    public string Iban => Institution.IBAN ?? string.Empty;
    public string AccountNumber => Institution.KontoNummer ?? string.Empty;
    public string Note => Institution.Notiz ?? string.Empty;
    public string BankConnectionPrimary => string.IsNullOrWhiteSpace(Iban) ? "–" : Iban;
    public string BankConnectionSecondary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Bic)) parts.Add($"BIC {Bic}");
            if (!string.IsNullOrWhiteSpace(AccountNumber)) parts.Add($"Konto {AccountNumber}");
            return parts.Count == 0 ? "Keine weiteren Bankangaben" : string.Join(" · ", parts);
        }
    }
    public string InitialBalanceText => Institution.Anfangsbestand.ToString("N2", SwissCulture);
    public string InitialDateText => Institution.Anfangsdatum.HasValue
        ? $"ab {Institution.Anfangsdatum.Value:dd.MM.yyyy}"
        : "ohne Anfangsdatum";
    public string BookedText => Institution.Gebucht.ToString("N2", SwissCulture);
    public string ClosingBalanceText => Institution.Schlussaldo.ToString("N2", SwissCulture);
}

public sealed class InstitutionTransactionDisplayRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public InstitutionTransactionDisplayRow(GeldinstitutTransaktionenViewModel.Row row) => Row = row;

    public GeldinstitutTransaktionenViewModel.Row Row { get; }
    public int Id => Row.Id;
    public string Account => Row.Konto;
    public string DateText => Row.Datum.ToString("dd.MM.yyyy", SwissCulture);
    public string IncomeText => Row.Einnahmen.ToString("N2", SwissCulture);
    public string ExpenseText => Row.Ausgaben.ToString("N2", SwissCulture);
    public string Address => Row.AdresseName ?? string.Empty;
    public string Note => Row.Notiz ?? string.Empty;
    public bool HasBudgetDateOverride => Row.HasBudgetDatumOverride;
    public string BudgetDateTooltip => Row.BudgetDatumTooltip ?? string.Empty;
}

public sealed record InstitutionReference(string TableName, string ColumnName, int Count);
