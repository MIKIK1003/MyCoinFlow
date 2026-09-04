using System.Globalization;

namespace MyCoinFlow.WinUI.Models;

public static class InvoicingInvoiceKindCodes
{
    public const string Full = "FULL";
    public const string Partial = "PARTIAL";
    public const string Final = "FINAL";
    public const string Correction = "CORRECTION";
    public const string Cancellation = "CANCELLATION";

    public static readonly IReadOnlyList<InvoicingCodeOption> PositiveOptions =
    [
        new(Full, "Normale Rechnung"),
        new(Partial, "Teilrechnung"),
        new(Final, "Schlussrechnung")
    ];

    public static bool IsPositive(string? value) => value is Full or Partial or Final;
    public static bool IsAdjustment(string? value) => value is Correction or Cancellation;

    public static string DisplayName(string? value) => value switch
    {
        Full => "Normale Rechnung",
        Partial => "Teilrechnung",
        Final => "Schlussrechnung",
        Correction => "Korrektur",
        Cancellation => "Storno",
        _ => value ?? "Rechnung"
    };
}

public static class InvoicingOpenItemStatusCodes
{
    public const string Open = "OPEN";
    public const string Corrected = "CORRECTED";
    public const string PartiallyPaid = "PARTIALLY_PAID";
    public const string Paid = "PAID";
    public const string Cancelled = "CANCELLED";

    public static string DisplayName(string? value) => value switch
    {
        Open => "Offen",
        Corrected => "Teilweise korrigiert",
        PartiallyPaid => "Teilbezahlt",
        Paid => "Bezahlt",
        Cancelled => "Storniert",
        _ => value ?? "Unbekannt"
    };
}

public static class InvoicingRevisionEventTypeCodes
{
    public const string InvoiceFinalized = "INVOICE_FINALIZED";
    public const string NextPartialCreated = "NEXT_PARTIAL_CREATED";
    public const string NextFinalCreated = "NEXT_FINAL_CREATED";
    public const string CorrectionCreated = "CORRECTION_CREATED";
    public const string CancellationCreated = "CANCELLATION_CREATED";
    public const string PaymentApplied = "PAYMENT_APPLIED";

    public static string DisplayName(string? value) => value switch
    {
        InvoiceFinalized => "Rechnung definitiv gesetzt",
        NextPartialCreated => "Weitere Teilrechnung angelegt",
        NextFinalCreated => "Schlussrechnung angelegt",
        CorrectionCreated => "Korrektur erstellt",
        CancellationCreated => "Storno erstellt",
        PaymentApplied => "Zahlung verbucht",
        _ => value ?? "Revisionsereignis"
    };
}

public sealed class InvoicingInvoiceDraft
{
    public int DocumentId { get; set; }
    public string InvoiceKind { get; set; } = InvoicingInvoiceKindCodes.Full;
    public decimal DiscountPercent { get; set; }
    public decimal FullRoundingAdjustment { get; set; }
    public int PaymentDays { get; set; } = 30;
    public decimal? SkontoPercent { get; set; }
    public int? SkontoDays { get; set; }
    public decimal? PartialGrossAmount { get; set; }
    public List<InvoicingInstallmentDraft> Installments { get; } = [];
}

