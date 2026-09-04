using System.Globalization;
using System.Net.Mail;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MyCoinFlow.WinUI.Models;

public static class InvoicingDocumentTypes
{
    public static readonly IReadOnlyList<(string Code, string DisplayName, string Prefix)> Defaults =
    [
        ("OFFER", "Offerte", "OFF"),
        ("ORDER", "Auftragsbestätigung", "AUF"),
        ("DELIVERY", "Lieferung", "LIE"),
        ("INVOICE", "Rechnung", "RE"),
        ("CORRECTION", "Korrektur- / Stornobeleg", "KOR")
    ];
}

public sealed class FinanceSettingsDraft
{
    public string IssuerName { get; set; } = string.Empty;
    public string IssuerStreet { get; set; } = string.Empty;
    public string IssuerPostalCode { get; set; } = string.Empty;
    public string IssuerCity { get; set; } = string.Empty;
    public string IssuerCountryCode { get; set; } = "CH";
    public string VatNumber { get; set; } = string.Empty;
    public string InvoiceEmail { get; set; } = string.Empty;
    public string InvoicePhone { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseTls { get; set; } = true;
    public string SmtpUserName { get; set; } = string.Empty;
    public string SmtpFromAddress { get; set; } = string.Empty;
    public bool HasStoredSmtpPassword { get; set; }
    public int DefaultPaymentDays { get; set; } = 30;
    public string BaseCurrency { get; set; } = "CHF";
    public int? ExchangeGainAccountId { get; set; }
    public int? ExchangeLossAccountId { get; set; }
    public List<DocumentNumberRangeSetting> NumberRanges { get; } = [];
    public List<DocumentCurrencySetting> Currencies { get; } = [];
    public List<ExchangeRateSetting> ExchangeRates { get; } = [];
    public List<VatRateSetting> VatRates { get; } = [];
    public List<PaymentAccountSetting> PaymentAccounts { get; } = [];
    public HashSet<int> RevenueAccountIds { get; } = [];
    public IReadOnlyList<FinanceAccountOption> AccountOptions { get; set; } = [];
    public IReadOnlyList<FinanceInstitutionOption> InstitutionOptions { get; set; } = [];
}

public sealed class DocumentNumberRangeSetting
{
    public string DocumentType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public long NextNumber { get; set; } = 1;
    public int Digits { get; set; } = 5;
    public double NextNumberValue
    {
        get => NextNumber;
        set => NextNumber = double.IsFinite(value) && value >= 1
            ? checked((long)value)
            : 0;
    }
    public double DigitsValue
    {
        get => Digits;
        set => Digits = double.IsFinite(value)
            ? checked((int)value)
            : 0;
    }
    public string Preview => $"{Prefix}{NextNumber.ToString($"D{Digits}", CultureInfo.InvariantCulture)}";
}

public sealed class DocumentCurrencySetting
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool SupportsSwissQr => Code is "CHF" or "EUR";
    public string PaymentOutput =>
        SupportsSwissQr ? "Swiss-QR möglich" : "Alternative Zahlungsangaben ab AP07";
}

public sealed class ExchangeRateSetting
{
    public int Id { get; set; }
    public string DocumentCurrency { get; set; } = string.Empty;
    public double RateToBase { get; set; }
    public DateTimeOffset ValidFrom { get; set; } = DateTimeOffset.Now.Date;
    public DateTimeOffset? ValidTo { get; set; }
    public string Source { get; set; } = "Manuell";
    public bool IsActive { get; set; } = true;
    public IReadOnlyList<DocumentCurrencySetting> CurrencyOptions { get; set; } = [];
}

