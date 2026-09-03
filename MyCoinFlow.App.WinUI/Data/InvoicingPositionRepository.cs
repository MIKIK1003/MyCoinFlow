using Microsoft.Data.SqlClient;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using System.Data;
using System.Globalization;

namespace MyCoinFlow.WinUI.Data;

public sealed class InvoicingPositionRepository
{
    private readonly InvoicingMasterDataRepository _masterDataRepository;

    public InvoicingPositionRepository(InvoicingMasterDataRepository? masterDataRepository = null)
    {
        _masterDataRepository = masterDataRepository ?? new InvoicingMasterDataRepository();
    }

    public async Task<InvoicingComposerWorkspace> LoadWorkspaceAsync(
        string contextSource,
        int contextSourceId,
        string contextTitle,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        var normalizedSource = NormalizeContextSource(contextSource);
        await InvoicingSchema.EnsureAsync(cancellationToken);
        var masterData = await _masterDataRepository.LoadAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await RequireContextAsync(
            connection,
            transaction: null,
            normalizedSource,
            contextSourceId,
            cancellationToken);
        var templates = await LoadTemplatesAsync(connection, includeInactive: false, cancellationToken);
        var positions = await LoadPositionsAsync(
            connection,
            normalizedSource,
            contextSourceId,
            cancellationToken);
        return new InvoicingComposerWorkspace(
            normalizedSource,
            contextSourceId,
            contextTitle.Trim(),
            masterData.Articles.Where(article => article.IsActive).ToList(),
            masterData.VatOptions,
            masterData.RevenueAccountOptions,
            templates,
            positions);
    }

