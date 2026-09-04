using Microsoft.Data.SqlClient;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;

namespace MyCoinFlow.WinUI.Services;

public sealed class InvoicingDmsService
{
    public const string DocumentEntityType = "FakturierungDokument";
    private static readonly SemaphoreSlim ArchiveGate = new(1, 1);
    private readonly AttachmentService _attachmentService;
    private readonly bool? _enabledOverride;

    public InvoicingDmsService(
        AttachmentService? attachmentService = null,
        bool? enabledOverride = null)
    {
        _attachmentService = attachmentService ?? new AttachmentService();
        _enabledOverride = enabledOverride;
    }

    public bool IsEnabled => _enabledOverride ?? AppModules.IsDmsEnabled;

    public async Task<IReadOnlyList<InvoicingDmsAttachment>> LoadDocumentAttachmentsAsync(
        int documentId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || documentId <= 0) return [];
        await EnsureDmsAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        const string sql = """
SELECT Id, COALESCE(Titel, N''), COALESCE(Kategorie, N''),
       COALESCE(OriginalName, FileName), FileName, FolderRel,
       COALESCE(SizeBytes, 0), COALESCE(InhaltHash, N''), ImportedAtUtc
FROM dbo.Attachment
WHERE EntityType = @entityType AND EntityId = @entityId
ORDER BY ImportedAtUtc DESC, Id DESC;
""";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@entityType", DocumentEntityType);
        command.Parameters.AddWithValue("@entityId", documentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var (root, _) = new DatabaseService().GetAttachmentSettings();
        var result = new List<InvoicingDmsAttachment>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var folder = reader.GetString(5);
            var fileName = reader.GetString(4);
            result.Add(new InvoicingDmsAttachment(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                fileName,
                folder,
                reader.GetInt64(6),
                reader.GetString(7),
                reader.GetDateTime(8),
                ResolveSafePath(root, folder, fileName)));
        }
        return result;
    }

    public async Task<InvoicingDmsAttachment> AddDocumentAttachmentAsync(
        int documentId,
        string sourcePath,
        string title,
        DateOnly documentDate,
        CancellationToken cancellationToken = default)
    {
        RequireEnabled();
        if (documentId <= 0)
            throw new ArgumentException("Die Dokument-ID ist ungültig.", nameof(documentId));
        await EnsureDmsAsync(cancellationToken);
        var stored = await Task.Run(
            () => _attachmentService.AttachEntityCopy(
                sourcePath,
                DocumentEntityType,
                documentId,
                title,
                "Fakturierung · Beilage",
                documentDate.ToDateTime(TimeOnly.MinValue)),
            cancellationToken);
        return (await LoadDocumentAttachmentsAsync(documentId, cancellationToken))
            .Single(value => value.Id == stored.AttachmentId);
    }

    public async Task<InvoicingDmsAttachment> ArchiveGeneratedPdfAsync(
        int documentId,
        string documentTitle,
        DateOnly documentDate,
        InvoicingPdfArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        RequireEnabled();
        await ArchiveGate.WaitAsync(cancellationToken);
        try
        {
            var existing = (await LoadDocumentAttachmentsAsync(documentId, cancellationToken))
                .FirstOrDefault(value => value.ContentHash.Equals(
                    artifact.Sha256,
                    StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;

            var directory = Path.Combine(Path.GetTempPath(), "MyCoinFlow", "DmsArchive");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}.pdf");
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, artifact.Content, cancellationToken);
                var stored = await Task.Run(
                    () => _attachmentService.AttachEntityCopy(
                        temporaryPath,
                        DocumentEntityType,
                        documentId,
                        documentTitle,
                        "Fakturierung · PDF-Ausgabe",
                        documentDate.ToDateTime(TimeOnly.MinValue)),
                    cancellationToken);
                return (await LoadDocumentAttachmentsAsync(documentId, cancellationToken))
                    .Single(value => value.Id == stored.AttachmentId);
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            }
        }
        finally
        {
            ArchiveGate.Release();
        }
    }

    public async Task<IReadOnlyList<InvoicingDmsAttachment>> GetSelectedAttachmentsAsync(
        int documentId,
        IReadOnlyCollection<int> selectedIds,
        CancellationToken cancellationToken = default)
    {
        if (selectedIds.Count == 0) return [];
        RequireEnabled();
        var selected = selectedIds.ToHashSet();
        var available = await LoadDocumentAttachmentsAsync(documentId, cancellationToken);
        var missing = selected.Except(available.Select(value => value.Id)).ToArray();
        if (missing.Length > 0)
            throw new InvoicingDeliveryValidationException(
                ["Mindestens eine gewählte DMS-Beilage gehört nicht mehr zum Dokument."]);
        var rows = available.Where(value => selected.Contains(value.Id)).ToList();
        if (rows.Any(value => !value.FileExists))
            throw new InvoicingDeliveryValidationException(
                ["Mindestens eine gewählte DMS-Beilage fehlt im Ablageordner."]);
        return rows;
    }

    private static async Task EnsureDmsAsync(CancellationToken cancellationToken) =>
        await Task.Run(() => new DatabaseService().EnsureAttachmentsSchema(), cancellationToken);

    private void RequireEnabled()
    {
        if (!IsEnabled)
            throw new InvalidOperationException(
                "Das DMS-Modul ist nicht freigeschaltet. Die E-Mail kann weiterhin ohne DMS-Beilagen versendet werden.");
    }

    private static string ResolveSafePath(string root, string folder, string fileName)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, folder, fileName));
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ein DMS-Dateipfad liegt außerhalb des konfigurierten Ablageordners.");
        return fullPath;
    }
}
