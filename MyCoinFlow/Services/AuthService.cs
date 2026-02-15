using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services
{
    public sealed class AuthService
    {
        /// <summary>
        /// Stellt dbo.Users sicher (inkl. IsAdmin).
        /// Idempotent: kann beliebig oft aufgerufen werden.
        /// </summary>
        public async Task EnsureSchemaAsync()
        {
            await using var c = new SqlConnection(ConnectionStrings.Current);
            await c.OpenAsync();

            var sql = @"
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

-- Spalte IsAdmin nachziehen, falls alte DB
IF COL_LENGTH('dbo.Users','IsAdmin') IS NULL
BEGIN
    ALTER TABLE dbo.Users ADD IsAdmin BIT NOT NULL CONSTRAINT DF_Users_IsAdmin2 DEFAULT(0);
END;
";
            await using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> HasAnyUserAsync()
        {
            await EnsureSchemaAsync();

            await using var c = new SqlConnection(ConnectionStrings.Current);
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT TOP 1 1 FROM dbo.Users;";
            var v = await cmd.ExecuteScalarAsync();
            return v != null && v != DBNull.Value;
        }

        public async Task<bool> ValidateUserAsync(string username, string password)
        {
            await EnsureSchemaAsync();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                return false;

            username = username.Trim();

            await using var c = new SqlConnection(ConnectionStrings.Current);
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT PasswordHash, IsActive
FROM dbo.Users
WHERE Username = @u;";
            cmd.Parameters.AddWithValue("@u", username);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
                return false;

            var hash = r.GetString(0);
            var isActive = r.GetBoolean(1);
            if (!isActive) return false;

            return PasswordHasher.Verify(password, hash);
        }

        public async Task<bool> GetIsAdminAsync(string username)
        {
            await EnsureSchemaAsync();

            if (string.IsNullOrWhiteSpace(username))
                return false;

            username = username.Trim();

            await using var c = new SqlConnection(ConnectionStrings.Current);
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT IsAdmin FROM dbo.Users WHERE Username = @u;";
            cmd.Parameters.AddWithValue("@u", username);

            var v = await cmd.ExecuteScalarAsync();
            if (v == null || v == DBNull.Value) return false;

            return Convert.ToBoolean(v);
        }


        /// <summary>
        /// Ersten User anlegen: wird immer Admin.
        /// </summary>
        public async Task CreateFirstUserAsync(string username, string password, string? email = null)
        {
            await EnsureSchemaAsync();

            username = (username ?? "").Trim();
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Username leer.");
            if (string.IsNullOrEmpty(password) || password.Length < 6)
                throw new ArgumentException("Passwort zu kurz (mind. 6).");

            await using var c = new SqlConnection(ConnectionStrings.Current);
            await c.OpenAsync();

            // Nur wenn wirklich noch kein User existiert
            await using (var chk = c.CreateCommand())
            {
                chk.CommandText = "SELECT COUNT(1) FROM dbo.Users;";
                var count = Convert.ToInt32(await chk.ExecuteScalarAsync());
                if (count > 0)
                    throw new InvalidOperationException("Es existiert bereits ein Benutzer in dieser Datenbank.");
            }

            var hash = PasswordHasher.Hash(password);

            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO dbo.Users (Username, PasswordHash, IsActive, IsAdmin, CreatedAt, Email)
VALUES (@u, @p, 1, 1, SYSDATETIME(), @e);";
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@p", hash);
            cmd.Parameters.AddWithValue("@e", (object?)email ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        // ===== Admin-Funktionen =====

        public sealed class UserRow
        {
            public int Id { get; set; }
            public string Username { get; set; } = "";
            public string? Email { get; set; }
            public bool IsActive { get; set; }
            public bool IsAdmin { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public async Task<List<UserRow>> GetUsersAsync()
        {
            await EnsureSchemaAsync();

            var list = new List<UserRow>();

            await using var c = new SqlConnection(ConnectionStrings.Current);
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT Id, Username, Email, IsActive, IsAdmin, CreatedAt
FROM dbo.Users
ORDER BY Username;";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new UserRow
                {
                    Id = r.GetInt32(0),
                    Username = r.GetString(1),
                    Email = r.IsDBNull(2) ? null : r.GetString(2),
                    IsActive = r.GetBoolean(3),
                    IsAdmin = r.GetBoolean(4),
                    CreatedAt = r.GetDateTime(5)
                });
            }
            return list;
        }

        public async Task CreateUserAsync(string username, string password, string? email, bool isAdmin)
        {
            await EnsureSchemaAsync();

            username = (username ?? "").Trim();
            if (string.IsNullOrWhiteSpace(username) || username.Length < 3 || username.Length > 64)
                throw new ArgumentException("Username ungültig (3–64).");
            if (string.IsNullOrEmpty(password) || password.Length < 6)
                throw new ArgumentException("Passwort zu kurz (mind. 6).");

            var hash = PasswordHasher.Hash(password);

            await using var c = new SqlConnection(ConnectionStrings.Current);
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO dbo.Users (Username, PasswordHash, IsActive, IsAdmin, CreatedAt, Email)
VALUES (@u, @p, 1, @a, SYSDATETIME(), @e);";
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@p", hash);
            cmd.Parameters.AddWithValue("@a", isAdmin ? 1 : 0);
            cmd.Parameters.AddWithValue("@e", (object?)email ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ResetPasswordAsync(int userId, string newPassword)
        {
            await EnsureSchemaAsync();

            if (userId <= 0) throw new ArgumentException("UserId ungültig.");
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
                throw new ArgumentException("Passwort zu kurz (mind. 6).");

            var hash = PasswordHasher.Hash(newPassword);

            await using var c = new SqlConnection(ConnectionStrings.Current);
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE dbo.Users SET PasswordHash=@p WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@p", hash);
            cmd.Parameters.AddWithValue("@id", userId);

            var n = await cmd.ExecuteNonQueryAsync();
            if (n != 1) throw new InvalidOperationException("User nicht gefunden.");
        }

        public async Task SetUserActiveAsync(int userId, bool isActive)
        {
            await EnsureSchemaAsync();

            if (userId <= 0) throw new ArgumentException("UserId ungültig.");

            await using var c = new SqlConnection(ConnectionStrings.Current);
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE dbo.Users SET IsActive=@a WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", userId);

            var n = await cmd.ExecuteNonQueryAsync();
            if (n != 1) throw new InvalidOperationException("User nicht gefunden.");
        }

        public async Task SetUserAdminAsync(int userId, bool isAdmin)
        {
            await EnsureSchemaAsync();

            if (userId <= 0) throw new ArgumentException("UserId ungültig.");

            await using var c = new SqlConnection(ConnectionStrings.Current);
            await c.OpenAsync();

            // Sicherheitsnetz: Mindestens ein Admin muss übrig bleiben.
            if (!isAdmin)
            {
                await using var chk = c.CreateCommand();
                chk.CommandText = @"
SELECT COUNT(1)
FROM dbo.Users
WHERE IsAdmin = 1 AND IsActive = 1 AND Id <> @id;";
                chk.Parameters.AddWithValue("@id", userId);

                var remaining = Convert.ToInt32(await chk.ExecuteScalarAsync());
                if (remaining <= 0)
                    throw new InvalidOperationException("Es muss mindestens ein aktiver Admin bestehen bleiben.");
            }

            await using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE dbo.Users SET IsAdmin=@a WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@a", isAdmin ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", userId);

            var n = await cmd.ExecuteNonQueryAsync();
            if (n != 1) throw new InvalidOperationException("User nicht gefunden.");
        }



    }
}
