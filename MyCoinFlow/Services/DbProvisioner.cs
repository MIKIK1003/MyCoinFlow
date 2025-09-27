using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Erstellt LocalDB-Datenbanken und klont das Schema (ohne Daten) aus einer Vorlage-DB.
    /// </summary>
    public sealed class DbProvisioner
    {
        private const string LocalDbInstance = @"(localdb)\MSSQLLocalDB";
        private const string MasterConnStr = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Initial Catalog=master;";

        public async Task CreateDatabaseAsync(string dbName, string? basePath = null)
        {
            ValidateDbName(dbName);
            string? mdf = null, ldf = null;

            if (!string.IsNullOrWhiteSpace(basePath))
            {
                var folder = NormalizeAndEnsureBasePath(basePath!);
                mdf = Path.Combine(folder, dbName + ".mdf");
                ldf = Path.Combine(folder, dbName + "_log.ldf");
            }

            await using var conn = new SqlConnection(MasterConnStr);
            await conn.OpenAsync();

            if (await DbExistsAsync(conn, dbName))
                throw new InvalidOperationException($"Die Datenbank '{dbName}' existiert bereits.");

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
DECLARE @sql nvarchar(max);
IF @mdf IS NULL OR @ldf IS NULL
BEGIN
    SET @sql = N'CREATE DATABASE ' + QUOTENAME(@dbname);
    EXEC (@sql);
END
ELSE
BEGIN
    DECLARE @mdfLit nvarchar(4000) = N'''' + REPLACE(@mdf, N'''', N'''''''') + N'''';
    DECLARE @ldfLit nvarchar(4000) = N'''' + REPLACE(@ldf, N'''', N'''''''') + N'''';
    SET @sql = N'CREATE DATABASE ' + QUOTENAME(@dbname) + N'
        ON (NAME=' + QUOTENAME(@dbname, '''') + N', FILENAME=' + @mdfLit + N')
       LOG ON (NAME=' + QUOTENAME(@dbname + N'_log', '''') + N', FILENAME=' + @ldfLit + N')';
    EXEC (@sql);
END";
            cmd.Parameters.AddWithValue("@dbname", dbName);
            cmd.Parameters.AddWithValue("@mdf", (object?)mdf ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ldf", (object?)ldf ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task CloneSchemaFromTemplateAsync(string sourceDbName, string targetDbName)
        {
            ValidateDbName(sourceDbName);
            ValidateDbName(targetDbName);

            await using var sqlConn = new SqlConnection(MasterConnStr);
            await sqlConn.OpenAsync();
            var server = new Server(new ServerConnection(sqlConn));

            var src = server.Databases[sourceDbName]
                      ?? throw new InvalidOperationException($"Quell-DB '{sourceDbName}' nicht gefunden.");
            var dst = server.Databases[targetDbName]
                      ?? throw new InvalidOperationException($"Ziel-DB '{targetDbName}' nicht gefunden.");

            var transfer = new Transfer(src)
            {
                CopyAllObjects = false,

                CopyAllTables = true,
                CopyAllViews = true,
                CopyAllStoredProcedures = false,
                CopyAllUserDefinedFunctions = false,
                CopyAllSchemas = true,

                CopySchema = true,
                CopyData = false,

                DestinationServer = server.Name,
                DestinationDatabase = targetDbName,
                DestinationLoginSecure = true,

                Options = new ScriptingOptions
                {
                    IncludeIfNotExists = true,
                    SchemaQualify = true,
                    SchemaQualifyForeignKeysReferences = true,
                    DriAll = true,
                    Indexes = true,
                    Triggers = true,
                    Default = true,
                    FullTextIndexes = false,
                    Permissions = false,
                    Bindings = true,
                    ClusteredIndexes = true,
                    NonClusteredIndexes = true,
                    ExtendedProperties = true,
                    WithDependencies = true
                }
            };

            var script = transfer.ScriptTransfer(); // StringCollection
            var batches = new List<string>(script.Count);
            foreach (string batch in script)
            {
                if (!string.IsNullOrWhiteSpace(batch))
                    batches.Add(batch);
            }

            var targetConnStr = $@"Server={LocalDbInstance};Integrated Security=true;Initial Catalog={targetDbName};";
            await using var targetConn = new SqlConnection(targetConnStr);
            await targetConn.OpenAsync();

            foreach (var batch in batches)
            {
                await using var cmd = targetConn.CreateCommand();
                cmd.CommandText = batch;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // ---- helpers ----

        private static void ValidateDbName(string dbName)
        {
            if (string.IsNullOrWhiteSpace(dbName))
                throw new ArgumentException("Datenbankname darf nicht leer sein.");
            if (!Regex.IsMatch(dbName, @"^[A-Za-z0-9_\-]+$"))
                throw new ArgumentException("DB-Name enthält unzulässige Zeichen (erlaubt: A-Z, a-z, 0-9, _, -).");
        }

        private static string NormalizeAndEnsureBasePath(string rawPath)
        {
            var p = Environment.ExpandEnvironmentVariables(rawPath.Trim());
            p = p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Path.IsPathRooted(p))
                throw new ArgumentException("Bitte einen absoluten Ordnerpfad angeben (z. B. C:\\Users\\…\\SQL-DBs).");
            Directory.CreateDirectory(p);
            return p;
        }

        private static async Task<bool> DbExistsAsync(SqlConnection masterConn, string dbName)
        {
            await using var cmd = masterConn.CreateCommand();
            cmd.CommandText = "SELECT DB_ID(@n)";
            cmd.Parameters.AddWithValue("@n", dbName);
            var id = await cmd.ExecuteScalarAsync();
            return id != DBNull.Value && id != null;
        }
    }
}
