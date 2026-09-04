using Microsoft.Data.SqlClient;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using System.Data;
using System.Globalization;

namespace MyCoinFlow.WinUI.Data;

public sealed class InvoicingInvoiceRepository
{
    public async Task<IReadOnlyList<InvoicingDocumentRecord>> EnrichDocumentsAsync(
        IReadOnlyList<InvoicingDocumentRecord> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        InvoicingSchema.RequireAuthenticated();
        await InvoicingSchema.EnsureAsync(cancellationToken);
        if (documents.Count == 0)
            return documents;

        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        var records = await LoadFinancialRecordsAsync(connection, cancellationToken);
        var installments = await LoadInstallmentsAsync(connection, cancellationToken);
        var openItems = await LoadOpenItemsAsync(connection, cancellationToken);
        var revisions = await LoadRevisionsAsync(connection, cancellationToken);

        foreach (var record in records.Values)
        {
            record.Installments = installments.TryGetValue(record.DocumentId, out var invoiceRates)
                ? invoiceRates
                : [];
            record.OpenItem = openItems.GetValueOrDefault(record.DocumentId);
            record.Revisions = revisions.TryGetValue(record.DocumentId, out var invoiceEvents)
                ? invoiceEvents
                : [];
        }

        foreach (var flow in documents.GroupBy(document => document.FlowId))
        {
            var positive = flow
                .Where(document => records.TryGetValue(document.Id, out var record) &&
                                   record.IsPositiveInvoice)
                .OrderBy(document => document.FlowSequence)
                .ToList();
            var invoiced = positive.Sum(document => records[document.Id].GrossAmount);
            var latestId = positive.LastOrDefault()?.Id;
            var complete = positive.Any(document =>
                records[document.Id].InvoiceKind is
                    InvoicingInvoiceKindCodes.Full or InvoicingInvoiceKindCodes.Final) ||
                positive.Any(document =>
                    invoiced >= records[document.Id].FullGrossBasis - 0.01m);

            foreach (var document in flow)
            {
                if (!records.TryGetValue(document.Id, out var record))
                    continue;
                record.FlowInvoicedGross = invoiced;
                record.IsLatestPositiveInvoice = document.Id == latestId;
                record.BillingComplete = complete;
                document.Financial = record;
            }
        }

        return documents;
    }

