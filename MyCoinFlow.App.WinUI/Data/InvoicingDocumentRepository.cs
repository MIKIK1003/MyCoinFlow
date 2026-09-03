using Microsoft.Data.SqlClient;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using System.Data;
using System.Globalization;

namespace MyCoinFlow.WinUI.Data;

public sealed class InvoicingDocumentRepository
{
    private readonly InvoicingMasterDataRepository _masterDataRepository;

    public InvoicingDocumentRepository(InvoicingMasterDataRepository? masterDataRepository = null)
    {
        _masterDataRepository = masterDataRepository ?? new InvoicingMasterDataRepository();
    }

    public async Task<InvoicingDocumentCreationWorkspace> LoadCreationWorkspaceAsync(
        DateOnly documentDate,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        await InvoicingSchema.EnsureAsync(cancellationToken);
        var objects = await _masterDataRepository.LoadBillableObjectsAsync(
            documentDate, cancellationToken: cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        var addresses = await LoadAddressesAsync(connection, cancellationToken);
        var (baseCurrency, currencies) = await LoadCurrenciesAsync(
            connection, documentDate, cancellationToken);
        return new InvoicingDocumentCreationWorkspace(
            documentDate,
            baseCurrency,
            objects.Objects.Where(item => item.IsSelectable).ToList(),
            addresses,
            currencies);
    }

    public async Task<InvoicingDocumentWorkspace> LoadDocumentsAsync(
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        var documents = await LoadHeadersAsync(connection, cancellationToken);
        var positions = await LoadPositionsAsync(connection, cancellationToken);
        var positionsByDocument = positions
            .GroupBy(position => position.DocumentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<InvoicingDocumentPositionRecord>)group.ToList());
        var flows = documents
            .GroupBy(document => document.FlowId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<InvoicingDocumentFlowStep>)group
                    .OrderBy(document => document.FlowSequence)
                    .Select(document => new InvoicingDocumentFlowStep(
                        document.Id,
                        document.FlowSequence,
                        document.DocumentType,
                        document.DocumentNumber,
                        document.Status,
                        document.DocumentDate))
                    .ToList());
        foreach (var document in documents)
        {
            document.Positions = positionsByDocument.GetValueOrDefault(document.Id) ?? [];
            document.Flow = flows.GetValueOrDefault(document.FlowId) ?? [];
        }
        return new InvoicingDocumentWorkspace(documents);
    }

    public async Task<int> CreateOfferAsync(
        InvoicingDocumentDraft draft,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        InvoicingDocumentValidator.ValidateAndNormalize(draft);
        await InvoicingSchema.EnsureAsync(cancellationToken);

        var documentDate = DateOnly.FromDateTime(draft.DocumentDate.Date);
        var objects = await _masterDataRepository.LoadBillableObjectsAsync(
            documentDate, cancellationToken: cancellationToken);
        var context = objects.Objects.FirstOrDefault(item =>
            item.SourceCode == draft.ContextSource &&
            item.SourceId == draft.ContextSourceId &&
            item.IsSelectable)
            ?? throw Validation("Der gewählte Objektkontext ist zum Dokumentdatum nicht mehr auswählbar.");
        ValidateRecipientChoice(context, draft);

        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
        try
        {
            await LockContextAsync(
                connection, transaction, draft.ContextSource, draft.ContextSourceId, cancellationToken);
            var recipient = await LoadRecipientAsync(
                connection, transaction, draft.RecipientAddressId, cancellationToken);
            var issuer = await LoadIssuerAsync(connection, transaction, cancellationToken);
            var currency = await LoadCurrencyAsync(
                connection, transaction, draft.CurrencyCode, documentDate, cancellationToken);
            var number = await AllocateNumberAsync(
                connection, transaction, InvoicingDocumentTypeCodes.Offer, cancellationToken);
            var documentId = await InsertOfferAsync(
                connection, transaction, draft, context, recipient, issuer, currency, number,
                cancellationToken);
            if (await CopyDraftPositionsAsync(
                    connection, transaction, documentId, draft.ContextSource,
                    draft.ContextSourceId, cancellationToken) == 0)
            {
                throw Validation("Für den gewählten Objektkontext ist mindestens eine Position erforderlich.");
            }
            await transaction.CommitAsync(cancellationToken);
            return documentId;
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    public async Task<int> TransitionAsync(
        int sourceDocumentId,
        DateOnly documentDate,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        if (sourceDocumentId <= 0)
            throw Validation("Ein vorhandenes Ausgangsdokument ist erforderlich.");
        if (documentDate < new DateOnly(2000, 1, 1) ||
            documentDate > new DateOnly(2100, 12, 31))
            throw Validation("Das Dokumentdatum ist ungültig.");

        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
        try
        {
            var source = await LoadTransitionSourceAsync(
                connection, transaction, sourceDocumentId, cancellationToken);
            var nextType = InvoicingDocumentTypeCodes.Next(source.Type);
            if (nextType is null)
                throw Validation("Ein Rechnungsentwurf ist der letzte nicht finanzwirksame AP05-Schritt.");
            if (source.Status != InvoicingDocumentStatusCodes.Draft)
                throw Validation($"{source.Number} wurde bereits weitergeführt.");
            if (source.NextId.HasValue)
                throw Validation($"{source.Number} besitzt bereits einen Nachfolger.");

            var number = await AllocateNumberAsync(
                connection, transaction, nextType, cancellationToken);
            var targetId = await InsertTransitionAsync(
                connection, transaction, sourceDocumentId, documentDate, nextType, number,
                cancellationToken);
            if (await CopyDocumentPositionsAsync(
                    connection, transaction, sourceDocumentId, targetId, cancellationToken) == 0)
                throw new InvalidOperationException(
                    "Das Ausgangsdokument besitzt keine kopierbaren Positionssnapshots.");
            await MarkTransferredAsync(
                connection, transaction, sourceDocumentId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return targetId;
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    private static void ValidateRecipientChoice(
        BillableObjectRecord context,
        InvoicingDocumentDraft draft)
    {
        if (context.SourceCode == InvoicingPositionTypes.Article)
            return;
        var owner = draft.RecipientKind == InvoicingRecipientKinds.Owner &&
                    context.RecipientAddressId == draft.RecipientAddressId;
        var tenant = draft.RecipientKind == InvoicingRecipientKinds.Tenant &&
                     context.TenantDirectBillingAvailable &&
                     context.TenantRecipientAddressId == draft.RecipientAddressId;
        if (!owner && !tenant)
            throw Validation("Der gewählte Immobilienempfänger ist zum Dokumentdatum nicht zulässig.");
    }

    private static async Task LockContextAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string source,
        int sourceId,
        CancellationToken cancellationToken)
    {
        var table = source == InvoicingPositionTypes.Article
            ? "dbo.FakturierungArtikel"
            : "dbo.StweEinheit";
        var active = source == InvoicingPositionTypes.Article ? " AND IsActive = 1" : string.Empty;
        var sql = $"SELECT COUNT(*) FROM {table} WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id{active};";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@id", sourceId);
        if (Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture) != 1)
            throw Validation("Der gewählte Objektkontext ist nicht mehr vorhanden oder aktiv.");
    }

    private static async Task<AddressSnapshot> LoadRecipientAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int addressId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, Name, COALESCE(Strasse, N''), COALESCE(PLZ, N''), COALESCE(Ort, N''),
       COALESCE(Land, N'')
FROM dbo.Adresse WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @id;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@id", addressId);
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw Validation("Die gewählte Empfängeradresse ist nicht mehr vorhanden.");
        var name = reader.GetString(1).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw Validation("Die gewählte Empfängeradresse besitzt keinen Namen.");
        return new AddressSnapshot(
            reader.GetInt32(0), name, reader.GetString(2).Trim(),
            reader.GetString(3).Trim(), reader.GetString(4).Trim(), reader.GetString(5).Trim());
    }

    private static async Task<IssuerSnapshot> LoadIssuerAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT IssuerName, IssuerStreet, IssuerPostalCode, IssuerCity, IssuerCountryCode,
       VatNumber, InvoiceEmail, InvoicePhone
FROM dbo.FakturierungEinstellung WITH (UPDLOCK, HOLDLOCK)
WHERE Id = 1;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw Validation("Die Fakturieren-Grundeinstellung fehlt.");
        var snapshot = new IssuerSnapshot(
            reader.GetString(0).Trim(), reader.GetString(1).Trim(),
            reader.GetString(2).Trim(), reader.GetString(3).Trim(),
            reader.GetString(4).Trim().ToUpperInvariant(), reader.GetString(5).Trim(),
            reader.GetString(6).Trim(), reader.GetString(7).Trim());
        if (string.IsNullOrWhiteSpace(snapshot.Name) ||
            string.IsNullOrWhiteSpace(snapshot.Street) ||
            string.IsNullOrWhiteSpace(snapshot.PostalCode) ||
            string.IsNullOrWhiteSpace(snapshot.City))
            throw Validation(
                "Die vollständige Ausstelleranschrift muss vor dem Dokumentstart eingerichtet sein.");
        return snapshot;
    }

    private static async Task<CurrencySnapshot> LoadCurrencyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string code,
        DateOnly documentDate,
        CancellationToken cancellationToken)
    {
        const string currencySql = """
SELECT setting.BaseCurrency, currency.DisplayName
FROM dbo.FakturierungEinstellung setting WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.FakturierungWaehrung currency WITH (UPDLOCK, HOLDLOCK)
  ON currency.Code = @code AND currency.IsActive = 1
WHERE setting.Id = 1;
""";
        string baseCurrency;
        string displayName;
        await using (var command = new SqlCommand(currencySql, connection, transaction))
        {
            AddText(command, "@code", SqlDbType.Char, 3, code);
            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SingleRow, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw Validation("Die Dokumentwährung ist nicht aktiv.");
            baseCurrency = reader.GetString(0).Trim();
            displayName = reader.GetString(1);
        }
        if (code == baseCurrency)
            return new CurrencySnapshot(code, displayName, 1m, "Basiswährung");

        const string rateSql = """
SELECT TOP (1) RateToBase, Source
FROM dbo.FakturierungWechselkurs WITH (UPDLOCK, HOLDLOCK)
WHERE DocumentCurrency = @code AND IsActive = 1
  AND ValidFrom <= @documentDate
  AND (ValidTo IS NULL OR ValidTo >= @documentDate)
ORDER BY ValidFrom DESC, Id DESC;
""";
        await using var rateCommand = new SqlCommand(rateSql, connection, transaction);
        AddText(rateCommand, "@code", SqlDbType.Char, 3, code);
        AddDate(rateCommand, "@documentDate", documentDate);
        await using var rateReader = await rateCommand.ExecuteReaderAsync(
            CommandBehavior.SingleRow, cancellationToken);
        if (!await rateReader.ReadAsync(cancellationToken))
            throw Validation($"Für {code} fehlt am Dokumentdatum ein aktiver Wechselkurs.");
        return new CurrencySnapshot(
            code, displayName, rateReader.GetDecimal(0), rateReader.GetString(1).Trim());
    }

    internal static async Task<string> AllocateNumberAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string documentType,
        CancellationToken cancellationToken)
    {
        const string selectSql = """
SELECT Prefix, NextNumber, Digits
FROM dbo.FakturierungNummernkreis WITH (UPDLOCK, HOLDLOCK)
WHERE DocumentType = @type;
""";
        string prefix;
        long nextNumber;
        byte digits;
        await using (var command = new SqlCommand(selectSql, connection, transaction))
        {
            AddText(command, "@type", SqlDbType.VarChar, 16, documentType);
            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SingleRow, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw Validation(
                    $"Der Nummernkreis für {InvoicingDocumentTypeCodes.DisplayName(documentType)} fehlt.");
            prefix = reader.GetString(0).Trim();
            nextNumber = reader.GetInt64(1);
            digits = reader.GetByte(2);
        }
        if (nextNumber == long.MaxValue)
            throw new InvalidOperationException("Der Dokumentnummernkreis ist ausgeschöpft.");

        const string updateSql = """
UPDATE dbo.FakturierungNummernkreis
SET NextNumber = NextNumber + 1, UpdatedAt = SYSDATETIME()
WHERE DocumentType = @type AND NextNumber = @expected;
""";
        await using var update = new SqlCommand(updateSql, connection, transaction);
        AddText(update, "@type", SqlDbType.VarChar, 16, documentType);
        update.Parameters.Add(new SqlParameter("@expected", SqlDbType.BigInt) { Value = nextNumber });
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException(
                "Die Dokumentnummer konnte nicht atomar reserviert werden.");
        return $"{prefix}{nextNumber.ToString($"D{digits}", CultureInfo.InvariantCulture)}";
    }

    private static async Task<int> InsertOfferAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        InvoicingDocumentDraft draft,
        BillableObjectRecord context,
        AddressSnapshot recipient,
        IssuerSnapshot issuer,
        CurrencySnapshot currency,
        string number,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT dbo.FakturierungDokument
