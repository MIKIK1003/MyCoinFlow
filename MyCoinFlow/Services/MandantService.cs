using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services
{
    public sealed class MandantService
    {
        private static string TemplateBakPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MyCoinFlow", "Master", "MyCoinFlowMaster.bak");

        public async Task<List<string>> GetAllDatabaseNamesAsync()
        {
            var list = new List<string>();

            await using var c = new SqlConnection(ConnectionStrings.Master);
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT name
FROM sys.databases
WHERE database_id > 4
ORDER BY name;";

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var name = r.GetString(0);
                if (!string.IsNullOrWhiteSpace(name))
                    list.Add(name);
            }

            return list;
        }

        public void SetActive(string dbName) => ConnectionStrings.SetActiveDatabase(dbName);

        public async Task CreateEmptyFromTemplateAsync(string newDbName)
        {
            if (string.IsNullOrWhiteSpace(newDbName))
                throw new ArgumentException("DB-Name darf nicht leer sein.", nameof(newDbName));

            newDbName = newDbName.Trim();

            var bak = TemplateBakPath;
            if (!File.Exists(bak))
                throw new FileNotFoundException("Template-Backup nicht gefunden: " + bak, bak);

            if (await DbExistsAsync(newDbName))
                throw new InvalidOperationException($"Die Datenbank '{newDbName}' existiert bereits.");

            // Restore
            await RestoreFromBakAsync(bak, newDbName);

            // 🔒 WICHTIG: Template darf keine produktiven User "mitbringen"
            await TryPurgeUsersAsync(newDbName);

            // aktiv setzen
            ConnectionStrings.SetActiveDatabase(newDbName);
        }

        public static async Task<bool> DbExistsAsync(string dbName)
        {
            await using var c = new SqlConnection(ConnectionStrings.Master);
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT DB_ID(@n)";
            cmd.Parameters.AddWithValue("@n", dbName);

            var id = await cmd.ExecuteScalarAsync();
            return id != null && id != DBNull.Value;
        }

        private static async Task TryPurgeUsersAsync(string dbName)
        {
            // defensiv: wenn Tabelle nicht existiert, einfach nichts machen.
            var cs = new SqlConnectionStringBuilder(ConnectionStrings.Master)
            {
                InitialCatalog = dbName,
                IntegratedSecurity = true,
                Encrypt = false,
                TrustServerCertificate = true
            }.ConnectionString;

            try
            {
                await using var c = new SqlConnection(cs);
                await c.OpenAsync();

                await using var cmd = c.CreateCommand();
                cmd.CommandText = @"
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
BEGIN
  DELETE FROM dbo.Users;
END
";
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // still – Mandant ist trotzdem angelegt, User werden dann beim Login neu erstellt
            }
        }

        private static async Task RestoreFromBakAsync(string bakPath, string targetDbName)
        {
            var (logicalData, logicalLog) = await GetLogicalNamesFromBackupAsync(bakPath);
            var (dataDir, logDir) = await GetInstanceDefaultPathsAsync();

            var targetMdf = Path.Combine(dataDir, $"{targetDbName}.mdf");
            var targetLdf = Path.Combine(logDir, $"{targetDbName}_log.ldf");

            var sql = @"
RESTORE DATABASE [" + targetDbName + @"]
FROM DISK = @bak
WITH REPLACE,
     MOVE @logicalData TO @mdf,
     MOVE @logicalLog  TO @ldf;";

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
  CAST(SERVERPROPERTY('InstanceDefaultLogPath')  AS nvarchar(4000)) AS LogPath;";

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
