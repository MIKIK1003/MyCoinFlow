using Microsoft.Data.SqlClient;
using MyCoinFlow.Models;
using MyCoinFlow.WinUI.Models;
using System.Data;
using System.Globalization;
using System.Text;

namespace MyCoinFlow.WinUI.Data;

public sealed class TransactionRepository
{
    private static SqlConnection CreateConnection() => new(ConnectionStrings.Current);

    public async Task VerifyDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT CASE WHEN OBJECT_ID('dbo.Transaktion', 'U') IS NULL THEN 0 ELSE 1 END", connection);
        var exists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
        if (!exists)
        {
            throw new InvalidOperationException($"Die Datenbank '{ConnectionStrings.ActiveDatabaseName}' enthält keine Transaktionstabelle.");
        }
    }

    public async Task<BudgetPeriod?> GetActiveBudgetPeriodAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        const string sql = @"
SELECT TOP (1) Startdatum, Enddatum, Bezeichnung
FROM dbo.Budgetzeitraum
WHERE IstAktiv = 1;";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new BudgetPeriod(
            reader.GetDateTime(0),
            reader.GetDateTime(1),
            reader.IsDBNull(2) ? "Aktiver Budgetzeitraum" : reader.GetString(2));
    }

    public async Task<IReadOnlyList<TransactionSummaryAccount>> GetSummaryAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using (var existsCommand = new SqlCommand(
            "SELECT CASE WHEN OBJECT_ID('dbo.NumberRangeRules', 'U') IS NULL THEN 0 ELSE 1 END", connection))
        {
            var exists = Convert.ToInt32(
                await existsCommand.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture) == 1;
            if (!exists)
                return Array.Empty<TransactionSummaryAccount>();
        }

        const string sql = @"
SELECT k.Id, k.Kontonummer, passendeRegel.Richtung
FROM dbo.Kontenplan k
CROSS APPLY
(
    SELECT TOP (1) r.Richtung, r.Bezeichnung
    FROM dbo.NumberRangeRules r
    WHERE k.Kontonummer BETWEEN r.RangeStart AND r.RangeEnd
    ORDER BY (r.RangeEnd - r.RangeStart), r.RangeStart
) passendeRegel
WHERE (passendeRegel.Richtung = N'Einnahme'
       AND passendeRegel.Bezeichnung = N'Einnahmen (Budgetiert)')
   OR (passendeRegel.Richtung = N'Ausgabe'
       AND passendeRegel.Bezeichnung = N'Ausgaben (Budgetiert)')
ORDER BY k.Kontonummer;";

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<TransactionSummaryAccount>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var direction = string.Equals(
                reader.GetString(2),
                "Einnahme",
                StringComparison.OrdinalIgnoreCase)
                ? TransactionSummaryDirection.Income
                : TransactionSummaryDirection.Expense;
            result.Add(new TransactionSummaryAccount(
                reader.GetInt32(0),
                reader.GetInt32(1),
                direction));
        }

        return result;
    }

    public async Task<IReadOnlyList<NumberRangeRule>> GetNumberRangeRulesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using (var existsCommand = new SqlCommand(
            "SELECT CASE WHEN OBJECT_ID('dbo.NumberRangeRules', 'U') IS NULL THEN 0 ELSE 1 END", connection))
        {
            var exists = Convert.ToInt32(
                await existsCommand.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture) == 1;
            if (!exists)
                return Array.Empty<NumberRangeRule>();
        }

        const string sql = @"
