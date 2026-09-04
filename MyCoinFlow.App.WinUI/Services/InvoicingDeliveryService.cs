using System.Net.Mail;
using System.Security.Cryptography;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;

namespace MyCoinFlow.WinUI.Services;

public sealed class InvoicingDeliveryService
{
    private readonly InvoicingDeliveryRepository _repository;
    private readonly InvoicingOutputRepository _outputRepository;
    private readonly InvoicingDmsService _dms;
    private readonly IInvoicingEmailTransport _transport;
    private readonly IInvoicingSmtpCredentialStore _credentialStore;

    public InvoicingDeliveryService(
        InvoicingDeliveryRepository? repository = null,
        InvoicingOutputRepository? outputRepository = null,
        InvoicingDmsService? dms = null,
        IInvoicingEmailTransport? transport = null,
        IInvoicingSmtpCredentialStore? credentialStore = null)
    {
        _repository = repository ?? new InvoicingDeliveryRepository();
        _outputRepository = outputRepository ?? new InvoicingOutputRepository();
        _dms = dms ?? new InvoicingDmsService();
        _transport = transport ?? new InvoicingSmtpEmailTransport();
        _credentialStore = credentialStore ?? new InvoicingSmtpCredentialStore();
    }

    public async Task<InvoicingDeliveryWorkspace> LoadAsync(
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var output = await _outputRepository.LoadWorkspaceAsync(documentId, cancellationToken);
        var configuration = await _repository.LoadConfigurationAsync(documentId, cancellationToken);
        var attemptsTask = _repository.LoadAttemptsAsync(documentId, cancellationToken);
        var dmsTask = _dms.IsEnabled
            ? _dms.LoadDocumentAttachmentsAsync(documentId, cancellationToken)
            : Task.FromResult<IReadOnlyList<InvoicingDmsAttachment>>([]);
        await Task.WhenAll(attemptsTask, dmsTask);

        var pdfReady = !output.RequiresPaymentSnapshot || output.Snapshot is not null;
        var pdfStatus = pdfReady
            ? "Die PDF wird bytegleich aus dem gespeicherten AP07-Ausgabestand erzeugt."
            : "Für diese definitive Rechnung muss zuerst in Vorschau / PDF das Zahlungskonto festgelegt werden.";
        return new InvoicingDeliveryWorkspace(
            documentId,
            output.Document.Title,
            output.Document.RecipientName,
            configuration.SuggestedRecipient,
            BuildDefaultSubject(output.Document),
            BuildDefaultBody(output.Document),
            configuration.Smtp,
            _credentialStore.HasPassword(),
            _dms.IsEnabled,
            pdfReady,
            pdfStatus,
            await dmsTask,
            await attemptsTask);
    }

    public async Task<InvoicingDmsAttachment> ArchivePdfAsync(
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var (workspace, artifact) = await BuildArtifactAsync(documentId, cancellationToken);
        return await _dms.ArchiveGeneratedPdfAsync(
            documentId,
            workspace.Document.Title,
            workspace.Document.DocumentDate,
            artifact,
            cancellationToken);
    }

    public async Task<InvoicingDmsAttachment> AddDmsAttachmentAsync(
        int documentId,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var output = await _outputRepository.LoadWorkspaceAsync(documentId, cancellationToken);
        return await _dms.AddDocumentAttachmentAsync(
            documentId,
            sourcePath,
            $"Beilage zu {output.Document.Title}",
            output.Document.DocumentDate,
            cancellationToken);
    }

