using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Stellt die AKTIVE Datenbank (ConnectionStrings.ActiveDatabaseName) aus einer .bak-Datei wieder her.
    /// Vorgehen:
    /// - aktive DB -> SINGLE_USER WITH ROLLBACK IMMEDIATE
    /// - RESTORE DATABASE ... WITH REPLACE, MOVE <logical> TO <aktuelle physische Pfade>, RECOVERY
    /// - MULTI_USER
    /// - ClearAllPools (neue Verbindungen gegen frische Dateien)
    /// </summary>
    public sealed class DbRestoreService
    {
        private const string MasterCs = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Initial Catalog=master;";

        public async Task RestoreActiveAsync(string backupFilePath)
        {
            if (string.IsNullOrWhiteSpace(backupFilePath))
                throw new ArgumentException("Pfad zur .bak-Datei ist erforderlich.", nameof(backupFilePath));
            if (!File.Exists(backupFilePath))
                throw new FileNotFoundException("Die .bak-Datei wurde nicht gefunden.", backupFilePath);

            var dbName = ConnectionStrings.ActiveDatabaseName;
            if (string.IsNullOrWhiteSpace(dbName))
                throw new InvalidOperationException("Aktiver DB-Name ist leer.");

            await using var conn = new SqlConnection(MasterCs);
            await conn.OpenAsync();

            // 1) Aktuelle physische Dateien (MDF/LDF) der aktiven DB ermitteln
            string mdfPath, ldfPath;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT TOP(1) mf.physical_name
FROM sys.master_files mf
JOIN sys.databases d ON d.database_id = mf.database_id
WHERE d.name = @db AND mf.type = 0; -- ROWS (DATA)
";
                cmd.Parameters.AddWithValue("@db", dbName);
                var data = await cmd.ExecuteScalarAsync();
                if (data == null || data == DBNull.Value)
                    throw new InvalidOperationException($"Daten-Datei der Datenbank '{dbName}' wurde nicht gefunden.");
                mdfPath = Convert.ToString(data)!;
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT TOP(1) mf.physical_name
FROM sys.master_files mf
JOIN sys.databases d ON d.database_id = mf.database_id
WHERE d.name = @db AND mf.type = 1; -- LOG
";
                cmd.Parameters.AddWithValue("@db", dbName);
                var log = await cmd.ExecuteScalarAsync();
                if (log == null || log == DBNull.Value)
                    throw new InvalidOperationException($"Log-Datei der Datenbank '{dbName}' wurde nicht gefunden.");
                ldfPath = Convert.ToString(log)!;
            }

            // 2) Logical Names aus dem Backup ermitteln
            string logicalDataName, logicalLogName;
            await using (var fl = conn.CreateCommand())
            {
                fl.CommandText = "RESTORE FILELISTONLY FROM DISK = @p";
                fl.Parameters.AddWithValue("@p", backupFilePath);
                await using var r = await fl.ExecuteReaderAsync();
                string? dataName = null, logName = null;
                while (await r.ReadAsync())
                {
                    var type = r["Type"]?.ToString();
                    var lname = r["LogicalName"]?.ToString();
                    if (string.Equals(type, "D", StringComparison.OrdinalIgnoreCase))
                        dataName = lname;
                    else if (string.Equals(type, "L", StringComparison.OrdinalIgnoreCase))
                        logName = lname;
                }
                if (string.IsNullOrEmpty(dataName) || string.IsNullOrEmpty(logName))
                    throw new InvalidOperationException("Logical File Names konnten nicht aus dem Backup gelesen werden.");
                logicalDataName = dataName!;
                logicalLogName = logName!;
            }

            // 3) SINGLE_USER → RESTORE … WITH REPLACE → MULTI_USER
            await using (var single = conn.CreateCommand())
            {
                single.CommandText = @"
DECLARE @db sysname = @dbname;
DECLARE @sql nvarchar(max) =
    N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE';
EXEC (@sql);";
                single.Parameters.AddWithValue("@dbname", dbName);
                await single.ExecuteNonQueryAsync();
            }

            await using (var restore = conn.CreateCommand())
            {
                restore.CommandText = @"
DECLARE @db   sysname        = @dbname;
DECLARE @bak  nvarchar(4000) = @path;
DECLARE @ld   sysname        = @logicalData;
DECLARE @ll   sysname        = @logicalLog;
DECLARE @mdf  nvarchar(4000) = @mdfPath;
DECLARE @ldf  nvarchar(4000) = @ldfPath;

DECLARE @sql nvarchar(max) =
N'RESTORE DATABASE ' + QUOTENAME(@db) + N'
 FROM DISK = ''' + REPLACE(@bak, '''', '''''') + N'''
 WITH REPLACE,
      MOVE ''' + REPLACE(@ld, '''', '''''') + N''' TO ''' + REPLACE(@mdf, '''', '''''') + N''',
      MOVE ''' + REPLACE(@ll, '''', '''''') + N''' TO ''' + REPLACE(@ldf, '''', '''''') + N''',
      RECOVERY';

EXEC (@sql);";
                restore.Parameters.AddWithValue("@dbname", dbName);
                restore.Parameters.AddWithValue("@path", backupFilePath);
                restore.Parameters.AddWithValue("@logicalData", logicalDataName);
                restore.Parameters.AddWithValue("@logicalLog", logicalLogName);
                restore.Parameters.AddWithValue("@mdfPath", mdfPath);
                restore.Parameters.AddWithValue("@ldfPath", ldfPath);
                await restore.ExecuteNonQueryAsync();
            }

            await using (var multi = conn.CreateCommand())
            {
                multi.CommandText = @"
DECLARE @db sysname = @dbname;
DECLARE @sql nvarchar(max) =
    N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET MULTI_USER';
EXEC (@sql);";
                multi.Parameters.AddWithValue("@dbname", dbName);
                await multi.ExecuteNonQueryAsync();
            }

            // 4) Pools leeren – neue Verbindungen sehen sofort den wiederhergestellten Stand
            try { SqlConnection.ClearAllPools(); } catch { /* ok */ }
        }
    }
}
