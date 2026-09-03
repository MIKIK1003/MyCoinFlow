using Microsoft.Data.SqlClient;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using System.Data;

namespace MyCoinFlow.WinUI.Data;

public sealed class InvoicingOutputRepository
{
    private readonly InvoicingDocumentRepository _documentRepository;
    private readonly InvoicingInvoiceRepository _invoiceRepository;

    public InvoicingOutputRepository(
        InvoicingDocumentRepository? documentRepository = null,
        InvoicingInvoiceRepository? invoiceRepository = null)
    {
        _documentRepository = documentRepository ?? new InvoicingDocumentRepository();
        _invoiceRepository = invoiceRepository ?? new InvoicingInvoiceRepository();
    }

    public async Task<InvoicingOutputWorkspace> LoadWorkspaceAsync(
        int documentId,
        CancellationToken cancellationToken = default)
    {
        if (documentId <= 0)
            throw Validation("Ein gültiges Dokument ist erforderlich.");

        InvoicingSchema.RequireAuthenticated();
        await InvoicingSchema.EnsureAsync(cancellationToken);
        var documents = await _documentRepository.LoadDocumentsAsync(cancellationToken);
        var enriched = await _invoiceRepository.EnrichDocumentsAsync(
            documents.Documents,
            cancellationToken);
        var document = enriched.FirstOrDefault(value => value.Id == documentId)
            ?? throw Validation("Das gewählte Fakturierungsdokument wurde nicht gefunden.");

        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        var snapshot = await LoadSnapshotAsync(connection, null, documentId, cancellationToken);
        var accounts = document.Status == InvoicingDocumentStatusCodes.Definitive &&
                       document.Financial?.IsPositiveInvoice == true &&
                       snapshot is null
            ? await LoadPaymentAccountsAsync(connection, document.CurrencyCode, cancellationToken)
            : [];
        return new InvoicingOutputWorkspace(document, snapshot, accounts);
    }

