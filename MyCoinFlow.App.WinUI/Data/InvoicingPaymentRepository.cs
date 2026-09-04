using Microsoft.Data.SqlClient;
using MyCoinFlow.Import;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MyCoinFlow.WinUI.Data;

public sealed class InvoicingPaymentRepository
{
    public async Task<InvoicingPaymentWorkspace> LoadWorkspaceAsync(
        BankImportItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.StagingId is null)
            throw new InvalidOperationException("Die CAMT-Zeile muss zuerst in der Importablage gespeichert werden.");

        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        var rows = await LoadCandidateRowsAsync(connection, cancellationToken);
        var scored = rows.Select(row => Score(item, row)).OrderByDescending(row => row.Score)
            .ThenBy(row => row.DueDate).ThenBy(row => row.DocumentNumber).ToList();
        var topScore = scored.FirstOrDefault()?.Score ?? 0;
        var uniqueSuggestion = topScore >= 55 && scored.Count(row => row.Score == topScore) == 1 &&
                               scored[0].CanBook;
        var candidates = scored.Select(row => row.ToCandidate(uniqueSuggestion && row.Score == topScore)).ToList();

        const string clarificationSql = """
SELECT COUNT(*)
FROM dbo.FakturierungZahlungKlaerfall
WHERE SourceItemId = @sourceItemId AND [Status] = 'OPEN';
""";
        await using var clarificationCommand = new SqlCommand(clarificationSql, connection);
        AddInt(clarificationCommand, "@sourceItemId", item.StagingId.Value);
        var clarificationCount = Convert.ToInt32(
            await clarificationCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return new InvoicingPaymentWorkspace(item, candidates, clarificationCount);
    }

