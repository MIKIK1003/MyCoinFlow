using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Mandanten = Datenbanken auf .\SQLEXPRESS.
    /// Ein Mandant gilt als "gültig", wenn dbo.Users existiert.
    /// Neue Mandanten werden aus dem Template-Backup (ProgramData) erstellt.
    /// </summary>
    public sealed class MandantService
    {
        private static string TemplateBakPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MyCoinFlow", "Master", "MyCoinFlowMaster.bak");

        /// <summary>
        /// Listet alle User-Datenbanken (database_id > 4), die eine dbo.Users Tabelle besitzen.
        /// </summary>
        public async Task<IList<string>> GetMandantenAsync()
        {
            var result = new List<string>();
            var names = new List<string>();

            // 1) DB-Namen aus master holen (immer .\SQLEXPRESS via ConnectionStrings.Master)
            await using (var c = new SqlConnection(ConnectionStrings.Master))
            {
                await c.OpenAsync();
                var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    names.Add(r.GetString(0));
            }

            // 2) Pro DB prüfen: dbo.Users existiert?
            foreach (var db in names)
            {
                try
                {
                    var cs = BuildDbConnectionString(db);
                    await using var c2 = new SqlConnection(cs);
                    await c2.OpenAsync();

                    var cmd2 = c2.CreateCommand();
                    cmd2.CommandText = "SELECT OBJECT_ID(N'dbo.Users','U')";
                    var obj = await cmd2.ExecuteScalarAsync();

                    if (obj != null && obj != DBNull.Value)
                        result.Add(db);
                }
                catch
                {
                    // defekte DB ignorieren
                }
            }

            return result;
        }

        /// <summary>
        /// Schaltet die aktive DB (nur Name speichern).
        /// </summary>
        public void SetActive(string dbName) => ConnectionStrings.SetActiveDatabase(dbName);

        /// <summary>
        /// Legt eine neue Mandanten-DB an (Restore aus Template .bak) und setzt sie als aktiv.
        /// Name muss eindeutig sein.
        /// </summary>
        public async Task CreateMandantDatabaseFromTemplateAsync(string newDatabaseName)
        {
            if (string.IsNullOrWhiteSpace(newDatabaseName))
                throw new ArgumentException("DB-Name darf nicht leer sein.", nameof(newDatabaseName));

            newDatabaseName = newDatabaseName.Trim();

            // 1) Sicherstellen: Template vorhanden
            var bak = TemplateBakPath;
            if (!File.Exists(bak))
                throw new FileNotFoundException("Template-Backup nicht gefunden: " + bak, bak);

            // 2) Existiert DB schon?
            if (await DbExistsAsync(newDatabaseName))
                throw new InvalidOperationException($"Die Datenbank '{newDatabaseName}' existiert bereits.");

            // 3) Restore aus Template
            await RestoreFromBakAsync(bak, newDatabaseName);

            // 4) Danach aktiv setzen
            ConnectionStrings.SetActiveDatabase(newDatabaseName);
        }

        private static string BuildDbConnectionString(string dbName)
        {
            // Gleicher Server wie Master (.\SQLEXPRESS), nur anderer Catalog
            var b = new SqlConnectionStringBuilder(ConnectionStrings.Master)
            {
                InitialCatalog = dbName,
                IntegratedSecurity = true,
                Encrypt = false,
                TrustServerCertificate = true
            };
            return b.ConnectionString;
        }

        private static async Task<bool> DbExistsAsync(string dbName)
        {
            await using var c = new SqlConnection(ConnectionStrings.Master);
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT DB_ID(@n)";
            cmd.Parameters.AddWithValue("@n", dbName);

            var id = await cmd.ExecuteScalarAsync();
            return id != null && id != DBNull.Value;
        }

        private static async Task RestoreFromBakAsync(string bakPath, string targetDbName)
        {
            // Logische Dateinamen im Backup ermitteln
            var (logicalData, logicalLog) = await GetLogicalNamesFromBackupAsync(bakPath);

            // Standardpfade der Instanz ermitteln
            var (dataDir, logDir) = await GetInstanceDefaultPathsAsync();

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
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = sql;
            cmd.CommandTimeout = 600;

            cmd.Parameters.AddWithValue("@bak", bakPath);
            cmd.Parameters.AddWithValue("@logicalData", logicalData);
            cmd.Parameters.AddWithValue("@logicalLog", logicalLog);
            cmd.Parameters.AddWithValue("@mdf", targetMdf);
            cmd.Parameters.AddWithValue("@ldf", targetLdf);

            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<(string dataDir, string logDir)> GetInstanceDefaultPathsAsync()
        {
            const string sql = @"
SELECT
  CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)) AS DataPath,
  CAST(SERVERPROPERTY('InstanceDefaultLogPath')  AS nvarchar(4000)) AS LogPath;
";

            await using var c = new SqlConnection(ConnectionStrings.Master);
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
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
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
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
    }
}