public sealed class InvoicingInstallmentDraft
{
    public DateTimeOffset DueDate { get; set; } = new(DateTime.Today);
    public decimal Amount { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class InvoicingAdjustmentDraft
{
    public int ReferenceInvoiceDocumentId { get; set; }
    public string AdjustmentKind { get; set; } = InvoicingInvoiceKindCodes.Correction;
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed record InvoicingInvoiceEditorWorkspace(
    InvoicingDocumentRecord Document,
    string BaseCurrencyCode,
    int DefaultPaymentDays,
    decimal PreviouslyInvoicedGross,
    decimal? AgreedFullGrossBasis,
    decimal LockedDiscountPercent,
    decimal LockedFullRoundingAdjustment,
    bool TermsLocked,
    string SuggestedInvoiceKind,
    IReadOnlyList<InvoicingCodeOption> AllowedKinds);

public sealed record InvoicingInvoiceCalculationPreview(
    decimal FullNetBeforeDiscount,
    decimal FullDiscountAmount,
    decimal FullNetAfterDiscount,
    decimal FullVatAmount,
    decimal FullRoundingAdjustment,
    decimal FullGrossBasis,
    decimal PreviouslyInvoicedGross,
    decimal RemainingBeforeInvoice,
    decimal NetAmount,
    decimal VatAmount,
    decimal DiscountAmount,
    decimal RoundingAdjustment,
    decimal GrossAmount,
    decimal BaseGrossAmount,
    DateOnly DueDate,
    decimal? SkontoAmount,
    DateOnly? SkontoDueDate)
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public string FullGrossDisplay(string currency) =>
        $"{FullGrossBasis.ToString("N2", SwissCulture)} {currency}";
    public string RemainingDisplay(string currency) =>
        $"{RemainingBeforeInvoice.ToString("N2", SwissCulture)} {currency}";
    public string GrossDisplay(string currency) =>
        $"{GrossAmount.ToString("N2", SwissCulture)} {currency}";
}

public sealed record InvoicingInstallmentRecord(
    int Id,
    int InvoiceDocumentId,
    int SequenceNumber,
    DateOnly DueDate,
    decimal Amount,
    string Label,
    decimal PaidAmount,
    string Status)
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    public string DueDateDisplay => DueDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    public string AmountDisplay(string currency) => $"{Amount.ToString("N2", SwissCulture)} {currency}";
    public string Display => $"{DueDateDisplay} · {Label}";
}

public sealed record InvoicingOpenItemRecord(
    int DocumentId,
    string CurrencyCode,
    string BaseCurrencyCode,
    decimal OriginalAmount,
    decimal CorrectionAmount,
    decimal PaidAmount,
    decimal OpenAmount,
    decimal BaseOriginalAmount,
    decimal BaseCorrectionAmount,
    decimal BasePaidAmount,
    decimal BaseOpenAmount,
    DateOnly DueDate,
    string Status,
    DateTime UpdatedAt,
    byte DunningLevel,
    bool IsDunningBlocked,
    DateTime? LastDunningAt)
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    public string StatusDisplay => InvoicingOpenItemStatusCodes.DisplayName(Status);
    public string OpenAmountDisplay =>
        $"{OpenAmount.ToString("N2", SwissCulture)} {CurrencyCode}";
    public string BaseOpenAmountDisplay =>
        $"{BaseOpenAmount.ToString("N2", SwissCulture)} {BaseCurrencyCode}";
    public string DueDateDisplay => DueDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    public string PaidAmountDisplay =>
        $"{PaidAmount.ToString("N2", SwissCulture)} {CurrencyCode}";
    public string DunningStatusDisplay => Status is InvoicingOpenItemStatusCodes.Paid or
        InvoicingOpenItemStatusCodes.Cancelled
        ? "Abgeschlossen"
        : IsDunningBlocked
            ? "Mahnsperre"
            : DunningLevel > 0
                ? $"Mahnstufe {DunningLevel}"
                : DueDate < DateOnly.FromDateTime(DateTime.Today)
                    ? "Überfällig"
                    : "Nicht fällig";
}

public sealed record InvoicingRevisionEventRecord(
    long Id,
    int DocumentId,
    int SequenceNumber,
    string EventType,
    int? ReferenceDocumentId,
    decimal Amount,
    string CurrencyCode,
    string Narrative,
    DateTime EventAt,
    string EventBy)
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    public string EventTypeDisplay => InvoicingRevisionEventTypeCodes.DisplayName(EventType);
    public string EventAtDisplay => EventAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
    public string AmountDisplay => $"{Amount.ToString("N2", SwissCulture)} {CurrencyCode}";
}