(
    FlowId, FlowSequence, DocumentType, DocumentNumber, DocumentDate, [Status], Subject,
    ContextSource, ContextSourceId, ContextTitleSnapshot, ContextSubtitleSnapshot,
    IssuerName, IssuerStreet, IssuerPostalCode, IssuerCity, IssuerCountryCode,
    IssuerVatNumber, IssuerEmail, IssuerPhone,
    RecipientAddressIdSnapshot, RecipientKind, RecipientName, RecipientStreet,
    RecipientPostalCode, RecipientCity, RecipientCountry,
    CurrencyCode, ExchangeRateToBase, ExchangeRateSource, PreviousDocumentId, CreatedBy
)
OUTPUT INSERTED.Id
VALUES
(
    @flowId, 10, 'OFFER', @number, @date, 'DRAFT', @subject,
    @contextSource, @contextId, @contextTitle, @contextSubtitle,
    @issuerName, @issuerStreet, @issuerPostal, @issuerCity, @issuerCountry,
    @issuerVat, @issuerEmail, @issuerPhone,
    @recipientId, @recipientKind, @recipientName, @recipientStreet,
    @recipientPostal, @recipientCity, @recipientCountry,
    @currency, @rate, @rateSource, NULL, @user
);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(
            new SqlParameter("@flowId", SqlDbType.UniqueIdentifier) { Value = Guid.NewGuid() });
        AddText(command, "@number", SqlDbType.NVarChar, 40, number);
        AddDate(command, "@date", DateOnly.FromDateTime(draft.DocumentDate.Date));
        AddText(command, "@subject", SqlDbType.NVarChar, 240, draft.Subject);
        AddText(command, "@contextSource", SqlDbType.VarChar, 16, draft.ContextSource);
        AddInt(command, "@contextId", draft.ContextSourceId);
        AddText(command, "@contextTitle", SqlDbType.NVarChar, 300, context.Title);
        AddText(command, "@contextSubtitle", SqlDbType.NVarChar, 300, context.Subtitle);
        AddText(command, "@issuerName", SqlDbType.NVarChar, 200, issuer.Name);
        AddText(command, "@issuerStreet", SqlDbType.NVarChar, 200, issuer.Street);
        AddText(command, "@issuerPostal", SqlDbType.NVarChar, 24, issuer.PostalCode);
        AddText(command, "@issuerCity", SqlDbType.NVarChar, 120, issuer.City);
        AddText(command, "@issuerCountry", SqlDbType.Char, 2, issuer.CountryCode);
        AddText(command, "@issuerVat", SqlDbType.NVarChar, 40, issuer.VatNumber);
        AddText(command, "@issuerEmail", SqlDbType.NVarChar, 256, issuer.Email);
        AddText(command, "@issuerPhone", SqlDbType.NVarChar, 80, issuer.Phone);
        AddInt(command, "@recipientId", recipient.Id);
        AddText(command, "@recipientKind", SqlDbType.VarChar, 16, draft.RecipientKind);
        AddText(command, "@recipientName", SqlDbType.NVarChar, 200, recipient.Name);
        AddText(command, "@recipientStreet", SqlDbType.NVarChar, 200, recipient.Street);
        AddText(command, "@recipientPostal", SqlDbType.NVarChar, 24, recipient.PostalCode);
        AddText(command, "@recipientCity", SqlDbType.NVarChar, 120, recipient.City);
        AddText(command, "@recipientCountry", SqlDbType.NVarChar, 100, recipient.Country);
        AddText(command, "@currency", SqlDbType.Char, 3, currency.Code);
        command.Parameters.Add(new SqlParameter("@rate", SqlDbType.Decimal)
        {
            Precision = 19,
            Scale = 8,
            Value = currency.Rate
        });
        AddText(command, "@rateSource", SqlDbType.NVarChar, 120, currency.Source);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<int> CopyDraftPositionsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        string contextSource,
        int contextSourceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT dbo.FakturierungDokumentPosition
