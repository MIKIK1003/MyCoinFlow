using Microsoft.Data.SqlClient;
using MyCoinFlow.WinUI.Models;
using System.Data;

namespace MyCoinFlow.WinUI.Data;

public sealed class AccountRepository
{
    private static SqlConnection CreateConnection() => new(ConnectionStrings.Current);

    public async Task<AccountDeletionPlan> AnalyzeDeletionAsync(
        int accountId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string foreignKeysSql = @"
SELECT schR.name, tR.name, cR.name
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.tables tP ON fk.referenced_object_id = tP.object_id
JOIN sys.columns cP ON cP.object_id = tP.object_id AND cP.column_id = fkc.referenced_column_id
JOIN sys.tables tR ON fk.parent_object_id = tR.object_id
JOIN sys.columns cR ON cR.object_id = tR.object_id AND cR.column_id = fkc.parent_column_id
JOIN sys.schemas schP ON schP.schema_id = tP.schema_id
JOIN sys.schemas schR ON schR.schema_id = tR.schema_id
WHERE schP.name = N'dbo' AND tP.name = N'Kontenplan' AND cP.name = N'Id';";

        var foreignKeys = new List<(string Schema, string Table, string Column)>();
        await using (var command = new SqlCommand(foreignKeysSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                foreignKeys.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        var references = new List<AccountReference>();
        foreach (var foreignKey in foreignKeys)
        {
            var table = $"{foreignKey.Schema}.{foreignKey.Table}";
            var sql = $"SELECT COUNT(*) FROM {Quote(foreignKey.Schema)}.{Quote(foreignKey.Table)} WHERE {Quote(foreignKey.Column)} = @id;";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = accountId });
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (count > 0)
                references.Add(new AccountReference(table, foreignKey.Column, count));
        }

        var examples = new List<string>();
        const string examplesSql = @"
SELECT TOP (6) Id, Name
FROM dbo.Adresse
WHERE DefaultKontoId = @id
ORDER BY Name;";
        await using (var command = new SqlCommand(examplesSql, connection))
        {
            command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = accountId });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                examples.Add($"#{reader.GetInt32(0)}: {(reader.IsDBNull(1) ? "(ohne Name)" : reader.GetString(1))}");
        }

        return new AccountDeletionPlan(references, examples);
    }

    public Task<int> DeleteCategoryMappingsAsync(int accountId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "DELETE FROM dbo.KategorieKontoMapping WHERE KontoId = @id;",
            accountId,
            cancellationToken);

    public Task<int> ClearAddressDefaultsAsync(int accountId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "UPDATE dbo.Adresse SET DefaultKontoId = NULL WHERE DefaultKontoId = @id;",
            accountId,
            cancellationToken);

    public Task<int> DeleteAsync(int accountId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "DELETE FROM dbo.Kontenplan WHERE Id = @id;",
            accountId,
            cancellationToken);

    private static async Task<int> ExecuteAsync(string sql, int accountId, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = accountId });
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}
