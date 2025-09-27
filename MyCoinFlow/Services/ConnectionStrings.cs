using System;
using System.IO;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Aktive DB (Mandant) zentral verwalten: ConnectionString.Current liest/schreibt den aktiven DB-Namen.
    /// </summary>
    public static class ConnectionStrings
    {
        private const string DefaultDbName = "MyCoinFlowDB";
        private static string _activeDbName = DefaultDbName;
        private static bool _loaded;

        public static string Current
        {
            get
            {
                EnsureLoaded();
                return $@"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Initial Catalog={_activeDbName};";
            }
        }

        public static string ActiveDatabaseName
        {
            get { EnsureLoaded(); return _activeDbName; }
        }

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