    public async Task<InvoicingInvoiceEditorWorkspace> LoadEditorWorkspaceAsync(
        InvoicingDocumentRecord document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        InvoicingSchema.RequireAuthenticated();
        await InvoicingSchema.EnsureAsync(cancellationToken);

        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
        try
        {
            var lockedDocument = await LoadDocumentForUpdateAsync(
                connection, transaction, document.Id, cancellationToken);
            EnsureFinalizableDraft(lockedDocument);
            var positions = await LoadDocumentPositionsAsync(
                connection, transaction, document.Id, cancellationToken);
            var billing = await LoadBillingSnapshotsAsync(
                connection, transaction, lockedDocument.FlowId, cancellationToken);
            var settings = await LoadSettingsAsync(connection, transaction, cancellationToken);
            var suggestedKind = await LoadSuggestedInvoiceKindAsync(
                connection, transaction, lockedDocument.Id, previousInvoiceExists: billing.Count > 0,
                cancellationToken);

            var previous = billing
                .Where(item => item.FlowSequence < lockedDocument.FlowSequence)
                .OrderBy(item => item.FlowSequence)
                .ToList();
            EnsureInvoiceDraftOrder(lockedDocument, billing, previous);

            var previouslyInvoiced = previous.Sum(item => item.GrossAmount);
            decimal? agreedBasis = previous.Count == 0 ? null : previous[0].FullGrossBasis;
            var lockedDiscount = previous.Count == 0 ? 0m : previous[0].DiscountPercent;
            var lockedRounding = agreedBasis.HasValue
                ? CalculateFullRounding(positions, lockedDiscount, agreedBasis.Value)
                : 0m;
            var kinds = previous.Count == 0
                ? InvoicingInvoiceKindCodes.PositiveOptions
                    .Where(option => option.Code is InvoicingInvoiceKindCodes.Full or
                        InvoicingInvoiceKindCodes.Partial)
                    .ToList()
                : InvoicingInvoiceKindCodes.PositiveOptions
                    .Where(option => option.Code is InvoicingInvoiceKindCodes.Partial or
                        InvoicingInvoiceKindCodes.Final)
                    .ToList();

            document.Positions = positions;
            await transaction.CommitAsync(cancellationToken);
            return new InvoicingInvoiceEditorWorkspace(
                document,
                settings.BaseCurrencyCode,
                settings.DefaultPaymentDays,
                previouslyInvoiced,
                agreedBasis,
                lockedDiscount,
                lockedRounding,
                previous.Count > 0,
                suggestedKind,
                kinds);
        }
        catch
        {
            await InvoicingDocumentRepository.RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    public async Task<InvoicingInvoiceCalculationPreview> FinalizeAsync(
        InvoicingInvoiceDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        InvoicingSchema.RequireAuthenticated();
        if (draft.DocumentId <= 0)
            throw Validation("Ein vorhandener Rechnungsentwurf ist erforderlich.");

        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
        try
        {
            var document = await LoadDocumentForUpdateAsync(
                connection, transaction, draft.DocumentId, cancellationToken);
            EnsureFinalizableDraft(document);
            var positions = await LoadDocumentPositionsAsync(
                connection, transaction, document.Id, cancellationToken);
            var billing = await LoadBillingSnapshotsAsync(
                connection, transaction, document.FlowId, cancellationToken);
            var previous = billing
                .Where(item => item.FlowSequence < document.FlowSequence)
                .OrderBy(item => item.FlowSequence)
                .ToList();
            EnsureInvoiceDraftOrder(document, billing, previous);

            decimal? agreedBasis = null;
            if (previous.Count == 0)
            {
                if (draft.InvoiceKind == InvoicingInvoiceKindCodes.Final)
                    throw Validation("Eine Schlussrechnung setzt mindestens eine definitive Teilrechnung voraus.");
            }
            else
            {
                if (previous[^1].InvoiceKind != InvoicingInvoiceKindCodes.Partial)
                    throw Validation("Nach einer normalen oder Schlussrechnung ist keine weitere Rechnung zulässig.");
                if (draft.InvoiceKind == InvoicingInvoiceKindCodes.Full)
                    throw Validation("Nach einer Teilrechnung ist nur eine weitere Teil- oder Schlussrechnung zulässig.");

                agreedBasis = previous[0].FullGrossBasis;
                var lockedDiscount = previous[0].DiscountPercent;
                var lockedRounding = CalculateFullRounding(positions, lockedDiscount, agreedBasis.Value);
                if (Math.Abs(draft.DiscountPercent - lockedDiscount) > 0.0001m ||
                    Math.Abs(draft.FullRoundingAdjustment - lockedRounding) > 0.01m)
                {
                    throw Validation(
                        "Rabatt und Rundung sind nach der ersten definitiven Teilrechnung eingefroren.");
                }
            }

            var previouslyInvoiced = previous.Sum(item => item.GrossAmount);
            var preview = InvoicingInvoiceCalculator.Calculate(
                positions,
                document.DocumentDate,
                document.ExchangeRateToBase,
                draft,
                previouslyInvoiced,
                agreedBasis);
            var paymentReference = CreatePaymentReference(document.DocumentNumber);

            await InsertFinancialRecordAsync(
                connection, transaction, document, draft, preview, paymentReference,
                referenceDocumentId: null, adjustmentReason: null, cancellationToken);
            await InsertOpenItemAsync(
                connection, transaction, document, preview, cancellationToken);
            await InsertInstallmentsAsync(
                connection, transaction, document.Id, draft.Installments, cancellationToken);
            await InsertRevisionAsync(
                connection, transaction, document.Id,
                InvoicingRevisionEventTypeCodes.InvoiceFinalized,
                referenceDocumentId: null,
                preview.GrossAmount,
                document.CurrencyCode,
                $"{InvoicingInvoiceKindCodes.DisplayName(draft.InvoiceKind)} " +
                $"{document.DocumentNumber} wurde definitiv gesetzt.",
                cancellationToken);
            await MarkDefinitiveAsync(connection, transaction, document.Id, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return preview;
        }
        catch
        {
            await InvoicingDocumentRepository.RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    public async Task<int> CreateNextInvoiceDraftAsync(
        int sourceDocumentId,
        string invoiceKind,
        DateOnly documentDate,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        invoiceKind = invoiceKind.Trim().ToUpperInvariant();
        if (sourceDocumentId <= 0)
            throw Validation("Eine definitive Teilrechnung ist erforderlich.");
        if (invoiceKind is not (InvoicingInvoiceKindCodes.Partial or InvoicingInvoiceKindCodes.Final))
            throw Validation("Als Folgerechnung ist nur eine Teil- oder Schlussrechnung zulässig.");
        ValidateDocumentDate(documentDate);

        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
        try
        {
            var source = await LoadDocumentForUpdateAsync(
                connection, transaction, sourceDocumentId, cancellationToken);
            if (source.DocumentType != InvoicingDocumentTypeCodes.Invoice ||
                source.Status != InvoicingDocumentStatusCodes.Definitive)
            {
                throw Validation("Nur eine definitive Teilrechnung kann weitergeführt werden.");
            }
            if (source.NextDocumentId.HasValue)
                throw Validation($"{source.DocumentNumber} besitzt bereits einen Nachfolger.");
            if (documentDate < source.DocumentDate)
                throw Validation("Das Folgerechnungsdatum darf nicht vor dem Ausgangsdokument liegen.");

            var billing = await LoadBillingSnapshotsAsync(
                connection, transaction, source.FlowId, cancellationToken);
            var sourceFinancial = billing.SingleOrDefault(item => item.DocumentId == source.Id)
                ?? throw Validation("Für die Ausgangsrechnung fehlt der definitive Finanzsnapshot.");
            if (sourceFinancial.InvoiceKind != InvoicingInvoiceKindCodes.Partial)
                throw Validation("Nur eine Teilrechnung kann durch eine weitere Rechnung ergänzt werden.");
            if (billing.OrderBy(item => item.FlowSequence).Last().DocumentId != source.Id)
                throw Validation("Nur die letzte definitive Teilrechnung kann weitergeführt werden.");

            var invoiced = billing.Sum(item => item.GrossAmount);
            var remaining = InvoicingInvoiceCalculator.RoundMoney(
                sourceFinancial.FullGrossBasis - invoiced);
            if (remaining <= 0m ||
                billing.Any(item => item.InvoiceKind is
                    InvoicingInvoiceKindCodes.Full or InvoicingInvoiceKindCodes.Final))
            {
                throw Validation("Die Rechnungsfolge ist bereits vollständig fakturiert.");
            }

            var number = await InvoicingDocumentRepository.AllocateNumberAsync(
                connection, transaction, InvoicingDocumentTypeCodes.Invoice, cancellationToken);
            var targetId = await InsertNextInvoiceDocumentAsync(
                connection, transaction, source.Id, source.FlowSequence + 10,
                number, documentDate, invoiceKind, cancellationToken);
            if (await InvoicingDocumentRepository.CopyDocumentPositionsAsync(
                    connection, transaction, source.Id, targetId, cancellationToken) == 0)
            {
                throw new InvalidOperationException(
                    "Die Ausgangsrechnung besitzt keine kopierbaren Positionssnapshots.");
            }

            await InsertRevisionAsync(
                connection, transaction, source.Id,
                invoiceKind == InvoicingInvoiceKindCodes.Final
                    ? InvoicingRevisionEventTypeCodes.NextFinalCreated
                    : InvoicingRevisionEventTypeCodes.NextPartialCreated,
                targetId,
                remaining,
                source.CurrencyCode,
                $"{InvoicingInvoiceKindCodes.DisplayName(invoiceKind)} {number} wurde als " +
                $"Nachfolger von {source.DocumentNumber} angelegt.",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return targetId;
        }
        catch
        {
            await InvoicingDocumentRepository.RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    public async Task<int> CreateAdjustmentAsync(
        InvoicingAdjustmentDraft draft,
        DateOnly documentDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        InvoicingSchema.RequireAuthenticated();
        draft.AdjustmentKind = draft.AdjustmentKind.Trim().ToUpperInvariant();
        draft.Reason = draft.Reason.Trim();
        if (draft.ReferenceInvoiceDocumentId <= 0)
            throw Validation("Eine definitive Bezugsrechnung ist erforderlich.");
        if (!InvoicingInvoiceKindCodes.IsAdjustment(draft.AdjustmentKind))
            throw Validation("Es ist eine Korrektur oder ein Storno erforderlich.");
        if (string.IsNullOrWhiteSpace(draft.Reason) || draft.Reason.Length > 500)
            throw Validation("Eine Begründung mit höchstens 500 Zeichen ist erforderlich.");
        ValidateDocumentDate(documentDate);

        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
        try
        {
            var target = await LoadDocumentForUpdateAsync(
                connection, transaction, draft.ReferenceInvoiceDocumentId, cancellationToken);
            if (target.DocumentType != InvoicingDocumentTypeCodes.Invoice ||
                target.Status != InvoicingDocumentStatusCodes.Definitive)
            {
                throw Validation("Korrektur und Storno benötigen eine definitive Bezugsrechnung.");
            }
            if (documentDate < target.DocumentDate)
                throw Validation("Das Korrekturdatum darf nicht vor der Bezugsrechnung liegen.");

            var billing = await LoadBillingSnapshotsAsync(
                connection, transaction, target.FlowId, cancellationToken);
            var targetFinancial = billing.SingleOrDefault(item => item.DocumentId == target.Id)
                ?? throw Validation("Für die Bezugsrechnung fehlt der definitive Finanzsnapshot.");
            var invoiced = billing.Sum(item => item.GrossAmount);
            var billingComplete = billing.Any(item => item.InvoiceKind is
                                      InvoicingInvoiceKindCodes.Full or InvoicingInvoiceKindCodes.Final) ||
                                  invoiced >= targetFinancial.FullGrossBasis - 0.01m;
            if (!billingComplete)
                throw Validation("Korrekturen sind erst nach vollständiger Fakturierung zulässig.");

            var openItem = await LoadOpenItemForUpdateAsync(
                connection, transaction, target.Id, cancellationToken);
            if (openItem.OpenAmount <= 0m)
                throw Validation("Der offene Posten der Bezugsrechnung ist bereits ausgeglichen.");

            var amount = draft.AdjustmentKind == InvoicingInvoiceKindCodes.Cancellation
                ? openItem.OpenAmount
                : InvoicingInvoiceCalculator.RoundMoney(draft.Amount);
            if (amount <= 0m)
                throw Validation("Der Korrekturbetrag muss grösser als null sein.");
            if (draft.AdjustmentKind == InvoicingInvoiceKindCodes.Correction &&
                amount >= openItem.OpenAmount)
            {
                throw Validation(
                    "Eine Korrektur muss kleiner als der offene Betrag sein; für den ganzen Rest ist Storno zu verwenden.");
            }

            var baseAmount = draft.AdjustmentKind == InvoicingInvoiceKindCodes.Cancellation
                ? openItem.BaseOpenAmount
                : Math.Min(
                    openItem.BaseOpenAmount,
                    InvoicingInvoiceCalculator.RoundMoney(amount * target.ExchangeRateToBase));
            if (baseAmount <= 0m)
                throw Validation("Der Korrekturbetrag ergibt keinen gültigen Basiswährungsbetrag.");

            var tail = await LoadFlowTailForUpdateAsync(
                connection, transaction, target.FlowId, cancellationToken);
            if (tail.Status == InvoicingDocumentStatusCodes.Draft)
                throw Validation("Ein vorhandener Dokumententwurf muss zuerst abgeschlossen werden.");
            var number = await InvoicingDocumentRepository.AllocateNumberAsync(
                connection, transaction, InvoicingDocumentTypeCodes.Correction, cancellationToken);
            var adjustmentId = await InsertAdjustmentDocumentAsync(
                connection, transaction, target.Id, tail.Id, tail.FlowSequence + 10,
                number, documentDate, draft, cancellationToken);
            if (await InvoicingDocumentRepository.CopyDocumentPositionsAsync(
                    connection, transaction, target.Id, adjustmentId, cancellationToken) == 0)
            {
                throw new InvalidOperationException(
                    "Die Bezugsrechnung besitzt keine kopierbaren Positionssnapshots.");
            }

            await InsertAdjustmentFinancialRecordAsync(
                connection, transaction, adjustmentId, targetFinancial,
                draft, amount, baseAmount, cancellationToken);
            await ApplyAdjustmentToOpenItemAsync(
                connection, transaction, target.Id, openItem, amount, baseAmount,
                cancellationToken);
            await InsertRevisionAsync(
                connection, transaction, target.Id,
                draft.AdjustmentKind == InvoicingInvoiceKindCodes.Cancellation
                    ? InvoicingRevisionEventTypeCodes.CancellationCreated
                    : InvoicingRevisionEventTypeCodes.CorrectionCreated,
                adjustmentId,
                -amount,
                target.CurrencyCode,
                $"{InvoicingInvoiceKindCodes.DisplayName(draft.AdjustmentKind)} {number}: {draft.Reason}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return adjustmentId;
        }
        catch
        {
            await InvoicingDocumentRepository.RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    private static async Task<Dictionary<int, InvoicingFinancialRecord>>
        LoadFinancialRecordsAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
    {
        const string sql = """
SELECT DocumentId, InvoiceKind, ReferenceInvoiceDocumentId,
       COALESCE(AdjustmentReason, N''), FullGrossBasis, PreviouslyInvoicedGross,
       NetAmount, VatAmount, DiscountPercent, DiscountAmount, RoundingAdjustment,
       GrossAmount, BaseCurrencyCode, BaseGrossAmount, PaymentDays, DueDate,
       SkontoPercent, SkontoDays, SkontoDueDate, SkontoAmount,
       COALESCE(PaymentReference, N''), FinalizedAt, FinalizedBy
FROM dbo.FakturierungRechnung;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<int, InvoicingFinancialRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var record = new InvoicingFinancialRecord
            {
                DocumentId = reader.GetInt32(0),
                InvoiceKind = reader.GetString(1),
                ReferenceInvoiceDocumentId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                AdjustmentReason = reader.GetString(3),
                FullGrossBasis = reader.GetDecimal(4),
                PreviouslyInvoicedGross = reader.GetDecimal(5),
                NetAmount = reader.GetDecimal(6),
                VatAmount = reader.GetDecimal(7),
                DiscountPercent = reader.GetDecimal(8),
                DiscountAmount = reader.GetDecimal(9),
                RoundingAdjustment = reader.GetDecimal(10),
                GrossAmount = reader.GetDecimal(11),
                BaseCurrencyCode = reader.GetString(12).Trim(),
                BaseGrossAmount = reader.GetDecimal(13),
                PaymentDays = reader.IsDBNull(14) ? null : reader.GetInt16(14),
                DueDate = reader.IsDBNull(15)
                    ? null
                    : DateOnly.FromDateTime(reader.GetDateTime(15)),
                SkontoPercent = reader.IsDBNull(16) ? null : reader.GetDecimal(16),
                SkontoDays = reader.IsDBNull(17) ? null : reader.GetInt16(17),
                SkontoDueDate = reader.IsDBNull(18)
                    ? null
                    : DateOnly.FromDateTime(reader.GetDateTime(18)),
                SkontoAmount = reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                PaymentReference = reader.GetString(20),
                FinalizedAt = reader.GetDateTime(21),
                FinalizedBy = reader.GetString(22)
            };
            result.Add(record.DocumentId, record);
        }
        return result;
    }

    private static async Task<Dictionary<int, IReadOnlyList<InvoicingInstallmentRecord>>>
        LoadInstallmentsAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, InvoiceDocumentId, SequenceNumber, DueDate, Amount, Label, PaidAmount, [Status]
FROM dbo.FakturierungAbzahlungsrate
ORDER BY InvoiceDocumentId, SequenceNumber;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<InvoicingInstallmentRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new InvoicingInstallmentRecord(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                DateOnly.FromDateTime(reader.GetDateTime(3)), reader.GetDecimal(4),
                reader.GetString(5), reader.GetDecimal(6), reader.GetString(7)));
        }
        return rows.GroupBy(row => row.InvoiceDocumentId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<InvoicingInstallmentRecord>)group.ToList());
    }

    private static async Task<Dictionary<int, InvoicingOpenItemRecord>> LoadOpenItemsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT DocumentId, CurrencyCode, BaseCurrencyCode, OriginalAmount, CorrectionAmount,
       PaidAmount, OpenAmount, BaseOriginalAmount, BaseCorrectionAmount, BasePaidAmount,
       BaseOpenAmount, DueDate, [Status], UpdatedAt,
       DunningLevel, IsDunningBlocked, LastDunningAt
FROM dbo.FakturierungOffenerPosten;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<int, InvoicingOpenItemRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var record = new InvoicingOpenItemRecord(
                reader.GetInt32(0), reader.GetString(1).Trim(), reader.GetString(2).Trim(),
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5),
                reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8),
                reader.GetDecimal(9), reader.GetDecimal(10),
                DateOnly.FromDateTime(reader.GetDateTime(11)), reader.GetString(12),
                reader.GetDateTime(13), reader.GetByte(14), reader.GetBoolean(15),
                reader.IsDBNull(16) ? null : reader.GetDateTime(16));
            result.Add(record.DocumentId, record);
        }
        return result;
    }

    private static async Task<Dictionary<int, IReadOnlyList<InvoicingRevisionEventRecord>>>
        LoadRevisionsAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, DocumentId, SequenceNumber, EventType, ReferenceDocumentId, Amount,
       CurrencyCode, Narrative, EventAt, EventBy
FROM dbo.FakturierungRevisionsereignis
ORDER BY DocumentId, SequenceNumber;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<InvoicingRevisionEventRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new InvoicingRevisionEventRecord(
                reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.GetDecimal(5),
                reader.GetString(6).Trim(), reader.GetString(7), reader.GetDateTime(8),
                reader.GetString(9)));
        }
        return rows.GroupBy(row => row.DocumentId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<InvoicingRevisionEventRecord>)group.ToList());
    }

    private static async Task<InvoiceDocumentSnapshot> LoadDocumentForUpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT document.Id, document.FlowId, document.FlowSequence, document.DocumentType,
       document.DocumentNumber, document.DocumentDate, document.[Status], document.Subject,
       document.CurrencyCode, document.ExchangeRateToBase,
       (SELECT TOP (1) successor.Id
        FROM dbo.FakturierungDokument successor WITH (UPDLOCK, HOLDLOCK)
        WHERE successor.PreviousDocumentId = document.Id) AS NextDocumentId
FROM dbo.FakturierungDokument document WITH (UPDLOCK, HOLDLOCK)
WHERE document.Id = @documentId;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@documentId", documentId);
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw Validation("Das Dokument ist nicht mehr vorhanden.");
        return new InvoiceDocumentSnapshot(
            reader.GetInt32(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetString(3),
            reader.GetString(4), DateOnly.FromDateTime(reader.GetDateTime(5)),
            reader.GetString(6), reader.GetString(7), reader.GetString(8).Trim(),
            reader.GetDecimal(9), reader.IsDBNull(10) ? null : reader.GetInt32(10));
    }

    private static async Task<IReadOnlyList<InvoicingDocumentPositionRecord>>
        LoadDocumentPositionsAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int documentId,
            CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, DocumentId, SequenceNumber, PositionType, SourcePositionId, ArticleIdSnapshot,
       Designation, Category, Unit, Quantity, UnitPrice,
       VatCodeSnapshot, VatRatePercentSnapshot, RevenueAccountIdSnapshot, RevenueAccountSnapshot,
       AncillaryClassificationSnapshot, MainTextPlain, MainTextFormatted,
       AdditionalTextPlain, AdditionalTextFormatted, IsFooter
FROM dbo.FakturierungDokumentPosition WITH (UPDLOCK, HOLDLOCK)
WHERE DocumentId = @documentId
ORDER BY SequenceNumber;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@documentId", documentId);
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
                reader.IsDBNull(13) ? null : reader.GetInt32(13),
                reader.GetString(14), reader.GetString(15), reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetString(17), reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetString(19), reader.GetBoolean(20)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<BillingSnapshot>> LoadBillingSnapshotsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid flowId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT document.Id, document.FlowSequence, invoice.InvoiceKind,
       invoice.FullGrossBasis, invoice.PreviouslyInvoicedGross,
       invoice.DiscountPercent, invoice.GrossAmount, invoice.BaseCurrencyCode
FROM dbo.FakturierungDokument document WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.FakturierungRechnung invoice WITH (UPDLOCK, HOLDLOCK)
  ON invoice.DocumentId = document.Id
WHERE document.FlowId = @flowId
  AND invoice.InvoiceKind IN ('FULL', 'PARTIAL', 'FINAL')
ORDER BY document.FlowSequence;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@flowId", SqlDbType.UniqueIdentifier)
        {
            Value = flowId
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<BillingSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new BillingSnapshot(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5),
                reader.GetDecimal(6), reader.GetString(7).Trim()));
        }
        return result;
    }

    private static async Task<InvoiceSettingsSnapshot> LoadSettingsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT BaseCurrency, DefaultPaymentDays
FROM dbo.FakturierungEinstellung WITH (UPDLOCK, HOLDLOCK)
WHERE Id = 1;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw Validation("Die Fakturierungseinstellungen fehlen.");
        return new InvoiceSettingsSnapshot(reader.GetString(0).Trim(), reader.GetInt16(1));
    }

    private static async Task<string> LoadSuggestedInvoiceKindAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        bool previousInvoiceExists,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT TOP (1) EventType
FROM dbo.FakturierungRevisionsereignis WITH (UPDLOCK, HOLDLOCK)
WHERE ReferenceDocumentId = @documentId
  AND EventType IN ('NEXT_PARTIAL_CREATED', 'NEXT_FINAL_CREATED')
ORDER BY EventAt DESC, Id DESC;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@documentId", documentId);
        var result = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        return result switch
        {
            InvoicingRevisionEventTypeCodes.NextFinalCreated => InvoicingInvoiceKindCodes.Final,
            InvoicingRevisionEventTypeCodes.NextPartialCreated => InvoicingInvoiceKindCodes.Partial,
            _ => previousInvoiceExists
                ? InvoicingInvoiceKindCodes.Partial
                : InvoicingInvoiceKindCodes.Full
        };
    }

    private static async Task<OpenItemSnapshot> LoadOpenItemForUpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT OriginalAmount, CorrectionAmount, PaidAmount, OpenAmount,
       BaseOriginalAmount, BaseCorrectionAmount, BasePaidAmount, BaseOpenAmount
FROM dbo.FakturierungOffenerPosten WITH (UPDLOCK, HOLDLOCK)
WHERE DocumentId = @documentId;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@documentId", documentId);
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw Validation("Der offene Posten der Bezugsrechnung fehlt.");
        return new OpenItemSnapshot(
            reader.GetDecimal(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3),
            reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7));
    }

    private static async Task<FlowTailSnapshot> LoadFlowTailForUpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid flowId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT TOP (1) Id, FlowSequence, [Status]
FROM dbo.FakturierungDokument WITH (UPDLOCK, HOLDLOCK)
WHERE FlowId = @flowId
ORDER BY FlowSequence DESC;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@flowId", SqlDbType.UniqueIdentifier)
        {
            Value = flowId
        });
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw Validation("Der Dokumentfluss ist nicht mehr vorhanden.");
        return new FlowTailSnapshot(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2));
    }

    private static void EnsureFinalizableDraft(InvoiceDocumentSnapshot document)
    {
        if (document.DocumentType != InvoicingDocumentTypeCodes.Invoice)
            throw Validation("Nur ein Rechnungsentwurf kann definitiv gesetzt werden.");
        if (document.Status != InvoicingDocumentStatusCodes.Draft)
            throw Validation($"{document.DocumentNumber} ist kein offener Rechnungsentwurf mehr.");
        if (document.NextDocumentId.HasValue)
            throw Validation($"{document.DocumentNumber} besitzt bereits einen Nachfolger.");
    }

    private static void EnsureInvoiceDraftOrder(
        InvoiceDocumentSnapshot document,
        IReadOnlyList<BillingSnapshot> billing,
        IReadOnlyList<BillingSnapshot> previous)
    {
        if (billing.Any(item => item.DocumentId == document.Id))
            throw Validation($"{document.DocumentNumber} ist bereits definitiv gesetzt.");
        if (billing.Any(item => item.FlowSequence > document.FlowSequence))
            throw Validation("Der Rechnungsentwurf ist nicht der aktuelle Schritt im Dokumentfluss.");
        if (previous.Count > 0 &&
            previous[^1].InvoiceKind != InvoicingInvoiceKindCodes.Partial)
        {
            throw Validation("Die Rechnungsfolge wurde bereits durch eine normale oder Schlussrechnung abgeschlossen.");
        }
    }

    private static decimal CalculateFullRounding(
        IReadOnlyList<InvoicingDocumentPositionRecord> positions,
        decimal discountPercent,
        decimal fullGrossBasis)
    {
        var financialPositions = positions.Where(position => !position.IsTextPosition).ToList();
        var netBeforeDiscount = InvoicingInvoiceCalculator.RoundMoney(
            financialPositions.Sum(position => position.LineTotal));
        var discount = InvoicingInvoiceCalculator.RoundMoney(
            netBeforeDiscount * discountPercent / 100m);
        var netAfterDiscount = netBeforeDiscount - discount;
        var factor = 1m - discountPercent / 100m;
        var vat = InvoicingInvoiceCalculator.RoundMoney(financialPositions.Sum(position =>
            InvoicingInvoiceCalculator.RoundMoney(
                position.LineTotal * factor * (position.VatRatePercentSnapshot ?? 0m) / 100m)));
        return InvoicingInvoiceCalculator.RoundMoney(fullGrossBasis - netAfterDiscount - vat);
    }

    private static string CreatePaymentReference(string documentNumber)
    {
        var normalized = new string(documentNumber
            .Trim()
            .ToUpperInvariant()
            .Where(character => char.IsLetterOrDigit(character))
            .ToArray());
        if (string.IsNullOrWhiteSpace(normalized))
            throw Validation("Aus der Dokumentnummer konnte keine Zahlungsreferenz gebildet werden.");
        return $"MCF-{normalized}";
    }

    private static void ValidateDocumentDate(DateOnly value)
    {
        if (value < new DateOnly(2000, 1, 1) || value > new DateOnly(2100, 12, 31))
            throw Validation("Das Dokumentdatum ist ungültig.");
    }

    private static async Task InsertFinancialRecordAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        InvoiceDocumentSnapshot document,
        InvoicingInvoiceDraft draft,
        InvoicingInvoiceCalculationPreview preview,
        string? paymentReference,
        int? referenceDocumentId,
        string? adjustmentReason,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT dbo.FakturierungRechnung
(
    DocumentId, InvoiceKind, ReferenceInvoiceDocumentId, AdjustmentReason,
    FullGrossBasis, PreviouslyInvoicedGross, NetAmount, VatAmount,
    DiscountPercent, DiscountAmount, RoundingAdjustment, GrossAmount,
    BaseCurrencyCode, BaseGrossAmount, PaymentDays, DueDate,
    SkontoPercent, SkontoDays, SkontoDueDate, SkontoAmount,
    PaymentReference, FinalizedAt, FinalizedBy
)
VALUES
(
    @documentId, @kind, @referenceId, @reason,
    @fullBasis, @previouslyInvoiced, @net, @vat,
    @discountPercent, @discountAmount, @rounding, @gross,
    @baseCurrency, @baseGross, @paymentDays, @dueDate,
    @skontoPercent, @skontoDays, @skontoDueDate, @skontoAmount,
    @paymentReference, SYSDATETIME(), @user
);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@documentId", document.Id);
        AddText(command, "@kind", SqlDbType.VarChar, 16, draft.InvoiceKind);
        AddNullableInt(command, "@referenceId", referenceDocumentId);
        AddNullableText(command, "@reason", SqlDbType.NVarChar, 500, adjustmentReason);
        AddDecimal(command, "@fullBasis", preview.FullGrossBasis, 19, 2);
        AddDecimal(command, "@previouslyInvoiced", preview.PreviouslyInvoicedGross, 19, 2);
        AddDecimal(command, "@net", preview.NetAmount, 19, 2);
        AddDecimal(command, "@vat", preview.VatAmount, 19, 2);
        AddDecimal(command, "@discountPercent", draft.DiscountPercent, 7, 4);
        AddDecimal(command, "@discountAmount", preview.DiscountAmount, 19, 2);
        AddDecimal(command, "@rounding", preview.RoundingAdjustment, 19, 2);
        AddDecimal(command, "@gross", preview.GrossAmount, 19, 2);
        AddText(command, "@baseCurrency", SqlDbType.Char, 3, await LoadBaseCurrencyCodeAsync(
            connection, transaction, cancellationToken));
        AddDecimal(command, "@baseGross", preview.BaseGrossAmount, 19, 2);
        AddSmallInt(command, "@paymentDays", draft.PaymentDays);
        AddDate(command, "@dueDate", preview.DueDate);
        AddNullableDecimal(command, "@skontoPercent", draft.SkontoPercent, 7, 4);
        AddNullableSmallInt(command, "@skontoDays", draft.SkontoDays);
        AddNullableDate(command, "@skontoDueDate", preview.SkontoDueDate);
        AddNullableDecimal(command, "@skontoAmount", preview.SkontoAmount, 19, 2);
        AddNullableText(command, "@paymentReference", SqlDbType.NVarChar, 80, paymentReference);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Der definitive Finanzsnapshot konnte nicht gespeichert werden.");
    }

    private static async Task InsertOpenItemAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        InvoiceDocumentSnapshot document,
        InvoicingInvoiceCalculationPreview preview,
        CancellationToken cancellationToken)
    {
        var baseCurrency = await LoadBaseCurrencyCodeAsync(
            connection, transaction, cancellationToken);
        const string sql = """
INSERT dbo.FakturierungOffenerPosten
(
    DocumentId, CurrencyCode, BaseCurrencyCode,
    OriginalAmount, CorrectionAmount, PaidAmount, OpenAmount,
    BaseOriginalAmount, BaseCorrectionAmount, BasePaidAmount, BaseOpenAmount,
    DueDate, [Status]
)
VALUES
(
    @documentId, @currency, @baseCurrency,
    @gross, 0, 0, @gross,
    @baseGross, 0, 0, @baseGross,
    @dueDate, 'OPEN'
);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@documentId", document.Id);
        AddText(command, "@currency", SqlDbType.Char, 3, document.CurrencyCode);
        AddText(command, "@baseCurrency", SqlDbType.Char, 3, baseCurrency);
        AddDecimal(command, "@gross", preview.GrossAmount, 19, 2);
        AddDecimal(command, "@baseGross", preview.BaseGrossAmount, 19, 2);
        AddDate(command, "@dueDate", preview.DueDate);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Der offene Posten konnte nicht gespeichert werden.");
    }

    private static async Task InsertInstallmentsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        IReadOnlyList<InvoicingInstallmentDraft> installments,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT dbo.FakturierungAbzahlungsrate
    (InvoiceDocumentId, SequenceNumber, DueDate, Amount, Label, PaidAmount, [Status])
VALUES (@documentId, @sequence, @dueDate, @amount, @label, 0, 'OPEN');
""";
        for (var index = 0; index < installments.Count; index++)
        {
            var installment = installments[index];
            await using var command = new SqlCommand(sql, connection, transaction);
            AddInt(command, "@documentId", documentId);
            AddInt(command, "@sequence", (index + 1) * 10);
            AddDate(command, "@dueDate", DateOnly.FromDateTime(installment.DueDate.Date));
            AddDecimal(command, "@amount", installment.Amount, 19, 2);
            AddText(command, "@label", SqlDbType.NVarChar, 160, installment.Label);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Der Abzahlungsplan konnte nicht gespeichert werden.");
        }
    }

    private static async Task InsertRevisionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        string eventType,
        int? referenceDocumentId,
        decimal amount,
        string currencyCode,
        string narrative,
        CancellationToken cancellationToken)
    {
        const string sql = """
DECLARE @sequence int;
SELECT @sequence = COALESCE(MAX(SequenceNumber), 0) + 10
FROM dbo.FakturierungRevisionsereignis WITH (UPDLOCK, HOLDLOCK)
WHERE DocumentId = @documentId;

INSERT dbo.FakturierungRevisionsereignis
    (DocumentId, SequenceNumber, EventType, ReferenceDocumentId,
     Amount, CurrencyCode, Narrative, EventAt, EventBy)
VALUES
    (@documentId, @sequence, @eventType, @referenceId,
     @amount, @currency, @narrative, SYSDATETIME(), @user);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@documentId", documentId);
        AddText(command, "@eventType", SqlDbType.VarChar, 32, eventType);
        AddNullableInt(command, "@referenceId", referenceDocumentId);
        AddDecimal(command, "@amount", amount, 19, 2);
        AddText(command, "@currency", SqlDbType.Char, 3, currencyCode);
        AddText(command, "@narrative", SqlDbType.NVarChar, 1000, narrative);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkDefinitiveAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
UPDATE dbo.FakturierungDokument
SET [Status] = 'DEFINITIVE', TransitionedAt = SYSDATETIME(), TransitionedBy = @user
WHERE Id = @documentId AND [Status] = 'DRAFT';
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        AddInt(command, "@documentId", documentId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Die Rechnung konnte nicht atomar definitiv gesetzt werden.");
    }

    private static async Task<string> LoadBaseCurrencyCodeAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT BaseCurrency
FROM dbo.FakturierungEinstellung WITH (UPDLOCK, HOLDLOCK)
WHERE Id = 1;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not string value || string.IsNullOrWhiteSpace(value))
            throw Validation("Die Basiswährung ist nicht eingerichtet.");
        return value.Trim();
    }

    private static async Task<int> InsertNextInvoiceDocumentAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int sourceDocumentId,
        int flowSequence,
        string documentNumber,
        DateOnly documentDate,
        string invoiceKind,
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
SELECT
    FlowId, @flowSequence, 'INVOICE', @number, @date, 'DRAFT', Subject,
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
        AddInt(command, "@flowSequence", flowSequence);
        AddText(command, "@number", SqlDbType.NVarChar, 40, documentNumber);
        AddDate(command, "@date", documentDate);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        AddInt(command, "@sourceId", sourceDocumentId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
        {
            throw new InvalidOperationException(
                $"Die {InvoicingInvoiceKindCodes.DisplayName(invoiceKind)} konnte nicht angelegt werden.");
        }
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<int> InsertAdjustmentDocumentAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int targetDocumentId,
        int previousDocumentId,
        int flowSequence,
        string documentNumber,
        DateOnly documentDate,
        InvoicingAdjustmentDraft draft,
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
    CurrencyCode, ExchangeRateToBase, ExchangeRateSource, PreviousDocumentId,
    CreatedBy, TransitionedAt, TransitionedBy
)
OUTPUT INSERTED.Id
SELECT
    FlowId, @flowSequence, 'CORRECTION', @number, @date, 'DEFINITIVE',
    LEFT(CONCAT(@subjectPrefix, DocumentNumber, N': ', Subject), 240),
    ContextSource, ContextSourceId, ContextTitleSnapshot, ContextSubtitleSnapshot,
    IssuerName, IssuerStreet, IssuerPostalCode, IssuerCity, IssuerCountryCode,
    IssuerVatNumber, IssuerEmail, IssuerPhone,
    RecipientAddressIdSnapshot, RecipientKind, RecipientName, RecipientStreet,
    RecipientPostalCode, RecipientCity, RecipientCountry,
    CurrencyCode, ExchangeRateToBase, ExchangeRateSource, @previousId,
    @user, SYSDATETIME(), @user
FROM dbo.FakturierungDokument WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @targetId;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@flowSequence", flowSequence);
        AddText(command, "@number", SqlDbType.NVarChar, 40, documentNumber);
        AddDate(command, "@date", documentDate);
        AddText(
            command,
            "@subjectPrefix",
            SqlDbType.NVarChar,
            40,
            $"{InvoicingInvoiceKindCodes.DisplayName(draft.AdjustmentKind)} zu ");
        AddInt(command, "@previousId", previousDocumentId);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        AddInt(command, "@targetId", targetDocumentId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
            throw new InvalidOperationException("Der Korrekturbeleg konnte nicht angelegt werden.");
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task InsertAdjustmentFinancialRecordAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int adjustmentDocumentId,
        BillingSnapshot target,
        InvoicingAdjustmentDraft draft,
        decimal amount,
        decimal baseAmount,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT dbo.FakturierungRechnung
(
    DocumentId, InvoiceKind, ReferenceInvoiceDocumentId, AdjustmentReason,
    FullGrossBasis, PreviouslyInvoicedGross, NetAmount, VatAmount,
    DiscountPercent, DiscountAmount, RoundingAdjustment, GrossAmount,
    BaseCurrencyCode, BaseGrossAmount, PaymentDays, DueDate,
    SkontoPercent, SkontoDays, SkontoDueDate, SkontoAmount,
    PaymentReference, FinalizedAt, FinalizedBy
)
VALUES
(
    @documentId, @kind, @referenceId, @reason,
    @fullBasis, @previouslyInvoiced, -@amount, 0,
    0, 0, 0, -@amount,
    @baseCurrency, -@baseAmount, NULL, NULL,
    NULL, NULL, NULL, NULL,
    NULL, SYSDATETIME(), @user
);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddInt(command, "@documentId", adjustmentDocumentId);
        AddText(command, "@kind", SqlDbType.VarChar, 16, draft.AdjustmentKind);
        AddInt(command, "@referenceId", draft.ReferenceInvoiceDocumentId);
        AddText(command, "@reason", SqlDbType.NVarChar, 500, draft.Reason);
        AddDecimal(command, "@fullBasis", target.FullGrossBasis, 19, 2);
        AddDecimal(command, "@previouslyInvoiced", target.PreviouslyInvoicedGross, 19, 2);
        AddDecimal(command, "@amount", amount, 19, 2);
        AddText(command, "@baseCurrency", SqlDbType.Char, 3, target.BaseCurrencyCode);
        AddDecimal(command, "@baseAmount", baseAmount, 19, 2);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Der Korrektur-Finanzsnapshot konnte nicht gespeichert werden.");
    }

    private static async Task ApplyAdjustmentToOpenItemAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        OpenItemSnapshot openItem,
        decimal amount,
        decimal baseAmount,
        CancellationToken cancellationToken)
    {
        var newCorrection = openItem.CorrectionAmount - amount;
        var newOpen = openItem.OpenAmount - amount;
        var newBaseCorrection = openItem.BaseCorrectionAmount - baseAmount;
        var newBaseOpen = openItem.BaseOpenAmount - baseAmount;
        var status = newOpen == 0m
            ? openItem.PaidAmount > 0m
                ? InvoicingOpenItemStatusCodes.Paid
                : InvoicingOpenItemStatusCodes.Cancelled
            : openItem.PaidAmount > 0m
                ? InvoicingOpenItemStatusCodes.PartiallyPaid
                : InvoicingOpenItemStatusCodes.Corrected;

        const string sql = """
UPDATE dbo.FakturierungOffenerPosten
SET CorrectionAmount = @correction,
    OpenAmount = @open,
    BaseCorrectionAmount = @baseCorrection,
    BaseOpenAmount = @baseOpen,
    [Status] = @status,
    UpdatedAt = SYSDATETIME()
WHERE DocumentId = @documentId
  AND CorrectionAmount = @expectedCorrection
  AND OpenAmount = @expectedOpen
  AND BaseCorrectionAmount = @expectedBaseCorrection
  AND BaseOpenAmount = @expectedBaseOpen;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddDecimal(command, "@correction", newCorrection, 19, 2);
        AddDecimal(command, "@open", newOpen, 19, 2);
        AddDecimal(command, "@baseCorrection", newBaseCorrection, 19, 2);
        AddDecimal(command, "@baseOpen", newBaseOpen, 19, 2);
        AddText(command, "@status", SqlDbType.VarChar, 24, status);
        AddInt(command, "@documentId", documentId);
        AddDecimal(command, "@expectedCorrection", openItem.CorrectionAmount, 19, 2);
        AddDecimal(command, "@expectedOpen", openItem.OpenAmount, 19, 2);
        AddDecimal(command, "@expectedBaseCorrection", openItem.BaseCorrectionAmount, 19, 2);
        AddDecimal(command, "@expectedBaseOpen", openItem.BaseOpenAmount, 19, 2);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Der offene Posten konnte nicht atomar korrigiert werden.");
    }

    private static InvoicingInvoiceValidationException Validation(string message) => new([message]);

    private static void AddInt(SqlCommand command, string name, int value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Int) { Value = value });

    private static void AddNullableInt(SqlCommand command, string name, int? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Int)
        {
            Value = value.HasValue ? value.Value : DBNull.Value
        });

    private static void AddSmallInt(SqlCommand command, string name, int value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.SmallInt) { Value = value });

    private static void AddNullableSmallInt(SqlCommand command, string name, int? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.SmallInt)
        {
            Value = value.HasValue ? value.Value : DBNull.Value
        });

    private static void AddDate(SqlCommand command, string name, DateOnly value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Date)
        {
            Value = value.ToDateTime(TimeOnly.MinValue)
        });

    private static void AddNullableDate(SqlCommand command, string name, DateOnly? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Date)
        {
            Value = value.HasValue
                ? value.Value.ToDateTime(TimeOnly.MinValue)
                : DBNull.Value
        });

    private static void AddDecimal(
        SqlCommand command,
        string name,
        decimal value,
        byte precision,
        byte scale) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Decimal)
        {
            Precision = precision,
            Scale = scale,
            Value = value
        });

    private static void AddNullableDecimal(
        SqlCommand command,
        string name,
        decimal? value,
        byte precision,
        byte scale) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Decimal)
        {
            Precision = precision,
            Scale = scale,
            Value = value.HasValue ? value.Value : DBNull.Value
        });

    private static void AddText(
        SqlCommand command,
        string name,
        SqlDbType type,
        int size,
        string value) =>
        command.Parameters.Add(new SqlParameter(name, type, size) { Value = value });

    private static void AddNullableText(
        SqlCommand command,
        string name,
        SqlDbType type,
        int size,
        string? value) =>
        command.Parameters.Add(new SqlParameter(name, type, size)
        {
            Value = value is null ? DBNull.Value : value
        });

    private sealed record InvoiceDocumentSnapshot(
        int Id,
        Guid FlowId,
        int FlowSequence,
        string DocumentType,
        string DocumentNumber,
        DateOnly DocumentDate,
        string Status,
        string Subject,
        string CurrencyCode,
        decimal ExchangeRateToBase,
        int? NextDocumentId);

    private sealed record BillingSnapshot(
        int DocumentId,
        int FlowSequence,
        string InvoiceKind,
        decimal FullGrossBasis,
        decimal PreviouslyInvoicedGross,
        decimal DiscountPercent,
        decimal GrossAmount,
        string BaseCurrencyCode);

    private sealed record InvoiceSettingsSnapshot(
        string BaseCurrencyCode,
        int DefaultPaymentDays);

    private sealed record OpenItemSnapshot(
        decimal OriginalAmount,
        decimal CorrectionAmount,
        decimal PaidAmount,
        decimal OpenAmount,
        decimal BaseOriginalAmount,
        decimal BaseCorrectionAmount,
        decimal BasePaidAmount,
        decimal BaseOpenAmount);

    private sealed record FlowTailSnapshot(int Id, int FlowSequence, string Status);
}