public sealed class VatRateSetting
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public double RatePercent { get; set; }
    public DateTimeOffset ValidFrom { get; set; } = DateTimeOffset.Now.Date;
    public DateTimeOffset? ValidTo { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PaymentAccountSetting : INotifyPropertyChanged
{
    private int? _institutionId;
    private bool _isQrIban;

    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "CHF";
    public bool IsQrIban
    {
        get => _isQrIban;
        set
        {
            if (_isQrIban == value) return;
            _isQrIban = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IbanType));
        }
    }
    public bool IsActive { get; set; } = true;
    public int? InstitutionId
    {
        get => _institutionId;
        set
        {
            if (_institutionId == value) return;
            _institutionId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IbanType));
        }
    }
    public string IbanType => InstitutionId is null
        ? "Nicht gewählt"
        : IsQrIban ? "QR-IBAN" : "IBAN";
    public IReadOnlyList<DocumentCurrencySetting> CurrencyOptions { get; set; } = [];
    public IReadOnlyList<FinanceInstitutionOption> InstitutionOptions { get; set; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record FinanceAccountOption(int Id, int Number, string Name)
{
    public string Display => $"{Number:D4} — {Name}";
}

public sealed record FinanceInstitutionOption(int Id, string Name, string Iban)
{
    public string Display => string.IsNullOrWhiteSpace(Iban)
        ? Name
        : $"{Name} — {Iban}";
}

public sealed record InvoicingWorkspaceOverview(
    string DatabaseName,
    int SchemaVersion,
    bool IsAdmin,
    bool IsConfigured,
    string IssuerName,
    string BaseCurrency,
    int ActiveCurrencyCount,
    int ActiveVatRateCount,
    int ActivePaymentAccountCount,
    int RevenueAccountCount,
    IReadOnlyList<string> MissingConfiguration);

public sealed class FinanceSettingsValidationException(IReadOnlyList<string> errors)
    : InvalidOperationException(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed record FinanceSettingsSaveResult(
    FinanceSettingsDraft Draft,
    IReadOnlyList<string> Warnings)
{
    public bool IsComplete => Warnings.Count == 0;
}

public static class FinanceSettingsValidator
{
    private const int MaximumPaymentDays = 365;

    public static void ValidateAndNormalize(FinanceSettingsDraft draft, DateOnly? effectiveDate = null)
    {
        var errors = GetValidationErrorsAndNormalize(draft, effectiveDate);
        if (errors.Count > 0)
            throw new FinanceSettingsValidationException(errors);
    }

    public static IReadOnlyList<string> GetValidationErrorsAndNormalize(
        FinanceSettingsDraft draft,
        DateOnly? effectiveDate = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Normalize(draft);

        var errors = new List<string>();
        Require(draft.IssuerName, "Aussteller: Firmen- oder Organisationsname", errors);
        Require(draft.IssuerStreet, "Aussteller: Strasse und Hausnummer", errors);
        Require(draft.IssuerPostalCode, "Aussteller: PLZ", errors);
        Require(draft.IssuerCity, "Aussteller: Ort", errors);

        if (!IsIsoCountryCode(draft.IssuerCountryCode))
            errors.Add("Der Aussteller-Ländercode muss aus zwei Buchstaben bestehen.");
        if (draft.DefaultPaymentDays is < 0 or > MaximumPaymentDays)
            errors.Add($"Das Standard-Zahlungsziel muss zwischen 0 und {MaximumPaymentDays} Tagen liegen.");
        if (!string.IsNullOrWhiteSpace(draft.InvoiceEmail) && !IsEmailAddress(draft.InvoiceEmail))
            errors.Add("Die Rechnungs-E-Mail-Adresse ist ungültig.");
        var hasSmtpConfiguration = !string.IsNullOrWhiteSpace(draft.SmtpHost) ||
            !string.IsNullOrWhiteSpace(draft.SmtpUserName) ||
            !string.IsNullOrWhiteSpace(draft.SmtpFromAddress);
        if (hasSmtpConfiguration)
        {
            Require(draft.SmtpHost, "E-Mail-Versand: SMTP-Host", errors);
            Require(draft.SmtpFromAddress, "E-Mail-Versand: Absenderadresse", errors);
            if (!string.IsNullOrWhiteSpace(draft.SmtpFromAddress) &&
                !IsEmailAddress(draft.SmtpFromAddress))
            {
                errors.Add("Die SMTP-Absenderadresse ist ungültig.");
            }
        }
        if (draft.SmtpPort is < 1 or > 65535)
            errors.Add("Der SMTP-Port muss zwischen 1 und 65535 liegen.");
        if (!IsIsoCurrencyCode(draft.BaseCurrency))
            errors.Add("Die Basiswährung muss ein dreistelliger ISO-Währungscode sein.");

        var validationDate = effectiveDate ?? DateOnly.FromDateTime(DateTime.Today);
        ValidateLengths(draft, errors);
        ValidateNumberRanges(draft, errors);
        ValidateCurrenciesAndRates(draft, validationDate, errors);
        ValidateVatRates(draft, validationDate, errors);
        ValidateAccounts(draft, errors);

        return errors;
    }

    public static bool IsSwissQrIban(string? iban)
    {
        var normalized = NormalizeIban(iban);
        if (normalized.Length != 21 || (normalized[..2] is not ("CH" or "LI")))
            return false;
        return IsValidIban(normalized) &&
               int.TryParse(normalized.AsSpan(4, 5), NumberStyles.None, CultureInfo.InvariantCulture, out var iid)
               && iid is >= 30000 and <= 31999;
    }

    public static bool IsValidIban(string? iban)
    {
        var normalized = NormalizeIban(iban);
        return normalized.Length is >= 15 and <= 34 &&
               normalized.All(char.IsLetterOrDigit) &&
               normalized[..2].All(character => character is >= 'A' and <= 'Z') &&
               normalized.AsSpan(2, 2).ToString().All(char.IsDigit) &&
               HasValidIbanChecksum(normalized);
    }

    public static string NormalizeIban(string? value) =>
        string.Concat((value ?? string.Empty).Where(character => !char.IsWhiteSpace(character)))
            .ToUpperInvariant();

    public static bool IsIsoCurrencyCode(string? value) =>
        value is { Length: 3 } && value.All(character => character is >= 'A' and <= 'Z');

    private static void Normalize(FinanceSettingsDraft draft)
    {
        draft.IssuerName = draft.IssuerName.Trim();
        draft.IssuerStreet = draft.IssuerStreet.Trim();
        draft.IssuerPostalCode = draft.IssuerPostalCode.Trim();
        draft.IssuerCity = draft.IssuerCity.Trim();
        draft.IssuerCountryCode = draft.IssuerCountryCode.Trim().ToUpperInvariant();
        draft.VatNumber = draft.VatNumber.Trim();
        draft.InvoiceEmail = draft.InvoiceEmail.Trim();
        draft.InvoicePhone = draft.InvoicePhone.Trim();
        draft.SmtpHost = draft.SmtpHost.Trim();
        draft.SmtpUserName = draft.SmtpUserName.Trim();
        draft.SmtpFromAddress = draft.SmtpFromAddress.Trim();
        draft.BaseCurrency = draft.BaseCurrency.Trim().ToUpperInvariant();

        foreach (var range in draft.NumberRanges)
        {
            range.DocumentType = range.DocumentType.Trim().ToUpperInvariant();
            range.DisplayName = range.DisplayName.Trim();
            range.Prefix = range.Prefix.Trim().ToUpperInvariant();
        }
        foreach (var currency in draft.Currencies)
        {
            currency.Code = currency.Code.Trim().ToUpperInvariant();
            currency.DisplayName = currency.DisplayName.Trim();
        }
        foreach (var rate in draft.ExchangeRates)
        {
            rate.DocumentCurrency = rate.DocumentCurrency.Trim().ToUpperInvariant();
            rate.Source = rate.Source.Trim();
        }
        foreach (var vat in draft.VatRates)
        {
            vat.Code = vat.Code.Trim().ToUpperInvariant();
            vat.DisplayName = vat.DisplayName.Trim();
        }
        foreach (var account in draft.PaymentAccounts)
        {
            account.DisplayName = account.DisplayName.Trim();
            account.Iban = NormalizeIban(account.Iban);
            account.CurrencyCode = account.CurrencyCode.Trim().ToUpperInvariant();
        }
    }

    private static void ValidateNumberRanges(FinanceSettingsDraft draft, List<string> errors)
    {
        var expected = InvoicingDocumentTypes.Defaults.Select(value => value.Code).ToHashSet(StringComparer.Ordinal);
        var actual = draft.NumberRanges.Select(value => value.DocumentType).ToList();
        if (actual.Count != actual.Distinct(StringComparer.Ordinal).Count())
            errors.Add("Jeder Dokumenttyp darf nur einen Nummernkreis besitzen.");
        if (!expected.SetEquals(actual))
            errors.Add("Für Offerte, Auftragsbestätigung, Lieferung, Rechnung und Korrektur-/Stornobeleg ist je ein Nummernkreis erforderlich.");

        foreach (var range in draft.NumberRanges)
        {
            if (range.NextNumber < 1)
                errors.Add($"Nummernkreis {range.DisplayName}: Die nächste Nummer muss mindestens 1 sein.");
            if (range.Digits is < 3 or > 12)
                errors.Add($"Nummernkreis {range.DisplayName}: Die Stellenzahl muss zwischen 3 und 12 liegen.");
            if (range.Prefix.Length > 12)
                errors.Add($"Nummernkreis {range.DisplayName}: Das Präfix darf höchstens 12 Zeichen lang sein.");
        }
    }

    private static void ValidateCurrenciesAndRates(
        FinanceSettingsDraft draft,
        DateOnly effectiveDate,
        List<string> errors)
    {
        var currencyCodes = draft.Currencies.Select(value => value.Code).ToList();
        if (currencyCodes.Count != currencyCodes.Distinct(StringComparer.Ordinal).Count())
            errors.Add("Jede Dokumentwährung darf nur einmal vorkommen.");
        foreach (var currency in draft.Currencies)
        {
            if (!IsIsoCurrencyCode(currency.Code))
                errors.Add($"Währung '{currency.Code}': Der Code muss aus drei Grossbuchstaben bestehen.");
            Require(currency.DisplayName, $"Währung {currency.Code}: Bezeichnung", errors);
        }

        var activeCurrencies = draft.Currencies
            .Where(value => value.IsActive)
            .Select(value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        if (!activeCurrencies.Contains(draft.BaseCurrency))
            errors.Add("Die Basiswährung muss als Dokumentwährung aktiviert sein.");

        var allRates = draft.ExchangeRates.ToList();
        var activeRates = allRates.Where(value => value.IsActive).ToList();
        var duplicateRate = allRates
            .GroupBy(value => (value.DocumentCurrency, value.ValidFrom.Date))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRate is not null)
            errors.Add($"Für {duplicateRate.Key.DocumentCurrency} existieren am {duplicateRate.Key.Date:dd.MM.yyyy} mehrere Wechselkurseinträge.");

        var allCurrencyCodes = currencyCodes.ToHashSet(StringComparer.Ordinal);
        foreach (var rate in allRates)
        {
            if (!allCurrencyCodes.Contains(rate.DocumentCurrency))
                errors.Add($"Wechselkurs {rate.DocumentCurrency}: Die Dokumentwährung ist nicht vorhanden.");
            else if (rate.IsActive && !activeCurrencies.Contains(rate.DocumentCurrency))
                errors.Add($"Wechselkurs {rate.DocumentCurrency}: Die Dokumentwährung ist nicht aktiviert.");
            if (rate.DocumentCurrency == draft.BaseCurrency)
                errors.Add($"Für die Basiswährung {draft.BaseCurrency} darf kein Wechselkurs erfasst werden.");
            if (!double.IsFinite(rate.RateToBase) || rate.RateToBase <= 0)
                errors.Add($"Wechselkurs {rate.DocumentCurrency}: Der Kurs muss grösser als 0 sein.");
            Require(rate.Source, $"Wechselkurs {rate.DocumentCurrency}: Quelle", errors);
            if (rate.ValidTo is { } validTo && validTo.Date < rate.ValidFrom.Date)
                errors.Add($"Wechselkurs {rate.DocumentCurrency}: Gültig bis liegt vor Gültig ab.");
        }

        foreach (var currency in activeCurrencies.Where(code => code != draft.BaseCurrency))
        {
            var hasCurrentRate = activeRates.Any(rate =>
                rate.DocumentCurrency == currency &&
                DateOnly.FromDateTime(rate.ValidFrom.Date) <= effectiveDate &&
                (rate.ValidTo is null || DateOnly.FromDateTime(rate.ValidTo.Value.Date) >= effectiveDate));
            if (!hasCurrentRate)
                errors.Add($"Für die aktive Fremdwährung {currency} fehlt ein heute gültiger manueller Wechselkurs.");
        }
    }

    private static void ValidateVatRates(
        FinanceSettingsDraft draft,
        DateOnly effectiveDate,
        List<string> errors)
    {
        var activeRates = draft.VatRates.Where(value => value.IsActive).ToList();
        if (activeRates.Count == 0)
            errors.Add("Mindestens ein aktiver MWST-Satz ist erforderlich.");
        if (activeRates.Count(value => value.IsDefault) != 1)
            errors.Add("Genau ein aktiver MWST-Satz muss als Standard markiert sein.");
        if (draft.VatRates.GroupBy(value => (value.Code, value.ValidFrom.Date)).Any(group => group.Count() > 1))
            errors.Add("MWST-Code und Gültig-ab-Datum müssen eindeutig sein.");

        foreach (var vat in draft.VatRates)
        {
            Require(vat.Code, "MWST: Code", errors);
            Require(vat.DisplayName, $"MWST {vat.Code}: Bezeichnung", errors);
            if (!double.IsFinite(vat.RatePercent) || vat.RatePercent is < 0 or > 100)
                errors.Add($"MWST {vat.Code}: Der Satz muss zwischen 0 und 100 Prozent liegen.");
            if (vat.ValidTo is { } validTo && validTo.Date < vat.ValidFrom.Date)
                errors.Add($"MWST {vat.Code}: Gültig bis liegt vor Gültig ab.");
            if (!vat.IsActive && vat.IsDefault)
                errors.Add($"MWST {vat.Code}: Ein inaktiver Satz darf nicht Standard sein.");
        }

        var currentDefaultCount = activeRates.Count(vat =>
            vat.IsDefault &&
            DateOnly.FromDateTime(vat.ValidFrom.Date) <= effectiveDate &&
            (vat.ValidTo is null || DateOnly.FromDateTime(vat.ValidTo.Value.Date) >= effectiveDate));
        if (currentDefaultCount != 1)
            errors.Add("Genau ein heute gültiger aktiver MWST-Satz muss Standard sein.");
    }

    private static void ValidateAccounts(FinanceSettingsDraft draft, List<string> errors)
    {
        var knownAccountIds = draft.AccountOptions.Select(value => value.Id).ToHashSet();
        var institutionsById = draft.InstitutionOptions.ToDictionary(value => value.Id);
        var activePaymentAccounts = draft.PaymentAccounts.Where(value => value.IsActive).ToList();
        if (activePaymentAccounts.Count == 0)
            errors.Add("Mindestens ein aktives Zahlungskonto ist erforderlich.");

        var storedPaymentAccounts = draft.PaymentAccounts.Where(value => value.IsActive || value.Id > 0).ToList();
        if (storedPaymentAccounts
            .Where(value => value.InstitutionId.HasValue)
            .GroupBy(value => (value.InstitutionId, value.CurrencyCode))
            .Any(group => group.Count() > 1))
            errors.Add("Geldinstitut und Währung müssen je Zahlungskonto eindeutig sein.");

        foreach (var account in storedPaymentAccounts)
        {
            var paymentCurrency = draft.Currencies.FirstOrDefault(value => value.Code == account.CurrencyCode);
            if (paymentCurrency is null)
                errors.Add($"Zahlungskonto: Die Währung {account.CurrencyCode} ist nicht vorhanden.");
            else if (account.IsActive && !paymentCurrency.IsActive)
                errors.Add($"Zahlungskonto: Die Währung {account.CurrencyCode} ist nicht aktiviert.");

            if (account.InstitutionId is null ||
                !institutionsById.TryGetValue(account.InstitutionId.Value, out var institution))
            {
                if (account.IsActive || account.Id == 0)
                    errors.Add("Zahlungskonto: Ein vorhandenes Geldinstitut ist erforderlich.");
                continue;
            }

            account.DisplayName = institution.Name.Trim();
            account.Iban = NormalizeIban(institution.Iban);
            account.IsQrIban = IsSwissQrIban(account.Iban);
            Require(account.DisplayName, "Zahlungskonto: Name des Geldinstituts", errors);
            if (!IsValidIban(account.Iban))
            {
                errors.Add(
                    $"Zahlungskonto {account.DisplayName}: Beim Geldinstitut ist keine gültige IBAN hinterlegt.");
            }
            else if (account.IsQrIban && account.CurrencyCode is not ("CHF" or "EUR"))
            {
                errors.Add($"QR-Konto {account.DisplayName}: Swiss-QR ist nur für CHF und EUR zulässig.");
            }
        }

        if (draft.RevenueAccountIds.Count == 0)
            errors.Add("Mindestens ein vorhandenes Ertragskonto muss zugelassen sein.");
        if (draft.RevenueAccountIds.Any(id => !knownAccountIds.Contains(id)))
            errors.Add("Die Auswahl der Ertragskonten enthält ein nicht mehr vorhandenes Kontenplan-Konto.");

        var hasForeignCurrency = draft.Currencies.Any(value =>
            value.IsActive && value.Code != draft.BaseCurrency);
        if (hasForeignCurrency)
        {
            if (draft.ExchangeGainAccountId is null || !knownAccountIds.Contains(draft.ExchangeGainAccountId.Value))
                errors.Add("Für aktive Fremdwährungen ist ein vorhandenes Kursgewinnkonto erforderlich.");
            if (draft.ExchangeLossAccountId is null || !knownAccountIds.Contains(draft.ExchangeLossAccountId.Value))
                errors.Add("Für aktive Fremdwährungen ist ein vorhandenes Kursverlustkonto erforderlich.");
            if (draft.ExchangeGainAccountId == draft.ExchangeLossAccountId)
                errors.Add("Kursgewinn- und Kursverlustkonto müssen verschieden sein.");
        }
    }

    private static bool IsIsoCountryCode(string? value) =>
        value is { Length: 2 } && value.All(character => character is >= 'A' and <= 'Z');

    private static bool HasValidIbanChecksum(string iban)
    {
        if (iban.Length < 4) return false;
        var remainder = 0;
        foreach (var character in iban[4..].Concat(iban[..4]))
        {
            if (char.IsDigit(character))
            {
                remainder = ((remainder * 10) + (character - '0')) % 97;
            }
            else if (character is >= 'A' and <= 'Z')
            {
                var value = character - 'A' + 10;
                remainder = ((remainder * 100) + value) % 97;
            }
            else
            {
                return false;
            }
        }
        return remainder == 1;
    }

    private static void ValidateLengths(FinanceSettingsDraft draft, List<string> errors)
    {
        Maximum(draft.IssuerName, 200, "Ausstellername", errors);
        Maximum(draft.IssuerStreet, 200, "Ausstellerstrasse", errors);
        Maximum(draft.IssuerPostalCode, 24, "Aussteller-PLZ", errors);
        Maximum(draft.IssuerCity, 120, "Ausstellerort", errors);
        Maximum(draft.VatNumber, 40, "MWST-/UID-Nummer", errors);
        Maximum(draft.InvoiceEmail, 256, "Rechnungs-E-Mail", errors);
        Maximum(draft.InvoicePhone, 80, "Telefon", errors);
        Maximum(draft.SmtpHost, 256, "SMTP-Host", errors);
        Maximum(draft.SmtpUserName, 256, "SMTP-Benutzername", errors);
        Maximum(draft.SmtpFromAddress, 256, "SMTP-Absenderadresse", errors);
        foreach (var currency in draft.Currencies)
            Maximum(currency.DisplayName, 80, $"Währung {currency.Code}: Bezeichnung", errors);
        foreach (var rate in draft.ExchangeRates)
            Maximum(rate.Source, 120, $"Wechselkurs {rate.DocumentCurrency}: Quelle", errors);
        foreach (var vat in draft.VatRates)
        {
            Maximum(vat.Code, 24, "MWST-Code", errors);
            Maximum(vat.DisplayName, 100, $"MWST {vat.Code}: Bezeichnung", errors);
        }
        foreach (var account in draft.PaymentAccounts)
            Maximum(account.DisplayName, 120, "Zahlungskonto-Bezeichnung", errors);
    }

    private static bool IsEmailAddress(string value)
    {
        try
        {
            return new MailAddress(value).Address.Equals(value, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void Require(string value, string field, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{field} ist erforderlich.");
    }

    private static void Maximum(string value, int length, string field, List<string> errors)
    {
        if (value.Length > length)
            errors.Add($"{field} darf höchstens {length} Zeichen lang sein.");
    }
}