(
    DocumentId, SequenceNumber, PositionType, SourcePositionId, ArticleIdSnapshot,
    Designation, Category, Unit, Quantity, UnitPrice,
    VatCodeSnapshot, VatRatePercentSnapshot, RevenueAccountSnapshot,
    AncillaryClassificationSnapshot, MainTextPlain, MainTextFormatted,
    AdditionalTextPlain, AdditionalTextFormatted, IsFooter
)
SELECT @documentId, SequenceNumber, PositionType, Id, ArticleId,
       Designation, Category, Unit, Quantity, UnitPrice,
       VatCodeSnapshot, VatRatePercentSnapshot, RevenueAccountSnapshot,
       AncillaryClassificationSnapshot, MainTextPlain, MainTextFormatted,
       AdditionalTextPlain, AdditionalTextFormatted, IsFooter
FROM dbo.FakturierungPositionsentwurf WITH (UPDLOCK, HOLDLOCK)
WHERE ContextSource = @contextSource AND ContextSourceId = @contextId;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@documentId", documentId);
        AddText(command, "@contextSource", SqlDbType.VarChar, 16, contextSource);
        AddInt(command, "@contextId", contextSourceId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<TransitionSource> LoadTransitionSourceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int sourceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT source.DocumentType, source.DocumentNumber, source.[Status], nextDocument.Id
FROM dbo.FakturierungDokument source WITH (UPDLOCK, HOLDLOCK)
LEFT JOIN dbo.FakturierungDokument nextDocument WITH (UPDLOCK, HOLDLOCK)
  ON nextDocument.PreviousDocumentId = source.Id
WHERE source.Id = @id;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@id", sourceId);
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw Validation("Das Ausgangsdokument ist nicht mehr vorhanden.");
        return new TransitionSource(
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3));
    }

    private static async Task<int> InsertTransitionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int sourceId,
        DateOnly documentDate,
        string type,
        string number,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT dbo.FakturierungDokument