    public async Task<IReadOnlyList<InvoicingTextTemplateRecord>> LoadTextTemplatesAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        return await LoadTemplatesAsync(connection, includeInactive, cancellationToken);
    }

    public async Task<int> SaveTextTemplateAsync(
        InvoicingTextTemplateDraft draft,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        InvoicingPositionValidator.ValidateAndNormalize(draft);
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            await using (var duplicateCommand = new SqlCommand(
                """
SELECT COUNT(*)
FROM dbo.FakturierungTextbaustein WITH (UPDLOCK, HOLDLOCK)
WHERE [Name] = @name AND Id <> @id;
""",
                connection,
                transaction))
            {
                AddText(duplicateCommand, "@name", SqlDbType.NVarChar, 160, draft.Name);
                duplicateCommand.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = draft.Id });
                if (Convert.ToInt32(
                        await duplicateCommand.ExecuteScalarAsync(cancellationToken),
                        CultureInfo.InvariantCulture) > 0)
                    throw new InvoicingPositionValidationException(
                        [$"Der Textbaustein «{draft.Name}» ist bereits vorhanden."]);
            }

            int id;
            if (draft.Id == 0)
            {
                await using var command = CreateTemplateCommand(
                    """
INSERT dbo.FakturierungTextbaustein
    ([Name], PlainText, FormattedText, IsActive, UpdatedAt, UpdatedBy)
OUTPUT INSERTED.Id
VALUES
    (@name, @plain, @formatted, @active, SYSDATETIME(), @user);
""",
                    connection,
                    transaction,
                    draft);
                id = Convert.ToInt32(
                    await command.ExecuteScalarAsync(cancellationToken),
                    CultureInfo.InvariantCulture);
            }
            else
            {
                await using var command = CreateTemplateCommand(
                    """
UPDATE dbo.FakturierungTextbaustein
SET [Name] = @name,
    PlainText = @plain,
    FormattedText = @formatted,
    IsActive = @active,
    UpdatedAt = SYSDATETIME(),
    UpdatedBy = @user
WHERE Id = @id;
""",
                    connection,
                    transaction,
                    draft);
                command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = draft.Id });
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("Der Textbaustein wurde zwischenzeitlich entfernt.");
                id = draft.Id;
            }

            await transaction.CommitAsync(cancellationToken);
            return id;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new InvoicingPositionValidationException(
                [$"Der Textbaustein «{draft.Name}» ist bereits vorhanden."]);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<int> SavePositionAsync(
        InvoicingPositionDraft draft,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        InvoicingPositionValidator.ValidateAndNormalize(draft);
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            await RequireContextAsync(
                connection,
                transaction,
                draft.ContextSource,
                draft.ContextSourceId,
                cancellationToken);
            if (draft.PositionType == InvoicingPositionTypes.Article)
                await RefreshFinancialSnapshotsAsync(connection, transaction, draft, cancellationToken);

            int id;
            if (draft.Id == 0)
            {
                draft.SequenceNumber = await GetNextSequenceNumberAsync(
                    connection,
                    transaction,
                    draft.ContextSource,
                    draft.ContextSourceId,
                    cancellationToken);
                await using var command = CreatePositionCommand(
                    """
INSERT dbo.FakturierungPositionsentwurf
    (ContextSource, ContextSourceId, SequenceNumber, PositionType, ArticleId,
     Designation, Category, Unit, Quantity, UnitPrice, VatRateId, VatCodeSnapshot,
     VatRatePercentSnapshot, RevenueAccountId, RevenueAccountSnapshot,
     AncillaryClassificationSnapshot, MainTextPlain, MainTextFormatted,
     AdditionalTextPlain, AdditionalTextFormatted, IsFooter, UpdatedAt, UpdatedBy)
OUTPUT INSERTED.Id
VALUES
    (@contextSource, @contextId, @sequence, @positionType, @articleId,
     @designation, @category, @unit, @quantity, @unitPrice, @vatId, @vatCode,
     @vatRate, @revenueId, @revenueSnapshot, @ancillary, @mainPlain, @mainFormatted,
     @additionalPlain, @additionalFormatted, @footer, SYSDATETIME(), @user);
""",
                    connection,
                    transaction,
                    draft);
                id = Convert.ToInt32(
                    await command.ExecuteScalarAsync(cancellationToken),
                    CultureInfo.InvariantCulture);
            }
            else
            {
                await using var command = CreatePositionCommand(
                    """
UPDATE dbo.FakturierungPositionsentwurf
SET PositionType = @positionType,
    ArticleId = @articleId,
    Designation = @designation,
    Category = @category,
    Unit = @unit,
    Quantity = @quantity,
    UnitPrice = @unitPrice,
    VatRateId = @vatId,
    VatCodeSnapshot = @vatCode,
    VatRatePercentSnapshot = @vatRate,
    RevenueAccountId = @revenueId,
    RevenueAccountSnapshot = @revenueSnapshot,
    AncillaryClassificationSnapshot = @ancillary,
    MainTextPlain = @mainPlain,
    MainTextFormatted = @mainFormatted,
    AdditionalTextPlain = @additionalPlain,
    AdditionalTextFormatted = @additionalFormatted,
    IsFooter = @footer,
    UpdatedAt = SYSDATETIME(),
    UpdatedBy = @user
WHERE Id = @id
  AND ContextSource = @contextSource
  AND ContextSourceId = @contextId;
""",
                    connection,
                    transaction,
                    draft);
                command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = draft.Id });
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException(
                        "Die Position wurde zwischenzeitlich entfernt oder gehört zu einem anderen Objektkontext.");
                id = draft.Id;
            }

            await NormalizeOrderAsync(
                connection,
                transaction,
                draft.ContextSource,
                draft.ContextSourceId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return id;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task DeletePositionAsync(
        string contextSource,
        int contextSourceId,
        int positionId,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        var normalizedSource = NormalizeContextSource(contextSource);
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            await RequireContextAsync(
                connection,
                transaction,
                normalizedSource,
                contextSourceId,
                cancellationToken);
            await using var command = new SqlCommand(
                """
DELETE dbo.FakturierungPositionsentwurf
WHERE Id = @id
  AND ContextSource = @contextSource
  AND ContextSourceId = @contextId;
""",
                connection,
                transaction);
            command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = positionId });
            AddText(command, "@contextSource", SqlDbType.VarChar, 16, normalizedSource);
            command.Parameters.Add(new SqlParameter("@contextId", SqlDbType.Int) { Value = contextSourceId });
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException(
                    "Die Position wurde zwischenzeitlich entfernt oder gehört zu einem anderen Objektkontext.");
            await NormalizeOrderAsync(
                connection,
                transaction,
                normalizedSource,
                contextSourceId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task MovePositionAsync(
        string contextSource,
        int contextSourceId,
        int positionId,
        int direction,
        CancellationToken cancellationToken = default)
    {
        if (direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(direction));

        InvoicingSchema.RequireAuthenticated();
        var normalizedSource = NormalizeContextSource(contextSource);
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            await RequireContextAsync(
                connection,
                transaction,
                normalizedSource,
                contextSourceId,
                cancellationToken);
            var order = await LoadOrderAsync(
                connection,
                transaction,
                normalizedSource,
                contextSourceId,
                cancellationToken);
            var index = order.FindIndex(item => item.Id == positionId);
            if (index < 0)
                throw new InvalidOperationException(
                    "Die Position wurde zwischenzeitlich entfernt oder gehört zu einem anderen Objektkontext.");
            var targetIndex = index + direction;
            if (targetIndex < 0 || targetIndex >= order.Count || order[targetIndex].IsFooter != order[index].IsFooter)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            (order[index], order[targetIndex]) = (order[targetIndex], order[index]);
            await WriteOrderAsync(
                connection,
                transaction,
                normalizedSource,
                contextSourceId,
                order.Select(item => item.Id).ToList(),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<IReadOnlyList<InvoicingTextTemplateRecord>> LoadTemplatesAsync(
        SqlConnection connection,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, [Name], PlainText, FormattedText, IsActive
FROM dbo.FakturierungTextbaustein
WHERE @includeInactive = 1 OR IsActive = 1
ORDER BY IsActive DESC, [Name], Id;
""";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@includeInactive", SqlDbType.Bit)
        {
            Value = includeInactive
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingTextTemplateRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new InvoicingTextTemplateRecord(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<InvoicingPositionRecord>> LoadPositionsAsync(
        SqlConnection connection,
        string contextSource,
        int contextSourceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, ContextSource, ContextSourceId, SequenceNumber, PositionType, ArticleId,
       Designation, Category, Unit, Quantity, UnitPrice, VatRateId, VatCodeSnapshot,
       VatRatePercentSnapshot, RevenueAccountId, RevenueAccountSnapshot,
       AncillaryClassificationSnapshot, MainTextPlain, MainTextFormatted,
       AdditionalTextPlain, AdditionalTextFormatted, IsFooter
FROM dbo.FakturierungPositionsentwurf
WHERE ContextSource = @contextSource AND ContextSourceId = @contextId
ORDER BY IsFooter, SequenceNumber, Id;
""";
        await using var command = new SqlCommand(sql, connection);
        AddText(command, "@contextSource", SqlDbType.VarChar, 16, contextSource);
        command.Parameters.Add(new SqlParameter("@contextId", SqlDbType.Int) { Value = contextSourceId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingPositionRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new InvoicingPositionRecord(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                GetNullableInt32(reader, 5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                GetNullableInt32(reader, 11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetDecimal(13),
                GetNullableInt32(reader, 14),
                reader.GetString(15),
                reader.GetString(16),
                reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetString(20),
                reader.GetBoolean(21)));
        }
        return result;
    }

    private static async Task RequireContextAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string contextSource,
        int contextSourceId,
        CancellationToken cancellationToken)
    {
        var sql = contextSource switch
        {
            InvoicingPositionTypes.Article =>
                "SELECT COUNT(*) FROM dbo.FakturierungArtikel WHERE Id = @id;",
            "PROPERTY" =>
                "SELECT COUNT(*) FROM dbo.StweEinheit WHERE Id = @id;",
            _ => throw new InvoicingPositionValidationException(
                ["Der stabile Fakturierungskontext ist ungültig."])
        };
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = contextSourceId });
        if (Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture) != 1)
            throw new InvoicingPositionValidationException(
                ["Das ausgewählte fakturierbare Objekt ist nicht mehr vorhanden."]);
    }

    private static async Task RefreshFinancialSnapshotsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        InvoicingPositionDraft draft,
        CancellationToken cancellationToken)
    {
        if (draft.ArticleId.HasValue)
        {
            await using var articleCommand = new SqlCommand(
                "SELECT AncillaryClassification FROM dbo.FakturierungArtikel WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id AND IsActive = 1;",
                connection,
                transaction);
            articleCommand.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = draft.ArticleId.Value });
            var ancillaryClassification =
                await articleCommand.ExecuteScalarAsync(cancellationToken) as string;
            if (string.IsNullOrWhiteSpace(ancillaryClassification))
                throw new InvoicingPositionValidationException(
                    ["Der gewählte aktive Artikel ist nicht mehr vorhanden."]);
            draft.AncillaryClassificationSnapshot = ancillaryClassification;
        }

        const string sql = """
SELECT vat.Code,
       vat.RatePercent,
       CONCAT(FORMAT(account.Kontonummer, '0000'), N' — ',
              COALESCE(NULLIF(account.Detail, N''), NULLIF(account.Untergruppe, N''),
                       NULLIF(account.Gruppe, N''), N'Konto'))
FROM dbo.FakturierungMwstSatz vat WITH (UPDLOCK, HOLDLOCK)
CROSS JOIN dbo.FakturierungErtragskonto revenue WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.Kontenplan account ON account.Id = revenue.AccountId
WHERE vat.Id = @vatId
  AND revenue.AccountId = @revenueId;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@vatId", SqlDbType.Int)
        {
            Value = draft.VatRateId ?? 0
        });
        command.Parameters.Add(new SqlParameter("@revenueId", SqlDbType.Int)
        {
            Value = draft.RevenueAccountId ?? 0
        });
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow,
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvoicingPositionValidationException(
                ["MWST oder zugelassenes Ertragskonto ist nicht mehr vorhanden."]);
        draft.VatCodeSnapshot = reader.GetString(0);
        draft.VatRatePercentSnapshot = reader.GetDecimal(1);
        draft.RevenueAccountSnapshot = reader.GetString(2);
    }

    private static SqlCommand CreateTemplateCommand(
        string sql,
        SqlConnection connection,
        SqlTransaction transaction,
        InvoicingTextTemplateDraft draft)
    {
        var command = new SqlCommand(sql, connection, transaction);
        AddText(command, "@name", SqlDbType.NVarChar, 160, draft.Name);
        AddLargeText(command, "@plain", draft.PlainText);
        AddNullableLargeText(command, "@formatted", draft.FormattedText);
        command.Parameters.Add(new SqlParameter("@active", SqlDbType.Bit)
        {
            Value = draft.IsActive
        });
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        return command;
    }

    private static SqlCommand CreatePositionCommand(
        string sql,
        SqlConnection connection,
        SqlTransaction transaction,
        InvoicingPositionDraft draft)
    {
        var command = new SqlCommand(sql, connection, transaction);
        AddText(command, "@contextSource", SqlDbType.VarChar, 16, draft.ContextSource);
        command.Parameters.Add(new SqlParameter("@contextId", SqlDbType.Int)
        {
            Value = draft.ContextSourceId
        });
        command.Parameters.Add(new SqlParameter("@sequence", SqlDbType.Int)
        {
            Value = draft.SequenceNumber
        });
        AddText(command, "@positionType", SqlDbType.VarChar, 16, draft.PositionType);
        AddNullableInt(command, "@articleId", draft.ArticleId);
        AddText(command, "@designation", SqlDbType.NVarChar, 200, draft.Designation);
        AddText(command, "@category", SqlDbType.NVarChar, 100, draft.Category);
        AddText(command, "@unit", SqlDbType.NVarChar, 40, draft.Unit);
        AddDecimal(command, "@quantity", 19, 4, draft.Quantity);
        AddDecimal(command, "@unitPrice", 19, 4, draft.UnitPrice);
        AddNullableInt(command, "@vatId", draft.VatRateId);
        AddText(command, "@vatCode", SqlDbType.NVarChar, 32, draft.VatCodeSnapshot);
        AddNullableDecimal(command, "@vatRate", 9, 4, draft.VatRatePercentSnapshot);
        AddNullableInt(command, "@revenueId", draft.RevenueAccountId);
        AddText(
            command,
            "@revenueSnapshot",
            SqlDbType.NVarChar,
            200,
            draft.RevenueAccountSnapshot);
        AddText(
            command,
            "@ancillary",
            SqlDbType.VarChar,
            32,
            draft.AncillaryClassificationSnapshot);
        AddLargeText(command, "@mainPlain", draft.MainTextPlain);
        AddNullableLargeText(command, "@mainFormatted", draft.MainTextFormatted);
        AddLargeText(command, "@additionalPlain", draft.AdditionalTextPlain);
        AddNullableLargeText(command, "@additionalFormatted", draft.AdditionalTextFormatted);
        command.Parameters.Add(new SqlParameter("@footer", SqlDbType.Bit)
        {
            Value = draft.IsFooter
        });
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        return command;
    }

    private static async Task<int> GetNextSequenceNumberAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string contextSource,
        int contextSourceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT COALESCE(MAX(SequenceNumber), 0) + 10
FROM dbo.FakturierungPositionsentwurf WITH (UPDLOCK, HOLDLOCK)
WHERE ContextSource = @contextSource AND ContextSourceId = @contextId;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddText(command, "@contextSource", SqlDbType.VarChar, 16, contextSource);
        command.Parameters.Add(new SqlParameter("@contextId", SqlDbType.Int)
        {
            Value = contextSourceId
        });
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static async Task NormalizeOrderAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string contextSource,
        int contextSourceId,
        CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(
            connection,
            transaction,
            contextSource,
            contextSourceId,
            cancellationToken);
        await WriteOrderAsync(
            connection,
            transaction,
            contextSource,
            contextSourceId,
            order.Select(item => item.Id).ToList(),
            cancellationToken);
    }

    private static async Task<List<PositionOrderItem>> LoadOrderAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string contextSource,
        int contextSourceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, IsFooter
FROM dbo.FakturierungPositionsentwurf WITH (UPDLOCK, HOLDLOCK)
WHERE ContextSource = @contextSource AND ContextSourceId = @contextId
ORDER BY IsFooter, SequenceNumber, Id;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddText(command, "@contextSource", SqlDbType.VarChar, 16, contextSource);
        command.Parameters.Add(new SqlParameter("@contextId", SqlDbType.Int)
        {
            Value = contextSourceId
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<PositionOrderItem>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new PositionOrderItem(reader.GetInt32(0), reader.GetBoolean(1)));
        return result;
    }

    private static async Task WriteOrderAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string contextSource,
        int contextSourceId,
        IReadOnlyList<int> positionIds,
        CancellationToken cancellationToken)
    {
        if (positionIds.Count == 0) return;

        await using (var offsetCommand = new SqlCommand(
            """
