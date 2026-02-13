using System;
using System.IO;
using System.Text.Json;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Zentrales Feature-Flag für Basic/Plus.
    /// Default = Basic (IsPlus=false).
    /// Wird aus %AppData%\MyCoinFlow\edition.json gelesen.
    /// </summary>
    public static class AppEdition
    {
        public static bool IsPlus { get; private set; } = false;

        private static string ConfigFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyCoinFlow");

        private static string ConfigPath => Path.Combine(ConfigFolder, "edition.json");

        public static void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    IsPlus = false;
                    return;
                }

                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<EditionConfig>(json);
                IsPlus = cfg?.IsPlus ?? false;
            }
            catch
            {
                IsPlus = false;
            }
        }

        public static void SetPlus(bool isPlus)
        {
            IsPlus = isPlus;

            try
            {
                Directory.CreateDirectory(ConfigFolder);
                var json = JsonSerializer.Serialize(
                    new EditionConfig { IsPlus = isPlus },
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // still
            }
        }

        private sealed class EditionConfig
        {
            public bool IsPlus { get; set; }
        }
    }
}
