using Microsoft.Data.SqlClient;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using System.Data;
using System.Globalization;

namespace MyCoinFlow.WinUI.Data;

public sealed class InvoicingMasterDataRepository
{
    public async Task<InvoicingMasterDataSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);

        var articles = await LoadArticlesAsync(connection, cancellationToken);
        var unitProfiles = await LoadUnitProfilesAsync(connection, DateOnly.FromDateTime(DateTime.Today), cancellationToken);
        var vatOptions = await LoadVatOptionsAsync(connection, cancellationToken);
        var revenueOptions = await LoadRevenueAccountOptionsAsync(connection, cancellationToken);
        var addressOptions = await LoadAddressOptionsAsync(connection, cancellationToken);
        var ownerOptions = await LoadOwnerOptionsAsync(connection, cancellationToken);
        return new InvoicingMasterDataSnapshot(
            articles,
            unitProfiles,
            vatOptions,
            revenueOptions,
            addressOptions,
            ownerOptions);
    }

    public async Task<IReadOnlyList<InvoicingRevenueAccountOption>> LoadAccountCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        return await LoadAllAccountOptionsAsync(connection, cancellationToken);
    }

    public async Task<BillableObjectsWorkspace> LoadBillableObjectsAsync(
        DateOnly effectiveDate,
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);

        var objects = new List<BillableObjectRecord>();
        foreach (var article in await LoadArticlesAsync(connection, cancellationToken))
        {
            if (!article.IsActive) continue;
            objects.Add(new BillableObjectRecord(
                $"ARTICLE:{article.Id}",
                "ARTICLE",
                article.Id,
                $"{article.ArticleNumber} · {article.Designation}",
                $"{article.Category} · {article.PriceDisplay}",
                string.Empty,
                string.Empty,
                "Direktes Sach-/Leistungsobjekt",
                "Nicht anwendbar",
                null,
                "Empfänger wird erst im Dokumentkontext gewählt",
                "Dokumentempfänger",
                null,
                string.Empty,
                true,
                false,
                "Bereit",
                InvoicingAncillaryClassifications.CanBeOfferedToTenant(article.AncillaryClassification)
                    ? "Mögliche Mieter-Nebenkosten: Überwälzbarkeit und Vereinbarung vor Verwendung manuell prüfen."
                    : "Keine automatische Mieter-Nebenkostenzuordnung.",
                article.AncillaryClassification));
        }

        objects.AddRange(await LoadPropertyObjectsAsync(connection, effectiveDate, cancellationToken));

        var normalizedSearch = (searchText ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            objects = objects.Where(item => new[]
            {
                item.Title,
                item.Subtitle,
                item.PropertyName,
                item.UnitName,
                item.PeriodAndUsage,
                item.ResponsibleParty,
                item.Recipient,
                item.TenantRecipient,
                item.Status
            }.Any(value => value.Contains(normalizedSearch, StringComparison.CurrentCultureIgnoreCase))).ToList();
        }

        return new BillableObjectsWorkspace(
            effectiveDate,
            objects.OrderBy(item => item.SourceCode).ThenBy(item => item.Title).ToList());
    }

    public async Task<int> SaveArticleAsync(
        InvoicingArticleDraft draft,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        InvoicingMasterDataValidator.ValidateAndNormalize(draft);
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            await RequireReferenceAsync(
                connection,
                transaction,
                "SELECT COUNT(*) FROM dbo.FakturierungMwstSatz WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id AND IsActive = 1;",
                draft.VatRateId,
                "Der gewählte aktive MWST-Satz ist nicht mehr vorhanden.",
                cancellationToken);
            await RequireReferenceAsync(
                connection,
                transaction,
                """
SELECT COUNT(*)
FROM dbo.FakturierungErtragskonto revenue WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.Kontenplan account ON account.Id = revenue.AccountId
WHERE revenue.AccountId = @id;
""",
                draft.RevenueAccountId,
                "Das gewählte zugelassene Ertragskonto ist nicht mehr vorhanden.",
                cancellationToken);

            await using (var duplicateCommand = new SqlCommand(
                """
SELECT COUNT(*)
FROM dbo.FakturierungArtikel WITH (UPDLOCK, HOLDLOCK)
WHERE ArticleNumber = @number AND Id <> @id;
""",
                connection,
                transaction))
            {
                AddText(duplicateCommand, "@number", SqlDbType.NVarChar, 64, draft.ArticleNumber);
                duplicateCommand.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = draft.Id });
                if (Convert.ToInt32(await duplicateCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0)
                    throw new InvoicingMasterDataValidationException(
                        [$"Die normalisierte Artikelnummer '{draft.ArticleNumber}' ist bereits vorhanden."]);
            }

            int articleId;
            if (draft.Id == 0)
            {
                await using var command = CreateArticleCommand(
                    """
INSERT dbo.FakturierungArtikel
    (ArticleNumber, Designation, [Description], Unit, Category, IsActive, SalePrice,
     VatRateId, RevenueAccountId, AncillaryClassification, UpdatedAt, UpdatedBy)
OUTPUT INSERTED.Id
VALUES
    (@number, @designation, @description, @unit, @category, @active, @price,
     @vat, @revenue, @classification, SYSDATETIME(), @user);
""",
                    connection,
                    transaction,
                    draft);
                articleId = Convert.ToInt32(
                    await command.ExecuteScalarAsync(cancellationToken),
                    CultureInfo.InvariantCulture);
            }
            else
            {
                await using var command = CreateArticleCommand(
                    """
UPDATE dbo.FakturierungArtikel
SET ArticleNumber = @number,
    Designation = @designation,
    [Description] = @description,
    Unit = @unit,
    Category = @category,
    IsActive = @active,
    SalePrice = @price,
    VatRateId = @vat,
    RevenueAccountId = @revenue,
    AncillaryClassification = @classification,
    UpdatedAt = SYSDATETIME(),
    UpdatedBy = @user
WHERE Id = @id;
""",
                    connection,
                    transaction,
                    draft);
                command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = draft.Id });
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("Der Artikel wurde zwischenzeitlich entfernt.");
                articleId = draft.Id;
            }

            await transaction.CommitAsync(cancellationToken);
            return articleId;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new InvoicingMasterDataValidationException(
                [$"Die normalisierte Artikelnummer '{draft.ArticleNumber}' ist bereits vorhanden."]);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task RegisterRevenueAccountAsync(
        int accountId,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAdministrator();
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        const string sql = """
IF NOT EXISTS (SELECT 1 FROM dbo.Kontenplan WHERE Id = @id)
    THROW 51020, N'Das Kontenplan-Konto ist nicht mehr vorhanden.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.FakturierungErtragskonto WHERE AccountId = @id)
    INSERT dbo.FakturierungErtragskonto (AccountId, UpdatedAt, UpdatedBy)
    VALUES (@id, SYSDATETIME(), @user);
""";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = accountId });
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveUnitProfileAsync(
        InvoicingUnitProfileDraft draft,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        InvoicingMasterDataValidator.ValidateAndNormalize(draft);
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            await RequireReferenceAsync(
                connection,
                transaction,
                "SELECT COUNT(*) FROM dbo.StweEinheit WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id;",
                draft.UnitId,
                "Die gewählte Stockwerkeinheit ist nicht mehr vorhanden.",
                cancellationToken);
            await EnsureNoOverlapAsync(
                connection,
                transaction,
                "dbo.FakturierungEinheitNutzung",
                draft.UnitId,
                draft.UsageId,
                DateOnly.FromDateTime(draft.ValidFrom.Date),
                draft.ValidTo.HasValue ? DateOnly.FromDateTime(draft.ValidTo.Value.Date) : null,
                "Der Nutzungszeitraum überschneidet sich mit einer vorhandenen Nutzung.",
                cancellationToken);

            if (draft.OwnerId.HasValue && draft.OwnerBillingAddressId.HasValue)
            {
                await RequireOwnerAtDateAsync(
                    connection,
                    transaction,
                    draft.UnitId,
                    draft.OwnerId.Value,
                    DateOnly.FromDateTime(draft.ValidFrom.Date),
                    cancellationToken);
                await RequireReferenceAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM dbo.Adresse WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id;",
                    draft.OwnerBillingAddressId.Value,
                    "Die gewählte Eigentümer-Rechnungsadresse ist nicht mehr vorhanden.",
                    cancellationToken);
                await SaveOwnerProfileAsync(
                    connection,
                    transaction,
                    draft.OwnerId.Value,
                    draft.OwnerBillingAddressId.Value,
                    cancellationToken);
            }

            if (draft.UsageType == InvoicingUsageTypes.Rented)
            {
                await EnsureNoOverlapAsync(
                    connection,
                    transaction,
                    "dbo.FakturierungMietverhaeltnis",
                    draft.UnitId,
                    draft.TenancyId,
                    DateOnly.FromDateTime(draft.ValidFrom.Date),
                    draft.ValidTo.HasValue ? DateOnly.FromDateTime(draft.ValidTo.Value.Date) : null,
                    "Das Mietverhältnis überschneidet sich mit einem vorhandenen Mietverhältnis.",
                    cancellationToken);
                await RequireReferenceAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM dbo.Adresse WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id;",
                    draft.TenantAddressId!.Value,
                    "Die gewählte Mieteradresse ist nicht mehr vorhanden.",
                    cancellationToken);
                draft.TenancyId = await SaveTenancyAsync(connection, transaction, draft, cancellationToken);
            }

            draft.UsageId = await SaveUsageAsync(connection, transaction, draft, cancellationToken);

            if (draft.UsageType != InvoicingUsageTypes.Rented && draft.TenancyId > 0)
            {
                await using var deleteCommand = new SqlCommand(
                    "DELETE FROM dbo.FakturierungMietverhaeltnis WHERE Id = @id AND UnitId = @unitId;",
                    connection,
                    transaction);
                deleteCommand.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = draft.TenancyId });
                deleteCommand.Parameters.Add(new SqlParameter("@unitId", SqlDbType.Int) { Value = draft.UnitId });
                await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
                draft.TenancyId = 0;
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task DeleteUnitProfilePeriodAsync(
        int usageId,
        int? tenancyId,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var usageCommand = new SqlCommand(
                "DELETE FROM dbo.FakturierungEinheitNutzung WHERE Id = @id;",
                connection,
                transaction))
            {
                usageCommand.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = usageId });
                if (await usageCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("Der Nutzungszeitraum wurde zwischenzeitlich entfernt.");
            }

            if (tenancyId.HasValue)
            {
                await using var tenancyCommand = new SqlCommand(
                    "DELETE FROM dbo.FakturierungMietverhaeltnis WHERE Id = @id;",
                    connection,
                    transaction);
                tenancyCommand.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = tenancyId.Value });
                await tenancyCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static SqlCommand CreateArticleCommand(
        string sql,
        SqlConnection connection,
        SqlTransaction transaction,
        InvoicingArticleDraft draft)
    {
        var command = new SqlCommand(sql, connection, transaction);
        AddText(command, "@number", SqlDbType.NVarChar, 64, draft.ArticleNumber);
        AddText(command, "@designation", SqlDbType.NVarChar, 200, draft.Designation);
        AddText(command, "@description", SqlDbType.NVarChar, 2000, draft.Description);
        AddText(command, "@unit", SqlDbType.NVarChar, 40, draft.Unit);
        AddText(command, "@category", SqlDbType.NVarChar, 100, draft.Category);
        command.Parameters.Add(new SqlParameter("@active", SqlDbType.Bit) { Value = draft.IsActive });
        command.Parameters.Add(new SqlParameter("@price", SqlDbType.Decimal)
        {
            Precision = 19,
            Scale = 4,
            Value = draft.SalePrice
        });
        command.Parameters.Add(new SqlParameter("@vat", SqlDbType.Int) { Value = draft.VatRateId });
        command.Parameters.Add(new SqlParameter("@revenue", SqlDbType.Int) { Value = draft.RevenueAccountId });
        AddText(command, "@classification", SqlDbType.VarChar, 32, draft.AncillaryClassification);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        return command;
    }

    private static async Task<int> SaveUsageAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        InvoicingUnitProfileDraft draft,
        CancellationToken cancellationToken)
    {
        var sql = draft.UsageId == 0
            ? """
INSERT dbo.FakturierungEinheitNutzung
    (UnitId, UsageType, ValidFrom, ValidTo, UpdatedAt, UpdatedBy)
VALUES (@unitId, @type, @from, @to, SYSDATETIME(), @user);
SELECT CONVERT(int, SCOPE_IDENTITY());
"""
            : """
UPDATE dbo.FakturierungEinheitNutzung
SET UnitId = @unitId,
    UsageType = @type,
    ValidFrom = @from,
    ValidTo = @to,
    UpdatedAt = SYSDATETIME(),
    UpdatedBy = @user
WHERE Id = @id;
IF @@ROWCOUNT <> 1
    THROW 51021, N'Der Nutzungszeitraum wurde zwischenzeitlich entfernt.', 1;
SELECT @id;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@unitId", SqlDbType.Int) { Value = draft.UnitId });
        AddText(command, "@type", SqlDbType.VarChar, 24, draft.UsageType);
        command.Parameters.Add(new SqlParameter("@from", SqlDbType.Date) { Value = draft.ValidFrom.Date });
        command.Parameters.Add(new SqlParameter("@to", SqlDbType.Date)
        {
            Value = draft.ValidTo.HasValue ? draft.ValidTo.Value.Date : DBNull.Value
        });
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        if (draft.UsageId > 0)
            command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = draft.UsageId });
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null)
            throw new InvalidOperationException("Der Nutzungszeitraum wurde zwischenzeitlich entfernt.");
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<int> SaveTenancyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        InvoicingUnitProfileDraft draft,
        CancellationToken cancellationToken)
    {
        var sql = draft.TenancyId == 0
            ? """
INSERT dbo.FakturierungMietverhaeltnis
    (UnitId, TenantAddressId, ValidFrom, ValidTo, AncillaryMode, ContractReference,
     DirectBillingAllowed, DirectBillingApprovalReference, UpdatedAt, UpdatedBy)
VALUES
    (@unitId, @tenantAddressId, @from, @to, @mode, @contract,
     @directBilling, @approval, SYSDATETIME(), @user);
SELECT CONVERT(int, SCOPE_IDENTITY());
"""
            : """
UPDATE dbo.FakturierungMietverhaeltnis
SET UnitId = @unitId,
    TenantAddressId = @tenantAddressId,
    ValidFrom = @from,
    ValidTo = @to,
    AncillaryMode = @mode,
    ContractReference = @contract,
    DirectBillingAllowed = @directBilling,
    DirectBillingApprovalReference = @approval,
    UpdatedAt = SYSDATETIME(),
    UpdatedBy = @user
WHERE Id = @id;
IF @@ROWCOUNT <> 1
    THROW 51022, N'Das Mietverhältnis wurde zwischenzeitlich entfernt.', 1;
SELECT @id;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@unitId", SqlDbType.Int) { Value = draft.UnitId });
        command.Parameters.Add(new SqlParameter("@tenantAddressId", SqlDbType.Int)
        {
            Value = draft.TenantAddressId!.Value
        });
        command.Parameters.Add(new SqlParameter("@from", SqlDbType.Date) { Value = draft.ValidFrom.Date });
        command.Parameters.Add(new SqlParameter("@to", SqlDbType.Date)
        {
            Value = draft.ValidTo.HasValue ? draft.ValidTo.Value.Date : DBNull.Value
        });
        AddText(command, "@mode", SqlDbType.VarChar, 16, draft.AncillaryMode);
        AddText(command, "@contract", SqlDbType.NVarChar, 160, draft.ContractReference);
        command.Parameters.Add(new SqlParameter("@directBilling", SqlDbType.Bit)
        {
            Value = draft.DirectBillingAllowed
        });
        AddNullableText(
            command,
            "@approval",
            SqlDbType.NVarChar,
            240,
            draft.DirectBillingApprovalReference);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        if (draft.TenancyId > 0)
            command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = draft.TenancyId });
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null)
            throw new InvalidOperationException("Das Mietverhältnis wurde zwischenzeitlich entfernt.");
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task SaveOwnerProfileAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int ownerId,
        int billingAddressId,
        CancellationToken cancellationToken)
    {
        const string sql = """
UPDATE dbo.FakturierungEigentuemerProfil
SET BillingAddressId = @addressId,
    UpdatedAt = SYSDATETIME(),
    UpdatedBy = @user
WHERE OwnerId = @ownerId;
IF @@ROWCOUNT = 0
    INSERT dbo.FakturierungEigentuemerProfil
        (OwnerId, BillingAddressId, UpdatedAt, UpdatedBy)
    VALUES (@ownerId, @addressId, SYSDATETIME(), @user);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@ownerId", SqlDbType.Int) { Value = ownerId });
        command.Parameters.Add(new SqlParameter("@addressId", SqlDbType.Int) { Value = billingAddressId });
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureNoOverlapAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string tableName,
        int unitId,
        int excludedId,
        DateOnly validFrom,
        DateOnly? validTo,
        string message,
        CancellationToken cancellationToken)
    {
        var sql = $"""
SELECT COUNT(*)
FROM {tableName} WITH (UPDLOCK, HOLDLOCK)
WHERE UnitId = @unitId
  AND Id <> @id
  AND @from <= COALESCE(ValidTo, CONVERT(date, '99991231'))
  AND ValidFrom <= COALESCE(@to, CONVERT(date, '99991231'));
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@unitId", SqlDbType.Int) { Value = unitId });
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = excludedId });
        command.Parameters.Add(new SqlParameter("@from", SqlDbType.Date)
        {
            Value = validFrom.ToDateTime(TimeOnly.MinValue)
        });
        command.Parameters.Add(new SqlParameter("@to", SqlDbType.Date)
        {
            Value = validTo.HasValue ? validTo.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value
        });
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0)
            throw new InvoicingMasterDataValidationException([message]);
    }

    private static async Task RequireOwnerAtDateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int unitId,
        int ownerId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT CASE
    WHEN COUNT(*) = 1 AND MAX(EigentuemerId) = @ownerId THEN 1
    ELSE 0
