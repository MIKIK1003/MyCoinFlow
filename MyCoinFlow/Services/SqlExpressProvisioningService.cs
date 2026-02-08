using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Stellt sicher, dass auf SQL Server Express (.\SQLEXPRESS) mindestens eine Start-DB existiert.
    /// Quelle ist ein Template-Backup (.bak) in ProgramData.
    ///
    /// Standard:
    ///  - Template:  C:\ProgramData\MyCoinFlow\Master\MyCoinFlowMaster.bak
    ///  - Default DB: MyCoinFlowDB
    ///
    /// Wichtig:
    ///  - Das Restore liest die .bak-Datei aus dem Dateisystem – SQL Server Service muss darauf zugreifen können.
    /// </summary>
    public sealed class SqlExpressProvisioningService
    {
        public const string DefaultInstance = @".\SQLEXPRESS";
        public const string DefaultDatabaseName = "MyCoinFlowDB";

        public static string TemplateBakPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MyCoinFlow", "Master", "MyCoinFlowMaster.bak");

        private static string MasterConnectionString =>
            new SqlConnectionStringBuilder
            {
                DataSource = DefaultInstance,
                InitialCatalog = "master",
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                Encrypt = false
            }.ConnectionString;

        public async Task EnsureDefaultDatabaseExistsAsync()
        {
            // 1) Kann ich master öffnen?
            await EnsureSqlReachableAsync().ConfigureAwait(false);

            // 2) Gibt es schon eine Default DB?
            if (await DbExistsAsync(DefaultDatabaseName).ConfigureAwait(false))
                return;

            // 3) Template muss vorhanden sein
            var bak = TemplateBakPath;
            if (!File.Exists(bak))
            {
                throw new FileNotFoundException(
                    "Template-Datenbank (Backup) wurde nicht gefunden. Erwartet: " + bak, bak);
            }

            // 4) Restore Default DB aus Template
            await RestoreFromBakAsync(bak, DefaultDatabaseName).ConfigureAwait(false);
        }

        private static async Task EnsureSqlReachableAsync()
        {
            await using var c = new SqlConnection(MasterConnectionString);
            await c.OpenAsync().ConfigureAwait(false);
        }

        public static async Task<bool> DbExistsAsync(string dbName)
        {
            if (string.IsNullOrWhiteSpace(dbName)) return false;

            await using var c = new SqlConnection(MasterConnectionString);
            await c.OpenAsync().ConfigureAwait(false);

            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT DB_ID(@n)";
            cmd.Parameters.AddWithValue("@n", dbName.Trim());

            var id = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return id != null && id != DBNull.Value;
        }

        private static async Task RestoreFromBakAsync(string bakPath, string targetDbName)
        {
            // 1) Default Data/Log Pfade der Instanz ermitteln
            var (dataDir, logDir) = await GetInstanceDefaultPathsAsync().ConfigureAwait(false);

            // 2) Logische Dateinamen aus Backup lesen
            var (logicalData, logicalLog) = await GetLogicalNamesFromBackupAsync(bakPath).ConfigureAwait(false);

            // 3) Zielpfade definieren
            var targetMdf = Path.Combine(dataDir, $"{targetDbName}.mdf");
            var targetLdf = Path.Combine(logDir, $"{targetDbName}_log.ldf");

            // 4) Restore durchführen
            //    REPLACE ist hier bewusst, damit "halb angelegte" DBs sauber überschrieben werden können.
            var sql = @"
RESTORE DATABASE [" + targetDbName + @"]
FROM DISK = @bak
WITH REPLACE,
     MOVE @logicalData TO @mdf,
     MOVE @logicalLog  TO @ldf;
";

            await using var c = new SqlConnection(MasterConnectionString);
            await c.OpenAsync().ConfigureAwait(false);

            await using var cmd = c.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = sql;

            cmd.Parameters.AddWithValue("@bak", bakPath);
            cmd.Parameters.AddWithValue("@logicalData", logicalData);
            cmd.Parameters.AddWithValue("@logicalLog", logicalLog);
            cmd.Parameters.AddWithValue("@mdf", targetMdf);
            cmd.Parameters.AddWithValue("@ldf", targetLdf);

            // Restore kann dauern – Timeout erhöhen
            cmd.CommandTimeout = 600;

            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private static async Task<(string dataDir, string logDir)> GetInstanceDefaultPathsAsync()
        {
            const string sql = @"
SELECT
  CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)) AS DataPath,
  CAST(SERVERPROPERTY('InstanceDefaultLogPath')  AS nvarchar(4000)) AS LogPath;
";

            await using var c = new SqlConnection(MasterConnectionString);
            await c.OpenAsync().ConfigureAwait(false);

            await using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            if (!await r.ReadAsync().ConfigureAwait(false))
                throw new InvalidOperationException("Konnte SQL Default-Pfade nicht ermitteln.");

            var data = (r["DataPath"] as string) ?? "";
            var log = (r["LogPath"] as string) ?? "";

            if (string.IsNullOrWhiteSpace(data) || string.IsNullOrWhiteSpace(log))
                throw new InvalidOperationException("SQL Default-Pfade sind leer. Restore kann nicht sicher ausgeführt werden.");

            return (data.Trim(), log.Trim());
        }

        private static async Task<(string logicalData, string logicalLog)> GetLogicalNamesFromBackupAsync(string bakPath)
        {
            const string sql = @"RESTORE FILELISTONLY FROM DISK = @bak;";

            await using var c = new SqlConnection(MasterConnectionString);
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
                throw new InvalidOperationException("Konnte logische Dateinamen aus Backup nicht lesen (RESTORE FILELISTONLY).");

            return (data!, log!);
        }
    }
}
