using Microsoft.Data.SqlClient;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.WinUI.Security;
using System.Data;

namespace MyCoinFlow.WinUI.Data;

public sealed class LoginRepository
{
    public async Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionStrings.Master);
        await connection.OpenAsync(cancellationToken);
        const string sql = @"
SELECT name
FROM sys.databases
WHERE database_id > 4
  AND state_desc = 'ONLINE'
ORDER BY name;";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
        }
        return result;
    }

    public async Task SelectDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        if (!await DatabaseExistsAsync(databaseName, cancellationToken))
            throw new InvalidOperationException($"Die Datenbank '{databaseName}' existiert nicht oder ist nicht erreichbar.");
        ConnectionStrings.SetActiveDatabase(databaseName);
        await EnsureSchemaAsync(cancellationToken);
        await EnsureTransactionBudgetDateColumnAsync(cancellationToken);
    }

    public async Task<bool> HasUsersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = new SqlConnection(ConnectionStrings.Current);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT TOP (1) 1 FROM dbo.Users;", connection);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task<LoginSession?> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        username = username.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return null;

        await EnsureSchemaAsync(cancellationToken);
        await using var connection = new SqlConnection(ConnectionStrings.Current);
        await connection.OpenAsync(cancellationToken);
        const string sql = @"
SELECT PasswordHash, IsActive, IsAdmin
FROM dbo.Users
WHERE Username = @username;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@username", SqlDbType.NVarChar, 64) { Value = username });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!reader.GetBoolean(1) || !PasswordHasher.Verify(password, reader.GetString(0))) return null;
        return new LoginSession(username, reader.GetBoolean(2), ConnectionStrings.ActiveDatabaseName);
    }

    public async Task CreateFirstUserAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        username = username.Trim();
        if (username.Length is < 3 or > 32 || username.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '.' or '_' or '-')))
            throw new ArgumentException("Benutzername: 3–32 Zeichen; erlaubt sind Buchstaben, Zahlen, Punkt, Unterstrich und Bindestrich.");
        if (password.Length < 6)
            throw new ArgumentException("Das Passwort muss mindestens 6 Zeichen haben.");

        await EnsureSchemaAsync(cancellationToken);
        await using var connection = new SqlConnection(ConnectionStrings.Current);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var countCommand = new SqlCommand("SELECT COUNT(1) FROM dbo.Users WITH (UPDLOCK, HOLDLOCK);", connection, (SqlTransaction)transaction))
        {
            var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
            if (count > 0)
                throw new InvalidOperationException("In diesem Mandanten existiert bereits ein Benutzer.");
        }

        const string insertSql = @"
INSERT INTO dbo.Users (Username, PasswordHash, IsActive, IsAdmin, CreatedAt)
VALUES (@username, @passwordHash, 1, 1, SYSDATETIME());";
        await using var insertCommand = new SqlCommand(insertSql, connection, (SqlTransaction)transaction);
        insertCommand.Parameters.Add(new SqlParameter("@username", SqlDbType.NVarChar, 64) { Value = username });
        insertCommand.Parameters.Add(new SqlParameter("@passwordHash", SqlDbType.NVarChar, 400) { Value = PasswordHasher.Hash(password) });
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionStrings.Master);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT DB_ID(@name);", connection);
        command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = databaseName.Trim() });
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }

    private static async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionStrings.Current);
        await connection.OpenAsync(cancellationToken);
        const string sql = @"
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        Username     NVARCHAR(64) NOT NULL CONSTRAINT UQ_Users_Username UNIQUE,
        PasswordHash NVARCHAR(400) NOT NULL,
        IsActive     BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT(1),
        IsAdmin      BIT NOT NULL CONSTRAINT DF_Users_IsAdmin DEFAULT(0),
        CreatedAt    DATETIME2 NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT(SYSDATETIME()),
        Email        NVARCHAR(256) NULL
    );
END;
IF COL_LENGTH('dbo.Users', 'IsAdmin') IS NULL
BEGIN
    ALTER TABLE dbo.Users ADD IsAdmin BIT NOT NULL CONSTRAINT DF_Users_IsAdmin2 DEFAULT(0);
END;";
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureTransactionBudgetDateColumnAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionStrings.Current);
        await connection.OpenAsync(cancellationToken);
        const string sql = @"
IF OBJECT_ID(N'dbo.Transaktion', N'U') IS NOT NULL AND COL_LENGTH('dbo.Transaktion', 'BudgetDatum') IS NULL
    ALTER TABLE dbo.Transaktion ADD BudgetDatum date NULL;";
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