    public async Task<InvoicingOutputSnapshot> CreateSnapshotAsync(
        int documentId,
        int paymentAccountId,
        CancellationToken cancellationToken = default)
    {
        if (documentId <= 0 || paymentAccountId <= 0)
            throw Validation("Dokument und Zahlungskonto sind erforderlich.");

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await CreateSnapshotCoreAsync(
                    documentId,
                    paymentAccountId,
                    cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 1205 && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
            }
        }
    }

    private static async Task<InvoicingOutputSnapshot> CreateSnapshotCoreAsync(
        int documentId,
        int paymentAccountId,
        CancellationToken cancellationToken)
    {
        InvoicingSchema.RequireAuthenticated();
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            await AcquireSnapshotLockAsync(
                connection,
                transaction,
                documentId,
                cancellationToken);
            var existing = await LoadSnapshotAsync(
                connection,
                transaction,
                documentId,
                cancellationToken,
                forUpdate: true);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }

            var document = await LoadDocumentForOutputAsync(
                connection,
                transaction,
                documentId,
                cancellationToken);
            var account = await LoadPaymentAccountForOutputAsync(
                connection,
                transaction,
                paymentAccountId,
                cancellationToken);
            var snapshot = BuildSnapshot(document, account);
            await InsertSnapshotAsync(connection, transaction, snapshot, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return snapshot;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task AcquireSnapshotLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock
    @Resource = @resource,
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 15000;
IF @lockResult < 0
    THROW 51025, N'Der Ausgabesnapshot wird gerade durch einen anderen Vorgang erstellt. Bitte erneut versuchen.', 1;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = $"MyCoinFlow:FakturierungAusgabe:{documentId}"
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static InvoicingOutputSnapshot BuildSnapshot(
        OutputDocumentSnapshot document,
        InvoicingPaymentAccountOption account)
    {
        if (document.Status != InvoicingDocumentStatusCodes.Definitive ||
            !InvoicingInvoiceKindCodes.IsPositive(document.InvoiceKind) ||
            document.GrossAmount <= 0m)
        {
            throw Validation("Nur definitive positive Rechnungen erhalten einen Zahlungsausgabesnapshot.");
        }
        if (!account.CurrencyCode.Equals(document.CurrencyCode, StringComparison.Ordinal))
            throw Validation("Das Zahlungskonto muss dieselbe Währung wie die Rechnung führen.");
        if (!FinanceSettingsValidator.IsValidIban(account.Iban))
            throw Validation("Das gewählte Zahlungskonto besitzt keine gültige IBAN.");

        var supportsSwissQr = account.SupportsSwissQr;
        if (account.IsQrIban && !supportsSwissQr)
            throw Validation("Eine QR-IBAN kann nur für einen Swiss-QR-Zahlteil in CHF oder EUR verwendet werden.");
        if (!supportsSwissQr && string.IsNullOrWhiteSpace(account.Bic))
            throw Validation("Alternative Zahlungsangaben benötigen beim gewählten Zahlungskonto einen BIC / SWIFT.");

        var referenceType = account.IsQrIban
            ? InvoicingPaymentReferenceTypes.Qrr
            : InvoicingPaymentReferenceTypes.Scor;
        var reference = account.IsQrIban
            ? SwissQrReferenceBuilder.CreateQrReference(document.Id)
            : SwissQrReferenceBuilder.CreateCreditorReference(document.Id);
        var outputKind = supportsSwissQr
            ? InvoicingOutputKinds.SwissQr
            : InvoicingOutputKinds.Alternative;
        var now = DateTime.Now;
        var createdAt = new DateTime(
            now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond),
            now.Kind);
        var qrPayload = string.Empty;
        if (supportsSwissQr)
        {
            var creditor = SwissQrParty.Create(
                document.IssuerName,
                document.IssuerStreet,
                document.IssuerPostalCode,
                document.IssuerCity,
                document.IssuerCountryCode);
            var debtor = SwissQrParty.Create(
                document.RecipientName,
                document.RecipientStreet,
                document.RecipientPostalCode,
                document.RecipientCity,
                document.RecipientCountry);
            qrPayload = SwissQrPayloadBuilder.Create(
                account.Iban,
                creditor,
                debtor,
                document.GrossAmount,
                document.CurrencyCode,
                referenceType,
                reference,
                document.DocumentNumber);
        }

        return new InvoicingOutputSnapshot(
            document.Id,
            account.Id,
            InvoicingOutputTemplateVersions.Current,
            outputKind,
            account.DisplayName,
            FinanceSettingsValidator.NormalizeIban(account.Iban),
            account.Bic.Trim().ToUpperInvariant(),
            account.AccountNumber.Trim(),
            account.CurrencyCode,
            account.IsQrIban,
            referenceType,
            reference,
            qrPayload,
            createdAt,
            CurrentUserContext.Username);
    }

    private static async Task<IReadOnlyList<InvoicingPaymentAccountOption>> LoadPaymentAccountsAsync(
        SqlConnection connection,
        string currencyCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT p.Id,
       p.DisplayName,
       p.Iban,
       COALESCE(g.BIC, N''),
       COALESCE(g.KontoNummer, N''),
       p.CurrencyCode
FROM dbo.FakturierungZahlungskonto p
LEFT JOIN dbo.Geldinstitut g ON g.Id = p.GeldinstitutId
WHERE p.IsActive = 1
  AND p.CurrencyCode = @currency
ORDER BY p.IsQrIban DESC, p.Id;
""";
        await using var command = new SqlCommand(sql, connection);
        AddText(command, "@currency", SqlDbType.Char, 3, currencyCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingPaymentAccountOption>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var iban = FinanceSettingsValidator.NormalizeIban(reader.GetString(2));
            result.Add(new InvoicingPaymentAccountOption(
                reader.GetInt32(0),
                reader.GetString(1).Trim(),
                iban,
                reader.GetString(3).Trim(),
                reader.GetString(4).Trim(),
                reader.GetString(5).Trim(),
                FinanceSettingsValidator.IsSwissQrIban(iban)));
        }
        return result;
    }

    private static async Task<InvoicingPaymentAccountOption> LoadPaymentAccountForOutputAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int paymentAccountId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT p.Id,
       p.DisplayName,
       p.Iban,
       COALESCE(g.BIC, N''),
       COALESCE(g.KontoNummer, N''),
       p.CurrencyCode,
       p.IsActive
FROM dbo.FakturierungZahlungskonto p WITH (UPDLOCK, HOLDLOCK)
LEFT JOIN dbo.Geldinstitut g ON g.Id = p.GeldinstitutId
WHERE p.Id = @id;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = paymentAccountId });
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow,
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || !reader.GetBoolean(6))
            throw Validation("Das gewählte Zahlungskonto ist nicht vorhanden oder nicht aktiv.");

        var iban = FinanceSettingsValidator.NormalizeIban(reader.GetString(2));
        return new InvoicingPaymentAccountOption(
            reader.GetInt32(0),
            reader.GetString(1).Trim(),
            iban,
            reader.GetString(3).Trim(),
            reader.GetString(4).Trim(),
            reader.GetString(5).Trim(),
            FinanceSettingsValidator.IsSwissQrIban(iban));
    }

    private static async Task<OutputDocumentSnapshot> LoadDocumentForOutputAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT document.Id, document.DocumentNumber, document.[Status], document.CurrencyCode,
       document.IssuerName, document.IssuerStreet, document.IssuerPostalCode,
       document.IssuerCity, document.IssuerCountryCode,
       document.RecipientName, document.RecipientStreet, document.RecipientPostalCode,
       document.RecipientCity, document.RecipientCountry,
       invoice.InvoiceKind, invoice.GrossAmount
FROM dbo.FakturierungDokument document WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.FakturierungRechnung invoice WITH (UPDLOCK, HOLDLOCK)
  ON invoice.DocumentId = document.Id
WHERE document.Id = @id;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = documentId });
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow,
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw Validation("Die definitive Rechnung wurde nicht gefunden.");
        return new OutputDocumentSnapshot(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetDecimal(15));
    }

    private static async Task<InvoicingOutputSnapshot?> LoadSnapshotAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int documentId,
        CancellationToken cancellationToken,
        bool forUpdate = false)
    {
        var lockHint = forUpdate ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        var sql = $"""
SELECT DocumentId, PaymentAccountId, TemplateVersion, OutputKind,
       PaymentAccountName, Iban, COALESCE(Bic, N''), COALESCE(AccountNumber, N''),
       CurrencyCode, IsQrIban, ReferenceType, PaymentReference,
       COALESCE(QrPayload, N''), CreatedAt, CreatedBy
FROM dbo.FakturierungDokumentAusgabe{lockHint}
WHERE DocumentId = @id;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = documentId });
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow,
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return new InvoicingOutputSnapshot(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt16(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetBoolean(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetDateTime(13),
            reader.GetString(14));
    }

    private static async Task InsertSnapshotAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        InvoicingOutputSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT dbo.FakturierungDokumentAusgabe
(
    DocumentId, PaymentAccountId, TemplateVersion, OutputKind, PaymentAccountName,
    Iban, Bic, AccountNumber, CurrencyCode, IsQrIban, ReferenceType,
    PaymentReference, QrPayload, CreatedAt, CreatedBy
)
VALUES
(
    @documentId, @accountId, @templateVersion, @outputKind, @accountName,
    @iban, @bic, @accountNumber, @currency, @isQrIban, @referenceType,
    @reference, @payload, @createdAt, @createdBy
);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@documentId", SqlDbType.Int)
        {
            Value = snapshot.DocumentId
        });
        command.Parameters.Add(new SqlParameter("@accountId", SqlDbType.Int)
        {
            Value = snapshot.PaymentAccountId
        });
        command.Parameters.Add(new SqlParameter("@templateVersion", SqlDbType.SmallInt)
        {
            Value = snapshot.TemplateVersion
        });
        AddText(command, "@outputKind", SqlDbType.VarChar, 16, snapshot.OutputKind);
        AddText(command, "@accountName", SqlDbType.NVarChar, 120, snapshot.PaymentAccountName);
        AddText(command, "@iban", SqlDbType.VarChar, 34, snapshot.Iban);
        AddNullableText(command, "@bic", SqlDbType.NVarChar, 16, snapshot.Bic);
        AddNullableText(command, "@accountNumber", SqlDbType.NVarChar, 80, snapshot.AccountNumber);
        AddText(command, "@currency", SqlDbType.Char, 3, snapshot.CurrencyCode);
        command.Parameters.Add(new SqlParameter("@isQrIban", SqlDbType.Bit)
        {
            Value = snapshot.IsQrIban
        });
        AddText(command, "@referenceType", SqlDbType.VarChar, 4, snapshot.ReferenceType);
        AddText(command, "@reference", SqlDbType.NVarChar, 80, snapshot.PaymentReference);
        AddNullableText(command, "@payload", SqlDbType.NVarChar, 997, snapshot.QrPayload);
        command.Parameters.Add(new SqlParameter("@createdAt", SqlDbType.DateTime2)
        {
            Value = snapshot.CreatedAt
        });
        AddText(command, "@createdBy", SqlDbType.NVarChar, 64, snapshot.CreatedBy);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Der Ausgabesnapshot konnte nicht gespeichert werden.");
    }

    private static InvoicingOutputValidationException Validation(string message) => new([message]);

    private static void AddText(
        SqlCommand command,
        string name,
        SqlDbType type,
        int length,
        string value) =>
        command.Parameters.Add(new SqlParameter(name, type, length) { Value = value.Trim() });

    private static void AddNullableText(
        SqlCommand command,
        string name,
        SqlDbType type,
        int length,
        string? value) =>
        command.Parameters.Add(new SqlParameter(name, type, length)
        {
            Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim()
        });

    private sealed record OutputDocumentSnapshot(
        int Id,
        string DocumentNumber,
        string Status,
        string CurrencyCode,
        string IssuerName,
        string IssuerStreet,
        string IssuerPostalCode,
        string IssuerCity,
        string IssuerCountryCode,
        string RecipientName,
        string RecipientStreet,
        string RecipientPostalCode,
        string RecipientCity,
        string RecipientCountry,
        string InvoiceKind,
        decimal GrossAmount);
}