SELECT Id, RangeStart, RangeEnd, Richtung, Bezeichnung
FROM dbo.NumberRangeRules
ORDER BY RangeStart, RangeEnd, Id;";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<NumberRangeRule>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new NumberRangeRule
            {
                Id = reader.GetInt32(0),
                RangeStart = reader.GetInt32(1),
                RangeEnd = reader.GetInt32(2),
                Richtung = reader.IsDBNull(3) ? "Neutral" : reader.GetString(3),
                Bezeichnung = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<LookupItem>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        const string sql = @"
SELECT Id, Kontonummer, Art, Gruppe, Untergruppe, Detail
FROM dbo.Kontenplan
ORDER BY Kontonummer, Detail;";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<LookupItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var number = reader.IsDBNull(1) ? string.Empty : reader.GetInt32(1).ToString("D4", CultureInfo.InvariantCulture);
            var art = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var group = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            var subgroup = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            var detail = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
            var name = string.IsNullOrWhiteSpace(detail) ? number : $"{number}  {detail}";
            var hierarchy = string.Join("/", new[] { art, group, subgroup }.Where(value => !string.IsNullOrWhiteSpace(value)));
            if (!string.IsNullOrWhiteSpace(hierarchy))
            {
                name += $"  [{hierarchy}]";
            }
            result.Add(new LookupItem { Id = reader.GetInt32(0), Name = name });
        }
        return result;
    }

    public Task<IReadOnlyList<LookupItem>> GetAddressesAsync(CancellationToken cancellationToken = default) =>
        GetSimpleLookupAsync("SELECT Id, Name FROM dbo.Adresse ORDER BY Name", cancellationToken);

    public Task<IReadOnlyList<LookupItem>> GetInstitutionsAsync(CancellationToken cancellationToken = default) =>
        GetSimpleLookupAsync("SELECT Id, Name FROM dbo.Geldinstitut ORDER BY Name", cancellationToken);

    private static async Task<IReadOnlyList<LookupItem>> GetSimpleLookupAsync(
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<LookupItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new LookupItem
            {
                Id = reader.GetInt32(0),
                Name = reader.IsDBNull(1) ? $"#{reader.GetInt32(0)}" : reader.GetString(1)
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<TransactionRecord>> SearchAsync(
        TransactionSearch search,
        CancellationToken cancellationToken = default)
    {
        var rawTokens = (search.Term ?? string.Empty)
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length > 0)
            .ToArray();

        var numberTokens = new List<int>();
        var amountTokens = new List<decimal>();
        var textTokens = new List<string>();
        foreach (var token in rawTokens)
        {
            var clean = token.Replace("'", string.Empty, StringComparison.Ordinal);
            if (int.TryParse(clean, out var number))
            {
                numberTokens.Add(number);
            }
            else if (decimal.TryParse(clean, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)
                     || decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
            {
                amountTokens.Add(amount);
            }
            else
            {
                textTokens.Add(token);
            }
        }

        var sql = new StringBuilder(@"
SELECT DISTINCT
       t.Id, t.Datum, t.BudgetDatum, t.VonKontoId, t.NachKontoId,
       t.Betrag, t.Notiz, t.AdresseId, a.Name,
       t.GeldinstitutId, g.Name, t.ImportQuelle,
       kv.Kontonummer, kv.Untergruppe, kv.Detail,
       kn.Kontonummer, kn.Untergruppe, kn.Detail,
       (SELECT COUNT(*) FROM dbo.Attachment ac WHERE ac.TransaktionId = t.Id) AS AttachmentCount
FROM dbo.Transaktion t
LEFT JOIN dbo.Adresse a ON t.AdresseId = a.Id
LEFT JOIN dbo.Geldinstitut g ON t.GeldinstitutId = g.Id
LEFT JOIN dbo.Attachment att ON att.TransaktionId = t.Id
LEFT JOIN dbo.AttachmentText atx ON atx.AttachmentId = att.Id
LEFT JOIN dbo.Kontenplan kv ON kv.Id = t.VonKontoId
LEFT JOIN dbo.Kontenplan kn ON kn.Id = t.NachKontoId
WHERE 1 = 1
");

        if (search.From.HasValue) sql.AppendLine("AND ISNULL(t.BudgetDatum, t.Datum) >= @from");
        if (search.To.HasValue) sql.AppendLine("AND ISNULL(t.BudgetDatum, t.Datum) <= @to");
        if (!string.IsNullOrWhiteSpace(search.Address))
            sql.AppendLine("AND a.Name LIKE @address COLLATE Latin1_General_CI_AI");

        for (var i = 0; i < textTokens.Count; i++)
        {
            sql.AppendLine($@"AND (
                   t.Notiz LIKE @q{i} COLLATE Latin1_General_CI_AI
                OR a.Name LIKE @q{i} COLLATE Latin1_General_CI_AI
                OR g.Name LIKE @q{i} COLLATE Latin1_General_CI_AI
                OR att.FileName LIKE @q{i} COLLATE Latin1_General_CI_AI
                OR atx.[Text] LIKE @q{i} COLLATE Latin1_General_CI_AI)");
        }
        for (var i = 0; i < numberTokens.Count; i++)
        {
            sql.AppendLine($@"AND (
                   kv.Kontonummer = @n{i}
                OR kn.Kontonummer = @n{i}
                OR t.Id = @n{i}
                OR ABS(t.Betrag) = @n{i})");
        }
        for (var i = 0; i < amountTokens.Count; i++)
        {
            sql.AppendLine($"AND ABS(t.Betrag) = @amount{i}");
        }
        sql.AppendLine("ORDER BY t.Datum DESC, t.Id DESC;");

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql.ToString(), connection);
        if (search.From.HasValue) command.Parameters.AddWithValue("@from", search.From.Value.Date);
        if (search.To.HasValue) command.Parameters.AddWithValue("@to", search.To.Value.Date);
        if (!string.IsNullOrWhiteSpace(search.Address))
            command.Parameters.AddWithValue("@address", $"%{search.Address.Trim()}%");
        for (var i = 0; i < textTokens.Count; i++) command.Parameters.AddWithValue($"@q{i}", $"%{textTokens[i]}%");
        for (var i = 0; i < numberTokens.Count; i++) command.Parameters.AddWithValue($"@n{i}", numberTokens[i]);
        for (var i = 0; i < amountTokens.Count; i++) command.Parameters.AddWithValue($"@amount{i}", amountTokens[i]);

        var result = new List<TransactionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sourceAccount = FormatAccount(reader, 12, 13, 14);
            var targetAccount = FormatAccount(reader, 15, 16, 17);
            var addressName = reader.IsDBNull(8) ? null : reader.GetString(8);
            var bankName = reader.IsDBNull(10) ? null : reader.GetString(10);
            var source = !reader.IsDBNull(3)
                ? sourceAccount
                : (reader.IsDBNull(9) && !string.IsNullOrWhiteSpace(addressName) ? addressName : bankName ?? "Bank");
            var target = !reader.IsDBNull(4) ? targetAccount : bankName ?? "Bank";

            result.Add(new TransactionRecord
            {
                Id = reader.GetInt32(0),
                Datum = reader.GetDateTime(1),
                BudgetDatum = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                VonKontoId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                NachKontoId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                VonKontoNummer = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                NachKontoNummer = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                Betrag = reader.GetDecimal(5),
                Notiz = reader.IsDBNull(6) ? null : reader.GetString(6),
                AdresseId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                AdresseName = addressName,
                GeldinstitutId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                BankName = bankName,
                ImportQuelle = reader.IsDBNull(11) ? null : reader.GetString(11),
                VonAnzeige = source ?? string.Empty,
                NachAnzeige = target ?? string.Empty,
                AttachmentCount = reader.IsDBNull(18) ? 0 : reader.GetInt32(18)
            });
        }
        return result;
    }

    private static string FormatAccount(SqlDataReader reader, int numberOrdinal, int subgroupOrdinal, int detailOrdinal)
    {
        if (reader.IsDBNull(numberOrdinal))
        {
            return "Konto";
        }
        var number = reader.GetInt32(numberOrdinal).ToString("D4", CultureInfo.InvariantCulture);
        var subgroup = reader.IsDBNull(subgroupOrdinal) ? string.Empty : reader.GetString(subgroupOrdinal);
        var detail = reader.IsDBNull(detailOrdinal) ? string.Empty : reader.GetString(detailOrdinal);
        return string.Join("  ", new[] { number, subgroup, detail }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public async Task SaveAsync(TransactionDraft draft, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var sql = draft.Id.HasValue
            ? @"UPDATE dbo.Transaktion SET
                    Datum = @date,
                    BudgetDatum = @budgetDate,
                    VonKontoId = @source,
                    NachKontoId = @target,
                    Betrag = @amount,
                    Notiz = @note,
                    AdresseId = @address,
                    GeldinstitutId = @institution
                WHERE Id = @id;"
            : @"INSERT INTO dbo.Transaktion
                    (Datum, BudgetDatum, VonKontoId, NachKontoId, Betrag, Notiz, AdresseId, GeldinstitutId)
                VALUES
                    (@date, @budgetDate, @source, @target, @amount, @note, @address, @institution);";

        await using var command = new SqlCommand(sql, connection);
        if (draft.Id.HasValue)
            command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = draft.Id.Value });
        command.Parameters.Add(new SqlParameter("@date", SqlDbType.Date) { Value = draft.Datum.Date });
        command.Parameters.Add(new SqlParameter("@budgetDate", SqlDbType.Date)
        {
            Value = (object?)draft.BudgetDatum?.Date ?? DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@source", SqlDbType.Int) { Value = (object?)draft.VonKontoId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@target", SqlDbType.Int) { Value = (object?)draft.NachKontoId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@amount", SqlDbType.Decimal)
        {
            Precision = 18,
            Scale = 2,
            Value = draft.Betrag
        });
        command.Parameters.Add(new SqlParameter("@note", SqlDbType.NVarChar, 200)
        {
            Value = (object?)draft.Notiz ?? DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@address", SqlDbType.Int) { Value = (object?)draft.AdresseId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@institution", SqlDbType.Int) { Value = (object?)draft.GeldinstitutId ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string usageSql = "SELECT COUNT(1) FROM dbo.StweSet WHERE TransaktionId = @id;";
        await using (var usageCommand = new SqlCommand(usageSql, connection))
        {
            usageCommand.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
            var usageCount = Convert.ToInt32(await usageCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (usageCount > 0)
            {
                throw new InvalidOperationException(
                    $"Diese Transaktion kann nicht gelöscht werden, weil sie in {usageCount} STWE-Set(s) verwendet wird.");
            }
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string deleteSubscriptionLinkSql = @"
IF OBJECT_ID('dbo.AboTransaktion', 'U') IS NOT NULL
    DELETE FROM dbo.AboTransaktion WHERE TransaktionId = @id;";
            await using (var linkCommand = new SqlCommand(deleteSubscriptionLinkSql, connection, (SqlTransaction)transaction))
            {
                linkCommand.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
                await linkCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var deleteCommand = new SqlCommand(
                "DELETE FROM dbo.Transaktion WHERE Id = @id;", connection, (SqlTransaction)transaction))
            {
                deleteCommand.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
                await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(TranslateDeleteConflict(exception), exception);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string TranslateDeleteConflict(SqlException exception)
    {
        var message = exception.Message ?? string.Empty;
        if (message.Contains("AboTransaktion", StringComparison.OrdinalIgnoreCase))
            return "Die Transaktion ist einem Abonnement zugeordnet. Entfernen Sie zuerst diese Zuordnung.";
        if (message.Contains("Stwe", StringComparison.OrdinalIgnoreCase))
            return "Die Transaktion wird in einem STWE-Set verwendet. Lösen Sie zuerst die Verknüpfung.";
        if (message.Contains("Attachment", StringComparison.OrdinalIgnoreCase))
            return "An der Transaktion hängen noch Dokumente. Entfernen Sie zuerst die Anhänge.";
        return "Die Transaktion ist noch mit anderen Daten verknüpft und kann deshalb nicht gelöscht werden.";
    }

    public async Task<bool> IsIncomeAccountAsync(int accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        const string accountSql = @"
SELECT Kontonummer,
       UPPER(ISNULL(Art, '')),
       UPPER(ISNULL(Gruppe, '')),
       UPPER(ISNULL(Untergruppe, '')),
       UPPER(ISNULL(Detail, ''))
FROM dbo.Kontenplan
WHERE Id = @id;";
        int? accountNumber;
        string accountText;
        await using (var accountCommand = new SqlCommand(accountSql, connection))
        {
            accountCommand.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = accountId });
            await using var reader = await accountCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return false;
            accountNumber = reader.IsDBNull(0) ? null : reader.GetInt32(0);
            accountText = string.Join(' ', Enumerable.Range(1, 4).Select(i => reader.IsDBNull(i) ? string.Empty : reader.GetString(i)));
        }

        if (accountNumber.HasValue)
        {
            const string ruleSql = @"
IF OBJECT_ID('dbo.NumberRangeRules', 'U') IS NOT NULL
BEGIN
    SELECT TOP (1) Richtung
    FROM dbo.NumberRangeRules
    WHERE @number BETWEEN RangeStart AND RangeEnd
    ORDER BY (RangeEnd - RangeStart), RangeStart;
END";
            await using var ruleCommand = new SqlCommand(ruleSql, connection);
            ruleCommand.Parameters.Add(new SqlParameter("@number", SqlDbType.Int) { Value = accountNumber.Value });
            var direction = (await ruleCommand.ExecuteScalarAsync(cancellationToken)) as string;
            if (!string.IsNullOrWhiteSpace(direction))
                return string.Equals(direction, "Einnahme", StringComparison.OrdinalIgnoreCase);
        }

        string[] incomeTerms = { "EINNAHM", "ERTRAG", "ERTRAEG", "ERLÖS", "ERLOS", "ERLOES", "REVENUE", "INCOME" };
        return incomeTerms.Any(term => accountText.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
