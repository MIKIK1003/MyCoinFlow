using Microsoft.Data.SqlClient;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using System.Data;

namespace MyCoinFlow.WinUI.Data;

public sealed class InvoicingWorkspaceRepository
{
    public async Task<InvoicingWorkspaceOverview> LoadOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        InvoicingSchema.RequireAuthenticated();
        await InvoicingSchema.EnsureAsync(cancellationToken);
        var schemaVersion = await InvoicingSchema.VerifyAsync(cancellationToken);
        await using var connection = await InvoicingSchema.OpenTenantConnectionAsync(cancellationToken);

        const string sql = """
SELECT
    e.IssuerName,
    e.BaseCurrency,
    CASE WHEN e.IssuerName <> N'' AND e.IssuerStreet <> N'' AND
                   e.IssuerPostalCode <> N'' AND e.IssuerCity <> N''
         THEN 1 ELSE 0 END AS HasIssuer,
    CASE WHEN EXISTS (
        SELECT 1 FROM dbo.FakturierungWaehrung w
        WHERE w.Code = e.BaseCurrency AND w.IsActive = 1)
         THEN 1 ELSE 0 END AS HasActiveBaseCurrency,
    (SELECT COUNT(*) FROM dbo.FakturierungWaehrung WHERE IsActive = 1) AS ActiveCurrencies,
    (SELECT COUNT(*) FROM dbo.FakturierungWaehrung
     WHERE IsActive = 1 AND Code <> e.BaseCurrency) AS ActiveForeignCurrencies,
    (SELECT COUNT(DISTINCT r.DocumentCurrency)
     FROM dbo.FakturierungWechselkurs r
     WHERE r.IsActive = 1 AND r.ValidFrom <= CONVERT(date, GETDATE())
       AND r.DocumentCurrency <> e.BaseCurrency
       AND (r.ValidTo IS NULL OR r.ValidTo >= CONVERT(date, GETDATE()))) AS CurrentRateCurrencies,
    (SELECT COUNT(*) FROM dbo.FakturierungMwstSatz
     WHERE IsActive = 1 AND ValidFrom <= CONVERT(date, GETDATE())
       AND (ValidTo IS NULL OR ValidTo >= CONVERT(date, GETDATE()))) AS ActiveVatRates,
    (SELECT COUNT(*) FROM dbo.FakturierungMwstSatz
     WHERE IsActive = 1 AND IsDefault = 1
       AND ValidFrom <= CONVERT(date, GETDATE())
       AND (ValidTo IS NULL OR ValidTo >= CONVERT(date, GETDATE()))) AS DefaultVatRates,
    (SELECT COUNT(*) FROM dbo.FakturierungZahlungskonto WHERE IsActive = 1) AS ActivePaymentAccounts,
    (SELECT COUNT(*) FROM dbo.FakturierungErtragskonto) AS RevenueAccounts,
    (SELECT COUNT(*) FROM dbo.FakturierungNummernkreis) AS NumberRanges,
    e.ExchangeGainAccountId,
    e.ExchangeLossAccountId
FROM dbo.FakturierungEinstellung e
WHERE e.Id = 1;
""";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Die Fakturieren-Grundeinstellung fehlt.");

        var issuerName = reader.GetString(0);
        var baseCurrency = reader.GetString(1).Trim();
        var hasIssuer = reader.GetInt32(2) == 1;
        var hasActiveBaseCurrency = reader.GetInt32(3) == 1;
        var activeCurrencies = reader.GetInt32(4);
        var activeForeignCurrencies = reader.GetInt32(5);
        var currentRateCurrencies = reader.GetInt32(6);
        var activeVatRates = reader.GetInt32(7);
        var defaultVatRates = reader.GetInt32(8);
        var activePaymentAccounts = reader.GetInt32(9);
        var revenueAccounts = reader.GetInt32(10);
        var numberRanges = reader.GetInt32(11);
        var gainAccountConfigured = !reader.IsDBNull(12);
        var lossAccountConfigured = !reader.IsDBNull(13);

        var missing = new List<string>();
        if (!hasIssuer) missing.Add("Ausstelleranschrift");
        if (!hasActiveBaseCurrency) missing.Add("aktive Basiswährung");
        if (numberRanges != InvoicingDocumentTypes.Defaults.Count) missing.Add("vollständige Nummernkreise");
        if (activeVatRates == 0 || defaultVatRates != 1) missing.Add("aktiver Standard-MWST-Satz");
        if (activePaymentAccounts == 0) missing.Add("Zahlungskonto");
        if (revenueAccounts == 0) missing.Add("zulässiges Ertragskonto");
        if (currentRateCurrencies < activeForeignCurrencies) missing.Add("heute gültige Fremdwährungskurse");
        if (activeForeignCurrencies > 0 && (!gainAccountConfigured || !lossAccountConfigured))
            missing.Add("Kursgewinn- und Kursverlustkonto");

        return new InvoicingWorkspaceOverview(
            ConnectionStrings.ActiveDatabaseName,
            schemaVersion,
            CurrentUserContext.IsAdmin,
            missing.Count == 0,
            issuerName,
            baseCurrency,
            activeCurrencies,
            activeVatRates,
            activePaymentAccounts,
            revenueAccounts,
            missing);
    }
}
