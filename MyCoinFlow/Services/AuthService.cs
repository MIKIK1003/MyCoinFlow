using System;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Auth-Service: sorgt für Users-Schema (Username-Login), legt Erstuser an, validiert Login.
    /// Email bleibt optional (Altbestand).
    /// </summary>
    public sealed class AuthService
    {
        private string _cs => ConnectionStrings.Current;

        public async Task EnsureSchemaAsync()
        {
            await using var c = new SqlConnection(_cs);
            await c.OpenAsync();

            // Tabelle anlegen
            await ExecAsync(c, @"
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        Username     NVARCHAR(100)     NULL,   -- wird gleich NOT NULL
        PasswordHash NVARCHAR(400)     NOT NULL,
        IsActive     BIT               NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT(1),
        CreatedAt    DATETIME2(7)      NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT(SYSUTCDATETIME()),
        Email        NVARCHAR(320)     NULL
    );
END");

            // Username-Spalte, falls fehlt
            await ExecAsync(c, @"
IF COL_LENGTH(N'dbo.Users', N'Username') IS NULL
    ALTER TABLE dbo.Users ADD Username NVARCHAR(100) NULL;");

            // Username füllen, wo leer
            await ExecAsync(c, @"
UPDATE u
   SET Username = CASE
                      WHEN u.Email IS NULL OR LTRIM(RTRIM(u.Email)) = '' THEN u.Username
                      WHEN CHARINDEX('@', u.Email) > 0 THEN LEFT(u.Email, CHARINDEX('@', u.Email) - 1)
                      ELSE u.Email
                  END
FROM dbo.Users u
WHERE (u.Username IS NULL OR LTRIM(RTRIM(u.Username)) = '');");

            // Username NOT NULL
            await ExecAsync(c, @"
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'Username' AND is_nullable = 1)
    ALTER TABLE dbo.Users ALTER COLUMN Username NVARCHAR(100) NOT NULL;");

            // Unique-Index
            await ExecAsync(c, @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_Username' AND object_id = OBJECT_ID(N'dbo.Users'))
    CREATE UNIQUE INDEX UX_Users_Username ON dbo.Users(Username);");

            // Email NULL-able (Alt-DB absichern)
            await ExecAsync(c, @"
IF COL_LENGTH(N'dbo.Users', N'Email') IS NOT NULL
BEGIN
    DECLARE @isNullable bit;
    SELECT @isNullable = is_nullable
      FROM sys.columns
     WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'Email';
    IF @isNullable = 0
        ALTER TABLE dbo.Users ALTER COLUMN Email NVARCHAR(320) NULL;
END");
        }

        public async Task<bool> HasAnyUserAsync()
        {
            await using var c = new SqlConnection(_cs);
            await c.OpenAsync();
            await EnsureSchemaAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Users) THEN 1 ELSE 0 END;";
            var x = await cmd.ExecuteScalarAsync();
            return ToInt32Safe(x) == 1;
        }

        public async Task CreateFirstUserAsync(string username, string password)
        {
            if (!IsValidUsername(username))
                throw new ArgumentException("Ungültiger Benutzername. Erlaubt: 3–32 Zeichen (A-Z, a-z, 0-9, ., _, -).");
            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
                throw new ArgumentException("Das Passwort muss mindestens 6 Zeichen haben.");

            await using var c = new SqlConnection(_cs);
            await c.OpenAsync();
            await EnsureSchemaAsync();

            if (await HasAnyInternalAsync(c))
                throw new InvalidOperationException("Es existiert bereits ein Benutzer.");

            await using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(1) FROM dbo.Users WHERE LOWER(Username)=LOWER(@u);";
                cmd.Parameters.Add(new SqlParameter("@u", SqlDbType.NVarChar, 100) { Value = username.Trim() });
                var cntObj = await cmd.ExecuteScalarAsync();
                var cnt = ToInt32Safe(cntObj);
                if (cnt > 0) throw new InvalidOperationException("Dieser Benutzername ist bereits vergeben.");
            }

            var hash = PasswordHasher.Hash(password);

            await using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO dbo.Users(Username, PasswordHash, Email) VALUES(@u,@p,@e);";
                cmd.Parameters.Add(new SqlParameter("@u", SqlDbType.NVarChar, 100) { Value = username.Trim() });
                cmd.Parameters.Add(new SqlParameter("@p", SqlDbType.NVarChar, 400) { Value = hash });
                cmd.Parameters.Add(new SqlParameter("@e", SqlDbType.NVarChar, 320) { Value = username.Trim() + "@local" });
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<bool> ValidateUserAsync(string username, string password)
        {
            if (!IsValidUsername(username) || string.IsNullOrWhiteSpace(password))
                return false;

            await using var c = new SqlConnection(_cs);
            await c.OpenAsync();
            await EnsureSchemaAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT TOP(1) PasswordHash, IsActive FROM dbo.Users WHERE LOWER(Username)=LOWER(@u);";
            cmd.Parameters.Add(new SqlParameter("@u", SqlDbType.NVarChar, 100) { Value = username.Trim() });

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return false;

            var hash = r.GetString(0);
            var active = r.GetBoolean(1);
            if (!active) return false;

            return PasswordHasher.Verify(password, hash);
        }

        // helpers
        private static async Task ExecAsync(SqlConnection c, string sql)
        {
            await using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<bool> HasAnyInternalAsync(SqlConnection c)
        {
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Users) THEN 1 ELSE 0 END;";
            var x = await cmd.ExecuteScalarAsync();
            return ToInt32Safe(x) == 1;
        }

        // Sichere Konvertierung für ExecuteScalar-Ergebnisse (NULL/DBNull -> 0)
        private static int ToInt32Safe(object? value)
        {
            if (value is null || value is DBNull) return 0;
            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                // Defensive: im Zweifel lieber 0 als Crash bei fehlerhaftem Scalar-Rückgabewert
                return 0;
            }
        }

        private static bool IsValidUsername(string username)
            => !string.IsNullOrWhiteSpace(username) &&
               username.Length >= 3 && username.Length <= 32 &&
               Regex.IsMatch(username, @"^[A-Za-z0-9._-]+$");
    }
}
