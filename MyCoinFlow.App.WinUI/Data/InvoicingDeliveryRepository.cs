using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;

namespace MyCoinFlow.WinUI.Data;

public sealed class InvoicingDeliveryRepository
{
    public async Task<(InvoicingSmtpConfiguration Smtp, string SuggestedRecipient)> LoadConfigurationAsync(
        int documentId,
        CancellationToken cancellationToken = default)
    {
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        const string sql = """
SELECT settings.SmtpHost, settings.SmtpPort, settings.SmtpUseTls,
       settings.SmtpUserName, settings.SmtpFromAddress,
       COALESCE(profile.InvoiceEmail, N'')
FROM dbo.FakturierungDokument document
CROSS JOIN dbo.FakturierungEinstellung settings
LEFT JOIN dbo.FakturierungEmpfaengerProfil profile
  ON profile.AddressId = document.RecipientAddressIdSnapshot
WHERE document.Id = @documentId AND settings.Id = 1;
""";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@documentId", SqlDbType.Int) { Value = documentId });
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Das Fakturierungsdokument wurde nicht gefunden.");
        return (
            new InvoicingSmtpConfiguration(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetBoolean(2),
                reader.GetString(3),
                reader.GetString(4)),
            reader.GetString(5));
    }

    public async Task SaveRecipientAddressAsync(
        int documentId,
        string recipientAddress,
        CancellationToken cancellationToken = default)
    {
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        const string sql = """
MERGE dbo.FakturierungEmpfaengerProfil WITH (HOLDLOCK) AS target
USING
(
    SELECT RecipientAddressIdSnapshot AS AddressId
    FROM dbo.FakturierungDokument
    WHERE Id = @documentId
) AS source
ON target.AddressId = source.AddressId
WHEN MATCHED THEN
    UPDATE SET InvoiceEmail = @email, UpdatedAt = SYSDATETIME(), UpdatedBy = @user
WHEN NOT MATCHED THEN
    INSERT (AddressId, InvoiceEmail, UpdatedBy)
    VALUES (source.AddressId, @email, @user);
""";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@documentId", SqlDbType.Int) { Value = documentId });
        AddText(command, "@email", SqlDbType.NVarChar, 256, recipientAddress);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Die Rechnungs-E-Mail konnte nicht beim Empfängerprofil gespeichert werden.");
    }