public sealed class InvoicingFinancialRecord
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public int DocumentId { get; init; }
    public string InvoiceKind { get; init; } = string.Empty;
    public int? ReferenceInvoiceDocumentId { get; init; }
    public string AdjustmentReason { get; init; } = string.Empty;
    public decimal FullGrossBasis { get; init; }
    public decimal PreviouslyInvoicedGross { get; init; }
    public decimal NetAmount { get; init; }
    public decimal VatAmount { get; init; }
    public decimal DiscountPercent { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal RoundingAdjustment { get; init; }
    public decimal GrossAmount { get; init; }
    public string BaseCurrencyCode { get; init; } = string.Empty;
    public decimal BaseGrossAmount { get; init; }
    public int? PaymentDays { get; init; }
    public DateOnly? DueDate { get; init; }
    public decimal? SkontoPercent { get; init; }
    public int? SkontoDays { get; init; }
    public DateOnly? SkontoDueDate { get; init; }
    public decimal? SkontoAmount { get; init; }
    public string PaymentReference { get; init; } = string.Empty;
    public DateTime FinalizedAt { get; init; }
    public string FinalizedBy { get; init; } = string.Empty;
    public bool IsLatestPositiveInvoice { get; set; }
    public bool BillingComplete { get; set; }
    public decimal FlowInvoicedGross { get; set; }
    public IReadOnlyList<InvoicingInstallmentRecord> Installments { get; set; } = [];
    public IReadOnlyList<InvoicingRevisionEventRecord> Revisions { get; set; } = [];
    public InvoicingOpenItemRecord? OpenItem { get; set; }

    public bool IsPositiveInvoice => InvoicingInvoiceKindCodes.IsPositive(InvoiceKind);
    public bool IsAdjustment => InvoicingInvoiceKindCodes.IsAdjustment(InvoiceKind);
    public string InvoiceKindDisplay => InvoicingInvoiceKindCodes.DisplayName(InvoiceKind);
    public decimal BillingRemaining => Math.Max(0m, FullGrossBasis - FlowInvoicedGross);
    public bool CanCreateNextInvoice =>
        InvoiceKind == InvoicingInvoiceKindCodes.Partial &&
        IsLatestPositiveInvoice &&
        !BillingComplete &&
        BillingRemaining > 0m;
    public bool CanAdjust =>
        IsPositiveInvoice &&
        BillingComplete &&
        OpenItem is { OpenAmount: > 0m };
    public string NetAmountDisplay => $"{NetAmount.ToString("N2", SwissCulture)}";
    public string VatAmountDisplay => $"{VatAmount.ToString("N2", SwissCulture)}";
    public string GrossAmountDisplay => $"{GrossAmount.ToString("N2", SwissCulture)}";
    public string BaseGrossAmountDisplay => $"{BaseGrossAmount.ToString("N2", SwissCulture)} {BaseCurrencyCode}";
    public string DueDateDisplay => DueDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "—";
    public string SkontoDisplay => SkontoPercent.HasValue
        ? $"{SkontoPercent:N2} % bis {SkontoDueDate:dd.MM.yyyy} · {SkontoAmount:N2}"
        : "Kein Skonto";
    public string TermsDisplay =>
        $"Rabatt {DiscountPercent:N2} % · Rundung {RoundingAdjustment:N2} · Zahlungsziel {PaymentDays ?? 0} Tage";
}

public sealed record InvoicingFinancialWorkspace(
    IReadOnlyDictionary<int, InvoicingFinancialRecord> Records)
{
    public int DefinitiveInvoiceCount =>
        Records.Values.Count(record => record.IsPositiveInvoice);
    public int OpenItemCount =>
        Records.Values.Count(record => record.OpenItem is { OpenAmount: > 0m });
}

