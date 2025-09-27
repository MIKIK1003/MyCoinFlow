using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Erzeugt .bak-Sicherungen von LocalDB-Datenbanken (COPY_ONLY; optional COMPRESSION).
    /// Fällt automatisch auf „ohne Kompression“ zurück, wenn nicht unterstützt.
    /// </summary>
    public sealed class DbBackupService
    {
        private const string MasterCs = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Initial Catalog=master;";

        public async Task BackupAsync(string dbName, string backupFilePath, bool useCompression = true)
        {
            if (string.IsNullOrWhiteSpace(dbName))
                throw new ArgumentException("DB-Name darf nicht leer sein.", nameof(dbName));
            if (string.IsNullOrWhiteSpace(backupFilePath))
                throw new ArgumentException("Zielpfad darf nicht leer sein.", nameof(backupFilePath));

            // Zielordner anlegen
            var dir = Path.GetDirectoryName(backupFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            await using var conn = new SqlConnection(MasterCs);
            await conn.OpenAsync();

            // Existenz der DB prüfen
            await using (var chk = conn.CreateCommand())
            {
                chk.CommandText = "SELECT DB_ID(@n)";
                chk.Parameters.AddWithValue("@n", dbName.Trim());
                var id = await chk.ExecuteScalarAsync();
                if (id == null || id == DBNull.Value)
                    throw new InvalidOperationException($"Die Datenbank '{dbName}' wurde nicht gefunden.");
            }

            // Dynamisches SQL bauen (DISK/Pfad nicht parametrierbar → sicher quoten)
            string BuildBackupSql(bool useCompressionInner)
            {
                var options = "WITH INIT, COPY_ONLY" + (useCompressionInner ? ", COMPRESSION" : "");
                return @"
DECLARE @db  sysname         = @dbname;
DECLARE @p   nvarchar(4000)  = @path;
DECLARE @sql nvarchar(max) =
    N'BACKUP DATABASE ' + QUOTENAME(@db) + N' TO DISK = ''' +
    REPLACE(@p, '''', '''''') + N''' " + options + @"';
EXEC (@sql);";
            }

            // 1) Versuch: mit Kompression (wenn gewünscht)
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = BuildBackupSql(useCompression);
                cmd.Parameters.AddWithValue("@dbname", dbName);
                cmd.Parameters.AddWithValue("@path", backupFilePath);
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // 2) Fallback: ohne Kompression
                await using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = BuildBackupSql(false);
                cmd2.Parameters.AddWithValue("@dbname", dbName);
                cmd2.Parameters.AddWithValue("@path", backupFilePath);
                await cmd2.ExecuteNonQueryAsync();
            }
        }
    }
}
