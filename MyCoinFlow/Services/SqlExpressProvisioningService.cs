using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Stellt sicher, dass auf .\SQLEXPRESS mindestens eine Default-DB (MyCoinFlowDB) existiert.
    /// Quelle ist ein Template-Backup (.bak) in ProgramData, das vom Installer bereitgestellt wird.
    ///
    /// Standardpfad:
    ///   C:\ProgramData\MyCoinFlow\Master\MyCoinFlowMaster.bak
    ///
    /// Default DB:
    ///   MyCoinFlowDB
    /// </summary>
    public sealed class SqlExpressProvisioningService
    {
        public string TemplateBakPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MyCoinFlow", "Master", "MyCoinFlowMaster.bak");

        public async Task EnsureDefaultDatabaseExistsAsync()
        {
            // 1) SQL erreichbar?
            await EnsureSqlReachableAsync().ConfigureAwait(false);

            // 2) Default DB existiert schon?
            if (await DbExistsAsync(ConnectionStrings.DefaultDatabaseName).ConfigureAwait(false))
                return;

            // 3) Template vorhanden?
            var bak = TemplateBakPath;
            if (!File.Exists(bak))
                throw new FileNotFoundException("Template-Backup nicht gefunden: " + bak, bak);

            // 4) Restore Default DB
            await RestoreFromBakAsync(bak, ConnectionStrings.DefaultDatabaseName).ConfigureAwait(false);

            // 5) Final check
            if (!await DbExistsAsync(ConnectionStrings.DefaultDatabaseName).ConfigureAwait(false))
                throw new InvalidOperationException("Default-DB konnte nicht erstellt werden.");
        }

        private static async Task EnsureSqlReachableAsync()
        {
            await using var c = new SqlConnection(ConnectionStrings.Master);
            await c.OpenAsync().ConfigureAwait(false);
        }

        public static async Task<bool> DbExistsAsync(string dbName)
        {
            if (string.IsNullOrWhiteSpace(dbName)) return false;

            await using var c = new SqlConnection(ConnectionStrings.Master);
            await c.OpenAsync().ConfigureAwait(false);

            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT DB_ID(@n)";
            cmd.Parameters.AddWithValue("@n", dbName.Trim());

            var id = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return id != null && id != DBNull.Value;
        }

        private static async Task RestoreFromBakAsync(string bakPath, string targetDbName)
        {
            // Logische Dateinamen aus Backup
            var (logicalData, logicalLog) = await GetLogicalNamesFromBackupAsync(bakPath).ConfigureAwait(false);

            // SQL Standardpfade der Instanz
            var (dataDir, logDir) = await GetInstanceDefaultPathsAsync().ConfigureAwait(false);

            var targetMdf = Path.Combine(dataDir, $"{targetDbName}.mdf");
            var targetLdf = Path.Combine(logDir, $"{targetDbName}_log.ldf");

            var sql = @"
RESTORE DATABASE [" + targetDbName + @"]
FROM DISK = @bak
WITH REPLACE,
     MOVE @logicalData TO @mdf,
     MOVE @logicalLog  TO @ldf;
";

            await using var c = new SqlConnection(ConnectionStrings.Master);
            await c.OpenAsync().ConfigureAwait(false);

            await using var cmd = c.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = sql;
            cmd.CommandTimeout = 600; // Restore dauert

            cmd.Parameters.AddWithValue("@bak", bakPath);
            cmd.Parameters.AddWithValue("@logicalData", logicalData);
            cmd.Parameters.AddWithValue("@logicalLog", logicalLog);
            cmd.Parameters.AddWithValue("@mdf", targetMdf);
            cmd.Parameters.AddWithValue("@ldf", targetLdf);

            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private static async Task<(string dataDir, string logDir)> GetInstanceDefaultPathsAsync()
        {
            const string sql = @"
SELECT
  CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)) AS DataPath,
  CAST(SERVERPROPERTY('InstanceDefaultLogPath')  AS nvarchar(4000)) AS LogPath;
";
            await using var c = new SqlConnection(ConnectionStrings.Master);
            await c.OpenAsync().ConfigureAwait(false);

            await using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            if (!await r.ReadAsync().ConfigureAwait(false))
                throw new InvalidOperationException("Konnte SQL Default-Pfade nicht ermitteln.");

            var data = (r["DataPath"] as string) ?? "";
            var log = (r["LogPath"] as string) ?? "";

            if (string.IsNullOrWhiteSpace(data) || string.IsNullOrWhiteSpace(log))
                throw new InvalidOperationException("SQL Default-Pfade sind leer. Restore nicht möglich.");

            return (data.Trim(), log.Trim());
        }

        private static async Task<(string logicalData, string logicalLog)> GetLogicalNamesFromBackupAsync(string bakPath)
        {
            const string sql = @"RESTORE FILELISTONLY FROM DISK = @bak;";

            await using var c = new SqlConnection(ConnectionStrings.Master);
            await c.OpenAsync().ConfigureAwait(false);

            await using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@bak", bakPath);

            await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

            string? data = null;
            string? log = null;

            while (await r.ReadAsync().ConfigureAwait(false))
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
    }
}