    public async Task<IReadOnlyList<InvoicingDeliveryAttempt>> LoadAttemptsAsync(
        int documentId,
        CancellationToken cancellationToken = default)
    {
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        const string sql = """
SELECT attempt.Id, attempt.AttemptNumber,
       COALESCE(latest.[Status], 'STARTED'), attempt.RecipientAddress,
       attempt.[Subject], attempt.PdfSha256,
       (SELECT COUNT(*) FROM dbo.FakturierungVersandanhang attachment
        WHERE attachment.DeliveryAttemptId = attempt.Id),
       attempt.CreatedAt, attempt.CreatedBy,
       COALESCE(latest.OccurredAt, attempt.CreatedAt),
       COALESCE(latest.Narrative, N'Versand wurde vorbereitet.')
FROM dbo.FakturierungVersandversuch attempt
OUTER APPLY
(
    SELECT TOP (1) event.[Status], event.OccurredAt, event.Narrative
    FROM dbo.FakturierungVersandereignis event
    WHERE event.DeliveryAttemptId = attempt.Id
    ORDER BY event.SequenceNumber DESC
) latest
WHERE attempt.DocumentId = @documentId
ORDER BY attempt.AttemptNumber DESC;
""";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@documentId", SqlDbType.Int) { Value = documentId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingDeliveryAttempt>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new InvoicingDeliveryAttempt(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetDateTime(7),
                reader.GetString(8),
                reader.GetDateTime(9),
                reader.GetString(10)));
        }
        return result;
    }

    public async Task<(long AttemptId, int AttemptNumber)> CreateAttemptAsync(
        int documentId,
        InvoicingSmtpConfiguration smtp,
        InvoicingPreparedEmail email,
        string pdfSha256,
        CancellationToken cancellationToken = default)
    {
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var lockResult = await AcquireDocumentLockAsync(
                connection, transaction, documentId, cancellationToken);
            if (lockResult < 0)
                throw new InvalidOperationException("Der Versand wird bereits durch einen anderen Vorgang vorbereitet.");

            await EnsureDocumentExistsAsync(connection, transaction, documentId, cancellationToken);
            var attemptNumber = await GetNextAttemptNumberAsync(
                connection, transaction, documentId, cancellationToken);
            const string insertSql = """
INSERT dbo.FakturierungVersandversuch
(
    DocumentId, AttemptNumber, MessageId, RecipientAddress, FromAddress,
    [Subject], Body, BodySha256, PdfSha256,
    SmtpHostSnapshot, SmtpPortSnapshot, SmtpUseTlsSnapshot, SmtpUserSnapshot,
    CreatedBy
)
OUTPUT INSERTED.Id
VALUES
(
    @documentId, @attemptNumber, @messageId, @recipient, @fromAddress,
    @subject, @body, @bodyHash, @pdfHash,
    @smtpHost, @smtpPort, @smtpTls, @smtpUser,
    @user
);
""";
            await using var insert = new SqlCommand(insertSql, connection, transaction);
            insert.Parameters.Add(new SqlParameter("@documentId", SqlDbType.Int) { Value = documentId });
            insert.Parameters.Add(new SqlParameter("@attemptNumber", SqlDbType.Int) { Value = attemptNumber });
            AddText(insert, "@messageId", SqlDbType.NVarChar, 200, email.MessageId);
            AddText(insert, "@recipient", SqlDbType.NVarChar, 256, email.RecipientAddress);
            AddText(insert, "@fromAddress", SqlDbType.NVarChar, 256, smtp.FromAddress);
            AddText(insert, "@subject", SqlDbType.NVarChar, 300, email.Subject);
            insert.Parameters.Add(new SqlParameter("@body", SqlDbType.NVarChar, -1) { Value = email.Body });
            AddText(insert, "@bodyHash", SqlDbType.Char, 64, Sha256(email.Body));
            AddText(insert, "@pdfHash", SqlDbType.Char, 64, pdfSha256);
            AddText(insert, "@smtpHost", SqlDbType.NVarChar, 256, smtp.Host);
            insert.Parameters.Add(new SqlParameter("@smtpPort", SqlDbType.Int) { Value = smtp.Port });
            insert.Parameters.Add(new SqlParameter("@smtpTls", SqlDbType.Bit) { Value = smtp.UseTls });
            AddText(insert, "@smtpUser", SqlDbType.NVarChar, 256, smtp.UserName);
            AddText(insert, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
            var attemptId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));

            const string attachmentSql = """
INSERT dbo.FakturierungVersandanhang
    (DeliveryAttemptId, SequenceNumber, DmsAttachmentId, FileName, ContentType,
     ContentSha256, SizeBytes, IsDocumentPdf)
VALUES
    (@attemptId, @sequence, @dmsId, @fileName, @contentType,
     @contentHash, @size, @isDocumentPdf);
""";
            var sequence = 0;
            foreach (var attachment in email.Attachments)
            {
                sequence += 10;
                await using var attachmentCommand = new SqlCommand(attachmentSql, connection, transaction);
                attachmentCommand.Parameters.Add(new SqlParameter("@attemptId", SqlDbType.BigInt) { Value = attemptId });
                attachmentCommand.Parameters.Add(new SqlParameter("@sequence", SqlDbType.Int) { Value = sequence });
                attachmentCommand.Parameters.Add(new SqlParameter("@dmsId", SqlDbType.Int)
                    { Value = (object?)attachment.DmsAttachmentId ?? DBNull.Value });
                AddText(attachmentCommand, "@fileName", SqlDbType.NVarChar, 260, attachment.FileName);
                AddText(attachmentCommand, "@contentType", SqlDbType.NVarChar, 120, attachment.ContentType);
                AddText(attachmentCommand, "@contentHash", SqlDbType.Char, 64, attachment.ContentSha256);
                attachmentCommand.Parameters.Add(new SqlParameter("@size", SqlDbType.BigInt)
                    { Value = attachment.Content.LongLength });
                attachmentCommand.Parameters.Add(new SqlParameter("@isDocumentPdf", SqlDbType.Bit)
                    { Value = attachment.IsDocumentPdf });
                await attachmentCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await InsertEventAsync(
                connection,
                transaction,
                attemptId,
                10,
                InvoicingDeliveryStatuses.Started,
                "Der SMTP-Versand wurde gestartet; das Ergebnis ist bis zum Folgeereignis ungeklärt.",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (attemptId, attemptNumber);
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    public async Task CompleteAttemptAsync(
        long attemptId,
        bool sent,
        string narrative,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string attemptSql = """
SELECT attempt.DocumentId, attempt.AttemptNumber, attempt.RecipientAddress,
       attempt.PdfSha256, document.CurrencyCode,
       COALESCE(invoice.GrossAmount,
           (SELECT SUM(position.Quantity * position.UnitPrice)
            FROM dbo.FakturierungDokumentPosition position
            WHERE position.DocumentId = document.Id), 0)
FROM dbo.FakturierungVersandversuch attempt WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.FakturierungDokument document ON document.Id = attempt.DocumentId
LEFT JOIN dbo.FakturierungRechnung invoice ON invoice.DocumentId = document.Id
WHERE attempt.Id = @attemptId;
""";
            await using var attemptCommand = new SqlCommand(attemptSql, connection, transaction);
            attemptCommand.Parameters.Add(new SqlParameter("@attemptId", SqlDbType.BigInt) { Value = attemptId });
            await using var reader = await attemptCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Der Versandversuch wurde nicht gefunden.");
            var documentId = reader.GetInt32(0);
            var attemptNumber = reader.GetInt32(1);
            var recipient = reader.GetString(2);
            var pdfHash = reader.GetString(3);
            var currency = reader.GetString(4).Trim();
            var amount = reader.GetDecimal(5);
            await reader.DisposeAsync();

            const string finalExistsSql = """
SELECT COUNT(*) FROM dbo.FakturierungVersandereignis
WHERE DeliveryAttemptId = @attemptId AND [Status] IN ('SENT', 'FAILED');
""";
            await using var finalExists = new SqlCommand(finalExistsSql, connection, transaction);
            finalExists.Parameters.Add(new SqlParameter("@attemptId", SqlDbType.BigInt) { Value = attemptId });
            if (Convert.ToInt32(await finalExists.ExecuteScalarAsync(cancellationToken)) != 0)
                throw new InvalidOperationException("Der Versandversuch besitzt bereits ein endgültiges Ergebnis.");

            await InsertEventAsync(
                connection,
                transaction,
                attemptId,
                20,
                sent ? InvoicingDeliveryStatuses.Sent : InvoicingDeliveryStatuses.Failed,
                Limit(narrative, 2000),
                cancellationToken);

            if (sent)
            {
                const string revisionSql = """
INSERT dbo.FakturierungRevisionsereignis
    (DocumentId, SequenceNumber, EventType, ReferenceDocumentId,
     Amount, CurrencyCode, Narrative, EventBy)
SELECT @documentId,
       COALESCE(MAX(SequenceNumber), 0) + 10,
       'EMAIL_SENT', NULL, @amount, @currency, @narrative, @user
FROM dbo.FakturierungRevisionsereignis WITH (UPDLOCK, HOLDLOCK)
WHERE DocumentId = @documentId;
""";
                await using var revision = new SqlCommand(revisionSql, connection, transaction);
                revision.Parameters.Add(new SqlParameter("@documentId", SqlDbType.Int) { Value = documentId });
                revision.Parameters.Add(new SqlParameter("@amount", SqlDbType.Decimal)
                    { Precision = 19, Scale = 2, Value = amount });
                AddText(revision, "@currency", SqlDbType.Char, 3, currency);
                AddText(
                    revision,
                    "@narrative",
                    SqlDbType.NVarChar,
                    1000,
                    $"E-Mail an {recipient} versendet · Versuch {attemptNumber} · PDF {pdfHash[..12]}…");
                AddText(revision, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
                await revision.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    private static async Task<int> AcquireDocumentLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
DECLARE @result int;
EXEC @result = sys.sp_getapplock
    @Resource = @resource,
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 15000;
SELECT @result;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255)
            { Value = $"MyCoinFlow:FakturierungVersand:{documentId}" });
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task EnsureDocumentExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.FakturierungDokument WITH (UPDLOCK, HOLDLOCK) WHERE Id=@id;",
            connection,
            transaction);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = documentId });
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException("Das Fakturierungsdokument wurde nicht gefunden.");
    }

    private static async Task<int> GetNextAttemptNumberAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT COALESCE(MAX(AttemptNumber), 0) + 1 FROM dbo.FakturierungVersandversuch WITH (UPDLOCK, HOLDLOCK) WHERE DocumentId=@id;",
            connection,
            transaction);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = documentId });
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertEventAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long attemptId,
        int sequenceNumber,
        string status,
        string narrative,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT dbo.FakturierungVersandereignis
    (DeliveryAttemptId, SequenceNumber, [Status], Narrative, OccurredBy)
VALUES (@attemptId, @sequence, @status, @narrative, @user);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@attemptId", SqlDbType.BigInt) { Value = attemptId });
        command.Parameters.Add(new SqlParameter("@sequence", SqlDbType.Int) { Value = sequenceNumber });
        AddText(command, "@status", SqlDbType.VarChar, 16, status);
        AddText(command, "@narrative", SqlDbType.NVarChar, 2000, narrative);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static void AddText(
        SqlCommand command,
        string name,
        SqlDbType type,
        int size,
        string value) =>
        command.Parameters.Add(new SqlParameter(name, type, size) { Value = value });
}
