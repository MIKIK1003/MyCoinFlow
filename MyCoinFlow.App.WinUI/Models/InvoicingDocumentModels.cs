using System.Globalization;

namespace MyCoinFlow.WinUI.Models;

public static class InvoicingDocumentTypeCodes
{
    public const string Offer = "OFFER";
    public const string Order = "ORDER";
    public const string Delivery = "DELIVERY";
    public const string Invoice = "INVOICE";

    public static readonly IReadOnlyList<InvoicingCodeOption> Options =
    [
        new(Offer, "Offerte"),
        new(Order, "Auftragsbestätigung"),
        new(Delivery, "Lieferung"),
        new(Invoice, "Rechnung")
    ];

    public static bool IsKnown(string? value) =>
        value is Offer or Order or Delivery or Invoice;

    public static string DisplayName(string? value) =>
        Options.FirstOrDefault(option => option.Code == value)?.DisplayName ?? value ?? "Dokument";

    public static int Step(string value) => value switch
    {
        Offer => 1,
        Order => 2,
        Delivery => 3,
        Invoice => 4,
        _ => 0
    };

    public static string? Next(string value) => value switch
    {
        Offer => Order,
        Order => Delivery,
        Delivery => Invoice,
        _ => null
    };
}

public static class InvoicingDocumentStatusCodes
{
    public const string Draft = "DRAFT";
    public const string Transferred = "TRANSFERRED";

    public static bool IsKnown(string? value) => value is Draft or Transferred;

    public static string DisplayName(string? value) => value switch
    {
        Draft => "Entwurf",
        Transferred => "Weitergeführt",
        _ => value ?? "Unbekannt"
    };
}

public static class InvoicingRecipientKinds
{
    public const string Customer = "CUSTOMER";
    public const string Owner = "OWNER";
    public const string Tenant = "TENANT";

    public static bool IsKnown(string? value) => value is Customer or Owner or Tenant;

    public static string DisplayName(string? value) => value switch
    {
        Customer => "Dokumentempfänger",
        Owner => "Eigentümer · sicherer Standard",
        Tenant => "Mieter · bewusst geprüft",
        _ => value ?? "Empfänger"
    };
}

public sealed class InvoicingDocumentDraft
{
    public DateTimeOffset DocumentDate { get; set; } = new(DateTime.Today);
    public string ContextSource { get; set; } = string.Empty;
    public int ContextSourceId { get; set; }
    public int RecipientAddressId { get; set; }
    public string RecipientKind { get; set; } = InvoicingRecipientKinds.Customer;
    public string Subject { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
}

public sealed record InvoicingDocumentCurrencyOption(
    string Code,
    string DisplayName,
    decimal RateToBase,
    string RateSource)
{
    public string Display => RateToBase == 1m
        ? $"{Code} · {DisplayName} · Basiswährung"
        : $"{Code} · {DisplayName} · Kurs {RateToBase:N6} ({RateSource})";
}

public sealed record InvoicingDocumentRecipientOption(
    int AddressId,
    string Kind,
    string Display,
    string Notice);

public sealed record InvoicingDocumentCreationWorkspace(
    DateOnly DocumentDate,
    string BaseCurrency,
    IReadOnlyList<BillableObjectRecord> SelectableObjects,
    IReadOnlyList<InvoicingAddressOption> Addresses,
    IReadOnlyList<InvoicingDocumentCurrencyOption> Currencies)
{
    public IReadOnlyList<InvoicingDocumentRecipientOption> GetRecipientOptions(
        BillableObjectRecord context)
    {
        if (context.SourceCode == InvoicingPositionTypes.Article)
        {
            return Addresses
                .Select(address => new InvoicingDocumentRecipientOption(
                    address.Id,
                    InvoicingRecipientKinds.Customer,
                    address.Display,
                    "Bewusst als Empfänger dieses Dokuments gewählt."))
                .ToList();
        }

        var result = new List<InvoicingDocumentRecipientOption>();
        if (context.RecipientAddressId is { } ownerAddressId)
        {
            var owner = Addresses.FirstOrDefault(address => address.Id == ownerAddressId);
            if (owner is not null)
            {
                result.Add(new InvoicingDocumentRecipientOption(
                    owner.Id,
                    InvoicingRecipientKinds.Owner,
                    $"{owner.Display} · Eigentümer (sicherer Standard)",
                    "Eigentümer bleibt die sichtbar verantwortliche Partei und der sichere Standardempfänger."));
            }
        }

        if (context.TenantDirectBillingAvailable &&
            context.TenantRecipientAddressId is { } tenantAddressId)
        {
            var tenant = Addresses.FirstOrDefault(address => address.Id == tenantAddressId);
            if (tenant is not null)
            {
                result.Add(new InvoicingDocumentRecipientOption(
                    tenant.Id,
                    InvoicingRecipientKinds.Tenant,
                    $"{tenant.Display} · Mieter (nur nach manueller Prüfung)",
                    "Die dokumentierten Voraussetzungen sind vorhanden; Überwälzbarkeit und Vertragsgrundlage bleiben manuell zu prüfen."));
            }
        }

        return result;
    }
}

public sealed record InvoicingDocumentPositionRecord(
    int Id,
    int DocumentId,
    int SequenceNumber,
    string PositionType,
    int? SourcePositionId,
    int? ArticleIdSnapshot,
    string Designation,
    string Category,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    string VatCodeSnapshot,
    decimal? VatRatePercentSnapshot,
    string RevenueAccountSnapshot,
    string AncillaryClassificationSnapshot,
    string MainTextPlain,
    string? MainTextFormatted,
    string AdditionalTextPlain,
    string? AdditionalTextFormatted,
    bool IsFooter)
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public bool IsTextPosition => PositionType == InvoicingPositionTypes.Text;
    public string PositionTypeDisplay => IsTextPosition
        ? IsFooter ? "Fusstext" : "Text / Abschnitt"
        : "Artikel / Leistung";
    public decimal LineTotal => IsTextPosition ? 0m : Quantity * UnitPrice;
    public string QuantityAndPriceDisplay => IsTextPosition
        ? "ohne Betrag"
        : $"{Quantity.ToString("N2", SwissCulture)} {Unit} × {UnitPrice.ToString("N2", SwissCulture)}";
    public string LineTotalDisplay => IsTextPosition ? "—" : LineTotal.ToString("N2", SwissCulture);
    public string TextPreview => InvoicingPositionValidator.Summarize(MainTextPlain, 180);
}