(
    FlowId, FlowSequence, DocumentType, DocumentNumber, DocumentDate, [Status], Subject,
    ContextSource, ContextSourceId, ContextTitleSnapshot, ContextSubtitleSnapshot,
    IssuerName, IssuerStreet, IssuerPostalCode, IssuerCity, IssuerCountryCode,
    IssuerVatNumber, IssuerEmail, IssuerPhone,
    RecipientAddressIdSnapshot, RecipientKind, RecipientName, RecipientStreet,
    RecipientPostalCode, RecipientCity, RecipientCountry,
    CurrencyCode, ExchangeRateToBase, ExchangeRateSource, PreviousDocumentId, CreatedBy
)
OUTPUT INSERTED.Id
SELECT FlowId, FlowSequence + 10, @type, @number, @date, 'DRAFT', Subject,
       ContextSource, ContextSourceId, ContextTitleSnapshot, ContextSubtitleSnapshot,
       IssuerName, IssuerStreet, IssuerPostalCode, IssuerCity, IssuerCountryCode,
       IssuerVatNumber, IssuerEmail, IssuerPhone,
       RecipientAddressIdSnapshot, RecipientKind, RecipientName, RecipientStreet,
       RecipientPostalCode, RecipientCity, RecipientCountry,
       CurrencyCode, ExchangeRateToBase, ExchangeRateSource, Id, @user