public sealed class InvoicingInvoiceValidationException(IReadOnlyList<string> errors)
    : InvalidOperationException(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public static class InvoicingInvoiceCalculator
{
    public static InvoicingInvoiceCalculationPreview Calculate(
        IReadOnlyList<InvoicingDocumentPositionRecord> positions,
        DateOnly documentDate,
        decimal exchangeRateToBase,
        InvoicingInvoiceDraft draft,
        decimal previouslyInvoicedGross = 0m,
        decimal? agreedFullGrossBasis = null)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(draft);

        var errors = new List<string>();
        draft.InvoiceKind = draft.InvoiceKind.Trim().ToUpperInvariant();
        if (!InvoicingInvoiceKindCodes.IsPositive(draft.InvoiceKind))
            errors.Add("Eine normale Rechnung, Teilrechnung oder Schlussrechnung ist erforderlich.");
        if (draft.DiscountPercent is < 0m or > 100m)
            errors.Add("Der Rabatt muss zwischen 0 und 100 Prozent liegen.");
        if (draft.FullRoundingAdjustment is < -1000m or > 1000m)
            errors.Add("Die Rundungskorrektur muss zwischen -1'000.00 und 1'000.00 liegen.");
        if (draft.PaymentDays is < 0 or > 365)
            errors.Add("Das Zahlungsziel muss zwischen 0 und 365 Tagen liegen.");
        if (exchangeRateToBase <= 0m)
            errors.Add("Der eingefrorene Wechselkurs muss positiv sein.");

        var hasSkontoPercent = draft.SkontoPercent.HasValue;
        var hasSkontoDays = draft.SkontoDays.HasValue;
        if (hasSkontoPercent != hasSkontoDays)
            errors.Add("Skontoprozent und Skontofrist müssen gemeinsam angegeben werden.");
        if (draft.SkontoPercent is <= 0m or > 100m)
            errors.Add("Skonto muss grösser als 0 und höchstens 100 Prozent sein.");
        if (draft.SkontoDays is < 0 || draft.SkontoDays > draft.PaymentDays)
            errors.Add("Die Skontofrist muss zwischen 0 und dem Zahlungsziel liegen.");

        var articlePositions = positions.Where(position => !position.IsTextPosition).ToList();
        if (articlePositions.Count == 0)
            errors.Add("Die Rechnung benötigt mindestens eine finanzielle Position.");
        if (articlePositions.Any(position =>
                position.Quantity <= 0m ||
                position.UnitPrice < 0m ||
                position.VatRatePercentSnapshot is null))
        {
            errors.Add("Alle finanziellen Positionssnapshots müssen Menge, Preis und MWST enthalten.");
        }

        var fullNetBeforeDiscount = RoundMoney(articlePositions.Sum(position => position.LineTotal));
        var fullDiscountAmount = RoundMoney(fullNetBeforeDiscount * draft.DiscountPercent / 100m);
        var fullNetAfterDiscount = fullNetBeforeDiscount - fullDiscountAmount;
        var discountFactor = 1m - draft.DiscountPercent / 100m;
        var fullVatAmount = RoundMoney(articlePositions.Sum(position =>
            RoundMoney(position.LineTotal * discountFactor *
                       (position.VatRatePercentSnapshot ?? 0m) / 100m)));
        var calculatedFullGross =
            RoundMoney(fullNetAfterDiscount + fullVatAmount + draft.FullRoundingAdjustment);
        var fullGrossBasis = agreedFullGrossBasis ?? calculatedFullGross;

        if (fullNetBeforeDiscount <= 0m)
            errors.Add("Die finanzielle Positionssumme muss grösser als null sein.");
        if (fullGrossBasis <= 0m)
            errors.Add("Der vereinbarte Gesamtbetrag muss grösser als null sein.");
        if (agreedFullGrossBasis.HasValue &&
            Math.Abs(calculatedFullGross - agreedFullGrossBasis.Value) > 0.01m)
        {
            errors.Add("Rabatt und Rundung stimmen nicht mehr mit der eingefrorenen Rechnungsbasis überein.");
        }
        if (previouslyInvoicedGross < 0m || previouslyInvoicedGross >= fullGrossBasis)
            errors.Add("Der bereits fakturierte Betrag ist für diese Rechnungsbasis ungültig.");

        var remaining = RoundMoney(fullGrossBasis - previouslyInvoicedGross);
        decimal grossAmount;
        switch (draft.InvoiceKind)
        {
            case InvoicingInvoiceKindCodes.Full:
                if (previouslyInvoicedGross != 0m)
                    errors.Add("Eine normale Rechnung ist nur ohne frühere Teilrechnung möglich.");
                grossAmount = fullGrossBasis;
                break;
            case InvoicingInvoiceKindCodes.Partial:
                if (!draft.PartialGrossAmount.HasValue)
                {
                    errors.Add("Für eine Teilrechnung ist ein Rechnungsbetrag erforderlich.");
                    grossAmount = 0m;
                }
                else
                {
                    grossAmount = RoundMoney(draft.PartialGrossAmount.Value);
                    if (grossAmount <= 0m || grossAmount >= remaining)
                        errors.Add("Die Teilrechnung muss grösser als null und kleiner als der verbleibende Betrag sein.");
                }
                break;
            case InvoicingInvoiceKindCodes.Final:
                if (previouslyInvoicedGross <= 0m)
                    errors.Add("Eine Schlussrechnung setzt mindestens eine definitive Teilrechnung voraus.");
                grossAmount = remaining;
                break;
            default:
                grossAmount = 0m;
                break;
        }

        if (errors.Count > 0)
            throw new InvoicingInvoiceValidationException(errors);

        var ratio = grossAmount / fullGrossBasis;
        var netAmount = RoundMoney(fullNetAfterDiscount * ratio);
        var vatAmount = RoundMoney(fullVatAmount * ratio);
        var roundingAdjustment = RoundMoney(grossAmount - netAmount - vatAmount);
        var discountAmount = RoundMoney(fullDiscountAmount * ratio);
        var baseGrossAmount = RoundMoney(grossAmount * exchangeRateToBase);
        var dueDate = documentDate.AddDays(draft.PaymentDays);
        DateOnly? skontoDueDate = draft.SkontoDays.HasValue
            ? documentDate.AddDays(draft.SkontoDays.Value)
            : null;
        decimal? skontoAmount = draft.SkontoPercent.HasValue
            ? RoundMoney(grossAmount * draft.SkontoPercent.Value / 100m)
            : null;

        ValidateInstallments(draft.Installments, documentDate, grossAmount);

        return new InvoicingInvoiceCalculationPreview(
            fullNetBeforeDiscount,
            fullDiscountAmount,
            fullNetAfterDiscount,
            fullVatAmount,
            draft.FullRoundingAdjustment,
            fullGrossBasis,
            previouslyInvoicedGross,
            remaining,
            netAmount,
            vatAmount,
            discountAmount,
            roundingAdjustment,
            grossAmount,
            baseGrossAmount,
            dueDate,
            skontoAmount,
            skontoDueDate);
    }

    public static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static void ValidateInstallments(
        IReadOnlyList<InvoicingInstallmentDraft> installments,
        DateOnly documentDate,
        decimal grossAmount)
    {
        if (installments.Count == 0)
            return;

        var errors = new List<string>();
        if (installments.Count > 60)
            errors.Add("Ein Abzahlungsplan darf höchstens 60 Raten enthalten.");

        DateOnly? previousDueDate = null;
        for (var index = 0; index < installments.Count; index++)
        {
            var installment = installments[index];
            installment.Label = installment.Label.Trim();
            var dueDate = DateOnly.FromDateTime(installment.DueDate.Date);
            if (dueDate < documentDate)
                errors.Add($"Rate {index + 1}: Die Fälligkeit darf nicht vor dem Rechnungsdatum liegen.");
            if (previousDueDate.HasValue && dueDate < previousDueDate.Value)
                errors.Add($"Rate {index + 1}: Die Fälligkeiten müssen chronologisch geordnet sein.");
            previousDueDate = dueDate;
            if (installment.Amount <= 0m)
                errors.Add($"Rate {index + 1}: Der Betrag muss grösser als null sein.");
            if (string.IsNullOrWhiteSpace(installment.Label))
                errors.Add($"Rate {index + 1}: Eine Bezeichnung ist erforderlich.");
            if (installment.Label.Length > 160)
                errors.Add($"Rate {index + 1}: Die Bezeichnung darf höchstens 160 Zeichen lang sein.");
        }

        var sum = RoundMoney(installments.Sum(installment => installment.Amount));
        if (sum != grossAmount)
            errors.Add("Die Summe aller Raten muss exakt dem Rechnungsbetrag entsprechen.");

        if (errors.Count > 0)
            throw new InvoicingInvoiceValidationException(errors);
    }
}
