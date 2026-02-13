using System;
using System.IO;
using System.Text.Json;

namespace MyCoinFlow.Services
{
    public static class AppFeatures
    {
        public static bool IsPlusEnabled { get; private set; } = false;

        private static string ConfigFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyCoinFlow");

        private static string ConfigPath => Path.Combine(ConfigFolder, "features.json");

        public static void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    IsPlusEnabled = false;
                    return;
                }

                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<FeatureConfig>(json);
                IsPlusEnabled = cfg?.IsPlusEnabled ?? false;
            }
            catch
            {
                IsPlusEnabled = false;
            }
        }

        public static void SetPlusEnabled(bool enabled)
        {
            IsPlusEnabled = enabled;

            try
            {
                Directory.CreateDirectory(ConfigFolder);
                var json = JsonSerializer.Serialize(new FeatureConfig { IsPlusEnabled = enabled },
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // still
            }
        }

        private sealed class FeatureConfig
        {
            public bool IsPlusEnabled { get; set; }
        }
    }
}