FROM dbo.FakturierungDokument WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @sourceId;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddText(command, "@type", SqlDbType.VarChar, 16, type);
        AddText(command, "@number", SqlDbType.NVarChar, 40, number);
        AddDate(command, "@date", documentDate);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        AddInt(command, "@sourceId", sourceId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
            throw new InvalidOperationException("Das Folgedokument konnte nicht erzeugt werden.");
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    internal static async Task<int> CopyDocumentPositionsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int sourceId,
        int targetId,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT dbo.FakturierungDokumentPosition
(
    DocumentId, SequenceNumber, PositionType, SourcePositionId, ArticleIdSnapshot,
    Designation, Category, Unit, Quantity, UnitPrice,
    VatCodeSnapshot, VatRatePercentSnapshot, RevenueAccountSnapshot,
    AncillaryClassificationSnapshot, MainTextPlain, MainTextFormatted,
    AdditionalTextPlain, AdditionalTextFormatted, IsFooter
)
SELECT @targetId, SequenceNumber, PositionType, SourcePositionId, ArticleIdSnapshot,
       Designation, Category, Unit, Quantity, UnitPrice,
       VatCodeSnapshot, VatRatePercentSnapshot, RevenueAccountSnapshot,
       AncillaryClassificationSnapshot, MainTextPlain, MainTextFormatted,
       AdditionalTextPlain, AdditionalTextFormatted, IsFooter
FROM dbo.FakturierungDokumentPosition WITH (UPDLOCK, HOLDLOCK)
WHERE DocumentId = @sourceId;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@targetId", targetId);
        AddInt(command, "@sourceId", sourceId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkTransferredAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int sourceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
UPDATE dbo.FakturierungDokument
SET [Status] = 'TRANSFERRED', TransitionedAt = SYSDATETIME(), TransitionedBy = @user
WHERE Id = @sourceId AND [Status] = 'DRAFT';
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        AddInt(command, "@sourceId", sourceId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException(
                "Der Ausgangsstatus konnte nicht atomar weitergeführt werden.");
    }

    private static async Task<IReadOnlyList<InvoicingDocumentRecord>> LoadHeadersAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT document.Id, document.FlowId, document.FlowSequence, document.DocumentType, document.DocumentNumber,
       document.DocumentDate, document.[Status], document.Subject,
       document.ContextSource, document.ContextSourceId,
       document.ContextTitleSnapshot, document.ContextSubtitleSnapshot,
       document.IssuerName, document.IssuerStreet, document.IssuerPostalCode,
       document.IssuerCity, document.IssuerCountryCode, document.IssuerVatNumber,
       document.IssuerEmail, document.IssuerPhone,
       document.RecipientAddressIdSnapshot, document.RecipientKind,
       document.RecipientName, document.RecipientStreet, document.RecipientPostalCode,
       document.RecipientCity, document.RecipientCountry,
       document.CurrencyCode, document.ExchangeRateToBase, document.ExchangeRateSource,
       document.PreviousDocumentId, COALESCE(previousDocument.DocumentNumber, N''),
       nextDocument.Id, COALESCE(nextDocument.DocumentNumber, N''),
       document.CreatedAt, document.CreatedBy,
       document.TransitionedAt, COALESCE(document.TransitionedBy, N'')
FROM dbo.FakturierungDokument document
LEFT JOIN dbo.FakturierungDokument previousDocument
  ON previousDocument.Id = document.PreviousDocumentId
LEFT JOIN dbo.FakturierungDokument nextDocument
  ON nextDocument.PreviousDocumentId = document.Id
ORDER BY document.DocumentDate DESC, document.Id DESC;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingDocumentRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new InvoicingDocumentRecord
            {
                Id = reader.GetInt32(0),
                FlowId = reader.GetGuid(1),
                FlowSequence = reader.GetInt32(2),
                DocumentType = reader.GetString(3),
                DocumentNumber = reader.GetString(4),
                DocumentDate = DateOnly.FromDateTime(reader.GetDateTime(5)),
                Status = reader.GetString(6),
                Subject = reader.GetString(7),
                ContextSource = reader.GetString(8),
                ContextSourceId = reader.GetInt32(9),
                ContextTitleSnapshot = reader.GetString(10),
                ContextSubtitleSnapshot = reader.GetString(11),
                IssuerName = reader.GetString(12),
                IssuerStreet = reader.GetString(13),
                IssuerPostalCode = reader.GetString(14),
                IssuerCity = reader.GetString(15),
                IssuerCountryCode = reader.GetString(16).Trim(),
                IssuerVatNumber = reader.GetString(17),
                IssuerEmail = reader.GetString(18),
                IssuerPhone = reader.GetString(19),
                RecipientAddressIdSnapshot = reader.GetInt32(20),
                RecipientKind = reader.GetString(21),
                RecipientName = reader.GetString(22),
                RecipientStreet = reader.GetString(23),
                RecipientPostalCode = reader.GetString(24),
                RecipientCity = reader.GetString(25),
                RecipientCountry = reader.GetString(26),
                CurrencyCode = reader.GetString(27).Trim(),
                ExchangeRateToBase = reader.GetDecimal(28),
                ExchangeRateSource = reader.GetString(29),
                PreviousDocumentId = reader.IsDBNull(30) ? null : reader.GetInt32(30),
                PreviousDocumentNumber = reader.GetString(31),
                NextDocumentId = reader.IsDBNull(32) ? null : reader.GetInt32(32),
                NextDocumentNumber = reader.GetString(33),
                CreatedAt = reader.GetDateTime(34),
                CreatedBy = reader.GetString(35),
                TransitionedAt = reader.IsDBNull(36) ? null : reader.GetDateTime(36),
                TransitionedBy = reader.GetString(37)
            });
        }
        return result;
    }

    private static async Task<IReadOnlyList<InvoicingDocumentPositionRecord>> LoadPositionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, DocumentId, SequenceNumber, PositionType, SourcePositionId, ArticleIdSnapshot,
       Designation, Category, Unit, Quantity, UnitPrice,
       VatCodeSnapshot, VatRatePercentSnapshot, RevenueAccountSnapshot,
       AncillaryClassificationSnapshot, MainTextPlain, MainTextFormatted,
       AdditionalTextPlain, AdditionalTextFormatted, IsFooter
