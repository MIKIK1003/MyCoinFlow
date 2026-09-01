using MyCoinFlow.Import;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Text.RegularExpressions;

namespace MyCoinFlow.WinUI.Services;

public sealed record BookingAssignmentState(
    IReadOnlyList<Adresse> Addresses,
    IReadOnlyList<KontoLookup> Accounts,
    IReadOnlyList<int> QuickAccountIds,
    int? SelectedAddressId,
    int? SelectedAccountId,
    bool CreateNewAddress,
    string NewAddressName,
    string NewAddressIban,
    bool IsBudgetedIncome,
    string? BudgetPeriodHint);

public sealed record BookingAssignmentInput(
    bool CreateNewAddress,
    int? AddressId,
    string NewAddressName,
    string NewAddressIban,
    bool IsBudgetedIncome,
    int? AccountId);

public sealed record BookingAssignmentResult(int AddressId, int AccountId);

/// <summary>
/// WinUI-Fassung des bestehenden ZuordnungDialog-Ablaufs. Die Datenbankmethoden,
/// Standardkonto-Regeln, Aliasbildung und Sonderregeln entsprechen dem WPF-Dialog.
/// </summary>
public sealed class BookingAssignmentWorkflow
{
    private readonly DatabaseService _db = new();

    public BookingAssignmentState Load(BankImportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var addresses = _db.LadeAdressen().OrderBy(address => address.Name).ToList();
        var accounts = _db.LadeKontoLookup();

        int? selectedAddressId = null;
        if (item.VorschlagAdresseId.HasValue &&
            addresses.Any(address => address.Id == item.VorschlagAdresseId.Value))
        {
            selectedAddressId = item.VorschlagAdresseId.Value;
        }

        if (!selectedAddressId.HasValue && !string.IsNullOrWhiteSpace(item.CounterpartyIban))
        {
            var expectedIban = NormalizeIban(item.CounterpartyIban);
            selectedAddressId = addresses.FirstOrDefault(address =>
                !string.IsNullOrWhiteSpace(address.IBAN) &&
                string.Equals(NormalizeIban(address.IBAN), expectedIban, StringComparison.OrdinalIgnoreCase))?.Id;
        }

        if (!selectedAddressId.HasValue && !string.IsNullOrWhiteSpace(item.CounterpartyName))
        {
            var expectedName = item.CounterpartyName.Trim();
            selectedAddressId = addresses.FirstOrDefault(address =>
                string.Equals(address.Name?.Trim(), expectedName, StringComparison.CurrentCultureIgnoreCase))?.Id;
        }

        var createNewAddress = !selectedAddressId.HasValue;
        var selectedAddress = selectedAddressId.HasValue
            ? _db.LadeAdresseById(selectedAddressId.Value)
            : null;

        var isIncome = item.Direction == KreditDebit.Credit;
        var selectedAccountId = item.VorschlagNachKontoId;
        if (!selectedAccountId.HasValue && selectedAddress != null)
        {
            if (isIncome && selectedAddress.IstBudgetiert && selectedAddress.StandardEinnahmenKontoId.HasValue)
                selectedAccountId = selectedAddress.StandardEinnahmenKontoId.Value;
            else if (!isIncome && selectedAddress.DefaultKontoId.HasValue)
                selectedAccountId = selectedAddress.DefaultKontoId.Value;
        }

        var quickAccountIds = new List<int>();
        try
        {
            quickAccountIds.AddRange(_db.LadeKontoSchnellwahl(CurrentUserContext.Username));
        }
        catch
        {
            // Wie im WPF-Dialog ist eine nicht verfügbare Schnellwahl nicht blockierend.
        }

        string? budgetPeriodHint = null;
        try
        {
            var periodId = _db.HoleAktivenBudgetzeitraumId();
            if (periodId.HasValue)
            {
                var period = _db.HoleBudgetzeitraum(periodId.Value);
                if (period != null &&
                    (item.BookingDate.Date < period.Startdatum.Date || item.BookingDate.Date > period.Enddatum.Date))
                {
                    budgetPeriodHint =
                        $"Hinweis: Diese Buchung ({item.BookingDate:dd.MM.yyyy}) liegt außerhalb vom aktiven Budgetzeitraum";
                }
            }
        }
        catch
        {
            // Der Hinweis ist wie im WPF-Dialog rein informativ.
        }

        return new BookingAssignmentState(
            addresses,
            accounts,
            quickAccountIds,
            selectedAddressId,
            selectedAccountId,
            createNewAddress,
            createNewAddress ? item.CounterpartyName?.Trim() ?? "" : "",
            createNewAddress ? item.CounterpartyIban?.Trim() ?? "" : "",
            isIncome && selectedAddress?.IstBudgetiert == true,
            budgetPeriodHint);
    }

