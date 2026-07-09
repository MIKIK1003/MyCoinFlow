using System;
using System.IO;
using System.Text.Json;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Einfacher Admin-Schalter (per JSON), unabhängig von Userverwaltung.
    /// Datei: %AppData%\MyCoinFlow\admin.json
    /// </summary>
    public static class AdminMode
    {
        public static bool IsAdmin { get; private set; } = false;

        private static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyCoinFlow");

        private static string PathFile => Path.Combine(Folder, "admin.json");

        public static void Load()
        {
            try
            {
                if (!File.Exists(PathFile))
                {
                    IsAdmin = false;
                    return;
                }

                var json = File.ReadAllText(PathFile);
                var cfg = JsonSerializer.Deserialize<AdminConfig>(json);
                IsAdmin = cfg?.IsAdmin ?? false;
            }
            catch
            {
                IsAdmin = false;
            }
        }

        public static void Set(bool isAdmin)
        {
            IsAdmin = isAdmin;
            try
            {
                Directory.CreateDirectory(Folder);
                var json = JsonSerializer.Serialize(new AdminConfig { IsAdmin = isAdmin }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PathFile, json);
            }
            catch
            {
                // still
            }
        }

        private sealed class AdminConfig
        {
            public bool IsAdmin { get; set; }
        }
    }
}
