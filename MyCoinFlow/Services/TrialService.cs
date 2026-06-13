using System;
using System.IO;
using System.Text.Json;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// 30-Tage-Testversion für MyCoinFlow.
    ///
    /// Noch nicht mit Login verknüpft.
    /// In diesem Schritt nur die technische Grundlage.
    /// </summary>
    public sealed class TrialService
    {
        private const int TrialDays = 30;

        private static string ConfigFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MyCoinFlow");

        private static string ConfigPath =>
            Path.Combine(ConfigFolder, "trial.json");

        public bool HasTrial()
        {
            return File.Exists(ConfigPath);
        }

        public bool IsTrialActive()
        {
            try
            {
                var info = Load();

                if (info == null)
                    return false;

                return info.ExpiresUtc > DateTime.UtcNow;
            }
            catch
            {
                return false;
            }
        }

        public int GetRemainingDays()
        {
            try
            {
                var info = Load();

                if (info == null)
                    return 0;

                var days = (info.ExpiresUtc - DateTime.UtcNow).TotalDays;

                return Math.Max(0, (int)Math.Ceiling(days));
            }
            catch
            {
                return 0;
            }
        }

        public bool StartTrial()
        {
            try
            {
                if (HasTrial())
                    return false;

                Directory.CreateDirectory(ConfigFolder);

                var info = new TrialInfo
                {
                    StartedUtc = DateTime.UtcNow,
                    ExpiresUtc = DateTime.UtcNow.AddDays(TrialDays)
                };

                var json = JsonSerializer.Serialize(
                    info,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                File.WriteAllText(ConfigPath, json);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public AppActivationStatus GetStatus()
        {
            if (!HasTrial())
                return AppActivationStatus.None;

            return IsTrialActive()
                ? AppActivationStatus.Trial
                : AppActivationStatus.Expired;
        }

        private TrialInfo? Load()
        {
            if (!File.Exists(ConfigPath))
                return null;

            var json = File.ReadAllText(ConfigPath);

            return JsonSerializer.Deserialize<TrialInfo>(json);
        }

        private sealed class TrialInfo
        {
            public DateTime StartedUtc { get; set; }

            public DateTime ExpiresUtc { get; set; }
        }
    }
}