public sealed record InvoicingDocumentFlowStep(
    int DocumentId,
    string DocumentType,
    string DocumentNumber,
    string Status,
    DateOnly DocumentDate)
{
    public int Step => InvoicingDocumentTypeCodes.Step(DocumentType);
    public string StepDisplay => $"{Step}. {InvoicingDocumentTypeCodes.DisplayName(DocumentType)}";
    public string StatusDisplay => InvoicingDocumentStatusCodes.DisplayName(Status);
    public string DateDisplay => DocumentDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}

public sealed class InvoicingDocumentRecord
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public int Id { get; init; }
    public Guid FlowId { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public string DocumentNumber { get; init; } = string.Empty;
    public DateOnly DocumentDate { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string ContextSource { get; init; } = string.Empty;
    public int ContextSourceId { get; init; }
    public string ContextTitleSnapshot { get; init; } = string.Empty;
    public string ContextSubtitleSnapshot { get; init; } = string.Empty;
    public string IssuerName { get; init; } = string.Empty;
    public string IssuerStreet { get; init; } = string.Empty;
    public string IssuerPostalCode { get; init; } = string.Empty;
    public string IssuerCity { get; init; } = string.Empty;
    public string IssuerCountryCode { get; init; } = string.Empty;
    public string IssuerVatNumber { get; init; } = string.Empty;
    public string IssuerEmail { get; init; } = string.Empty;
    public string IssuerPhone { get; init; } = string.Empty;
    public int RecipientAddressIdSnapshot { get; init; }
    public string RecipientKind { get; init; } = string.Empty;
    public string RecipientName { get; init; } = string.Empty;
    public string RecipientStreet { get; init; } = string.Empty;
    public string RecipientPostalCode { get; init; } = string.Empty;
    public string RecipientCity { get; init; } = string.Empty;
    public string RecipientCountry { get; init; } = string.Empty;
    public string CurrencyCode { get; init; } = string.Empty;
    public decimal ExchangeRateToBase { get; init; }
    public string ExchangeRateSource { get; init; } = string.Empty;
    public int? PreviousDocumentId { get; init; }
    public string PreviousDocumentNumber { get; init; } = string.Empty;
    public int? NextDocumentId { get; init; }
    public string NextDocumentNumber { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public DateTime? TransitionedAt { get; init; }
    public string TransitionedBy { get; init; } = string.Empty;
    public IReadOnlyList<InvoicingDocumentPositionRecord> Positions { get; set; } = [];
    public IReadOnlyList<InvoicingDocumentFlowStep> Flow { get; set; } = [];

    public string DocumentTypeDisplay => InvoicingDocumentTypeCodes.DisplayName(DocumentType);
    public string StatusDisplay => InvoicingDocumentStatusCodes.DisplayName(Status);
    public string Title => $"{DocumentTypeDisplay} {DocumentNumber}";
    public string DateDisplay => DocumentDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    public string RecipientKindDisplay => InvoicingRecipientKinds.DisplayName(RecipientKind);
    public string RecipientDisplay => JoinAddress(
        RecipientName, RecipientStreet, RecipientPostalCode, RecipientCity, RecipientCountry);
    public string IssuerDisplay => JoinAddress(
        IssuerName, IssuerStreet, IssuerPostalCode, IssuerCity, IssuerCountryCode);
    public decimal PositionsTotal => Positions.Sum(position => position.LineTotal);
    public string PositionsTotalDisplay =>
        $"{PositionsTotal.ToString("N2", SwissCulture)} {CurrencyCode} · nicht finanzwirksamer Positionswert";
    public string FlowDisplay => string.Join(
        " → ",
        Flow.OrderBy(step => step.Step).Select(step => step.DocumentNumber));
    public string? NextDocumentType => InvoicingDocumentTypeCodes.Next(DocumentType);
    public bool CanTransition =>
        Status == InvoicingDocumentStatusCodes.Draft &&
        NextDocumentType is not null &&
        NextDocumentId is null;
    public string NextActionLabel => CanTransition
        ? $"In {InvoicingDocumentTypeCodes.DisplayName(NextDocumentType)} übernehmen"
        : DocumentType == InvoicingDocumentTypeCodes.Invoice
            ? "Rechnungsentwurf ist letzter AP05-Schritt"
            : "Bereits weitergeführt";
    public string SearchText => string.Join(
        " ",
        DocumentNumber,
        DocumentTypeDisplay,
        StatusDisplay,
        Subject,
        ContextTitleSnapshot,
        ContextSubtitleSnapshot,
        RecipientDisplay,
        FlowDisplay);

    private static string JoinAddress(
        string name,
        string street,
        string postalCode,
        string city,
        string country)
    {
        var location = string.Join(
            " ",
            new[] { postalCode, city }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.Join(
            ", ",
            new[] { name, street, location, country }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}

public sealed record InvoicingDocumentWorkspace(IReadOnlyList<InvoicingDocumentRecord> Documents)
{
    public int DraftCount =>
        Documents.Count(document => document.Status == InvoicingDocumentStatusCodes.Draft);
    public int TransferredCount =>
        Documents.Count(document => document.Status == InvoicingDocumentStatusCodes.Transferred);
    public int FlowCount => Documents.Select(document => document.FlowId).Distinct().Count();
    public int InvoiceDraftCount =>
        Documents.Count(document => document.DocumentType == InvoicingDocumentTypeCodes.Invoice);
}

public sealed class InvoicingDocumentValidationException(IReadOnlyList<string> errors)
    : InvalidOperationException(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public static class InvoicingDocumentValidator
{
    public static void ValidateAndNormalize(InvoicingDocumentDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        draft.ContextSource = draft.ContextSource.Trim().ToUpperInvariant();
        draft.RecipientKind = draft.RecipientKind.Trim().ToUpperInvariant();
        draft.Subject = draft.Subject.Trim();
        draft.CurrencyCode = draft.CurrencyCode.Trim().ToUpperInvariant();

        var errors = new List<string>();
        if (draft.ContextSource is not (InvoicingPositionTypes.Article or "PROPERTY") ||
            draft.ContextSourceId <= 0)
        {
            errors.Add("Ein gültiger fakturierbarer Objektkontext ist erforderlich.");
        }
        if (draft.RecipientAddressId <= 0)
            errors.Add("Ein vorhandener Dokumentempfänger ist erforderlich.");
        if (!InvoicingRecipientKinds.IsKnown(draft.RecipientKind))
            errors.Add("Die Empfängerart ist ungültig.");
        if (draft.ContextSource == InvoicingPositionTypes.Article &&
            draft.RecipientKind != InvoicingRecipientKinds.Customer)
        {
            errors.Add("Direkte Artikel-/Leistungsobjekte benötigen einen bewusst gewählten Dokumentempfänger.");
        }
        if (draft.ContextSource == "PROPERTY" &&
            draft.RecipientKind is not (InvoicingRecipientKinds.Owner or InvoicingRecipientKinds.Tenant))
        {
            errors.Add("Immobiliendokumente erlauben nur den Eigentümerstandard oder die dokumentierte Mieteroption.");
        }
        if (string.IsNullOrWhiteSpace(draft.Subject))
            errors.Add("Der Dokumentkopf ist erforderlich.");
        if (draft.Subject.Length > 240)
            errors.Add("Der Dokumentkopf darf höchstens 240 Zeichen lang sein.");
        if (!FinanceSettingsValidator.IsIsoCurrencyCode(draft.CurrencyCode))
            errors.Add("Eine aktive dreistellige Dokumentwährung ist erforderlich.");
        if (draft.DocumentDate.Date < new DateTime(2000, 1, 1) ||
            draft.DocumentDate.Date > new DateTime(2100, 12, 31))
        {
            errors.Add("Das Dokumentdatum muss zwischen 01.01.2000 und 31.12.2100 liegen.");
        }

        if (errors.Count > 0)
            throw new InvoicingDocumentValidationException(errors);
    }
}
