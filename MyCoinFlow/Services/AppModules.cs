using System;
using System.IO;
using System.Text.Json;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Zentrale Modulfreigabe für MyCoinFlow 2.0.
    ///
    /// AppEdition bleibt aus Kompatibilitätsgründen bestehen.
    /// AppModules ist ab Version 2.0 die neue zentrale Stelle für Modulrechte.
    /// </summary>
    public static class AppModules
    {
        public static bool IsFinanceEnabled { get; private set; } = true;
        public static bool IsPropertyEnabled { get; private set; } = false;
        public static bool IsWealthEnabled { get; private set; } = false;
        public static bool IsHomeEnabled { get; private set; } = false;
        public static bool IsDmsEnabled { get; private set; } = false;

        private static string ConfigFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyCoinFlow");

        private static string ConfigPath =>
            Path.Combine(ConfigFolder, "modules.json");

        public static void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    ApplyLegacyEditionFallback();
                    return;
                }

                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<ModuleConfig>(json);

                if (cfg == null)
                {
                    ApplyLegacyEditionFallback();
                    return;
                }

                SetModules(
                    finance: cfg.Finance,
                    property: cfg.Property,
                    wealth: cfg.Wealth,
                    home: cfg.Home,
                    dms: cfg.Dms,
                    persist: false);
            }
            catch
            {
                ApplyLegacyEditionFallback();
            }
        }

        public static void SetModules(
            bool finance,
            bool property,
            bool wealth,
            bool home,
            bool dms = false,
            bool persist = true)
        {
            // Finanzen ist das Grundmodul und bleibt immer aktiv.
            IsFinanceEnabled = true;

            IsPropertyEnabled = property;
            IsWealthEnabled = wealth;
            IsHomeEnabled = home;
            IsDmsEnabled = dms;

            if (persist)
                Save();
        }

        public static void ResetToBasic()
        {
            SetModules(
                finance: true,
                property: false,
                wealth: false,
                home: false,
                dms: false,
                persist: true);
        }

        public static string GetStatusText()
        {
            return
                $"Finanzen: {(IsFinanceEnabled ? "aktiv" : "nicht aktiv")}\n" +
                $"Immobilien: {(IsPropertyEnabled ? "aktiv" : "nicht aktiv")}\n" +
                $"Wealth: {(IsWealthEnabled ? "aktiv" : "nicht aktiv")}\n" +
                $"Haushalt: {(IsHomeEnabled ? "aktiv" : "nicht aktiv")}\n" +
                $"DMS: {(IsDmsEnabled ? "aktiv" : "nicht aktiv")}";
        }

        private static void ApplyLegacyEditionFallback()
        {
            try
            {
                AppEdition.Load();

                SetModules(
                    finance: true,
                    property: AppEdition.IsPlus,
                    wealth: false,
                    home: false,
                    dms: false,
                    persist: false);
            }
            catch
            {
                IsFinanceEnabled = true;
                IsPropertyEnabled = false;
                IsWealthEnabled = false;
                IsHomeEnabled = false;
                IsDmsEnabled = false;
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);

                var cfg = new ModuleConfig
                {
                    Finance = IsFinanceEnabled,
                    Property = IsPropertyEnabled,
                    Wealth = IsWealthEnabled,
                    Home = IsHomeEnabled,
                    Dms = IsDmsEnabled
                };

                var json = JsonSerializer.Serialize(
                    cfg,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // bewusst still: Lizenz-/Modulstatus darf die App nicht blockieren
            }
        }

        private sealed class ModuleConfig
        {
            public bool Finance { get; set; } = true;
            public bool Property { get; set; }
            public bool Wealth { get; set; }
            public bool Home { get; set; }
            public bool Dms { get; set; }
        }
    }
}