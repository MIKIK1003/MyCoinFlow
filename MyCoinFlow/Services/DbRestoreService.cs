using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Restore eines .bak in die aktive DB (überschreibt).
    /// Nutzt ConnectionStrings.Master (.\SQLEXPRESS).
    /// Robust: SINGLE_USER, REPLACE, MOVE auf SQL Default Data/Log Pfade.
    /// Ohne QUOTENAME: DB-Name wird in C# sicher gequotet.
    /// </summary>
    public sealed class DbRestoreService
    {
        public async Task RestoreActiveAsync(string bakFilePath)
        {
            if (string.IsNullOrWhiteSpace(bakFilePath))
                throw new ArgumentException("Backup-Datei darf nicht leer sein.", nameof(bakFilePath));

            bakFilePath = bakFilePath.Trim();

            if (!File.Exists(bakFilePath))
                throw new FileNotFoundException("Backup-Datei nicht gefunden: " + bakFilePath, bakFilePath);

            var targetDb = ConnectionStrings.ActiveDatabaseName;
            if (string.IsNullOrWhiteSpace(targetDb))
                throw new InvalidOperationException("Aktive Datenbank ist nicht gesetzt.");

            var targetDbQuoted = QuoteDbName(targetDb);

            await using var c = new SqlConnection(ConnectionStrings.Master);
            await c.OpenAsync();

            if (!await DbExistsAsync(c, targetDb))
                throw new InvalidOperationException($"Datenbank '{targetDb}' wurde nicht gefunden.");

            var (logicalData, logicalLog) = await GetLogicalNamesFromBackupAsync(c, bakFilePath);
            var (dataDir, logDir) = await GetInstanceDefaultPathsAsync(c);

            var targetMdf = Path.Combine(dataDir, $"{targetDb}.mdf");
            var targetLdf = Path.Combine(logDir, $"{targetDb}_log.ldf");

            // DISK/MOVE brauchen dynamisches SQL (Dateipfade als String-Literal)
            // Wir bauen das dynamische SQL in T-SQL, aber ohne QUOTENAME.
            var sql = @"
DECLARE @bak nvarchar(4000) = @bakPath;

-- Verbindungen kicken
EXEC(N'ALTER DATABASE " + targetDbQuoted + @" SET SINGLE_USER WITH ROLLBACK IMMEDIATE');

-- Restore
DECLARE @sql nvarchar(max) =
    N'RESTORE DATABASE " + targetDbQuoted + @" FROM DISK = ''' + REPLACE(@bak, '''', '''''') + N''' ' +
    N' WITH REPLACE, ' +
    N' MOVE ''' + REPLACE(@logicalData, '''', '''''') + N''' TO ''' + REPLACE(@mdf, '''', '''''') + N''',' +
    N' MOVE ''' + REPLACE(@logicalLog,  '''', '''''') + N''' TO ''' + REPLACE(@ldf, '''', '''''') + N''';';

EXEC(@sql);

-- Zurück auf MULTI_USER
EXEC(N'ALTER DATABASE " + targetDbQuoted + @" SET MULTI_USER');
";

            await using var cmd = c.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = sql;
            cmd.CommandTimeout = 900;

            cmd.Parameters.AddWithValue("@bakPath", bakFilePath);
            cmd.Parameters.AddWithValue("@logicalData", logicalData);
            cmd.Parameters.AddWithValue("@logicalLog", logicalLog);
            cmd.Parameters.AddWithValue("@mdf", targetMdf);
            cmd.Parameters.AddWithValue("@ldf", targetLdf);

            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // best effort: DB wieder MULTI_USER
                try
                {
                    await using var fix = c.CreateCommand();
                    fix.CommandText = "ALTER DATABASE " + targetDbQuoted + " SET MULTI_USER;";
                    await fix.ExecuteNonQueryAsync();
                }
                catch { /* ignore */ }

                throw;
            }
        }

        private static async Task<bool> DbExistsAsync(SqlConnection masterConn, string dbName)
        {
            await using var cmd = masterConn.CreateCommand();
            cmd.CommandText = "SELECT DB_ID(@n)";
            cmd.Parameters.AddWithValue("@n", dbName);

            var id = await cmd.ExecuteScalarAsync();
            return id != null && id != DBNull.Value;
        }

        private static async Task<(string logicalData, string logicalLog)> GetLogicalNamesFromBackupAsync(SqlConnection masterConn, string bakPath)
        {
            await using var cmd = masterConn.CreateCommand();
            cmd.CommandText = "RESTORE FILELISTONLY FROM DISK = @bak;";
            cmd.Parameters.AddWithValue("@bak", bakPath);

            await using var r = await cmd.ExecuteReaderAsync();

            string? data = null;
            string? log = null;

            while (await r.ReadAsync())
            {
                var type = (r["Type"] as string) ?? "";
                var logical = (r["LogicalName"] as string) ?? "";

                if (type.Equals("D", StringComparison.OrdinalIgnoreCase))
                    data ??= logical;
                else if (type.Equals("L", StringComparison.OrdinalIgnoreCase))
                    log ??= logical;
            }

            if (string.IsNullOrWhiteSpace(data) || string.IsNullOrWhiteSpace(log))
                throw new InvalidOperationException("Konnte logische Dateinamen aus Backup nicht lesen.");

            return (data!, log!);
        }

        private static async Task<(string dataDir, string logDir)> GetInstanceDefaultPathsAsync(SqlConnection masterConn)
        {
            await using var cmd = masterConn.CreateCommand();
            cmd.CommandText = @"
SELECT
  CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)) AS DataPath,
  CAST(SERVERPROPERTY('InstanceDefaultLogPath')  AS nvarchar(4000)) AS LogPath;
";
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
                throw new InvalidOperationException("Konnte SQL Default-Pfade nicht ermitteln.");

            var data = (r["DataPath"] as string) ?? "";
            var log = (r["LogPath"] as string) ?? "";

            if (string.IsNullOrWhiteSpace(data) || string.IsNullOrWhiteSpace(log))
                throw new InvalidOperationException("SQL Default-Pfade sind leer. Restore nicht möglich.");

            return (data.Trim(), log.Trim());
        }

        private static string QuoteDbName(string name)
        {
            // SQL Identifier quoting: ] becomes ]]
            return "[" + name.Replace("]", "]]") + "]";
        }
    }
}
