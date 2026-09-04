using MyCoinFlow.Import;
using System.Globalization;

namespace MyCoinFlow.WinUI.Models;

public sealed record InvoicingPaymentCandidate(
    int DocumentId,
    string DocumentNumber,
    string RecipientName,
    string CurrencyCode,
    decimal OpenAmount,
    string BaseCurrencyCode,
    decimal BaseOpenAmount,
    DateOnly DueDate,
    string PaymentReference,
    string PaymentAccountIban,
    int PaymentAccountId,
    int GeldinstitutId,
    int Score,
    string MatchKind,
    string MatchExplanation,
    bool IsSuggested,
    bool CanBook,
    string BlockingReason,
    byte DunningLevel,
    bool IsDunningBlocked)
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public string Heading => $"{DocumentNumber} · {RecipientName}";
    public string AmountDisplay =>
        $"Offen {OpenAmount.ToString("N2", SwissCulture)} {CurrencyCode}";
    public string DueDisplay => $"Fällig {DueDate:dd.MM.yyyy}";
    public string ProposalDisplay => IsSuggested ? "Vorschlag" : $"Treffer {Score}";
    public string StateDisplay => CanBook
        ? MatchExplanation
        : $"{MatchExplanation} · {BlockingReason}";
}

public sealed record InvoicingPaymentWorkspace(
    BankImportItem ImportItem,
    IReadOnlyList<InvoicingPaymentCandidate> Candidates,
    int OpenClarificationCount)
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public string AmountDisplay =>
        $"{ImportItem.Amount.ToString("N2", SwissCulture)} {ImportItem.Currency}";
    public string ReferenceDisplay => string.IsNullOrWhiteSpace(ImportItem.StructuredReference)
        ? string.IsNullOrWhiteSpace(ImportItem.ServiceRef) ? "Keine Referenz" : ImportItem.ServiceRef
        : ImportItem.StructuredReference;
}

public sealed record InvoicingPaymentBookingResult(
    long PaymentId,
    int DocumentId,
    string DocumentNumber,
    decimal AllocatedAmount,
    decimal SurplusAmount,
    string CurrencyCode,
    decimal BaseBookedAmount,
    string BaseCurrencyCode,
    int TransactionCount)
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public string Summary => SurplusAmount > 0m
        ? $"{AllocatedAmount.ToString("N2", SwissCulture)} {CurrencyCode} auf {DocumentNumber} verbucht; " +
          $"{SurplusAmount.ToString("N2", SwissCulture)} {CurrencyCode} bleiben im Klärbestand."
        : $"{AllocatedAmount.ToString("N2", SwissCulture)} {CurrencyCode} auf {DocumentNumber} verbucht " +
          $"({BaseBookedAmount.ToString("N2", SwissCulture)} {BaseCurrencyCode}, {TransactionCount} Buchung(en)).";
}

public static class InvoicingPaymentMatchKinds
{
    public const string Reference = "REFERENCE";
    public const string DocumentNumber = "DOCUMENT_NUMBER";
    public const string Manual = "MANUAL";
}

public static class InvoicingClarificationReasons
{
    public const string NoMatch = "NO_MATCH";
    public const string Ambiguous = "AMBIGUOUS";
    public const string WrongDirection = "WRONG_DIRECTION";
    public const string CurrencyMismatch = "CURRENCY_MISMATCH";
    public const string Overpayment = "OVERPAYMENT";
    public const string Configuration = "CONFIGURATION";
}

public sealed record InvoicingClarificationRecord(
    long Id,
    int SourceItemId,
    string DocumentNumber,
    DateOnly BookingDate,
    string CurrencyCode,
    decimal Amount,
    string ReasonCode,
    string Narrative,
    DateTime CreatedAt,
    string CreatedBy)
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    public string Heading => string.IsNullOrWhiteSpace(DocumentNumber)
        ? $"CAMT-Zeile #{SourceItemId} · ohne Rechnungsbezug"
        : $"CAMT-Zeile #{SourceItemId} · {DocumentNumber}";
    public string AmountDisplay => $"{Amount.ToString("N2", SwissCulture)} {CurrencyCode}";
    public string DateDisplay => BookingDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    public string ReasonDisplay => ReasonCode switch
    {
        InvoicingClarificationReasons.NoMatch => "Kein Treffer",
        InvoicingClarificationReasons.Ambiguous => "Mehrdeutig",
        InvoicingClarificationReasons.WrongDirection => "Falsche Richtung",
        InvoicingClarificationReasons.CurrencyMismatch => "Währung abweichend",
        InvoicingClarificationReasons.Overpayment => "Überzahlung",
        InvoicingClarificationReasons.Configuration => "Konfiguration fehlt",
        _ => ReasonCode
    };
}