END
FROM dbo.StweEinheitEigentum WITH (UPDLOCK, HOLDLOCK)
WHERE EinheitId = @unitId
  AND GueltigVon <= @date
  AND (GueltigBis IS NULL OR GueltigBis >= @date);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@unitId", SqlDbType.Int) { Value = unitId });
        command.Parameters.Add(new SqlParameter("@ownerId", SqlDbType.Int) { Value = ownerId });
        command.Parameters.Add(new SqlParameter("@date", SqlDbType.Date)
        {
            Value = effectiveDate.ToDateTime(TimeOnly.MinValue)
        });
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 1)
        {
            throw new InvoicingMasterDataValidationException(
                ["Der gewählte Eigentümer ist zu Beginn dieses Nutzungszeitraums nicht eindeutig der Einheit zugeordnet."]);
        }
    }

    private static async Task RequireReferenceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        int id,
        string message,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 1)
            throw new InvoicingMasterDataValidationException([message]);
    }

    private static async Task<IReadOnlyList<InvoicingArticleRecord>> LoadArticlesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT article.Id,
       article.ArticleNumber,
       article.Designation,
       article.[Description],
       article.Unit,
       article.Category,
       article.IsActive,
       article.SalePrice,
       article.VatRateId,
       CONCAT(vat.Code, N' · ', vat.DisplayName, N' · ', CONVERT(nvarchar(32), vat.RatePercent), N' %'),
       article.RevenueAccountId,
       CONCAT(FORMAT(account.Kontonummer, '0000'), N' — ',
              COALESCE(NULLIF(account.Detail, N''), NULLIF(account.Untergruppe, N''),
                       NULLIF(account.Gruppe, N''), N'Konto')),
       article.AncillaryClassification
