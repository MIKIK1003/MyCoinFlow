using Microsoft.Data.SqlClient;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using System.Data;

namespace MyCoinFlow.WinUI.Data;

public sealed class FinanceSettingsRepository
{
    public async Task<FinanceSettingsDraft> LoadAsync(CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAdministrator();
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);

        var draft = new FinanceSettingsDraft();
        await LoadHeaderAsync(connection, draft, cancellationToken);
        draft.AccountOptions = await LoadAccountOptionsAsync(connection, cancellationToken);
        draft.InstitutionOptions = await LoadInstitutionOptionsAsync(connection, cancellationToken);
        await LoadNumberRangesAsync(connection, draft, cancellationToken);
        await LoadCurrenciesAsync(connection, draft, cancellationToken);
        await LoadExchangeRatesAsync(connection, draft, cancellationToken);
        await LoadVatRatesAsync(connection, draft, cancellationToken);
        await LoadPaymentAccountsAsync(connection, draft, cancellationToken);
        await LoadRevenueAccountsAsync(connection, draft, cancellationToken);

        foreach (var rate in draft.ExchangeRates)
            rate.CurrencyOptions = draft.Currencies;
        foreach (var account in draft.PaymentAccounts)
        {
            account.CurrencyOptions = draft.Currencies;
            account.InstitutionOptions = draft.InstitutionOptions;
        }
        return draft;
    }

    public async Task<FinanceSettingsSaveResult> SaveAsync(
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAdministrator();
        var validationWarnings = FinanceSettingsValidator
            .GetValidationErrorsAndNormalize(draft)
            .ToList();
        await InvoicingSchema.EnsureAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var persistenceWarnings = new List<string>();

        try
        {
            await TrySaveSectionAsync(
                connection, transaction, "Issuer", "Aussteller- und Kontaktdaten",
                () => SaveIssuerAsync(connection, transaction, draft, cancellationToken),
                persistenceWarnings, cancellationToken);
            await TrySaveSectionAsync(
                connection, transaction, "Country", "Aussteller-Ländercode",
                () => SaveIssuerCountryAsync(connection, transaction, draft, cancellationToken),
                persistenceWarnings, cancellationToken);
            await TrySaveSectionAsync(
                connection, transaction, "Currencies", "Basis- und Dokumentwährungen",
                () => SaveCurrenciesAsync(connection, transaction, draft, cancellationToken),
                persistenceWarnings, cancellationToken);
            await TrySaveSectionAsync(
                connection, transaction, "FinanceHeader", "Basiswährung und Kursdifferenzkonten",
                () => SaveFinanceHeaderAsync(connection, transaction, draft, cancellationToken),
                persistenceWarnings, cancellationToken);
            await TrySaveSectionAsync(
                connection, transaction, "PaymentTerms", "Standard-Zahlungsziel",
                () => SavePaymentTermsAsync(connection, transaction, draft, cancellationToken),
                persistenceWarnings, cancellationToken);
            await TrySaveSectionAsync(
                connection, transaction, "NumberRanges", "Dokumentnummernkreise",
                () => SaveNumberRangesAsync(connection, transaction, draft, cancellationToken),
                persistenceWarnings, cancellationToken);
            await TrySaveSectionAsync(
                connection, transaction, "ExchangeRates", "Manuelle Wechselkurse",
                () => SaveExchangeRatesAsync(connection, transaction, draft, cancellationToken),
                persistenceWarnings, cancellationToken);
            await TrySaveSectionAsync(
                connection, transaction, "VatRates", "MWST-Konfiguration",
                () => SaveVatRatesAsync(connection, transaction, draft, cancellationToken),
                persistenceWarnings, cancellationToken);
            await TrySaveSectionAsync(
                connection, transaction, "PaymentAccounts", "Zahlungs- und QR-Konten",
                () => SavePaymentAccountsAsync(connection, transaction, draft, cancellationToken),
                persistenceWarnings, cancellationToken);
            await TrySaveSectionAsync(
                connection, transaction, "RevenueAccounts", "Zulässige Ertragskonten",
                () => SaveRevenueAccountsAsync(connection, transaction, draft, cancellationToken),
                persistenceWarnings, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Der ursprüngliche Speicherfehler bleibt massgeblich; SQL Server kann bei
                // XACT_ABORT die Transaktion bereits vollständig zurückgerollt haben.
            }
            throw;
        }

        var warnings = validationWarnings
            .Concat(persistenceWarnings)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new FinanceSettingsSaveResult(draft, warnings);
    }

    private static async Task LoadHeaderAsync(
        SqlConnection connection,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT IssuerName, IssuerStreet, IssuerPostalCode, IssuerCity, IssuerCountryCode,
       VatNumber, InvoiceEmail, InvoicePhone, DefaultPaymentDays, BaseCurrency,
       ExchangeGainAccountId, ExchangeLossAccountId
FROM dbo.FakturierungEinstellung
WHERE Id = 1;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Die Fakturieren-Grundeinstellung fehlt.");

        draft.IssuerName = reader.GetString(0);
        draft.IssuerStreet = reader.GetString(1);
        draft.IssuerPostalCode = reader.GetString(2);
        draft.IssuerCity = reader.GetString(3);
        draft.IssuerCountryCode = reader.GetString(4).Trim();
        draft.VatNumber = reader.GetString(5);
        draft.InvoiceEmail = reader.GetString(6);
        draft.InvoicePhone = reader.GetString(7);
        draft.DefaultPaymentDays = reader.GetInt16(8);
        draft.BaseCurrency = reader.GetString(9).Trim();
        draft.ExchangeGainAccountId = reader.IsDBNull(10) ? null : reader.GetInt32(10);
        draft.ExchangeLossAccountId = reader.IsDBNull(11) ? null : reader.GetInt32(11);
    }

    private static async Task<IReadOnlyList<FinanceAccountOption>> LoadAccountOptionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, Kontonummer,
       COALESCE(NULLIF(Detail, N''), NULLIF(Untergruppe, N''), NULLIF(Gruppe, N''), N'Konto')