FROM dbo.FakturierungDokumentPosition
ORDER BY DocumentId, SequenceNumber;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingDocumentPositionRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new InvoicingDocumentPositionRecord(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.GetString(6), reader.GetString(7), reader.GetString(8),
                reader.GetDecimal(9), reader.GetDecimal(10), reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                reader.GetString(13), reader.GetString(14), reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16), reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18), reader.GetBoolean(19)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<InvoicingAddressOption>> LoadAddressesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, Name, COALESCE(Strasse, N''), COALESCE(PLZ, N''), COALESCE(Ort, N'')
FROM dbo.Adresse
WHERE LEN(LTRIM(RTRIM(Name))) > 0
ORDER BY Name, Id;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingAddressOption>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new InvoicingAddressOption(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4)));
        return result;
    }

    private static async Task<(string, IReadOnlyList<InvoicingDocumentCurrencyOption>)>
        LoadCurrenciesAsync(
            SqlConnection connection,
            DateOnly documentDate,
            CancellationToken cancellationToken)
    {
        const string sql = """
SELECT currency.Code, currency.DisplayName,
       CASE WHEN currency.Code = setting.BaseCurrency
            THEN CONVERT(decimal(19,8), 1) ELSE rate.RateToBase END,
       CASE WHEN currency.Code = setting.BaseCurrency
            THEN N'Basiswährung' ELSE rate.Source END,
       setting.BaseCurrency
FROM dbo.FakturierungEinstellung setting
JOIN dbo.FakturierungWaehrung currency ON currency.IsActive = 1
OUTER APPLY
(
    SELECT TOP (1) exchangeRate.RateToBase, exchangeRate.Source
    FROM dbo.FakturierungWechselkurs exchangeRate
    WHERE exchangeRate.DocumentCurrency = currency.Code
      AND exchangeRate.IsActive = 1
      AND exchangeRate.ValidFrom <= @date
      AND (exchangeRate.ValidTo IS NULL OR exchangeRate.ValidTo >= @date)
    ORDER BY exchangeRate.ValidFrom DESC, exchangeRate.Id DESC
) rate
WHERE setting.Id = 1
  AND (currency.Code = setting.BaseCurrency OR rate.RateToBase IS NOT NULL)
ORDER BY CASE WHEN currency.Code = setting.BaseCurrency THEN 0 ELSE 1 END, currency.Code;
""";
        await using var command = new SqlCommand(sql, connection);
        AddDate(command, "@date", documentDate);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingDocumentCurrencyOption>();
        var baseCurrency = string.Empty;
        while (await reader.ReadAsync(cancellationToken))
        {
            baseCurrency = reader.GetString(4).Trim();
            result.Add(new InvoicingDocumentCurrencyOption(
                reader.GetString(0).Trim(), reader.GetString(1),
                reader.GetDecimal(2), reader.GetString(3)));
        }
        if (string.IsNullOrWhiteSpace(baseCurrency))
            throw Validation("Die Basiswährung ist nicht eingerichtet.");
        return (baseCurrency, result);
    }

    private static InvoicingDocumentValidationException Validation(string message) =>
        new([message]);

    internal static async Task RollbackQuietlyAsync(SqlTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Der ursprüngliche Fehler bleibt maßgeblich; XACT_ABORT kann bereits zurückgerollt haben.
        }
    }

    private static void AddInt(SqlCommand command, string name, int value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Int) { Value = value });

    private static void AddDate(SqlCommand command, string name, DateOnly value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Date)
        {
            Value = value.ToDateTime(TimeOnly.MinValue)
        });

    private static void AddText(
        SqlCommand command,
        string name,
        SqlDbType type,
        int size,
        string value) =>
        command.Parameters.Add(new SqlParameter(name, type, size) { Value = value });

    private sealed record AddressSnapshot(
        int Id, string Name, string Street, string PostalCode, string City, string Country);
    private sealed record IssuerSnapshot(
        string Name, string Street, string PostalCode, string City, string CountryCode,
        string VatNumber, string Email, string Phone);
    private sealed record CurrencySnapshot(
        string Code, string DisplayName, decimal Rate, string Source);
    private sealed record TransitionSource(
        string Type, string Number, string Status, int? NextId);
}
