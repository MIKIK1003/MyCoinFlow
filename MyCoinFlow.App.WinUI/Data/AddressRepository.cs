using Microsoft.Data.SqlClient;
using MyCoinFlow.WinUI.Models;
using System.Data;

namespace MyCoinFlow.WinUI.Data;

public sealed class AddressRepository
{
    private static SqlConnection CreateConnection() => new(ConnectionStrings.Current);

    public async Task<IReadOnlyList<AddressReference>> AnalyzeDeletionAsync(
        int addressId,
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
WHERE schP.name = N'dbo' AND tP.name = N'Adresse' AND cP.name = N'Id';";

        var foreignKeys = new List<(string Schema, string Table, string Column)>();
        await using (var command = new SqlCommand(foreignKeysSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                foreignKeys.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        var references = new List<AddressReference>();
        foreach (var foreignKey in foreignKeys)
        {
            var sql = $"SELECT COUNT(*) FROM {Quote(foreignKey.Schema)}.{Quote(foreignKey.Table)} WHERE {Quote(foreignKey.Column)} = @id;";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = addressId });
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (count > 0)
            {
                references.Add(new AddressReference(
                    $"{foreignKey.Schema}.{foreignKey.Table}",
                    foreignKey.Column,
                    count));
            }
        }

        return references;
    }

    public async Task DeleteAliasesAsync(int addressId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "DELETE FROM dbo.AdresseAlias WHERE AdresseId = @id;",
            connection);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = addressId });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(int addressId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("DELETE FROM dbo.Adresse WHERE Id = @id;", connection);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = addressId });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Quote(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}