FROM dbo.FakturierungArtikel article
JOIN dbo.FakturierungMwstSatz vat ON vat.Id = article.VatRateId
JOIN dbo.Kontenplan account ON account.Id = article.RevenueAccountId
ORDER BY article.ArticleNumber, article.Id;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingArticleRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new InvoicingArticleRecord(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetBoolean(6),
                reader.GetDecimal(7),
                reader.GetInt32(8),
                reader.GetString(9),
                reader.GetInt32(10),
                reader.GetString(11),
                reader.GetString(12)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<InvoicingVatOption>> LoadVatOptionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT vat.Id, vat.Code, vat.DisplayName, vat.RatePercent
FROM dbo.FakturierungMwstSatz vat
WHERE vat.IsActive = 1
   OR EXISTS (SELECT 1 FROM dbo.FakturierungArtikel article WHERE article.VatRateId = vat.Id)
ORDER BY vat.IsActive DESC, vat.Code, vat.ValidFrom DESC;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingVatOption>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new InvoicingVatOption(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDecimal(3)));
        return result;
    }

    private static async Task<IReadOnlyList<InvoicingRevenueAccountOption>> LoadRevenueAccountOptionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT account.Id, account.Kontonummer,
       COALESCE(NULLIF(account.Detail, N''), NULLIF(account.Untergruppe, N''),
                NULLIF(account.Gruppe, N''), N'Konto')