    public BookingAssignmentResult Save(BankImportItem item, BookingAssignmentInput input)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!input.AccountId.HasValue)
            throw new InvalidOperationException("Bitte ein Standardkonto wählen.");

        var accountId = input.AccountId.Value;
        var isIncome = item.Direction == KreditDebit.Credit;
        int addressId;
        var saveSpecialRule = false;
        int? ruleAccountId = null;

        if (input.CreateNewAddress)
        {
            var name = (input.NewAddressName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Bitte einen Namen für die neue Adresse eingeben.");

            var normalizedIban = NormalizeIban(input.NewAddressIban);
            var newAddress = new Adresse
            {
                Name = name,
                IBAN = string.IsNullOrWhiteSpace(normalizedIban) ? null : normalizedIban
            };

            if (isIncome && input.IsBudgetedIncome)
            {
                newAddress.IstBudgetiert = true;
                newAddress.StandardEinnahmenKontoId = accountId;
                newAddress.DefaultKontoId = null;
            }
            else
            {
                newAddress.IstBudgetiert = false;
                newAddress.StandardEinnahmenKontoId = null;
                newAddress.DefaultKontoId = accountId;
            }

            addressId = _db.SpeichereAdresse(newAddress);
        }
        else
        {
            if (!input.AddressId.HasValue)
            {
                throw new InvalidOperationException(
                    "Bitte eine bestehende Adresse wählen oder 'Neue Adresse anlegen' aktivieren.");
            }

            addressId = input.AddressId.Value;
            var address = _db.LadeAdresseById(addressId);

            if (isIncome && input.IsBudgetedIncome)
            {
                address.IstBudgetiert = true;
                if (!address.StandardEinnahmenKontoId.HasValue || address.StandardEinnahmenKontoId.Value <= 0)
                {
                    address.StandardEinnahmenKontoId = accountId;
                    _db.AktualisiereAdresse(address);
                }
                else if (address.StandardEinnahmenKontoId.Value != accountId)
                {
                    saveSpecialRule = true;
                    ruleAccountId = accountId;
                }
            }
            else
            {
                if (!address.DefaultKontoId.HasValue || address.DefaultKontoId.Value <= 0)
                {
                    address.DefaultKontoId = accountId;
                    _db.AktualisiereAdresse(address);
                }
                else if (address.DefaultKontoId.Value != accountId)
                {
                    saveSpecialRule = true;
                    ruleAccountId = accountId;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(item.CounterpartyName))
            _db.SpeichereAdressAlias(addressId, item.CounterpartyName.Trim(), "Exact");

        var aliasCandidate = BuildAliasCandidate(item.Text, item.ServiceRef);
        if (!string.IsNullOrWhiteSpace(aliasCandidate))
            _db.SpeichereAdressAlias(addressId, aliasCandidate, "Contains");

        if (saveSpecialRule && ruleAccountId.HasValue)
        {
            var ruleText = BuildAliasCandidate(item.Text, item.ServiceRef);
            if (string.IsNullOrWhiteSpace(ruleText))
                ruleText = string.IsNullOrWhiteSpace(item.Text) ? null : item.Text.Trim();

            if (!string.IsNullOrWhiteSpace(ruleText))
            {
                _db.LernAdressBuchungsregel(
                    adresseId: addressId,
                    istEinnahme: isIncome,
                    textPattern: ruleText,
                    patternModus: "Contains",
                    kontoId: ruleAccountId.Value,
                    betrag: item.Amount,
                    prioritaet: 100);
            }
        }

        return new BookingAssignmentResult(addressId, accountId);
    }

    private static string NormalizeIban(string? iban) =>
        string.IsNullOrWhiteSpace(iban) ? "" : iban.Replace(" ", "").ToUpperInvariant();

    private static readonly HashSet<string> AliasStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "RECHNUNG", "REFERENZ", "ZAHLUNG", "GEBUEHR", "KARTENZAHLUNG", "BELASTUNG",
        "GUTSCHRIFT", "MITTEILUNG", "VALUTA", "SEPA", "SWIFT", "UETR", "CHF", "EUR", "USD",
        "VISA", "MASTERCARD", "TWINT", "POSTFINANCE", "UBS", "CS", "BANK", "KONTO", "IBAN"
    };

    private static string? BuildAliasCandidate(string? text, string? serviceReference)
    {
        var source = !string.IsNullOrWhiteSpace(text) ? text : serviceReference ?? "";
        if (string.IsNullOrWhiteSpace(source)) return null;

        var cleaned = Regex.Replace(source, @"[A-Z]{2}\d{2}[A-Z0-9]{4,}", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b\d{5,}\b", " ");

        var words = Regex.Matches(cleaned.ToUpperInvariant(), @"[A-ZÄÖÜ0-9]{3,}")
            .Cast<Match>()
            .Select(match => match.Value)
            .Where(word => !AliasStopWords.Contains(word))
            .ToList();
        if (words.Count == 0) return null;

        var code = string.Join("-", words.Take(4).Select(word => word.Length <= 5 ? word : word[..5]));
        if (code.Replace("-", "").Length < 8)
        {
            code = string.Join("-", words.OrderByDescending(word => word.Length).Take(2)
                .Select(word => word.Length <= 6 ? word : word[..6]));
        }

        return code;
    }
}
