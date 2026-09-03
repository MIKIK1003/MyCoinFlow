using System.Globalization;
using System.Text;

namespace MyCoinFlow.WinUI.Models;

public sealed record InvoicingCodeOption(string Code, string DisplayName)
{
    public string Display => DisplayName;
}

public static class InvoicingAncillaryClassifications
{
    public const string Standard = "STANDARD";
    public const string TenantOperatingCost = "TENANT_OPERATING_COST";
    public const string Repair = "REPAIR";
    public const string Renewal = "RENEWAL";
    public const string NonTransferable = "NON_TRANSFERABLE";

    public static readonly IReadOnlyList<InvoicingCodeOption> Options =
    [
        new(Standard, "Allgemeine Sach-/Leistung"),
        new(TenantOperatingCost, "Mögliche Mieter-Nebenkosten · manuell prüfen"),
        new(Repair, "Reparatur · Eigentümer"),
        new(Renewal, "Erneuerung · Eigentümer"),
        new(NonTransferable, "Nicht überwälzbar · Eigentümer")
    ];

    public static bool IsKnown(string code) => Options.Any(option => option.Code == code);

    public static bool CanBeOfferedToTenant(string code) => code == TenantOperatingCost;

    public static string DisplayName(string code) =>
        Options.FirstOrDefault(option => option.Code == code)?.DisplayName ?? code;
}

public static class InvoicingUsageTypes
{
    public const string OwnerOccupied = "OWNER_OCCUPIED";
    public const string Rented = "RENTED";
    public const string Vacant = "VACANT";

    public static readonly IReadOnlyList<InvoicingCodeOption> Options =
    [
        new(OwnerOccupied, "Selbstbewohnt"),
        new(Rented, "Vermietet"),
        new(Vacant, "Leerstand")
    ];

    public static bool IsKnown(string code) => Options.Any(option => option.Code == code);

    public static string DisplayName(string? code) =>
        Options.FirstOrDefault(option => option.Code == code)?.DisplayName ?? "Nutzung nicht erfasst";
}

public static class InvoicingAncillaryModes
{
    public const string Included = "INCLUDED";
    public const string Advance = "ADVANCE";
    public const string FlatRate = "FLAT_RATE";

    public static readonly IReadOnlyList<InvoicingCodeOption> Options =
    [
        new(Included, "Im Mietzins enthalten"),
        new(Advance, "Akonto"),
        new(FlatRate, "Pauschale")
    ];

    public static bool IsKnown(string code) => Options.Any(option => option.Code == code);

    public static string DisplayName(string? code) =>
        Options.FirstOrDefault(option => option.Code == code)?.DisplayName ?? "Nicht dokumentiert";
}

public sealed class InvoicingArticleDraft
{
    public int Id { get; set; }
    public string ArticleNumber { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public decimal SalePrice { get; set; }
    public int VatRateId { get; set; }
    public int RevenueAccountId { get; set; }
    public string AncillaryClassification { get; set; } = InvoicingAncillaryClassifications.Standard;
}

public sealed record InvoicingArticleRecord(
    int Id,
    string ArticleNumber,
    string Designation,
    string Description,
    string Unit,
    string Category,
    bool IsActive,
    decimal SalePrice,
    int VatRateId,
    string VatDisplay,
    int RevenueAccountId,
    string RevenueAccountDisplay,
    string AncillaryClassification)
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    public string PriceDisplay => $"{SalePrice.ToString("N2", SwissCulture)} Basiswährung / {Unit}";
    public string ActiveDisplay => IsActive ? "Aktiv" : "Inaktiv";
    public string AncillaryDisplay => InvoicingAncillaryClassifications.DisplayName(AncillaryClassification);

    public InvoicingArticleDraft ToDraft() => new()
    {
        Id = Id,
        ArticleNumber = ArticleNumber,
        Designation = Designation,
        Description = Description,
        Unit = Unit,
        Category = Category,
        IsActive = IsActive,
        SalePrice = SalePrice,
        VatRateId = VatRateId,
        RevenueAccountId = RevenueAccountId,
        AncillaryClassification = AncillaryClassification
    };
}

