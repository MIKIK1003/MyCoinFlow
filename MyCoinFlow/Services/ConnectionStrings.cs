using System;
using System.IO;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Aktive DB zentral verwalten.
    /// Standard-Server ist SQL Server Express: .\SQLEXPRESS
    /// </summary>
    public static class ConnectionStrings
    {
        // EIN Standard (kein entweder/oder):
        // Wir arbeiten konsistent mit SQL Server Express (Installer installiert SQLEXPRESS).
        private const string DefaultServer = @".\SQLEXPRESS";

        private const string DefaultDbName = "MyCoinFlowDB";

        private static string _activeDbName = DefaultDbName;
        private static bool _loaded;

        /// <summary>
        /// ConnectionString zur aktuell aktiven DB.
        /// </summary>
        public static string Current
        {
            get
            {
                EnsureLoaded();
                return new SqlConnectionStringBuilder
                {
                    DataSource = DefaultServer,
                    InitialCatalog = _activeDbName,
                    IntegratedSecurity = true,
                    Encrypt = false,
                    TrustServerCertificate = true
                }.ConnectionString;
            }
        }

        /// <summary>
        /// ConnectionString auf master (für DB-Existenzprüfung, Listing, Restore, etc.)
        /// </summary>
        public static string Master
        {
            get
            {
                return new SqlConnectionStringBuilder
                {
                    DataSource = DefaultServer,
                    InitialCatalog = "master",
                    IntegratedSecurity = true,
                    Encrypt = false,
                    TrustServerCertificate = true
                }.ConnectionString;
            }
        }

        /// <summary>
        /// Nur der DB-Name der aktiven DB.
        /// </summary>
        public static string ActiveDatabaseName
        {
            get { EnsureLoaded(); return _activeDbName; }
        }

        /// <summary>
        /// Standard-DB-Name (Default).
        /// </summary>
        public static string DefaultDatabaseName => DefaultDbName;

        public static void SetActiveDatabase(string dbName)
        {
            if (string.IsNullOrWhiteSpace(dbName))
                throw new ArgumentException("DB-Name darf nicht leer sein.", nameof(dbName));

            _activeDbName = dbName.Trim();

            // Verbindungspools leeren: ab jetzt nutzen neue Verbindungen garantiert die neue DB
            try { SqlConnection.ClearAllPools(); } catch { /* ok */ }

            Save();
        }

        private static string ConfigFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyCoinFlow");

        private static string ConfigPath => Path.Combine(ConfigFolder, "config.json");

        private static void EnsureLoaded()
        {
            if (_loaded) return;

            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();

                    _activeDbName = string.IsNullOrWhiteSpace(cfg.ActiveDatabaseName)
                        ? DefaultDbName
                        : cfg.ActiveDatabaseName.Trim();
                }
                else
                {
                    _activeDbName = DefaultDbName;
                }
            }
            catch
            {
                _activeDbName = DefaultDbName;
            }

            _loaded = true;
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);
                var cfg = new AppConfig { ActiveDatabaseName = _activeDbName };
                var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // im worst case bleibt's in-memory
            }
        }

        private class AppConfig
        {
            public string ActiveDatabaseName { get; set; } = DefaultDbName;
        }
    }
}
