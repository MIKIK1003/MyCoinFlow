using MyCoinFlow.Models;
using MyCoinFlow.ViewModels;
using System.Globalization;

namespace MyCoinFlow.WinUI.Models;

public sealed class AccountDisplayRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public AccountDisplayRow(KontoplanEintrag account) => Account = account;

    public KontoplanEintrag Account { get; }
    public int Id => Account.Id;
    public int AccountNumber => Account.Kontonummer;
    public string AccountNumberText => Account.Kontonummer.ToString("D4", CultureInfo.InvariantCulture);
    public string Art => Account.Art;
    public string Group => Account.Gruppe;
    public string Subgroup => Account.Untergruppe;
    public string Detail => Account.Detail ?? string.Empty;
    public string BudgetText => Account.Budgetwert?.ToString("C2", SwissCulture) ?? "–";
    public string BookedText => Account.Gebucht.ToString("C2", SwissCulture);
    public string DifferenceText => Account.Differenz.ToString("C2", SwissCulture);
    public string SelectionText => $"{AccountNumberText}  —  {Detail}";
}

public sealed class AccountTransactionDisplayRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public AccountTransactionDisplayRow(KontoTransaktionenViewModel.Row row) => Row = row;

    public KontoTransaktionenViewModel.Row Row { get; }
    public int Id => Row.Id;
    public string Institution => Row.GeldinstitutName ?? string.Empty;
    public string DateText => Row.Datum.ToString("dd.MM.yyyy", SwissCulture);
    public string IncomeText => Row.Einnahmen.ToString("N2", SwissCulture);
    public string ExpenseText => Row.Ausgaben.ToString("N2", SwissCulture);
    public string Address => Row.AdresseName ?? string.Empty;
    public string Note => Row.Notiz ?? string.Empty;
    public bool HasBudgetDateOverride => Row.HasBudgetDatumOverride;
    public string BudgetDateTooltip => Row.BudgetDatumTooltip ?? string.Empty;
}

public sealed record AccountReference(string TableName, string ColumnName, int Count);

public sealed record AccountDeletionPlan(
    IReadOnlyList<AccountReference> References,
    IReadOnlyList<string> AddressExamples)
{
    public int MappingCount => References
        .Where(reference => reference.TableName.Equals("dbo.KategorieKontoMapping", StringComparison.OrdinalIgnoreCase))
        .Sum(reference => reference.Count);

    public int AddressCount => References
        .Where(reference => reference.TableName.Equals("dbo.Adresse", StringComparison.OrdinalIgnoreCase))
        .Sum(reference => reference.Count);

    public bool HasReferences => References.Any(reference => reference.Count > 0);

    public bool HasReferencesOtherThanMappings => References.Any(reference =>
        reference.Count > 0 &&
        !reference.TableName.Equals("dbo.KategorieKontoMapping", StringComparison.OrdinalIgnoreCase));

    public bool HasHardBlockersBesidesAddresses => References.Any(reference =>
        reference.Count > 0 &&
        !reference.TableName.Equals("dbo.Adresse", StringComparison.OrdinalIgnoreCase));
}