UPDATE dbo.FakturierungPositionsentwurf
SET SequenceNumber = SequenceNumber + 1000000
WHERE ContextSource = @contextSource AND ContextSourceId = @contextId;
""",
            connection,
            transaction))
        {
            AddText(offsetCommand, "@contextSource", SqlDbType.VarChar, 16, contextSource);
            offsetCommand.Parameters.Add(new SqlParameter("@contextId", SqlDbType.Int)
            {
                Value = contextSourceId
            });
            await offsetCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string sql = """
UPDATE dbo.FakturierungPositionsentwurf
SET SequenceNumber = @sequence
WHERE Id = @id
  AND ContextSource = @contextSource
  AND ContextSourceId = @contextId;
""";
        for (var index = 0; index < positionIds.Count; index++)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add(new SqlParameter("@sequence", SqlDbType.Int)
            {
                Value = (index + 1) * 10
            });
            command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int)
            {
                Value = positionIds[index]
            });
            AddText(command, "@contextSource", SqlDbType.VarChar, 16, contextSource);
            command.Parameters.Add(new SqlParameter("@contextId", SqlDbType.Int)
            {
                Value = contextSourceId
            });
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Die Positionsreihenfolge konnte nicht gespeichert werden.");
        }
    }

    private static string NormalizeContextSource(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized is not (InvoicingPositionTypes.Article or "PROPERTY"))
            throw new InvoicingPositionValidationException(
                ["Der stabile Fakturierungskontext ist ungültig."]);
        return normalized;
    }

    private static int? GetNullableInt32(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static void AddText(
        SqlCommand command,
        string name,
        SqlDbType type,
        int size,
        string value) =>
        command.Parameters.Add(new SqlParameter(name, type, size) { Value = value });

    private static void AddLargeText(SqlCommand command, string name, string value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, -1)
        {
            Value = value
        });

    private static void AddNullableLargeText(SqlCommand command, string name, string? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, -1)
        {
            Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value
        });

    private static void AddNullableInt(SqlCommand command, string name, int? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Int)
        {
            Value = value.HasValue ? value.Value : DBNull.Value
        });

    private static void AddDecimal(
        SqlCommand command,
        string name,
        byte precision,
        byte scale,
        decimal value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Decimal)
        {
            Precision = precision,
            Scale = scale,
            Value = value
        });

    private static void AddNullableDecimal(
        SqlCommand command,
        string name,
        byte precision,
        byte scale,
        decimal? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Decimal)
        {
            Precision = precision,
            Scale = scale,
            Value = value.HasValue ? value.Value : DBNull.Value
        });

    private sealed record PositionOrderItem(int Id, bool IsFooter);
}
