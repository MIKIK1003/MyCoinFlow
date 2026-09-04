using System.Globalization;

namespace MyCoinFlow.WinUI.Models;

public static class InvoicingDeliveryStatuses
{
    public const string Started = "STARTED";
    public const string Sent = "SENT";
    public const string Failed = "FAILED";

    public static string DisplayName(string value) => value switch
    {
        Started => "Gestartet · Ergebnis ungeklärt",
        Sent => "Versendet",
        Failed => "Fehlgeschlagen",
        _ => value
    };
}

public sealed record InvoicingSmtpConfiguration(
    string Host,
    int Port,
    bool UseTls,
    string UserName,
    string FromAddress)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        Port is >= 1 and <= 65535 &&
        !string.IsNullOrWhiteSpace(FromAddress);

    public string Display => IsConfigured
        ? $"{Host}:{Port} · {(UseTls ? "TLS" : "ohne TLS")} · {FromAddress}"
        : "SMTP ist unter Finanzen noch nicht vollständig eingerichtet.";
}

public sealed record InvoicingDmsAttachment(
    int Id,
    string Title,
    string Category,
    string OriginalName,
    string FileName,
    string FolderRelative,
    long SizeBytes,
    string ContentHash,
    DateTime ImportedAtUtc,
    string FullPath)
{
    public string SizeDisplay => SizeBytes < 1024 * 1024
        ? $"{Math.Max(1, SizeBytes / 1024d):N0} KB"
        : $"{SizeBytes / 1024d / 1024d:N1} MB";
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? OriginalName : Title;
    public string Details =>
        $"{(string.IsNullOrWhiteSpace(Category) ? "DMS" : Category)} · {SizeDisplay} · " +
        ImportedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
    public bool FileExists => File.Exists(FullPath);
}

public sealed record InvoicingDeliveryAttempt(
    long Id,
    int AttemptNumber,
    string Status,
    string RecipientAddress,
    string Subject,
    string PdfSha256,
    int AttachmentCount,
    DateTime CreatedAt,
    string CreatedBy,
    DateTime LastEventAt,
    string Narrative)
{
    public string StatusDisplay => InvoicingDeliveryStatuses.DisplayName(Status);
    public string AttemptDisplay => $"Versuch {AttemptNumber} · {StatusDisplay}";
    public string Details =>
        $"{LastEventAt:dd.MM.yyyy HH:mm} · {RecipientAddress} · {AttachmentCount} Anhang/Anhänge";
}

public sealed record InvoicingDeliveryWorkspace(
    int DocumentId,
    string DocumentTitle,
    string RecipientName,
    string SuggestedRecipientAddress,
    string DefaultSubject,
    string DefaultBody,
    InvoicingSmtpConfiguration Smtp,
    bool HasStoredSmtpPassword,
    bool DmsEnabled,
    bool PdfReady,
    string PdfStatus,
    IReadOnlyList<InvoicingDmsAttachment> DmsAttachments,
    IReadOnlyList<InvoicingDeliveryAttempt> Attempts)
{
    public bool CanSend => PdfReady && Smtp.IsConfigured;
}

public sealed record InvoicingEmailAttachmentData(
    string FileName,
    string ContentType,
    byte[] Content,
    string ContentSha256,
    int? DmsAttachmentId,
    bool IsDocumentPdf);

public sealed record InvoicingPreparedEmail(
    string MessageId,
    string RecipientAddress,
    string Subject,
    string Body,
    IReadOnlyList<InvoicingEmailAttachmentData> Attachments);

public sealed record InvoicingDeliveryDraft(
    int DocumentId,
    string RecipientAddress,
    string Subject,
    string Body,
    IReadOnlyCollection<int> SelectedDmsAttachmentIds,
    bool RememberRecipientAddress);

public sealed record InvoicingDeliveryResult(
    long AttemptId,
    int AttemptNumber,
    string Status,
    string Message,
    int? ArchivedPdfAttachmentId);

public sealed class InvoicingDeliveryValidationException(IReadOnlyList<string> errors)
    : InvalidOperationException(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
