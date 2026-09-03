using System.Globalization;

namespace MyCoinFlow.WinUI.Models;

public static class InvoicingPositionTypes
{
    public const string Article = "ARTICLE";
    public const string Text = "TEXT";
    public static bool IsKnown(string value) => value is Article or Text;
}

public sealed record InvoicingFormattedTextSnapshot(string PlainText, string? FormattedText)
{
    public bool HasFormatting => InvoicingFormattedText.HasRtfSignature(FormattedText);
}

public static class InvoicingFormattedText
{
    public const int MaximumPlainTextLength = 100_000;
    public const int MaximumFormattedTextLength = 250_000;

    public static bool HasRtfSignature(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.TrimStart().StartsWith(@"{\rtf", StringComparison.OrdinalIgnoreCase);

    public static string CleanPlainText(string? value) =>
        NormalizeLineEndings(value).TrimEnd('\n', '\0');

    public static string NormalizeLineEndings(string? value) => (value ?? string.Empty)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Replace('\v', '\n')
        .Replace('\f', '\n')
        .Replace('\u0085', '\n')
        .Replace('\u2028', '\n')
        .Replace('\u2029', '\n');

    public static string NormalizeForComparison(string? value) =>
        CleanPlainText(value).TrimEnd('\0');

    public static void ValidateShape(
        string plainText,
        string? formattedText,
        string field,
        List<string> errors)
    {
        if (plainText.Length > MaximumPlainTextLength)
            errors.Add($"{field}: Der Klartext darf höchstens {MaximumPlainTextLength:N0} Zeichen lang sein.");
        if (formattedText?.Length > MaximumFormattedTextLength)
            errors.Add($"{field}: Der Formatsnapshot darf höchstens {MaximumFormattedTextLength:N0} Zeichen lang sein.");
        if (!string.IsNullOrWhiteSpace(formattedText) && !HasRtfSignature(formattedText))
            errors.Add($"{field}: Der Formatsnapshot ist kein unterstütztes RTF-Dokument.");
        if (!string.IsNullOrWhiteSpace(formattedText) && string.IsNullOrWhiteSpace(plainText))
            errors.Add($"{field}: Ein Formatsnapshot benötigt den zugehörigen Klartext.");
    }
}

public sealed class InvoicingTextTemplateDraft
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PlainText { get; set; } = string.Empty;
    public string? FormattedText { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed record InvoicingTextTemplateRecord(
    int Id,
    string Name,
    string PlainText,
    string? FormattedText,
    bool IsActive)
{
    public string ActiveDisplay => IsActive ? "Aktiv" : "Inaktiv";
    public string Preview => InvoicingPositionValidator.Summarize(PlainText, 120);

    public InvoicingTextTemplateDraft ToDraft() => new()
    {
        Id = Id,
        Name = Name,
        PlainText = PlainText,
        FormattedText = FormattedText,
        IsActive = IsActive
    };
}

public sealed class InvoicingPositionDraft
{
    public int Id { get; set; }
    public string ContextSource { get; set; } = string.Empty;
    public int ContextSourceId { get; set; }
    public int SequenceNumber { get; set; }
    public string PositionType { get; set; } = InvoicingPositionTypes.Article;
    public int? ArticleId { get; set; }
    public string Designation { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1m;
    public decimal UnitPrice { get; set; }
    public int? VatRateId { get; set; }
    public string VatCodeSnapshot { get; set; } = string.Empty;
    public decimal? VatRatePercentSnapshot { get; set; }
    public int? RevenueAccountId { get; set; }
    public string RevenueAccountSnapshot { get; set; } = string.Empty;
    public string AncillaryClassificationSnapshot { get; set; } = InvoicingAncillaryClassifications.Standard;
    public string MainTextPlain { get; set; } = string.Empty;
    public string? MainTextFormatted { get; set; }
    public string AdditionalTextPlain { get; set; } = string.Empty;
    public string? AdditionalTextFormatted { get; set; }
    public bool IsFooter { get; set; }
    public bool IsTextPosition => PositionType == InvoicingPositionTypes.Text;
}

public sealed record InvoicingPositionRecord(
    int Id,
    string ContextSource,
    int ContextSourceId,
    int SequenceNumber,
    string PositionType,
    int? ArticleId,
    string Designation,
    string Category,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    int? VatRateId,
    string VatCodeSnapshot,
    decimal? VatRatePercentSnapshot,
    int? RevenueAccountId,
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
    public string PositionTypeDisplay => IsTextPosition ? IsFooter ? "Fusstext" : "Text / Abschnitt" : "Artikel / Leistung";
    public string SequenceDisplay => SequenceNumber.ToString(CultureInfo.InvariantCulture);
    public string AmountDisplay => IsTextPosition
        ? "ohne Betrag"
        : $"{Quantity.ToString("N2", SwissCulture)} {Unit} × {UnitPrice.ToString("N2", SwissCulture)}";
    public decimal LineTotal => IsTextPosition ? 0m : Quantity * UnitPrice;
    public string LineTotalDisplay => IsTextPosition ? "—" : LineTotal.ToString("N2", SwissCulture);
    public string TextPreview => InvoicingPositionValidator.Summarize(MainTextPlain, 140);

    public InvoicingPositionDraft ToDraft() => new()
    {
        Id = Id,
        ContextSource = ContextSource,
        ContextSourceId = ContextSourceId,
        SequenceNumber = SequenceNumber,
        PositionType = PositionType,
        ArticleId = ArticleId,
        Designation = Designation,
        Category = Category,
        Unit = Unit,
        Quantity = Quantity,
        UnitPrice = UnitPrice,
        VatRateId = VatRateId,
        VatCodeSnapshot = VatCodeSnapshot,
        VatRatePercentSnapshot = VatRatePercentSnapshot,
        RevenueAccountId = RevenueAccountId,
        RevenueAccountSnapshot = RevenueAccountSnapshot,
        AncillaryClassificationSnapshot = AncillaryClassificationSnapshot,
        MainTextPlain = MainTextPlain,
        MainTextFormatted = MainTextFormatted,
        AdditionalTextPlain = AdditionalTextPlain,
        AdditionalTextFormatted = AdditionalTextFormatted,
        IsFooter = IsFooter
    };
}

public sealed record InvoicingComposerWorkspace(
    string ContextSource,
    int ContextSourceId,
    string ContextTitle,
    IReadOnlyList<InvoicingArticleRecord> Articles,
    IReadOnlyList<InvoicingVatOption> VatOptions,
    IReadOnlyList<InvoicingRevenueAccountOption> RevenueAccountOptions,
    IReadOnlyList<InvoicingTextTemplateRecord> TextTemplates,
    IReadOnlyList<InvoicingPositionRecord> Positions)
{
    public decimal Total => Positions.Sum(position => position.LineTotal);
}

public sealed class InvoicingPositionValidationException(IReadOnlyList<string> errors)
    : InvalidOperationException(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public static class InvoicingPositionValidator
{
    public static void ValidateAndNormalize(InvoicingTextTemplateDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        draft.Name = draft.Name.Trim();
        draft.PlainText = InvoicingFormattedText.CleanPlainText(draft.PlainText);
        draft.FormattedText = NormalizeRtf(draft.FormattedText);

        var errors = new List<string>();
        Require(draft.Name, "Bezeichnung", errors);
        Require(draft.PlainText, "Text", errors);
        Maximum(draft.Name, 160, "Bezeichnung", errors);
        InvoicingFormattedText.ValidateShape(draft.PlainText, draft.FormattedText, "Text", errors);
        ThrowIfAny(errors);
    }

    public static void ValidateAndNormalize(InvoicingPositionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        draft.ContextSource = draft.ContextSource.Trim().ToUpperInvariant();
        draft.PositionType = draft.PositionType.Trim().ToUpperInvariant();
        draft.Designation = draft.Designation.Trim();
        draft.Category = draft.Category.Trim();
        draft.Unit = draft.Unit.Trim();
        draft.VatCodeSnapshot = draft.VatCodeSnapshot.Trim();
        draft.RevenueAccountSnapshot = draft.RevenueAccountSnapshot.Trim();
        draft.AncillaryClassificationSnapshot = draft.AncillaryClassificationSnapshot.Trim().ToUpperInvariant();
        draft.MainTextPlain = InvoicingFormattedText.CleanPlainText(draft.MainTextPlain);
        draft.MainTextFormatted = NormalizeRtf(draft.MainTextFormatted);
        draft.AdditionalTextPlain = InvoicingFormattedText.CleanPlainText(draft.AdditionalTextPlain);
        draft.AdditionalTextFormatted = NormalizeRtf(draft.AdditionalTextFormatted);

        var errors = new List<string>();
        if (draft.ContextSource is not (InvoicingPositionTypes.Article or "PROPERTY") || draft.ContextSourceId <= 0)
            errors.Add("Der stabile Fakturierungskontext ist ungültig.");
        if (!InvoicingPositionTypes.IsKnown(draft.PositionType))
            errors.Add("Die Positionsart ist ungültig.");
        if (draft.SequenceNumber < 0 || draft.SequenceNumber % 10 != 0)
            errors.Add("Die Positionsreihenfolge muss leer oder in Zehnerschritten angegeben sein.");

        InvoicingFormattedText.ValidateShape(draft.MainTextPlain, draft.MainTextFormatted, "Haupttext", errors);
        InvoicingFormattedText.ValidateShape(draft.AdditionalTextPlain, draft.AdditionalTextFormatted, "Zusatztext", errors);

        if (draft.PositionType == InvoicingPositionTypes.Text)
        {
            Require(draft.MainTextPlain, "Text / Abschnitt", errors);
            if (string.IsNullOrWhiteSpace(draft.Designation))
                draft.Designation = Summarize(draft.MainTextPlain, 200);
            draft.ArticleId = null;
            draft.Category = string.Empty;
            draft.Unit = string.Empty;
            draft.Quantity = 0m;
            draft.UnitPrice = 0m;
            draft.VatRateId = null;
            draft.VatCodeSnapshot = string.Empty;
            draft.VatRatePercentSnapshot = null;
            draft.RevenueAccountId = null;
            draft.RevenueAccountSnapshot = string.Empty;
            draft.AncillaryClassificationSnapshot = InvoicingAncillaryClassifications.Standard;
            draft.AdditionalTextPlain = string.Empty;
            draft.AdditionalTextFormatted = null;
        }
        else
        {
            draft.IsFooter = false;
            Require(draft.Designation, "Bezeichnung", errors);
            Require(draft.Category, "Kategorie", errors);
            Require(draft.Unit, "Einheit", errors);
            if (draft.Quantity <= 0) errors.Add("Die Menge muss grösser als 0 sein.");
            if (draft.UnitPrice < 0) errors.Add("Der Einzelpreis darf nicht negativ sein.");
            if (IsPieceUnit(draft.Unit) && draft.Quantity != decimal.Truncate(draft.Quantity))
                errors.Add($"Für die Einheit {draft.Unit} sind nur ganze Stückzahlen erlaubt.");
            if (!draft.VatRateId.HasValue || draft.VatRateId <= 0 || !draft.VatRatePercentSnapshot.HasValue)
                errors.Add("Ein vorhandener MWST-Snapshot ist erforderlich.");
            if (!draft.RevenueAccountId.HasValue || draft.RevenueAccountId <= 0)
                errors.Add("Ein vorhandener Ertragskonto-Snapshot ist erforderlich.");
            if (!InvoicingAncillaryClassifications.IsKnown(draft.AncillaryClassificationSnapshot))
                errors.Add("Die Nebenkostenklassifikation ist ungültig.");
        }

        Maximum(draft.Designation, 200, "Bezeichnung", errors);
        Maximum(draft.Category, 100, "Kategorie", errors);
        Maximum(draft.Unit, 40, "Einheit", errors);
        Maximum(draft.VatCodeSnapshot, 32, "MWST-Code", errors);
        Maximum(draft.RevenueAccountSnapshot, 200, "Ertragskonto", errors);
        ThrowIfAny(errors);
    }

    public static string Summarize(string? value, int maximum)
    {
        var compact = string.Join(" ", InvoicingFormattedText.NormalizeLineEndings(value)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (compact.Length <= maximum) return compact;
        return compact[..Math.Max(1, maximum - 1)] + "…";
    }

    private static string? NormalizeRtf(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('\0');

    private static bool IsPieceUnit(string value) => value.Trim().TrimEnd('.').ToLowerInvariant()
        is "stk" or "stück" or "stueck";

    private static void Require(string value, string field, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add($"{field} ist erforderlich.");
    }

    private static void Maximum(string value, int maximum, string field, List<string> errors)
    {
        if (value.Length > maximum) errors.Add($"{field} darf höchstens {maximum} Zeichen lang sein.");
    }

    private static void ThrowIfAny(List<string> errors)
    {
        if (errors.Count > 0) throw new InvoicingPositionValidationException(errors);
    }
}