    public async Task<InvoicingPaymentBookingResult> BookAsync(
        int stagingId,
        int documentId,
        string requestedMatchKind,
        CancellationToken cancellationToken = default)
    {
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var source = await LoadSourceForUpdateAsync(connection, transaction, stagingId, cancellationToken);
            if (source.Direction != "CRDT")
                throw Validation("Nur CAMT-Gutschriften können einer Rechnung als Zahlung zugeordnet werden.");

            var invoice = await LoadInvoiceForUpdateAsync(
                connection, transaction, documentId, cancellationToken);
            ValidateBooking(source, invoice);

            var importIdentity = BuildImportIdentity(source);
            if (await PaymentExistsAsync(connection, transaction, importIdentity, cancellationToken))
                throw Validation("Diese CAMT-Zeile wurde bereits als Rechnungszahlung verbucht.");

            var allocatedAmount = Math.Min(source.Amount, invoice.OpenAmount);
            var surplusAmount = source.Amount - allocatedAmount;
            var (rate, rateSource) = await LoadExchangeRateAsync(
                connection, transaction, source.Currency, invoice.BaseCurrencyCode,
                source.BookingDate, cancellationToken);
            var baseBooked = RoundMoney(allocatedAmount * rate);
            if (baseBooked <= 0m)
                throw Validation("Der umgerechnete Basiswährungsbetrag ist kleiner als ein Rappen und kann nicht gebucht werden.");
            var baseCarrying = allocatedAmount == invoice.OpenAmount
                ? invoice.BaseOpenAmount
                : RoundMoney(invoice.BaseOpenAmount / invoice.OpenAmount * allocatedAmount);
            var exchangeDifference = baseBooked - baseCarrying;
            var exchangeAccountId = await LoadExchangeAccountAsync(
                connection, transaction, exchangeDifference, cancellationToken);

            var matchKind = requestedMatchKind is InvoicingPaymentMatchKinds.Reference or
                InvoicingPaymentMatchKinds.DocumentNumber
                ? requestedMatchKind
                : InvoicingPaymentMatchKinds.Manual;
            var paymentId = await InsertPaymentAsync(
                connection, transaction, source, invoice, importIdentity, allocatedAmount,
                surplusAmount, rate, rateSource, baseBooked, baseCarrying,
                exchangeDifference, exchangeAccountId, matchKind, cancellationToken);

            var weights = await LoadRevenueWeightsAsync(
                connection, transaction, documentId, cancellationToken);
            var splits = CreateSplits(weights, allocatedAmount, baseBooked);
            var transactionIds = new List<int>(splits.Count);
            for (var index = 0; index < splits.Count; index++)
            {
                var split = splits[index];
                var sequence = (index + 1) * 10;
                var transactionId = await InsertTransactionAsync(
                    connection, transaction, source, invoice, split,
                    importIdentity, sequence, cancellationToken);
                transactionIds.Add(transactionId);
                await InsertSplitAsync(
                    connection, transaction, paymentId, sequence, split,
                    transactionId, cancellationToken);
            }

            await ApplyPaymentToOpenItemAsync(
                connection, transaction, invoice, allocatedAmount, baseCarrying,
                cancellationToken);
            await ApplyPaymentToInstallmentsAsync(
                connection, transaction, documentId, allocatedAmount, cancellationToken);
            await InsertRevisionAsync(
                connection, transaction, invoice, allocatedAmount, source,
                cancellationToken);
            await ArchiveSourceAsync(
                connection, transaction, source.Id, transactionIds[0], cancellationToken);
            await ResolveExistingClarificationsAsync(
                connection, transaction, source.Id, paymentId, cancellationToken);
            if (surplusAmount > 0m)
            {
                await InsertClarificationAsync(
                    connection, transaction, source, documentId, paymentId,
                    importIdentity + "/OVERPAYMENT", InvoicingClarificationReasons.Overpayment,
                    $"Überzahlung: {surplusAmount:N2} {source.Currency} konnten dem offenen Posten nicht zugeordnet werden.",
                    surplusAmount, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new InvoicingPaymentBookingResult(
                paymentId, documentId, invoice.DocumentNumber, allocatedAmount, surplusAmount,
                source.Currency, baseBooked, invoice.BaseCurrencyCode, transactionIds.Count);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task AddToClarificationAsync(
        int stagingId,
        int? documentId,
        string reasonCode,
        string narrative,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(narrative))
            throw Validation("Für den Klärbestand ist ein nachvollziehbarer Grund erforderlich.");
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var source = await LoadSourceForUpdateAsync(connection, transaction, stagingId, cancellationToken);
            var identity = BuildImportIdentity(source) + "/MANUAL";
            await InsertClarificationAsync(
                connection, transaction, source, documentId, null, identity, reasonCode,
                narrative.Trim(), source.Amount, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task SetDunningAsync(
        int documentId,
        byte level,
        bool blocked,
        CancellationToken cancellationToken = default)
    {
        if (level > 4) throw Validation("Die Mahnstufe muss zwischen 0 und 4 liegen.");
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        const string sql = """
UPDATE dbo.FakturierungOffenerPosten
SET DunningLevel = @level,
    IsDunningBlocked = @blocked,
    LastDunningAt = CASE WHEN @level > 0 THEN SYSDATETIME() ELSE LastDunningAt END,
    UpdatedAt = SYSDATETIME()
WHERE DocumentId = @documentId AND OpenAmount > 0;
""";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@level", SqlDbType.TinyInt) { Value = level });
        command.Parameters.Add(new SqlParameter("@blocked", SqlDbType.Bit) { Value = blocked });
        AddInt(command, "@documentId", documentId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw Validation("Der Mahnstatus kann nur bei einem noch offenen Posten geändert werden.");
    }

    public async Task<IReadOnlyList<InvoicingClarificationRecord>> LoadOpenClarificationsAsync(
        CancellationToken cancellationToken = default)
    {
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        const string sql = """
SELECT clarification.Id, clarification.SourceItemId,
       COALESCE(document.DocumentNumber, N''), clarification.BookingDate,
       clarification.CurrencyCode, clarification.Amount, clarification.ReasonCode,
       clarification.Narrative, clarification.CreatedAt, clarification.CreatedBy
FROM dbo.FakturierungZahlungKlaerfall clarification
LEFT JOIN dbo.FakturierungDokument document ON document.Id = clarification.DocumentId
WHERE clarification.[Status] = 'OPEN'
ORDER BY clarification.CreatedAt, clarification.Id;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingClarificationRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new InvoicingClarificationRecord(
                reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2),
                DateOnly.FromDateTime(reader.GetDateTime(3)), reader.GetString(4).Trim(),
                reader.GetDecimal(5), reader.GetString(6), reader.GetString(7),
                reader.GetDateTime(8), reader.GetString(9)));
        }
        return result;
    }

    public async Task ResolveClarificationAsync(
        long clarificationId,
        CancellationToken cancellationToken = default)
    {
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        const string sql = """
UPDATE dbo.FakturierungZahlungKlaerfall
SET [Status] = 'RESOLVED', ResolvedAt = SYSDATETIME(), ResolvedBy = @user
WHERE Id = @id AND [Status] = 'OPEN';
""";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = clarificationId });
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw Validation("Der Klärfall ist nicht mehr offen.");
    }

    private static async Task<List<CandidateRow>> LoadCandidateRowsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT document.Id, document.DocumentNumber, document.RecipientName,
       document.CurrencyCode, openItem.OpenAmount, openItem.BaseCurrencyCode,
       openItem.BaseOpenAmount, openItem.DueDate,
       COALESCE(output.PaymentReference, N''), invoice.PaymentReference,
       COALESCE(output.Iban, ''), COALESCE(output.PaymentAccountId, 0),
       COALESCE(account.GeldinstitutId, 0), openItem.DunningLevel,
       openItem.IsDunningBlocked
FROM dbo.FakturierungDokument document
JOIN dbo.FakturierungRechnung invoice ON invoice.DocumentId = document.Id
JOIN dbo.FakturierungOffenerPosten openItem ON openItem.DocumentId = document.Id
LEFT JOIN dbo.FakturierungDokumentAusgabe output ON output.DocumentId = document.Id
LEFT JOIN dbo.FakturierungZahlungskonto account ON account.Id = output.PaymentAccountId
WHERE document.[Status] = 'DEFINITIVE'
  AND invoice.InvoiceKind IN ('FULL', 'PARTIAL', 'FINAL')
  AND openItem.OpenAmount > 0
ORDER BY openItem.DueDate, document.DocumentNumber;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CandidateRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CandidateRow(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3).Trim(), reader.GetDecimal(4), reader.GetString(5).Trim(),
                reader.GetDecimal(6), DateOnly.FromDateTime(reader.GetDateTime(7)),
                reader.GetString(8), reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                reader.GetString(10), reader.GetInt32(11), reader.GetInt32(12),
                reader.GetByte(13), reader.GetBoolean(14), 0, string.Empty, string.Empty,
                false, string.Empty));
        }
        return result;
    }

    private static CandidateRow Score(BankImportItem item, CandidateRow row)
    {
        var structured = NormalizeReference(item.StructuredReference);
        var outputReference = NormalizeReference(row.OutputReference);
        var invoiceReference = NormalizeReference(row.InvoiceReference);
        var haystack = NormalizeReference($"{item.Text} {item.ServiceRef}");
        var documentNumber = NormalizeReference(row.DocumentNumber);
        var score = 0;
        var kind = InvoicingPaymentMatchKinds.Manual;
        var explanation = "Manuelle Auswahl";
        if (structured.Length > 0 &&
            (structured == outputReference || structured == invoiceReference))
        {
            score = 100;
            kind = InvoicingPaymentMatchKinds.Reference;
            explanation = "Exakte strukturierte Zahlungsreferenz";
        }
        else if ((outputReference.Length > 0 && haystack.Contains(outputReference, StringComparison.Ordinal)) ||
                 (invoiceReference.Length > 0 && haystack.Contains(invoiceReference, StringComparison.Ordinal)))
        {
            score = 90;
            kind = InvoicingPaymentMatchKinds.Reference;
            explanation = "Zahlungsreferenz im Buchungstext";
        }
        else if (documentNumber.Length > 0 && haystack.Contains(documentNumber, StringComparison.Ordinal))
        {
            score = 70;
            kind = InvoicingPaymentMatchKinds.DocumentNumber;
            explanation = "Dokumentnummer im Buchungstext";
        }
        else if (string.Equals(item.Currency, row.CurrencyCode, StringComparison.OrdinalIgnoreCase) &&
                 item.Amount == row.OpenAmount &&
                 ContainsEither(item.CounterpartyName, row.RecipientName))
        {
            score = 55;
            explanation = "Betrag, Währung und Name stimmen überein";
        }
        else if (string.Equals(item.Currency, row.CurrencyCode, StringComparison.OrdinalIgnoreCase) &&
                 item.Amount == row.OpenAmount)
        {
            score = 35;
            explanation = "Nur Betrag und Währung stimmen überein";
        }

        var blocking = string.Empty;
        if (item.Direction != KreditDebit.Credit)
            blocking = "Keine Gutschrift";
        else if (!string.Equals(item.Currency, row.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            blocking = $"Rechnungswährung {row.CurrencyCode} stimmt nicht mit CAMT {item.Currency} überein";
        else if (row.PaymentAccountId <= 0 || row.GeldinstitutId <= 0)
            blocking = "Die Rechnung besitzt noch keinen eingefrorenen Zahlungskonto-Snapshot";
        else if (!SameIban(item.AccountIban, row.PaymentAccountIban))
            blocking = "CAMT-Zielkonto stimmt nicht mit dem Rechnungskonto überein";

        return row with
        {
            Score = score,
            MatchKind = kind,
            MatchExplanation = explanation,
            CanBook = blocking.Length == 0,
            BlockingReason = blocking
        };
    }

    private static async Task<SourceRow> LoadSourceForUpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int id,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, BatchId, AccountIban, Currency, BookingDate, ValueDate, Amount,
       Direction, ServiceRef, StructuredReference, [Text], CounterpartyName,
       CounterpartyIban, Uetr, PurposeCode, UniqKey
FROM dbo.BankImportItem WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @id AND [Status] = 0;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@id", id);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw Validation("Die CAMT-Zeile ist nicht mehr offen oder wurde bereits verarbeitet.");
        return new SourceRow(
            reader.GetInt32(0), reader.GetInt32(1), GetString(reader, 2), GetString(reader, 3),
            reader.GetDateTime(4).Date, reader.IsDBNull(5) ? null : reader.GetDateTime(5).Date,
            reader.GetDecimal(6), GetString(reader, 7), GetString(reader, 8), GetString(reader, 9),
            GetString(reader, 10), GetString(reader, 11), GetString(reader, 12),
            GetString(reader, 13), GetString(reader, 14), GetString(reader, 15));
    }

    private static async Task<InvoiceRow> LoadInvoiceForUpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT document.Id, document.DocumentNumber, document.RecipientAddressIdSnapshot,
       document.RecipientName, document.CurrencyCode, openItem.OpenAmount,
       openItem.PaidAmount, openItem.BaseCurrencyCode, openItem.BaseOpenAmount,
       openItem.BasePaidAmount, output.PaymentAccountId, output.Iban,
       paymentAccount.GeldinstitutId, output.PaymentReference
FROM dbo.FakturierungDokument document WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.FakturierungRechnung invoice WITH (UPDLOCK, HOLDLOCK)
  ON invoice.DocumentId = document.Id AND invoice.InvoiceKind IN ('FULL', 'PARTIAL', 'FINAL')
JOIN dbo.FakturierungOffenerPosten openItem WITH (UPDLOCK, HOLDLOCK)
  ON openItem.DocumentId = document.Id
JOIN dbo.FakturierungDokumentAusgabe output WITH (UPDLOCK, HOLDLOCK)
  ON output.DocumentId = document.Id
JOIN dbo.FakturierungZahlungskonto paymentAccount WITH (UPDLOCK, HOLDLOCK)
  ON paymentAccount.Id = output.PaymentAccountId
WHERE document.Id = @documentId AND document.[Status] = 'DEFINITIVE';
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@documentId", documentId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw Validation("Die definitive Rechnung oder ihr Zahlungskonto-Snapshot fehlt.");
        return new InvoiceRow(
            reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
            reader.GetString(4).Trim(), reader.GetDecimal(5), reader.GetDecimal(6),
            reader.GetString(7).Trim(), reader.GetDecimal(8), reader.GetDecimal(9),
            reader.GetInt32(10), reader.GetString(11), reader.GetInt32(12), reader.GetString(13));
    }

    private static void ValidateBooking(SourceRow source, InvoiceRow invoice)
    {
        if (invoice.OpenAmount <= 0m)
            throw Validation("Der offene Posten ist bereits abgeschlossen.");
        if (!string.Equals(source.Currency, invoice.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            throw Validation($"CAMT-Währung {source.Currency} und Rechnungswährung {invoice.CurrencyCode} stimmen nicht überein.");
        if (!SameIban(source.AccountIban, invoice.PaymentAccountIban))
            throw Validation("Die Zahlung ging nicht auf dem in der Rechnung eingefrorenen Zahlungskonto ein.");
    }

    private static async Task<bool> PaymentExistsAsync(
        SqlConnection connection, SqlTransaction transaction, string identity,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.FakturierungZahlung WITH (UPDLOCK, HOLDLOCK) WHERE ImportIdentity = @identity;",
            connection, transaction);
        AddText(command, "@identity", SqlDbType.NVarChar, 200, identity);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<(decimal Rate, string Source)> LoadExchangeRateAsync(
        SqlConnection connection, SqlTransaction transaction, string currency,
        string baseCurrency, DateTime bookingDate, CancellationToken cancellationToken)
    {
        if (string.Equals(currency, baseCurrency, StringComparison.OrdinalIgnoreCase))
            return (1m, "Basiswährung");
        const string sql = """
SELECT TOP (1) RateToBase, Source
FROM dbo.FakturierungWechselkurs WITH (UPDLOCK, HOLDLOCK)
WHERE DocumentCurrency = @currency AND IsActive = 1
  AND ValidFrom <= @date AND (ValidTo IS NULL OR ValidTo >= @date)
ORDER BY ValidFrom DESC, Id DESC;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddText(command, "@currency", SqlDbType.Char, 3, currency.ToUpperInvariant());
        command.Parameters.Add(new SqlParameter("@date", SqlDbType.Date) { Value = bookingDate.Date });
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw Validation($"Für {currency} fehlt am {bookingDate:dd.MM.yyyy} ein gültiger Zahlungstageskurs.");
        return (reader.GetDecimal(0), reader.GetString(1));
    }

    private static async Task<int?> LoadExchangeAccountAsync(
        SqlConnection connection, SqlTransaction transaction, decimal difference,
        CancellationToken cancellationToken)
    {
        if (difference == 0m) return null;
        var column = difference > 0m ? "ExchangeGainAccountId" : "ExchangeLossAccountId";
        await using var command = new SqlCommand(
            $"SELECT {column} FROM dbo.FakturierungEinstellung WITH (UPDLOCK, HOLDLOCK) WHERE Id = 1;",
            connection, transaction);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull)
            throw Validation(difference > 0m
                ? "Für den Kursgewinn fehlt das konfigurierte Konto."
                : "Für den Kursverlust fehlt das konfigurierte Konto.");
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<long> InsertPaymentAsync(
        SqlConnection connection, SqlTransaction transaction, SourceRow source, InvoiceRow invoice,
        string identity, decimal allocated, decimal surplus, decimal rate, string rateSource,
        decimal baseBooked, decimal baseCarrying, decimal difference, int? exchangeAccountId,
        string matchKind, CancellationToken cancellationToken)
    {
        const string sql = """
INSERT dbo.FakturierungZahlung
(
    DocumentId, PaymentAccountId, ImportIdentity, SourceItemId, SourceBatchId,
    BookingDate, ValueDate, AccountIban, StructuredReference, ServiceReference,
    CounterpartyName, OriginalCurrencyCode, OriginalAmount, AllocatedAmount,
    SurplusAmount, BaseCurrencyCode, ExchangeRateToBase, ExchangeRateSource,
    BaseBookedAmount, BaseCarryingAmount, ExchangeDifferenceBase,
    ExchangeAccountId, MatchKind, CreatedBy
)
OUTPUT INSERTED.Id
VALUES
(
    @documentId, @paymentAccountId, @identity, @sourceId, @batchId,
    @bookingDate, @valueDate, @iban, @structured, @service,
    @counterparty, @currency, @original, @allocated,
    @surplus, @baseCurrency, @rate, @rateSource,
    @baseBooked, @baseCarrying, @difference,
    @exchangeAccountId, @matchKind, @user
);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@documentId", invoice.DocumentId);
        AddInt(command, "@paymentAccountId", invoice.PaymentAccountId);
        AddText(command, "@identity", SqlDbType.NVarChar, 200, identity);
        AddInt(command, "@sourceId", source.Id);
        AddInt(command, "@batchId", source.BatchId);
        command.Parameters.Add(new SqlParameter("@bookingDate", SqlDbType.Date) { Value = source.BookingDate });
        command.Parameters.Add(new SqlParameter("@valueDate", SqlDbType.Date) { Value = (object?)source.ValueDate ?? DBNull.Value });
        AddText(command, "@iban", SqlDbType.VarChar, 34, NormalizeIban(source.AccountIban));
        AddNullableText(command, "@structured", 80, source.StructuredReference);
        AddNullableText(command, "@service", 160, source.ServiceReference);
        AddNullableText(command, "@counterparty", 200, source.CounterpartyName);
        AddText(command, "@currency", SqlDbType.Char, 3, source.Currency.ToUpperInvariant());
        AddDecimal(command, "@original", source.Amount, 19, 2);
        AddDecimal(command, "@allocated", allocated, 19, 2);
        AddDecimal(command, "@surplus", surplus, 19, 2);
        AddText(command, "@baseCurrency", SqlDbType.Char, 3, invoice.BaseCurrencyCode);
        AddDecimal(command, "@rate", rate, 19, 8);
        AddText(command, "@rateSource", SqlDbType.NVarChar, 120, rateSource);
        AddDecimal(command, "@baseBooked", baseBooked, 19, 2);
        AddDecimal(command, "@baseCarrying", baseCarrying, 19, 2);
        AddDecimal(command, "@difference", difference, 19, 2);
        AddNullableInt(command, "@exchangeAccountId", exchangeAccountId);
        AddText(command, "@matchKind", SqlDbType.VarChar, 24, matchKind);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<List<RevenueWeight>> LoadRevenueWeightsAsync(
        SqlConnection connection, SqlTransaction transaction, int documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT RevenueAccountIdSnapshot, SUM(Quantity * UnitPrice)
FROM dbo.FakturierungDokumentPosition WITH (UPDLOCK, HOLDLOCK)
WHERE DocumentId = @documentId AND PositionType = 'ARTICLE'
GROUP BY RevenueAccountIdSnapshot
ORDER BY RevenueAccountIdSnapshot;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@documentId", documentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<RevenueWeight>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0))
                throw Validation("Eine Rechnungsposition besitzt kein eingefrorenes Ertragskonto.");
            var weight = reader.GetDecimal(1);
            if (weight > 0m) result.Add(new RevenueWeight(reader.GetInt32(0), weight));
        }
        if (result.Count == 0)
            throw Validation("Die Rechnung enthält keine aufteilbaren Ertragspositionen.");
        return result;
    }

    private static List<PaymentSplit> CreateSplits(
        IReadOnlyList<RevenueWeight> weights, decimal documentAmount, decimal baseAmount)
    {
        var documentShares = AllocateMoney(weights, documentAmount);
        var baseShares = AllocateMoney(weights, baseAmount);
        var primary = Enumerable.Range(0, weights.Count)
            .Where(index => documentShares[index] > 0m && baseShares[index] > 0m)
            .OrderByDescending(index => weights[index].Weight)
            .FirstOrDefault(-1);
        if (primary < 0)
        {
            primary = Enumerable.Range(0, weights.Count)
                .OrderByDescending(index => weights[index].Weight).First();
            Array.Clear(documentShares);
            Array.Clear(baseShares);
            documentShares[primary] = documentAmount;
            baseShares[primary] = baseAmount;
        }
        for (var index = 0; index < weights.Count; index++)
        {
            if (index == primary || (documentShares[index] > 0m && baseShares[index] > 0m)) continue;
            documentShares[primary] += documentShares[index];
            baseShares[primary] += baseShares[index];
            documentShares[index] = 0m;
            baseShares[index] = 0m;
        }
        return Enumerable.Range(0, weights.Count)
            .Where(index => documentShares[index] > 0m && baseShares[index] > 0m)
            .Select(index => new PaymentSplit(
                weights[index].AccountId, weights[index].Weight,
                documentShares[index], baseShares[index]))
            .ToList();
    }

    private static decimal[] AllocateMoney(IReadOnlyList<RevenueWeight> weights, decimal amount)
    {
        var result = new decimal[weights.Count];
        var total = weights.Sum(item => item.Weight);
        decimal assigned = 0m;
        for (var index = 0; index < weights.Count; index++)
        {
            result[index] = index == weights.Count - 1
                ? amount - assigned
                : RoundMoney(amount * weights[index].Weight / total);
            assigned += result[index];
        }
        return result;
    }

    private static async Task<int> InsertTransactionAsync(
        SqlConnection connection, SqlTransaction transaction, SourceRow source, InvoiceRow invoice,
        PaymentSplit split, string identity, int sequence, CancellationToken cancellationToken)
    {
        const string sql = """
INSERT dbo.Transaktion
    (Datum, VonKontoId, NachKontoId, Betrag, Notiz, AdresseId,
     GeldinstitutId, ImportQuelle, ImportHash)
OUTPUT INSERTED.Id
VALUES
    (@date, NULL, @revenueAccountId, @amount, @note, @addressId,
     @institutionId, N'CAMT-RECHNUNG', @hash);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@date", SqlDbType.Date) { Value = source.BookingDate });
        AddInt(command, "@revenueAccountId", split.RevenueAccountId);
        AddDecimal(command, "@amount", split.BaseAmount, 18, 2);
        var note = $"Zahlung {invoice.DocumentNumber} · {source.CounterpartyName} · {source.ServiceReference}".Trim();
        AddText(command, "@note", SqlDbType.NVarChar, 200, Truncate(note, 200));
        AddInt(command, "@addressId", invoice.RecipientAddressId);
        AddInt(command, "@institutionId", invoice.GeldinstitutId);
        AddText(command, "@hash", SqlDbType.NVarChar, 64, Sha256($"{identity}|{sequence}|{split.RevenueAccountId}"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task InsertSplitAsync(
        SqlConnection connection, SqlTransaction transaction, long paymentId, int sequence,
        PaymentSplit split, int transactionId, CancellationToken cancellationToken)
    {
        const string sql = """
INSERT dbo.FakturierungZahlungAufteilung
    (PaymentId, SequenceNumber, RevenueAccountId, WeightAmount,
     DocumentAmount, BaseAmount, TransactionId)
VALUES
    (@paymentId, @sequence, @accountId, @weight,
     @documentAmount, @baseAmount, @transactionId);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@paymentId", SqlDbType.BigInt) { Value = paymentId });
        AddInt(command, "@sequence", sequence);
        AddInt(command, "@accountId", split.RevenueAccountId);
        AddDecimal(command, "@weight", split.Weight, 19, 4);
        AddDecimal(command, "@documentAmount", split.DocumentAmount, 19, 2);
        AddDecimal(command, "@baseAmount", split.BaseAmount, 19, 2);
        AddInt(command, "@transactionId", transactionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ApplyPaymentToOpenItemAsync(
        SqlConnection connection, SqlTransaction transaction, InvoiceRow invoice,
        decimal allocated, decimal baseCarrying, CancellationToken cancellationToken)
    {
        var newOpen = invoice.OpenAmount - allocated;
        var status = newOpen == 0m
            ? InvoicingOpenItemStatusCodes.Paid
            : InvoicingOpenItemStatusCodes.PartiallyPaid;
        const string sql = """
UPDATE dbo.FakturierungOffenerPosten
SET PaidAmount = PaidAmount + @allocated,
    OpenAmount = OpenAmount - @allocated,
    BasePaidAmount = BasePaidAmount + @baseCarrying,
    BaseOpenAmount = BaseOpenAmount - @baseCarrying,
    [Status] = @status,
    UpdatedAt = SYSDATETIME()
WHERE DocumentId = @documentId
  AND OpenAmount = @expectedOpen
  AND BaseOpenAmount = @expectedBaseOpen;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddDecimal(command, "@allocated", allocated, 19, 2);
        AddDecimal(command, "@baseCarrying", baseCarrying, 19, 2);
        AddText(command, "@status", SqlDbType.VarChar, 24, status);
        AddInt(command, "@documentId", invoice.DocumentId);
        AddDecimal(command, "@expectedOpen", invoice.OpenAmount, 19, 2);
        AddDecimal(command, "@expectedBaseOpen", invoice.BaseOpenAmount, 19, 2);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Der offene Posten konnte nicht atomar fortgeschrieben werden.");
    }

    private static async Task ApplyPaymentToInstallmentsAsync(
        SqlConnection connection, SqlTransaction transaction, int documentId,
        decimal paymentAmount, CancellationToken cancellationToken)
    {
        const string loadSql = """
SELECT Id, Amount, PaidAmount
FROM dbo.FakturierungAbzahlungsrate WITH (UPDLOCK, HOLDLOCK)
WHERE InvoiceDocumentId = @documentId AND PaidAmount < Amount
ORDER BY SequenceNumber;
""";
        var installments = new List<(int Id, decimal Amount, decimal Paid)>();
        await using (var command = new SqlCommand(loadSql, connection, transaction))
        {
            AddInt(command, "@documentId", documentId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                installments.Add((reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2)));
        }
        var remaining = paymentAmount;
        foreach (var installment in installments)
        {
            if (remaining <= 0m) break;
            var applied = Math.Min(remaining, installment.Amount - installment.Paid);
            var paid = installment.Paid + applied;
            var status = paid == installment.Amount ? "PAID" : "PARTIALLY_PAID";
            await using var update = new SqlCommand("""
UPDATE dbo.FakturierungAbzahlungsrate
SET PaidAmount = @paid, [Status] = @status
WHERE Id = @id AND PaidAmount = @expectedPaid;
""", connection, transaction);
            AddDecimal(update, "@paid", paid, 19, 2);
            AddText(update, "@status", SqlDbType.VarChar, 20, status);
            AddInt(update, "@id", installment.Id);
            AddDecimal(update, "@expectedPaid", installment.Paid, 19, 2);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Der Ratenstatus konnte nicht atomar fortgeschrieben werden.");
            remaining -= applied;
        }
    }

    private static async Task InsertRevisionAsync(
        SqlConnection connection, SqlTransaction transaction, InvoiceRow invoice,
        decimal amount, SourceRow source, CancellationToken cancellationToken)
    {
        const string sql = """
INSERT dbo.FakturierungRevisionsereignis
    (DocumentId, SequenceNumber, EventType, ReferenceDocumentId,
     Amount, CurrencyCode, Narrative, EventBy)
SELECT @documentId, COALESCE(MAX(SequenceNumber), 0) + 10, 'PAYMENT_APPLIED', NULL,
       @amount, @currency, @narrative, @user
FROM dbo.FakturierungRevisionsereignis WITH (UPDLOCK, HOLDLOCK)
WHERE DocumentId = @documentId;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@documentId", invoice.DocumentId);
        AddDecimal(command, "@amount", amount, 19, 2);
        AddText(command, "@currency", SqlDbType.Char, 3, invoice.CurrencyCode);
        AddText(command, "@narrative", SqlDbType.NVarChar, 500,
            Truncate($"CAMT-Zahlung vom {source.BookingDate:dd.MM.yyyy}; Referenz {source.StructuredReference ?? source.ServiceReference}", 500));
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ArchiveSourceAsync(
        SqlConnection connection, SqlTransaction transaction, int sourceId,
        int transactionId, CancellationToken cancellationToken)
    {
        const string sql = """
INSERT dbo.BankImportItemArchive
(
    SourceItemId, BatchId, AccountIban, Currency, BookingDate, ValueDate, Amount,
    Direction, ServiceRef, StructuredReference, [Text], CounterpartyName,
    CounterpartyIban, Uetr, PurposeCode, VorschlagAdresseId,
    VorschlagNachKontoId, VorschlagVonKontoId, VorschlagGeldinstitutId,
    BookedTransaktionId, ArchiveReason
)
SELECT Id, BatchId, AccountIban, Currency, BookingDate, ValueDate, Amount,
       Direction, ServiceRef, StructuredReference, [Text], CounterpartyName,
       CounterpartyIban, Uetr, PurposeCode, VorschlagAdresseId,
       VorschlagNachKontoId, VorschlagVonKontoId, VorschlagGeldinstitutId,
       @transactionId, N'invoice-payment'
FROM dbo.BankImportItem
WHERE Id = @sourceId;

DELETE dbo.BankImportItem WHERE Id = @sourceId;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@transactionId", transactionId);
        AddInt(command, "@sourceId", sourceId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) < 2)
            throw new InvalidOperationException("Die CAMT-Zeile konnte nicht atomar archiviert werden.");
    }

    private static async Task ResolveExistingClarificationsAsync(
        SqlConnection connection, SqlTransaction transaction, int sourceId,
        long paymentId, CancellationToken cancellationToken)
    {
        const string sql = """
UPDATE dbo.FakturierungZahlungKlaerfall
SET [Status] = 'RESOLVED', PaymentId = @paymentId,
    ResolvedAt = SYSDATETIME(), ResolvedBy = @user
WHERE SourceItemId = @sourceId AND [Status] = 'OPEN';
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@paymentId", SqlDbType.BigInt) { Value = paymentId });
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        AddInt(command, "@sourceId", sourceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertClarificationAsync(
        SqlConnection connection, SqlTransaction transaction, SourceRow source,
        int? documentId, long? paymentId, string identity, string reasonCode,
        string narrative, decimal amount, CancellationToken cancellationToken)
    {
        const string sql = """
IF NOT EXISTS
(
    SELECT 1 FROM dbo.FakturierungZahlungKlaerfall WITH (UPDLOCK, HOLDLOCK)
    WHERE ImportIdentity = @identity
)
INSERT dbo.FakturierungZahlungKlaerfall
(
    ImportIdentity, SourceItemId, SourceBatchId, DocumentId, PaymentId,
    BookingDate, CurrencyCode, Amount, ReasonCode, Narrative, CreatedBy
)
VALUES
(
    @identity, @sourceId, @batchId, @documentId, @paymentId,
    @bookingDate, @currency, @amount, @reason, @narrative, @user
);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddText(command, "@identity", SqlDbType.NVarChar, 200, identity);
        AddInt(command, "@sourceId", source.Id);
        AddInt(command, "@batchId", source.BatchId);
        AddNullableInt(command, "@documentId", documentId);
        command.Parameters.Add(new SqlParameter("@paymentId", SqlDbType.BigInt) { Value = (object?)paymentId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@bookingDate", SqlDbType.Date) { Value = source.BookingDate });
        AddText(command, "@currency", SqlDbType.Char, 3, source.Currency.ToUpperInvariant());
        AddDecimal(command, "@amount", amount, 19, 2);
        AddText(command, "@reason", SqlDbType.VarChar, 32, reasonCode);
        AddText(command, "@narrative", SqlDbType.NVarChar, 500, Truncate(narrative, 500));
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string BuildImportIdentity(SourceRow source)
    {
        var stableKey = string.Join("|",
            NormalizeIban(source.AccountIban),
            source.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            source.Amount.ToString("F2", CultureInfo.InvariantCulture),
            NormalizeReference(source.ServiceReference),
            NormalizeReference(source.StructuredReference),
            NormalizeReference(source.Uetr));
        return "CAMT:" + Sha256(stableKey);
    }
    private static string NormalizeReference(string? value) => new(
        (value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static string NormalizeIban(string? value) => NormalizeReference(value);
    private static bool SameIban(string? left, string? right) =>
        NormalizeIban(left) is { Length: > 0 } normalized && normalized == NormalizeIban(right);
    private static bool ContainsEither(string? left, string? right)
    {
        var first = NormalizeReference(left);
        var second = NormalizeReference(right);
        return first.Length >= 4 && second.Length >= 4 &&
               (first.Contains(second, StringComparison.Ordinal) || second.Contains(first, StringComparison.Ordinal));
    }
    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
    private static string GetString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    private static InvoicingInvoiceValidationException Validation(string message) => new([message]);

    private static void AddInt(SqlCommand command, string name, int value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Int) { Value = value });
    private static void AddNullableInt(SqlCommand command, string name, int? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Int) { Value = (object?)value ?? DBNull.Value });
    private static void AddDecimal(SqlCommand command, string name, decimal value, byte precision, byte scale) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Decimal)
        {
            Precision = precision,
            Scale = scale,
            Value = value
        });
    private static void AddText(
        SqlCommand command, string name, SqlDbType type, int length, string value) =>
        command.Parameters.Add(new SqlParameter(name, type, length) { Value = value });
    private static void AddNullableText(
        SqlCommand command, string name, int length, string? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, length)
        {
            Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim()
        });

    private sealed record CandidateRow(
        int DocumentId, string DocumentNumber, string RecipientName, string CurrencyCode,
        decimal OpenAmount, string BaseCurrencyCode, decimal BaseOpenAmount, DateOnly DueDate,
        string OutputReference, string InvoiceReference, string PaymentAccountIban,
        int PaymentAccountId, int GeldinstitutId, byte DunningLevel, bool IsDunningBlocked,
        int Score, string MatchKind, string MatchExplanation, bool CanBook, string BlockingReason)
    {
        public InvoicingPaymentCandidate ToCandidate(bool suggested) => new(
            DocumentId, DocumentNumber, RecipientName, CurrencyCode, OpenAmount,
            BaseCurrencyCode, BaseOpenAmount, DueDate,
            string.IsNullOrWhiteSpace(OutputReference) ? InvoiceReference : OutputReference,
            PaymentAccountIban, PaymentAccountId, GeldinstitutId, Score, MatchKind,
            MatchExplanation, suggested, CanBook, BlockingReason, DunningLevel,
            IsDunningBlocked);
    }

    private sealed record SourceRow(
        int Id, int BatchId, string AccountIban, string Currency, DateTime BookingDate,
        DateTime? ValueDate, decimal Amount, string Direction, string ServiceReference,
        string StructuredReference, string Text, string CounterpartyName,
        string CounterpartyIban, string Uetr, string PurposeCode, string UniqKey);
    private sealed record InvoiceRow(
        int DocumentId, string DocumentNumber, int RecipientAddressId, string RecipientName,
        string CurrencyCode, decimal OpenAmount, decimal PaidAmount, string BaseCurrencyCode,
        decimal BaseOpenAmount, decimal BasePaidAmount, int PaymentAccountId,
        string PaymentAccountIban, int GeldinstitutId, string PaymentReference);
    private sealed record RevenueWeight(int AccountId, decimal Weight);
    private sealed record PaymentSplit(
        int RevenueAccountId, decimal Weight, decimal DocumentAmount, decimal BaseAmount);
}
