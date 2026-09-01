using MyCoinFlow.Models;
using MyCoinFlow.ViewModels;
using System.Globalization;

namespace MyCoinFlow.WinUI.Models;

public sealed class AddressDisplayRow
{
    public AddressDisplayRow(Adresse address) => Address = address;

    public Adresse Address { get; }
    public int Id => Address.Id;
    public string Name => Address.Name;
    public string Type => Address.Typ ?? string.Empty;
    public string Iban => Address.IBAN ?? string.Empty;
    public string Note => Address.Notiz ?? string.Empty;
    public string Street => Address.Strasse ?? string.Empty;
    public string PostalCode => Address.PLZ ?? string.Empty;
    public string City => Address.Ort ?? string.Empty;
    public string Country => Address.Land ?? string.Empty;
    public string TypeText => string.IsNullOrWhiteSpace(Type) ? "Ohne Typ" : Type;
    public string StreetText => string.IsNullOrWhiteSpace(Street) ? "–" : Street;
    public string LocationText
    {
        get
        {
            var postalAndCity = string.Join(" ", new[] { PostalCode, City }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            return string.Join(" · ", new[] { postalAndCity, Country }
                .Where(value => !string.IsNullOrWhiteSpace(value))) switch
            {
                "" => "Keine Ortsangabe",
                var value => value
            };
        }
    }
    public string IbanText => string.IsNullOrWhiteSpace(Iban) ? "Keine IBAN" : Iban;
    public string BudgetText => Address.IstBudgetiert ? "Budgetierte Einnahme" : string.Empty;
}

public sealed class AddressTransactionDisplayRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public AddressTransactionDisplayRow(AdresseTransaktionenViewModel.Row row) => Row = row;

    public AdresseTransaktionenViewModel.Row Row { get; }
    public int Id => Row.Id;
    public string Account => Row.Konto;
    public string DateText => Row.Datum.ToString("dd.MM.yyyy", SwissCulture);
    public string IncomeText => Row.Einnahmen.ToString("N2", SwissCulture);
    public string ExpenseText => Row.Ausgaben.ToString("N2", SwissCulture);
    public string Institution => Row.GeldinstitutName ?? string.Empty;
    public string Note => Row.Notiz ?? string.Empty;
    public bool HasBudgetDateOverride => Row.HasBudgetDatumOverride;
    public string BudgetDateTooltip => Row.BudgetDatumTooltip ?? string.Empty;
}

public sealed record AddressReference(string TableName, string ColumnName, int Count);
