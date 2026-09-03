using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MyCoinFlow.WinUI.Models;

public static class InvoicingOutputKinds
{
    public const string SwissQr = "SWISS_QR";
    public const string Alternative = "ALTERNATIVE";
}

public static class InvoicingPaymentReferenceTypes
{
    public const string Qrr = "QRR";
    public const string Scor = "SCOR";
}

public static class InvoicingOutputTemplateVersions
{
    public const int Current = 1;
}

public sealed record InvoicingPaymentAccountOption(
    int Id,
    string DisplayName,
    string Iban,
    string Bic,
    string AccountNumber,
    string CurrencyCode,
    bool IsQrIban)
{
    public bool SupportsSwissQr =>
        CurrencyCode is "CHF" or "EUR" &&
        Iban.Length == 21 &&
        (Iban.StartsWith("CH", StringComparison.Ordinal) ||
         Iban.StartsWith("LI", StringComparison.Ordinal));

    public string IbanType => IsQrIban ? "QR-IBAN" : "IBAN";
    public string OutputType => SupportsSwissQr
        ? $"Swiss QR Code · {IbanType}"
        : "Alternative Zahlungsangaben";
    public string Display => $"{DisplayName} · {IbanType} {SwissQrReferenceBuilder.FormatIban(Iban)}";
}

public sealed record InvoicingOutputSnapshot(
    int DocumentId,
    int PaymentAccountId,
    int TemplateVersion,
    string OutputKind,
    string PaymentAccountName,
    string Iban,
    string Bic,
    string AccountNumber,
    string CurrencyCode,
    bool IsQrIban,
    string ReferenceType,
    string PaymentReference,
    string QrPayload,
    DateTime CreatedAt,
    string CreatedBy)
{
    public bool HasSwissQr => OutputKind == InvoicingOutputKinds.SwissQr;
    public string OutputKindDisplay => HasSwissQr
        ? $"Swiss QR Code · {ReferenceType}"
        : "Alternative Zahlungsangaben ohne Swiss QR Code";
    public string IbanDisplay => SwissQrReferenceBuilder.FormatIban(Iban);
    public string ReferenceDisplay => ReferenceType == InvoicingPaymentReferenceTypes.Qrr
        ? SwissQrReferenceBuilder.FormatQrReference(PaymentReference)
        : SwissQrReferenceBuilder.FormatCreditorReference(PaymentReference);
}