public sealed record InvoicingVatOption(int Id, string Code, string DisplayName, decimal RatePercent)
{
    public string Display => $"{Code} · {DisplayName} · {RatePercent:N2} %";
}

public sealed record InvoicingRevenueAccountOption(int Id, int AccountNumber, string Name)
{
    public string Display => $"{AccountNumber:D4} — {Name}";
}

public sealed record InvoicingAddressOption(
    int Id,
    string Name,
    string Street,
    string PostalCode,
    string City)
{
    public string Display
    {
        get
        {
            var location = string.Join(" ", new[] { PostalCode, City }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var address = string.Join(", ", new[] { Street, location }.Where(value => !string.IsNullOrWhiteSpace(value)));
            return string.IsNullOrWhiteSpace(address) ? Name : $"{Name} — {address}";
        }
    }
}

public sealed record InvoicingOwnerOption(
    int Id,
    string Name,
    int? BillingAddressId,
    string BillingAddressDisplay);

public sealed class InvoicingUnitProfileDraft
{
    public int UsageId { get; set; }
    public int UnitId { get; set; }
    public string UsageType { get; set; } = InvoicingUsageTypes.OwnerOccupied;
    public DateTimeOffset ValidFrom { get; set; } = new(DateTime.Today);
    public DateTimeOffset? ValidTo { get; set; }
    public int? OwnerId { get; set; }
    public int? OwnerBillingAddressId { get; set; }
    public int TenancyId { get; set; }
    public int? TenantAddressId { get; set; }
    public string AncillaryMode { get; set; } = InvoicingAncillaryModes.Included;
    public string ContractReference { get; set; } = string.Empty;
    public bool DirectBillingAllowed { get; set; }
    public string DirectBillingApprovalReference { get; set; } = string.Empty;
}

public sealed record InvoicingUnitProfileRecord(
    int PropertyId,
    string PropertyName,
    int UnitId,
    string UnitName,
    string UnitType,
    int? UsageId,
    string? UsageType,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    int? OwnerId,
    string OwnerName,
    int? OwnerBillingAddressId,
    string OwnerBillingAddress,
    int? TenancyId,
    int? TenantAddressId,
    string TenantAddress,
    string? AncillaryMode,
    string ContractReference,
    bool DirectBillingAllowed,
    string DirectBillingApprovalReference)
{
    public string PropertyAndUnit => $"{PropertyName} → {UnitName}";
    public string UsageDisplay => UsageId.HasValue
        ? $"{InvoicingUsageTypes.DisplayName(UsageType)} · {FormatRange(ValidFrom, ValidTo)}"
        : "Nutzung noch nicht erfasst";
    public string ResponsiblePartyDisplay => string.IsNullOrWhiteSpace(OwnerName)
        ? "Eigentümerzuordnung fehlt oder ist mehrdeutig"
        : OwnerName;
    public string RecipientDisplay => string.IsNullOrWhiteSpace(OwnerBillingAddress)
        ? "Eigentümer-Rechnungsadresse fehlt"
        : DirectBillingAllowed && !string.IsNullOrWhiteSpace(TenantAddress)
            ? $"{OwnerBillingAddress} · Eigentümer-Standard; Mieter nach Prüfung wählbar"
            : $"{OwnerBillingAddress} · Eigentümer-Standard";

    public InvoicingUnitProfileDraft ToDraft() => new()
    {
        UsageId = UsageId ?? 0,
        UnitId = UnitId,
        UsageType = UsageType ?? InvoicingUsageTypes.OwnerOccupied,
        ValidFrom = ValidFrom.HasValue
            ? new DateTimeOffset(ValidFrom.Value.ToDateTime(TimeOnly.MinValue))
            : new DateTimeOffset(DateTime.Today),
        ValidTo = ValidTo.HasValue
            ? new DateTimeOffset(ValidTo.Value.ToDateTime(TimeOnly.MinValue))
            : null,
        OwnerId = OwnerId,
        OwnerBillingAddressId = OwnerBillingAddressId,
        TenancyId = TenancyId ?? 0,
        TenantAddressId = TenantAddressId,
        AncillaryMode = AncillaryMode ?? InvoicingAncillaryModes.Included,
        ContractReference = ContractReference,
        DirectBillingAllowed = DirectBillingAllowed,
        DirectBillingApprovalReference = DirectBillingApprovalReference
    };

    private static string FormatRange(DateOnly? from, DateOnly? to) =>
        from.HasValue
            ? $"{from.Value:dd.MM.yyyy}–{(to.HasValue ? to.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "offen")}"
            : "ohne Zeitraum";
}

public sealed record InvoicingMasterDataSnapshot(
    IReadOnlyList<InvoicingArticleRecord> Articles,
    IReadOnlyList<InvoicingUnitProfileRecord> UnitProfiles,
    IReadOnlyList<InvoicingVatOption> VatOptions,
    IReadOnlyList<InvoicingRevenueAccountOption> RevenueAccountOptions,
    IReadOnlyList<InvoicingAddressOption> AddressOptions,
    IReadOnlyList<InvoicingOwnerOption> OwnerOptions);

public sealed record BillableObjectRecord(
    string StableKey,
    string SourceCode,
    int SourceId,
    string Title,
    string Subtitle,
    string PropertyName,
    string UnitName,
    string PeriodAndUsage,
    string ResponsibleParty,
    int? RecipientAddressId,
    string Recipient,
    string RecipientKind,
    int? TenantRecipientAddressId,
    string TenantRecipient,
    bool IsSelectable,
    bool TenantDirectBillingAvailable,
    string Status,
    string LegalHint,
    string AncillaryClassification)
{
    public string SourceDisplay => SourceCode == "ARTICLE" ? "Artikel / Leistung" : "Liegenschaft / Einheit";
}

public sealed record BillableObjectsWorkspace(
    DateOnly EffectiveDate,
    IReadOnlyList<BillableObjectRecord> Objects)
{
    public int DirectObjectCount => Objects.Count(item => item.SourceCode == "ARTICLE");
    public int PropertyObjectCount => Objects.Count(item => item.SourceCode == "PROPERTY");
    public int SelectableCount => Objects.Count(item => item.IsSelectable);
    public int ReviewCount => Objects.Count(item => !item.IsSelectable);
}

public sealed class InvoicingMasterDataValidationException(IReadOnlyList<string> errors)
    : InvalidOperationException(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public static class InvoicingMasterDataValidator
{
    public static string NormalizeArticleNumber(string? value)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim();
        var result = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }
            result.Append(char.ToUpperInvariant(character));
        }
        return result.ToString();
    }

    public static void ValidateAndNormalize(InvoicingArticleDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        draft.ArticleNumber = NormalizeArticleNumber(draft.ArticleNumber);
        draft.Designation = draft.Designation.Trim();
        draft.Description = draft.Description.Trim();
        draft.Unit = draft.Unit.Trim();
        draft.Category = draft.Category.Trim();
        draft.AncillaryClassification = draft.AncillaryClassification.Trim().ToUpperInvariant();

        var errors = new List<string>();
        Require(draft.ArticleNumber, "Artikelnummer", errors);
        Require(draft.Designation, "Bezeichnung", errors);
        Require(draft.Unit, "Einheit", errors);
        Require(draft.Category, "Kategorie", errors);
        Maximum(draft.ArticleNumber, 64, "Artikelnummer", errors);
        Maximum(draft.Designation, 200, "Bezeichnung", errors);
        Maximum(draft.Description, 2000, "Beschreibung", errors);
        Maximum(draft.Unit, 40, "Einheit", errors);
        Maximum(draft.Category, 100, "Kategorie", errors);
        if (draft.SalePrice < 0)
            errors.Add("Der Verkaufspreis darf nicht negativ sein.");
        if (draft.VatRateId <= 0)
            errors.Add("Ein vorhandener MWST-Satz ist erforderlich.");
        if (draft.RevenueAccountId <= 0)
            errors.Add("Ein vorhandenes, zugelassenes Ertragskonto ist erforderlich.");
        if (!InvoicingAncillaryClassifications.IsKnown(draft.AncillaryClassification))
            errors.Add("Die rechtliche Nebenkostenklassifikation ist ungültig.");

        ThrowIfAny(errors);
    }

    public static void ValidateAndNormalize(InvoicingUnitProfileDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        draft.UsageType = draft.UsageType.Trim().ToUpperInvariant();
        draft.AncillaryMode = draft.AncillaryMode.Trim().ToUpperInvariant();
        draft.ContractReference = draft.ContractReference.Trim();
        draft.DirectBillingApprovalReference = draft.DirectBillingApprovalReference.Trim();

        var errors = new List<string>();
        if (draft.UnitId <= 0)
            errors.Add("Eine vorhandene Stockwerkeinheit ist erforderlich.");
        if (!InvoicingUsageTypes.IsKnown(draft.UsageType))
            errors.Add("Der Nutzungstyp ist ungültig.");
        if (draft.ValidTo is { } validTo && validTo.Date < draft.ValidFrom.Date)
            errors.Add("Gültig bis darf nicht vor Gültig ab liegen.");
        if (draft.ValidFrom.Year < 1753)
            errors.Add("Gültig ab ist erforderlich.");
        if (!draft.OwnerId.HasValue)
            errors.Add("Der zum Nutzungsbeginn verantwortliche Eigentümer ist erforderlich.");
        if (!draft.OwnerBillingAddressId.HasValue)
            errors.Add("Eine vorhandene Eigentümer-Rechnungsadresse ist erforderlich.");

        if (draft.UsageType == InvoicingUsageTypes.Rented)
        {
            if (!draft.TenantAddressId.HasValue)
                errors.Add("Für eine vermietete Nutzung ist eine vorhandene Mieteradresse erforderlich.");
            if (!InvoicingAncillaryModes.IsKnown(draft.AncillaryMode))
                errors.Add("Der Nebenkostenmodus des Mietverhältnisses ist ungültig.");
            Require(draft.ContractReference, "Vertragsreferenz", errors);
            Maximum(draft.ContractReference, 160, "Vertragsreferenz", errors);
            Maximum(draft.DirectBillingApprovalReference, 240, "Freigabenachweis", errors);
            if (draft.DirectBillingAllowed &&
                draft.AncillaryMode is not (InvoicingAncillaryModes.Advance or InvoicingAncillaryModes.FlatRate))
                errors.Add("Direktfakturierung an Mieter ist nur bei dokumentiertem Akonto oder dokumentierter Pauschale zulässig.");
            if (draft.DirectBillingAllowed && string.IsNullOrWhiteSpace(draft.DirectBillingApprovalReference))
                errors.Add("Für Mieter-Direktfakturierung ist ein dokumentierter Freigabenachweis erforderlich.");
        }
        else if (draft.TenantAddressId.HasValue || draft.DirectBillingAllowed)
        {
            errors.Add("Selbstnutzung und Leerstand dürfen keine Mieter-Direktfakturierung enthalten.");
        }

        ThrowIfAny(errors);
    }

    public static bool Overlaps(DateOnly leftFrom, DateOnly? leftTo, DateOnly rightFrom, DateOnly? rightTo) =>
        leftFrom <= (rightTo ?? DateOnly.MaxValue) &&
        rightFrom <= (leftTo ?? DateOnly.MaxValue);

    private static void ThrowIfAny(List<string> errors)
    {
        if (errors.Count > 0)
            throw new InvoicingMasterDataValidationException(errors);
    }

    private static void Require(string value, string field, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{field} ist erforderlich.");
    }

    private static void Maximum(string value, int maximum, string field, List<string> errors)
    {
        if (value.Length > maximum)
            errors.Add($"{field} darf höchstens {maximum} Zeichen lang sein.");
    }
}