FROM dbo.Kontenplan
ORDER BY Kontonummer, Id;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<FinanceAccountOption>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new FinanceAccountOption(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2)));
        return result;
    }

    private static async Task<IReadOnlyList<FinanceInstitutionOption>> LoadInstitutionOptionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, Name, COALESCE(IBAN, N'')
FROM dbo.Geldinstitut
ORDER BY Name, Id;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<FinanceInstitutionOption>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new FinanceInstitutionOption(
                reader.GetInt32(0),
                reader.GetString(1),
                FinanceSettingsValidator.NormalizeIban(reader.GetString(2))));
        }
        return result;
    }

    private static async Task LoadNumberRangesAsync(
        SqlConnection connection,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT DocumentType, DisplayName, Prefix, NextNumber, Digits
FROM dbo.FakturierungNummernkreis
ORDER BY CASE DocumentType
    WHEN 'OFFER' THEN 1 WHEN 'ORDER' THEN 2 WHEN 'DELIVERY' THEN 3
    WHEN 'INVOICE' THEN 4 WHEN 'CORRECTION' THEN 5 ELSE 99 END;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            draft.NumberRanges.Add(new DocumentNumberRangeSetting
            {
                DocumentType = reader.GetString(0),
                DisplayName = reader.GetString(1),
                Prefix = reader.GetString(2),
                NextNumber = reader.GetInt64(3),
                Digits = reader.GetByte(4)
            });
        }
    }

    private static async Task LoadCurrenciesAsync(
        SqlConnection connection,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Code, DisplayName, IsActive
FROM dbo.FakturierungWaehrung
ORDER BY CASE WHEN Code = @baseCurrency THEN 0 ELSE 1 END, Code;
""";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@baseCurrency", SqlDbType.Char, 3) { Value = draft.BaseCurrency });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            draft.Currencies.Add(new DocumentCurrencySetting
            {
                Code = reader.GetString(0).Trim(),
                DisplayName = reader.GetString(1),
                IsActive = reader.GetBoolean(2)
            });
        }
    }

    private static async Task LoadExchangeRatesAsync(
        SqlConnection connection,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, DocumentCurrency, RateToBase, ValidFrom, ValidTo, Source, IsActive
FROM dbo.FakturierungWechselkurs
ORDER BY DocumentCurrency, ValidFrom DESC, Id DESC;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            draft.ExchangeRates.Add(new ExchangeRateSetting
            {
                Id = reader.GetInt32(0),
                DocumentCurrency = reader.GetString(1).Trim(),
                RateToBase = Convert.ToDouble(reader.GetDecimal(2)),
                ValidFrom = ToDate(reader.GetDateTime(3)),
                ValidTo = reader.IsDBNull(4) ? null : ToDate(reader.GetDateTime(4)),
                Source = reader.GetString(5),
                IsActive = reader.GetBoolean(6)
            });
        }
    }

    private static async Task LoadVatRatesAsync(
        SqlConnection connection,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Id, Code, DisplayName, RatePercent, ValidFrom, ValidTo, IsDefault, IsActive
FROM dbo.FakturierungMwstSatz
ORDER BY IsDefault DESC, Code, ValidFrom DESC, Id DESC;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            draft.VatRates.Add(new VatRateSetting
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                DisplayName = reader.GetString(2),
                RatePercent = Convert.ToDouble(reader.GetDecimal(3)),
                ValidFrom = ToDate(reader.GetDateTime(4)),
                ValidTo = reader.IsDBNull(5) ? null : ToDate(reader.GetDateTime(5)),
                IsDefault = reader.GetBoolean(6),
                IsActive = reader.GetBoolean(7)
            });
        }
    }

    private static async Task LoadPaymentAccountsAsync(
        SqlConnection connection,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT p.Id,
       COALESCE(NULLIF(g.Name, N''), p.DisplayName),
       COALESCE(NULLIF(g.IBAN, N''), p.Iban),
       p.CurrencyCode, p.GeldinstitutId, p.IsActive
FROM dbo.FakturierungZahlungskonto p
LEFT JOIN dbo.Geldinstitut g ON g.Id = p.GeldinstitutId
ORDER BY p.IsActive DESC, COALESCE(NULLIF(g.Name, N''), p.DisplayName), p.Id;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var iban = FinanceSettingsValidator.NormalizeIban(reader.GetString(2));
            draft.PaymentAccounts.Add(new PaymentAccountSetting
            {
                Id = reader.GetInt32(0),
                DisplayName = reader.GetString(1),
                Iban = iban,
                CurrencyCode = reader.GetString(3).Trim(),
                InstitutionId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                IsQrIban = FinanceSettingsValidator.IsSwissQrIban(iban),
                IsActive = reader.GetBoolean(5)
            });
        }
    }

    private static async Task LoadRevenueAccountsAsync(
        SqlConnection connection,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT AccountId FROM dbo.FakturierungErtragskonto ORDER BY AccountId;",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            draft.RevenueAccountIds.Add(reader.GetInt32(0));
    }

    private static async Task VerifyAccountIdsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        var accountIds = draft.RevenueAccountIds
            .Concat(draft.ExchangeGainAccountId is { } gain ? [gain] : [])
            .Concat(draft.ExchangeLossAccountId is { } loss ? [loss] : [])
            .Distinct()
            .ToList();
        if (accountIds.Count == 0) return;

        var names = accountIds.Select((_, index) => $"@id{index}").ToArray();
        await using var command = new SqlCommand(
            $"SELECT COUNT(*) FROM dbo.Kontenplan WITH (HOLDLOCK) WHERE Id IN ({string.Join(", ", names)});",
            connection,
            transaction);
        for (var index = 0; index < accountIds.Count; index++)
            command.Parameters.Add(new SqlParameter(names[index], SqlDbType.Int) { Value = accountIds[index] });
        var existingCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (existingCount != accountIds.Count)
            throw new FinanceSettingsValidationException(
                ["Mindestens ein ausgewähltes Konto existiert im aktiven Mandanten nicht mehr. Bitte neu laden."]);
    }

    private static async Task VerifyInstitutionIdsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        var institutionIds = draft.PaymentAccounts
            .Where(value => value.InstitutionId.HasValue)
            .Select(value => value.InstitutionId!.Value)
            .Distinct()
            .ToList();
        if (institutionIds.Count == 0) return;

        var names = institutionIds.Select((_, index) => $"@institutionId{index}").ToArray();
        await using var command = new SqlCommand(
            $"SELECT COUNT(*) FROM dbo.Geldinstitut WITH (HOLDLOCK) WHERE Id IN ({string.Join(", ", names)});",
            connection,
            transaction);
        for (var index = 0; index < institutionIds.Count; index++)
        {
            command.Parameters.Add(
                new SqlParameter(names[index], SqlDbType.Int) { Value = institutionIds[index] });
        }
        var existingCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (existingCount != institutionIds.Count)
        {
            throw new FinanceSettingsValidationException(
                ["Mindestens ein ausgewähltes Geldinstitut existiert im aktiven Mandanten nicht mehr. Bitte neu laden."]);
        }
    }

    private static async Task SaveCurrenciesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            "UPDATE dbo.FakturierungWaehrung SET IsActive = 0, UpdatedAt = SYSDATETIME();",
            cancellationToken);

        const string sql = """
UPDATE dbo.FakturierungWaehrung
SET DisplayName = @name, IsActive = @active, UpdatedAt = SYSDATETIME()
WHERE Code = @code;
IF @@ROWCOUNT = 0
    INSERT dbo.FakturierungWaehrung (Code, DisplayName, IsActive)
    VALUES (@code, @name, @active);
""";
        foreach (var currency in draft.Currencies)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            AddText(command, "@code", SqlDbType.Char, 3, currency.Code);
            AddText(command, "@name", SqlDbType.NVarChar, 80, currency.DisplayName);
            command.Parameters.Add(new SqlParameter("@active", SqlDbType.Bit) { Value = currency.IsActive });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task SaveIssuerAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
UPDATE dbo.FakturierungEinstellung
SET IssuerName = @name,
    IssuerStreet = @street,
    IssuerPostalCode = @postal,
    IssuerCity = @city,
    VatNumber = @vatNumber,
    InvoiceEmail = @email,
    InvoicePhone = @phone,
    UpdatedAt = SYSDATETIME(),
    UpdatedBy = @user
WHERE Id = 1;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddText(command, "@name", SqlDbType.NVarChar, 200, draft.IssuerName);
        AddText(command, "@street", SqlDbType.NVarChar, 200, draft.IssuerStreet);
        AddText(command, "@postal", SqlDbType.NVarChar, 24, draft.IssuerPostalCode);
        AddText(command, "@city", SqlDbType.NVarChar, 120, draft.IssuerCity);
        AddText(command, "@vatNumber", SqlDbType.NVarChar, 40, draft.VatNumber);
        AddText(command, "@email", SqlDbType.NVarChar, 256, draft.InvoiceEmail);
        AddText(command, "@phone", SqlDbType.NVarChar, 80, draft.InvoicePhone);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Die Aussteller- und Kontaktdaten konnten nicht gespeichert werden.");
    }

    private static async Task SaveIssuerCountryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
UPDATE dbo.FakturierungEinstellung
SET IssuerCountryCode = @country, UpdatedAt = SYSDATETIME(), UpdatedBy = @user
WHERE Id = 1;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddText(command, "@country", SqlDbType.Char, 2, draft.IssuerCountryCode);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Der Aussteller-Ländercode konnte nicht gespeichert werden.");
    }

    private static async Task SaveFinanceHeaderAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
UPDATE dbo.FakturierungEinstellung
SET BaseCurrency = @baseCurrency,
    ExchangeGainAccountId = @gainAccount,
    ExchangeLossAccountId = @lossAccount,
    UpdatedAt = SYSDATETIME(),
    UpdatedBy = @user
WHERE Id = 1;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        AddText(command, "@baseCurrency", SqlDbType.Char, 3, draft.BaseCurrency);
        AddNullableInt(command, "@gainAccount", draft.ExchangeGainAccountId);
        AddNullableInt(command, "@lossAccount", draft.ExchangeLossAccountId);
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Basiswährung und Kursdifferenzkonten konnten nicht gespeichert werden.");
    }

    private static async Task SavePaymentTermsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
UPDATE dbo.FakturierungEinstellung
SET DefaultPaymentDays = @paymentDays, UpdatedAt = SYSDATETIME(), UpdatedBy = @user
WHERE Id = 1;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(
            new SqlParameter("@paymentDays", SqlDbType.SmallInt) { Value = draft.DefaultPaymentDays });
        AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Das Standard-Zahlungsziel konnte nicht gespeichert werden.");
    }

    private static async Task SaveNumberRangesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
UPDATE dbo.FakturierungNummernkreis
SET DisplayName = @name, Prefix = @prefix, NextNumber = @next, Digits = @digits,
    UpdatedAt = SYSDATETIME()
WHERE DocumentType = @type;
IF @@ROWCOUNT = 0
    INSERT dbo.FakturierungNummernkreis
        (DocumentType, DisplayName, Prefix, NextNumber, Digits)
    VALUES (@type, @name, @prefix, @next, @digits);
""";
        foreach (var range in draft.NumberRanges)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            AddText(command, "@type", SqlDbType.VarChar, 24, range.DocumentType);
            AddText(command, "@name", SqlDbType.NVarChar, 80, range.DisplayName);
            AddText(command, "@prefix", SqlDbType.NVarChar, 12, range.Prefix);
            command.Parameters.Add(new SqlParameter("@next", SqlDbType.BigInt) { Value = range.NextNumber });
            command.Parameters.Add(new SqlParameter("@digits", SqlDbType.TinyInt) { Value = range.Digits });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task SaveExchangeRatesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            "UPDATE dbo.FakturierungWechselkurs SET IsActive = 0, UpdatedAt = SYSDATETIME();",
            cancellationToken);

        const string sql = """
UPDATE dbo.FakturierungWechselkurs
SET DocumentCurrency = @currency, RateToBase = @rate, ValidFrom = @validFrom,
    ValidTo = @validTo, Source = @source, IsActive = @active, UpdatedAt = SYSDATETIME()
WHERE (@id > 0 AND Id = @id)
   OR (@id = 0 AND DocumentCurrency = @currency AND ValidFrom = @validFrom);
IF @@ROWCOUNT = 0
    INSERT dbo.FakturierungWechselkurs
        (DocumentCurrency, RateToBase, ValidFrom, ValidTo, Source, IsActive)
    VALUES (@currency, @rate, @validFrom, @validTo, @source, @active);
""";
        foreach (var rate in draft.ExchangeRates)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = rate.Id });
            AddText(command, "@currency", SqlDbType.Char, 3, rate.DocumentCurrency);
            command.Parameters.Add(new SqlParameter("@rate", SqlDbType.Decimal)
            {
                Precision = 19,
                Scale = 8,
                Value = Convert.ToDecimal(rate.RateToBase)
            });
            AddDate(command, "@validFrom", rate.ValidFrom);
            AddNullableDate(command, "@validTo", rate.ValidTo);
            AddText(command, "@source", SqlDbType.NVarChar, 120, rate.Source);
            command.Parameters.Add(new SqlParameter("@active", SqlDbType.Bit) { Value = rate.IsActive });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task SaveVatRatesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            "UPDATE dbo.FakturierungMwstSatz SET IsActive = 0, IsDefault = 0, UpdatedAt = SYSDATETIME();",
            cancellationToken);

        const string sql = """
UPDATE dbo.FakturierungMwstSatz
SET Code = @code, DisplayName = @name, RatePercent = @rate, ValidFrom = @validFrom,
    ValidTo = @validTo, IsDefault = @isDefault, IsActive = @active, UpdatedAt = SYSDATETIME()
WHERE (@id > 0 AND Id = @id)
   OR (@id = 0 AND Code = @code AND ValidFrom = @validFrom);
IF @@ROWCOUNT = 0
    INSERT dbo.FakturierungMwstSatz
        (Code, DisplayName, RatePercent, ValidFrom, ValidTo, IsDefault, IsActive)
    VALUES (@code, @name, @rate, @validFrom, @validTo, @isDefault, @active);
""";
        foreach (var vat in draft.VatRates)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = vat.Id });
            AddText(command, "@code", SqlDbType.NVarChar, 24, vat.Code);
            AddText(command, "@name", SqlDbType.NVarChar, 100, vat.DisplayName);
            command.Parameters.Add(new SqlParameter("@rate", SqlDbType.Decimal)
            {
                Precision = 7,
                Scale = 4,
                Value = Convert.ToDecimal(vat.RatePercent)
            });
            AddDate(command, "@validFrom", vat.ValidFrom);
            AddNullableDate(command, "@validTo", vat.ValidTo);
            command.Parameters.Add(new SqlParameter("@isDefault", SqlDbType.Bit) { Value = vat.IsDefault });
            command.Parameters.Add(new SqlParameter("@active", SqlDbType.Bit) { Value = vat.IsActive });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task SavePaymentAccountsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            "UPDATE dbo.FakturierungZahlungskonto SET IsActive = 0, UpdatedAt = SYSDATETIME();",
            cancellationToken);

        const string sql = """
UPDATE dbo.FakturierungZahlungskonto
SET DisplayName = @name, Iban = @iban, CurrencyCode = @currency, IsQrIban = @isQr,
    GeldinstitutId = @institutionId, IsActive = @active, UpdatedAt = SYSDATETIME()
WHERE (@id > 0 AND Id = @id)
   OR (@id = 0 AND GeldinstitutId = @institutionId AND CurrencyCode = @currency);
IF @@ROWCOUNT = 0
    INSERT dbo.FakturierungZahlungskonto
        (DisplayName, Iban, CurrencyCode, IsQrIban, GeldinstitutId, IsActive)
    VALUES (@name, @iban, @currency, @isQr, @institutionId, @active);
""";
        foreach (var account in draft.PaymentAccounts)
        {
            if (account.InstitutionId is null) continue;
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = account.Id });
            AddText(command, "@name", SqlDbType.NVarChar, 120, account.DisplayName);
            AddText(command, "@iban", SqlDbType.VarChar, 34, account.Iban);
            AddText(command, "@currency", SqlDbType.Char, 3, account.CurrencyCode);
            command.Parameters.Add(new SqlParameter("@isQr", SqlDbType.Bit) { Value = account.IsQrIban });
            command.Parameters.Add(
                new SqlParameter("@institutionId", SqlDbType.Int) { Value = account.InstitutionId.Value });
            command.Parameters.Add(new SqlParameter("@active", SqlDbType.Bit) { Value = account.IsActive });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task SaveRevenueAccountsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FinanceSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            "DELETE FROM dbo.FakturierungErtragskonto;",
            cancellationToken);

        const string sql = """
INSERT dbo.FakturierungErtragskonto (AccountId, UpdatedBy)
VALUES (@accountId, @user);
""";
        foreach (var accountId in draft.RevenueAccountIds.Order())
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add(new SqlParameter("@accountId", SqlDbType.Int) { Value = accountId });
            AddText(command, "@user", SqlDbType.NVarChar, 64, CurrentUserContext.Username);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task TrySaveSectionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string savepoint,
        string displayName,
        Func<Task> saveAsync,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            $"SAVE TRANSACTION {savepoint};",
            cancellationToken);
        try
        {
            await saveAsync();
        }
        catch (Exception exception) when (
            exception is SqlException or
            InvalidOperationException or
            ArgumentException or
            OverflowException)
        {
            await ExecuteAsync(
                connection,
                transaction,
                $"ROLLBACK TRANSACTION {savepoint};",
                CancellationToken.None);
            var detail = exception.Message
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "Unbekannter Speicherfehler.";
            warnings.Add($"{displayName}: {detail}");
        }
    }

    private static Task<int> ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        var command = new SqlCommand(sql, connection, transaction);
        return ExecuteAndDisposeAsync(command, cancellationToken);
    }

    private static async Task<int> ExecuteAndDisposeAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        await using (command)
            return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddText(
        SqlCommand command,
        string name,
        SqlDbType type,
        int size,
        string value) =>
        command.Parameters.Add(new SqlParameter(name, type, size) { Value = value });

    private static void AddNullableInt(SqlCommand command, string name, int? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Int)
        {
            Value = value is { } number ? number : DBNull.Value
        });

    private static void AddDate(SqlCommand command, string name, DateTimeOffset value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Date) { Value = value.Date });

    private static void AddNullableDate(SqlCommand command, string name, DateTimeOffset? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Date)
        {
            Value = value is { } date ? date.Date : DBNull.Value
        });

    private static DateTimeOffset ToDate(DateTime value) =>
        new(DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified), TimeSpan.Zero);
}