    public async Task<InvoicingDeliveryResult> SendAsync(
        InvoicingDeliveryDraft draft,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateAndNormalize(draft);
        if (errors.Count > 0)
            throw new InvoicingDeliveryValidationException(errors);

        var configuration = await _repository.LoadConfigurationAsync(
            draft.DocumentId,
            cancellationToken);
        if (!configuration.Smtp.IsConfigured)
            throw new InvoicingDeliveryValidationException(
                ["SMTP ist unter Finanzen noch nicht vollständig eingerichtet."]);
        var password = _credentialStore.GetPassword();
        if (!string.IsNullOrWhiteSpace(configuration.Smtp.UserName) && string.IsNullOrEmpty(password))
        {
            throw new InvoicingDeliveryValidationException(
                ["Für den SMTP-Benutzer fehlt das lokal geschützte Kennwort."]);
        }
        if (draft.RememberRecipientAddress)
        {
            await _repository.SaveRecipientAddressAsync(
                draft.DocumentId,
                draft.RecipientAddress.Trim(),
                cancellationToken);
        }

        var (output, artifact) = await BuildArtifactAsync(draft.DocumentId, cancellationToken);
        InvoicingDmsAttachment? archivedPdf = null;
        if (_dms.IsEnabled)
        {
            archivedPdf = await _dms.ArchiveGeneratedPdfAsync(
                draft.DocumentId,
                output.Document.Title,
                output.Document.DocumentDate,
                artifact,
                cancellationToken);
        }

        var selectedDms = await _dms.GetSelectedAttachmentsAsync(
            draft.DocumentId,
            draft.SelectedDmsAttachmentIds,
            cancellationToken);
        var attachments = new List<InvoicingEmailAttachmentData>
        {
            new(
                artifact.SuggestedFileName,
                "application/pdf",
                artifact.Content,
                artifact.Sha256,
                archivedPdf?.Id,
                true)
        };
        foreach (var row in selectedDms)
        {
            if (row.ContentHash.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                continue;
            var content = await File.ReadAllBytesAsync(row.FullPath, cancellationToken);
            var hash = Convert.ToHexString(SHA256.HashData(content));
            if (!string.IsNullOrWhiteSpace(row.ContentHash) &&
                !row.ContentHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvoicingDeliveryValidationException(
                    [$"Die DMS-Beilage „{row.DisplayTitle}“ wurde außerhalb des DMS verändert. Bitte zuerst als neue DMS-Version übernehmen."]);
            }
            if (attachments.Any(value => value.ContentSha256.Equals(hash, StringComparison.OrdinalIgnoreCase)))
                continue;
            attachments.Add(new InvoicingEmailAttachmentData(
                Path.GetFileName(row.OriginalName),
                ContentType(row.OriginalName),
                content,
                hash,
                row.Id,
                false));
        }

        var prepared = new InvoicingPreparedEmail(
            $"MCF-{Guid.NewGuid():N}",
            draft.RecipientAddress.Trim(),
            draft.Subject.Trim(),
            draft.Body.Trim(),
            attachments);
        var attempt = await _repository.CreateAttemptAsync(
            draft.DocumentId,
            configuration.Smtp,
            prepared,
            artifact.Sha256,
            cancellationToken);

        try
        {
            await _transport.SendAsync(configuration.Smtp, password, prepared, cancellationToken);
        }
        catch (Exception exception)
        {
            var failure = $"SMTP-Versand fehlgeschlagen: {SafeFailure(exception)}";
            try
            {
                await _repository.CompleteAttemptAsync(
                    attempt.AttemptId,
                    sent: false,
                    failure,
                    CancellationToken.None);
            }
            catch (Exception logException)
            {
                throw new InvalidOperationException(
                    $"Versuch {attempt.AttemptNumber} ist fehlgeschlagen; auch der Fehlernachweis konnte nicht abgeschlossen werden. " +
                    $"Der gestartete Versuch bleibt sichtbar. SMTP: {SafeFailure(exception)} · Nachweis: {SafeFailure(logException)}",
                    exception);
            }
            return new InvoicingDeliveryResult(
                attempt.AttemptId,
                attempt.AttemptNumber,
                InvoicingDeliveryStatuses.Failed,
                failure,
                archivedPdf?.Id);
        }

        try
        {
            await _repository.CompleteAttemptAsync(
                attempt.AttemptId,
                sent: true,
                $"SMTP hat die Nachricht an {draft.RecipientAddress} ohne Fehler angenommen.",
                CancellationToken.None);
            return new InvoicingDeliveryResult(
                attempt.AttemptId,
                attempt.AttemptNumber,
                InvoicingDeliveryStatuses.Sent,
                $"E-Mail an {draft.RecipientAddress} versendet.",
                archivedPdf?.Id);
        }
        catch (Exception exception)
        {
            return new InvoicingDeliveryResult(
                attempt.AttemptId,
                attempt.AttemptNumber,
                InvoicingDeliveryStatuses.Started,
                "SMTP hat die Nachricht angenommen, aber der Abschlussnachweis konnte nicht gespeichert werden. " +
                $"Versuch {attempt.AttemptNumber} bitte vor einer Wiederholung prüfen: {SafeFailure(exception)}",
                archivedPdf?.Id);
        }
    }

    private async Task<(InvoicingOutputWorkspace Workspace, InvoicingPdfArtifact Artifact)> BuildArtifactAsync(
        int documentId,
        CancellationToken cancellationToken)
    {
        var output = await _outputRepository.LoadWorkspaceAsync(documentId, cancellationToken);
        if (output.RequiresPaymentSnapshot && output.Snapshot is null)
        {
            throw new InvoicingDeliveryValidationException(
                ["Bitte zuerst in Vorschau / PDF das Zahlungskonto verbindlich festlegen."]);
        }
        var artifact = await Task.Run(
            () => InvoicingPdfDocumentBuilder.Build(output),
            cancellationToken);
        return (output, artifact);
    }

    private static List<string> ValidateAndNormalize(InvoicingDeliveryDraft draft)
    {
        var errors = new List<string>();
        if (draft.DocumentId <= 0)
            errors.Add("Ein Fakturierungsdokument ist erforderlich.");
        if (!IsEmailAddress(draft.RecipientAddress))
            errors.Add("Eine gültige Empfängeradresse ist erforderlich.");
        if (string.IsNullOrWhiteSpace(draft.Subject))
            errors.Add("Der E-Mail-Betreff ist erforderlich.");
        else if (draft.Subject.Length > 300)
            errors.Add("Der E-Mail-Betreff darf höchstens 300 Zeichen lang sein.");
        if (string.IsNullOrWhiteSpace(draft.Body))
            errors.Add("Der Nachrichtentext ist erforderlich.");
        return errors;
    }

    private static bool IsEmailAddress(string value)
    {
        try
        {
            var trimmed = value.Trim();
            return new MailAddress(trimmed).Address.Equals(trimmed, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string BuildDefaultSubject(InvoicingDocumentRecord document) =>
        $"{document.Title} – {document.IssuerName}";

    private static string BuildDefaultBody(InvoicingDocumentRecord document) =>
        $"Guten Tag {document.RecipientName}\r\n\r\n" +
        $"im Anhang erhalten Sie {document.Title}.\r\n\r\n" +
        $"Freundliche Grüsse\r\n{document.IssuerName}";

    private static string ContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => "application/octet-stream"
    };

    private static string SafeFailure(Exception exception)
    {
        var message = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length <= 1200 ? message : message[..1200];
    }
}