FROM dbo.FakturierungErtragskonto revenue
JOIN dbo.Kontenplan account ON account.Id = revenue.AccountId
ORDER BY account.Kontonummer, account.Id;
""";
        return await ReadAccountOptionsAsync(connection, sql, cancellationToken);
    }

    private static async Task<IReadOnlyList<InvoicingRevenueAccountOption>> LoadAllAccountOptionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, Kontonummer,
       COALESCE(NULLIF(Detail, N''), NULLIF(Untergruppe, N''), NULLIF(Gruppe, N''), N'Konto')
FROM dbo.Kontenplan
ORDER BY Kontonummer, Id;
""";
        return await ReadAccountOptionsAsync(connection, sql, cancellationToken);
    }

    private static async Task<IReadOnlyList<InvoicingRevenueAccountOption>> ReadAccountOptionsAsync(
        SqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingRevenueAccountOption>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new InvoicingRevenueAccountOption(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2)));
        return result;
    }

    private static async Task<IReadOnlyList<InvoicingAddressOption>> LoadAddressOptionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, Name, COALESCE(Strasse, N''), COALESCE(PLZ, N''), COALESCE(Ort, N'')
FROM dbo.Adresse
ORDER BY Name, Id;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingAddressOption>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new InvoicingAddressOption(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        return result;
    }

    private static async Task<IReadOnlyList<InvoicingUnitProfileRecord>> LoadUnitProfilesAsync(
        SqlConnection connection,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT property.Id,
       property.Name,
       unit.Id,
       unit.Bezeichnung,
       COALESCE(unit.Typ, N''),
       usagePeriod.Id,
       usagePeriod.UsageType,
       usagePeriod.ValidFrom,
       usagePeriod.ValidTo,
       CASE WHEN ownerState.OwnerCount = 1 THEN ownerState.OwnerId END,
       COALESCE(owner.Name, N''),
       ownerProfile.BillingAddressId,
       COALESCE(ownerAddress.Name, N''),
       tenancy.Id,
       tenancy.TenantAddressId,
       COALESCE(tenantAddress.Name, N''),
       tenancy.AncillaryMode,
       COALESCE(tenancy.ContractReference, N''),
       COALESCE(tenancy.DirectBillingAllowed, 0),
       COALESCE(tenancy.DirectBillingApprovalReference, N'')
FROM dbo.StweLiegenschaft property
JOIN dbo.StweEinheit unit ON unit.LiegenschaftId = property.Id
LEFT JOIN dbo.FakturierungEinheitNutzung usagePeriod ON usagePeriod.UnitId = unit.Id
OUTER APPLY
(
    SELECT COUNT(*) AS OwnerCount, MAX(ownership.EigentuemerId) AS OwnerId
    FROM dbo.StweEinheitEigentum ownership
    WHERE ownership.EinheitId = unit.Id
      AND ownership.GueltigVon <= COALESCE(usagePeriod.ValidFrom, @effectiveDate)
      AND (ownership.GueltigBis IS NULL OR ownership.GueltigBis >= COALESCE(usagePeriod.ValidFrom, @effectiveDate))
) ownerState
LEFT JOIN dbo.StweEigentuemer owner
  ON owner.Id = CASE WHEN ownerState.OwnerCount = 1 THEN ownerState.OwnerId END
LEFT JOIN dbo.FakturierungEigentuemerProfil ownerProfile ON ownerProfile.OwnerId = owner.Id
LEFT JOIN dbo.Adresse ownerAddress ON ownerAddress.Id = ownerProfile.BillingAddressId
OUTER APPLY
(
    SELECT TOP (1)
           rental.Id,
           rental.TenantAddressId,
           rental.AncillaryMode,
           rental.ContractReference,
           rental.DirectBillingAllowed,
           rental.DirectBillingApprovalReference
    FROM dbo.FakturierungMietverhaeltnis rental
    WHERE usagePeriod.Id IS NOT NULL
      AND usagePeriod.UsageType = 'RENTED'
      AND rental.UnitId = unit.Id
      AND rental.ValidFrom <= usagePeriod.ValidFrom
      AND COALESCE(rental.ValidTo, CONVERT(date, '99991231'))
          >= COALESCE(usagePeriod.ValidTo, CONVERT(date, '99991231'))
    ORDER BY rental.ValidFrom DESC, rental.Id DESC
) tenancy
LEFT JOIN dbo.Adresse tenantAddress ON tenantAddress.Id = tenancy.TenantAddressId
ORDER BY property.Name, property.Id, unit.Bezeichnung, unit.Id,
         usagePeriod.ValidFrom DESC, usagePeriod.Id DESC;
""";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@effectiveDate", SqlDbType.Date)
        {
            Value = effectiveDate.ToDateTime(TimeOnly.MinValue)
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingUnitProfileRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new InvoicingUnitProfileRecord(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                GetNullableInt32(reader, 5),
                GetNullableString(reader, 6),
                GetNullableDateOnly(reader, 7),
                GetNullableDateOnly(reader, 8),
                GetNullableInt32(reader, 9),
                reader.GetString(10),
                GetNullableInt32(reader, 11),
                reader.GetString(12),
                GetNullableInt32(reader, 13),
                GetNullableInt32(reader, 14),
                reader.GetString(15),
                GetNullableString(reader, 16),
                reader.GetString(17),
                GetBoolean(reader, 18),
                reader.GetString(19)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<InvoicingOwnerOption>> LoadOwnerOptionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT owner.Id,
       owner.Name,
       ownerProfile.BillingAddressId,
       COALESCE(address.Name, N'')
FROM dbo.StweEigentuemer owner
LEFT JOIN dbo.FakturierungEigentuemerProfil ownerProfile ON ownerProfile.OwnerId = owner.Id
LEFT JOIN dbo.Adresse address ON address.Id = ownerProfile.BillingAddressId
ORDER BY owner.Name, owner.Id;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<InvoicingOwnerOption>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new InvoicingOwnerOption(
                reader.GetInt32(0),
                reader.GetString(1),
                GetNullableInt32(reader, 2),
                reader.GetString(3)));
        return result;
    }

    private static async Task<IReadOnlyList<BillableObjectRecord>> LoadPropertyObjectsAsync(
        SqlConnection connection,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT property.Id,
       property.Name,
       unit.Id,
       unit.Bezeichnung,
       COALESCE(unit.Typ, N''),
       usagePeriod.Id,
       usagePeriod.UsageType,
       usagePeriod.ValidFrom,
       usagePeriod.ValidTo,
       ownerState.OwnerCount,
       CASE WHEN ownerState.OwnerCount = 1 THEN ownerState.OwnerId END,
       COALESCE(owner.Name, N''),
       ownerProfile.BillingAddressId,
       COALESCE(ownerAddress.Name, N''),
       tenancy.Id,
       tenancy.TenantAddressId,
       COALESCE(tenantAddress.Name, N''),
       tenancy.AncillaryMode,
       COALESCE(tenancy.ContractReference, N''),
       COALESCE(tenancy.DirectBillingAllowed, 0),
       COALESCE(tenancy.DirectBillingApprovalReference, N'')
FROM dbo.StweLiegenschaft property
JOIN dbo.StweEinheit unit ON unit.LiegenschaftId = property.Id
OUTER APPLY
(
    SELECT TOP (1)
           period.Id,
           period.UsageType,
           period.ValidFrom,
           period.ValidTo
    FROM dbo.FakturierungEinheitNutzung period
    WHERE period.UnitId = unit.Id
      AND period.ValidFrom <= @effectiveDate
      AND (period.ValidTo IS NULL OR period.ValidTo >= @effectiveDate)
    ORDER BY period.ValidFrom DESC, period.Id DESC
) usagePeriod
OUTER APPLY
(
    SELECT COUNT(*) AS OwnerCount, MAX(ownership.EigentuemerId) AS OwnerId
    FROM dbo.StweEinheitEigentum ownership
    WHERE ownership.EinheitId = unit.Id
      AND ownership.GueltigVon <= @effectiveDate
      AND (ownership.GueltigBis IS NULL OR ownership.GueltigBis >= @effectiveDate)
) ownerState
LEFT JOIN dbo.StweEigentuemer owner
  ON owner.Id = CASE WHEN ownerState.OwnerCount = 1 THEN ownerState.OwnerId END
LEFT JOIN dbo.FakturierungEigentuemerProfil ownerProfile ON ownerProfile.OwnerId = owner.Id
LEFT JOIN dbo.Adresse ownerAddress ON ownerAddress.Id = ownerProfile.BillingAddressId
OUTER APPLY
(
    SELECT TOP (1)
           rental.Id,
           rental.TenantAddressId,
           rental.AncillaryMode,
           rental.ContractReference,
           rental.DirectBillingAllowed,
           rental.DirectBillingApprovalReference
    FROM dbo.FakturierungMietverhaeltnis rental
    WHERE rental.UnitId = unit.Id
      AND rental.ValidFrom <= @effectiveDate
      AND (rental.ValidTo IS NULL OR rental.ValidTo >= @effectiveDate)
    ORDER BY rental.ValidFrom DESC, rental.Id DESC
) tenancy
LEFT JOIN dbo.Adresse tenantAddress ON tenantAddress.Id = tenancy.TenantAddressId
ORDER BY property.Name, property.Id, unit.Bezeichnung, unit.Id;
""";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@effectiveDate", SqlDbType.Date)
        {
            Value = effectiveDate.ToDateTime(TimeOnly.MinValue)
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<BillableObjectRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var propertyId = reader.GetInt32(0);
            var propertyName = reader.GetString(1);
            var unitId = reader.GetInt32(2);
            var unitName = reader.GetString(3);
            var unitType = reader.GetString(4);
            var usageId = GetNullableInt32(reader, 5);
            var usageType = GetNullableString(reader, 6);
            var validFrom = GetNullableDateOnly(reader, 7);
            var validTo = GetNullableDateOnly(reader, 8);
            var ownerCount = reader.GetInt32(9);
            var ownerName = reader.GetString(11);
            var ownerAddressId = GetNullableInt32(reader, 12);
            var ownerAddressName = reader.GetString(13);
            var tenancyId = GetNullableInt32(reader, 14);
            var tenantAddressId = GetNullableInt32(reader, 15);
            var tenantAddressName = reader.GetString(16);
            var ancillaryMode = GetNullableString(reader, 17);
            var contractReference = reader.GetString(18);
            var directBillingAllowed = GetBoolean(reader, 19);
            var approvalReference = reader.GetString(20);

            var tenantAvailable =
                usageType == InvoicingUsageTypes.Rented &&
                tenancyId.HasValue &&
                tenantAddressId.HasValue &&
                directBillingAllowed &&
                ancillaryMode is InvoicingAncillaryModes.Advance or InvoicingAncillaryModes.FlatRate &&
                !string.IsNullOrWhiteSpace(contractReference) &&
                !string.IsNullOrWhiteSpace(approvalReference);
            var selectable = usageId.HasValue && ownerCount == 1 && ownerAddressId.HasValue;
            var status = !usageId.HasValue
                ? "Nutzung zum Stichtag fehlt"
                : ownerCount == 0
                    ? "Eigentümer zum Stichtag fehlt"
                    : ownerCount > 1
                        ? "Eigentümerzuordnung zum Stichtag ist mehrdeutig"
                        : !ownerAddressId.HasValue
                            ? "Eigentümer-Rechnungsadresse fehlt"
                            : tenantAvailable
                                ? "Bereit · Eigentümer ist Standard, Mieter nach dokumentierter Prüfung wählbar"
                                : "Bereit · Eigentümer ist sicherer Standardempfänger";
            var periodAndUsage = usageId.HasValue
                ? $"{FormatDateRange(validFrom, validTo)} · {InvoicingUsageTypes.DisplayName(usageType)}"
                : $"{effectiveDate:dd.MM.yyyy} · Nutzung nicht erfasst";

            result.Add(new BillableObjectRecord(
                $"PROPERTY:{unitId}",
                "PROPERTY",
                unitId,
                $"{propertyName} → {unitName}",
                string.IsNullOrWhiteSpace(unitType) ? "Stockwerkeinheit" : unitType,
                propertyName,
                unitName,
                periodAndUsage,
                ownerCount == 1 ? ownerName : status,
                ownerAddressId,
                ownerAddressName,
                "Eigentümer · sicherer Standard",
                tenantAvailable ? tenantAddressId : null,
                tenantAvailable ? tenantAddressName : string.Empty,
                selectable,
                tenantAvailable,
                status,
                "Prüfrahmen: Art. 712h ZGB, Art. 257a/257b OR, VMWG und BWO-Hinweise zur jährlichen Akontoabrechnung. " +
                "Die Software trifft keine automatische Rechtsentscheidung; Reparaturen, Erneuerungen und nicht überwälzbare Kosten bleiben beim Eigentümer.",
                string.Empty));
        }
        return result;
    }

    private static string FormatDateRange(DateOnly? from, DateOnly? to) =>
        from.HasValue
            ? $"{from.Value:dd.MM.yyyy}–{(to.HasValue ? to.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "offen")}"
            : "Zeitraum fehlt";

    private static int? GetNullableInt32(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static string? GetNullableString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateOnly? GetNullableDateOnly(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(ordinal));

    private static bool GetBoolean(SqlDataReader reader, int ordinal) =>
        !reader.IsDBNull(ordinal) &&
        Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

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
            Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value
        });
}