public sealed record InvoicingOutputWorkspace(
    InvoicingDocumentRecord Document,
    InvoicingOutputSnapshot? Snapshot,
    IReadOnlyList<InvoicingPaymentAccountOption> PaymentAccounts)
{
    public bool RequiresPaymentSnapshot =>
        Document.Status == InvoicingDocumentStatusCodes.Definitive &&
        Document.Financial?.IsPositiveInvoice == true;

    public bool CanGenerate => !RequiresPaymentSnapshot || Snapshot is not null;

    public string SuggestedFileName
    {
        get
        {
            var number = string.Concat(Document.DocumentNumber.Where(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_'));
            if (string.IsNullOrWhiteSpace(number))
                number = $"Dokument-{Document.Id}";
            return $"{Document.DocumentTypeDisplay}-{number}.pdf";
        }
    }
}

public sealed record InvoicingPdfArtifact(
    byte[] Content,
    string SuggestedFileName,
    string Sha256,
    int PageCount,
    string QrPayload);

public sealed record SwissQrParty(
    string Name,
    string Street,
    string BuildingNumber,
    string PostalCode,
    string City,
    string CountryCode)
{
    private static readonly Regex StreetPattern = new(
        @"^(?<street>.*?)[\s,]+(?<number>\d+[\p{L}\d]*(?:[-/]\p{L}?[\d]+)?)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SwissQrParty Create(
        string name,
        string streetAndNumber,
        string postalCode,
        string city,
        string country)
    {
        var normalizedStreet = Clean(streetAndNumber);
        var match = StreetPattern.Match(normalizedStreet);
        var street = match.Success ? match.Groups["street"].Value.Trim() : normalizedStreet;
        var building = match.Success ? match.Groups["number"].Value.Trim() : string.Empty;
        return new SwissQrParty(
            Clean(name),
            street,
            building,
            Clean(postalCode),
            Clean(city),
            NormalizeCountry(country));
    }

    private static string Clean(string? value) =>
        Regex.Replace(value?.Trim() ?? string.Empty, @"\s+", " ");

    private static string NormalizeCountry(string? value)
    {
        var country = Clean(value).ToUpperInvariant();
        if (country.Length == 2 && country.All(char.IsLetter))
            return country;

        return country switch
        {
            "SCHWEIZ" or "SUISSE" or "SVIZZERA" or "SWITZERLAND" => "CH",
            "LIECHTENSTEIN" => "LI",
            "DEUTSCHLAND" or "GERMANY" or "ALLEMAGNE" => "DE",
            "ÖSTERREICH" or "OESTERREICH" or "AUSTRIA" => "AT",
            "FRANKREICH" or "FRANCE" => "FR",
            "ITALIEN" or "ITALIA" or "ITALY" => "IT",
            _ => country
        };
    }
}

public static class SwissQrReferenceBuilder
{
    private static readonly int[] Modulo10Table = [0, 9, 4, 6, 8, 2, 7, 1, 3, 5];

    public static string CreateQrReference(int documentId)
    {
        if (documentId <= 0)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        var body = documentId.ToString(CultureInfo.InvariantCulture).PadLeft(26, '0');
        if (body.Length > 26)
            throw new InvoicingOutputValidationException(
                ["Die Dokument-ID ist für eine 27-stellige QR-Referenz zu lang."]);
        return body + CalculateModulo10Recursive(body);
    }

    public static bool IsValidQrReference(string? value)
    {
        if (value is null || value.Length != 27 || value.Any(character => !char.IsDigit(character)))
            return false;
        return CalculateModulo10Recursive(value[..26]) == value[26] - '0' &&
               value.Any(character => character != '0');
    }

    public static string CreateCreditorReference(int documentId)
    {
        if (documentId <= 0)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        var body = $"MCF{documentId.ToString(CultureInfo.InvariantCulture)}";
        var numeric = ConvertLettersToDigits(body + "RF00");
        var checkDigits = 98 - Modulo97(numeric);
        return $"RF{checkDigits:00}{body}";
    }

    public static bool IsValidCreditorReference(string? value)
    {
        var normalized = NormalizeReference(value);
        if (normalized.Length is < 5 or > 25 ||
            !normalized.StartsWith("RF", StringComparison.Ordinal) ||
            normalized.Any(character => !char.IsLetterOrDigit(character)))
        {
            return false;
        }

        var rearranged = normalized[4..] + normalized[..4];
        return Modulo97(ConvertLettersToDigits(rearranged)) == 1;
    }

    public static string FormatIban(string? value)
    {
        var normalized = NormalizeReference(value);
        return string.Join(" ", Enumerable.Range(0, (normalized.Length + 3) / 4)
            .Select(index => normalized.Substring(index * 4, Math.Min(4, normalized.Length - index * 4))));
    }

    public static string FormatQrReference(string? value)
    {
        var normalized = NormalizeReference(value);
        if (normalized.Length <= 2) return normalized;
        var groups = new List<string> { normalized[..2] };
        for (var index = 2; index < normalized.Length; index += 5)
            groups.Add(normalized.Substring(index, Math.Min(5, normalized.Length - index)));
        return string.Join(" ", groups);
    }

    public static string FormatCreditorReference(string? value)
    {
        var normalized = NormalizeReference(value);
        return string.Join(" ", Enumerable.Range(0, (normalized.Length + 3) / 4)
            .Select(index => normalized.Substring(index * 4, Math.Min(4, normalized.Length - index * 4))));
    }

    private static int CalculateModulo10Recursive(string digits)
    {
        var carry = 0;
        foreach (var character in digits)
            carry = Modulo10Table[(carry + character - '0') % 10];
        return (10 - carry) % 10;
    }

    private static string NormalizeReference(string? value) =>
        string.Concat((value ?? string.Empty)
            .Where(char.IsLetterOrDigit))
            .ToUpperInvariant();

    private static string ConvertLettersToDigits(string value)
    {
        var builder = new StringBuilder(value.Length * 2);
        foreach (var character in value.ToUpperInvariant())
        {
            if (char.IsDigit(character))
                builder.Append(character);
            else if (character is >= 'A' and <= 'Z')
                builder.Append((character - 'A' + 10).ToString(CultureInfo.InvariantCulture));
            else
                throw new InvoicingOutputValidationException(
                    ["Die ISO-Zahlungsreferenz enthält ein unzulässiges Zeichen."]);
        }
        return builder.ToString();
    }

    private static int Modulo97(string digits)
    {
        var remainder = 0;
        foreach (var character in digits)
            remainder = (remainder * 10 + character - '0') % 97;
        return remainder;
    }
}

public static class SwissQrPayloadBuilder
{
    public const string SpecificationVersion = "2.3";
    public const string PayloadVersion = "0200";

    public static string Create(
        string iban,
        SwissQrParty creditor,
        SwissQrParty debtor,
        decimal amount,
        string currency,
        string referenceType,
        string paymentReference,
        string documentNumber)
    {
        var normalizedIban = FinanceSettingsValidator.NormalizeIban(iban);
        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        var normalizedReference = string.Concat(paymentReference.Where(char.IsLetterOrDigit))
            .ToUpperInvariant();
        Validate(
            normalizedIban,
            creditor,
            debtor,
            amount,
            normalizedCurrency,
            referenceType,
            normalizedReference);

        var lines = new[]
        {
            "SPC",
            PayloadVersion,
            "1",
            normalizedIban,
            "S",
            creditor.Name,
            creditor.Street,
            creditor.BuildingNumber,
            creditor.PostalCode,
            creditor.City,
            creditor.CountryCode,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            amount.ToString("0.00", CultureInfo.InvariantCulture),
            normalizedCurrency,
            "S",
            debtor.Name,
            debtor.Street,
            debtor.BuildingNumber,
            debtor.PostalCode,
            debtor.City,
            debtor.CountryCode,
            referenceType,
            normalizedReference,
            Limit($"Rechnung {documentNumber.Trim()}", 140),
            "EPD"
        };
        var payload = string.Join("\n", lines);
        if (payload.Length > 997)
            throw new InvoicingOutputValidationException(
                ["Die Swiss-QR-Nutzlast überschreitet 997 Zeichen."]);
        return payload;
    }

    private static void Validate(
        string iban,
        SwissQrParty creditor,
        SwissQrParty debtor,
        decimal amount,
        string currency,
        string referenceType,
        string paymentReference)
    {
        var errors = new List<string>();
        if (!FinanceSettingsValidator.IsValidIban(iban) ||
            iban.Length != 21 ||
            iban[..2] is not ("CH" or "LI"))
        {
            errors.Add("Swiss QR benötigt eine gültige 21-stellige CH- oder LI-IBAN.");
        }
        if (currency is not ("CHF" or "EUR"))
            errors.Add("Swiss QR unterstützt ausschließlich CHF oder EUR.");
        if (amount is < 0.01m or > 999999999.99m)
            errors.Add("Der Swiss-QR-Betrag muss zwischen 0.01 und 999'999'999.99 liegen.");

        ValidateParty(creditor, "Zahlungsempfänger", errors);
        ValidateParty(debtor, "Zahlungspflichtiger", errors);

        if (referenceType == InvoicingPaymentReferenceTypes.Qrr)
        {
            if (!FinanceSettingsValidator.IsSwissQrIban(iban))
                errors.Add("Eine QR-Referenz darf nur zusammen mit einer QR-IBAN verwendet werden.");
            if (!SwissQrReferenceBuilder.IsValidQrReference(paymentReference))
                errors.Add("Die 27-stellige QR-Referenz ist ungültig.");
        }
        else if (referenceType == InvoicingPaymentReferenceTypes.Scor)
        {
            if (FinanceSettingsValidator.IsSwissQrIban(iban))
                errors.Add("Eine ISO-Creditor-Reference darf nicht zusammen mit einer QR-IBAN verwendet werden.");
            if (!SwissQrReferenceBuilder.IsValidCreditorReference(paymentReference))
                errors.Add("Die ISO-11649-Creditor-Reference ist ungültig.");
        }
        else
        {
            errors.Add("Die Swiss-QR-Referenzart ist ungültig.");
        }

        if (errors.Count > 0)
            throw new InvoicingOutputValidationException(errors);
    }

    private static void ValidateParty(
        SwissQrParty party,
        string role,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(party.Name) || party.Name.Length > 70)
            errors.Add($"{role}: Der Name muss 1 bis 70 Zeichen enthalten.");
        if (party.Street.Length > 70)
            errors.Add($"{role}: Die Strasse darf höchstens 70 Zeichen enthalten.");
        if (party.BuildingNumber.Length > 16)
            errors.Add($"{role}: Die Hausnummer darf höchstens 16 Zeichen enthalten.");
        if (string.IsNullOrWhiteSpace(party.PostalCode) || party.PostalCode.Length > 16)
            errors.Add($"{role}: Die Postleitzahl muss 1 bis 16 Zeichen enthalten.");
        if (string.IsNullOrWhiteSpace(party.City) || party.City.Length > 35)
            errors.Add($"{role}: Der Ort muss 1 bis 35 Zeichen enthalten.");
        if (party.CountryCode.Length != 2 || party.CountryCode.Any(character => !char.IsLetter(character)))
            errors.Add($"{role}: Ein zweistelliger ISO-Ländercode ist erforderlich.");
    }

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];
}

public sealed class InvoicingOutputValidationException(IReadOnlyList<string> errors)
    : InvalidOperationException(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
