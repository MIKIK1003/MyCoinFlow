using System;
using System.IO;

namespace MyCoinFlow.Services.Update
{
    /// <summary>
    /// Findet den lokalen OneDrive-Root und liefert Pfade für den Update-Feed
    /// und die Setup-Datei im Ordner:
    ///   ...\OneDrive\Documents\MyCoinFlowUpdate\
    /// (bzw. "Dokumente" lokalisiert).
    /// </summary>
    internal static class OneDriveLocalResolver
    {
        public static string? TryGetReleaseFeedLocalPath()
        {
            var d = TryGetUpdateFolder();
            if (string.IsNullOrWhiteSpace(d)) return null;

            var p = Path.Combine(d!, "version.json");
            return File.Exists(p) ? p : null;
        }

        /// <summary>Ordner ...\OneDrive\(Documents|Dokumente)\MyCoinFlowUpdate</summary>
        public static string? TryGetUpdateFolder()
        {
            var root = GetOneDriveRoot();
            if (string.IsNullOrWhiteSpace(root)) return null;

            var candidates = new[]
            {
                Path.Combine(root, "Documents",  "MyCoinFlowUpdate"),
                Path.Combine(root, "Dokumente",  "MyCoinFlowUpdate"),
            };

            foreach (var d in candidates)
                if (Directory.Exists(d)) return d;

            return null;
        }

        /// <summary>Vollständiger Pfad zur lokalen Setup-Datei (falls vorhanden).</summary>
        public static string? TryGetSetupLocalPath(string fileName = "MyCoinFlow-Setup.exe")
        {
            var folder = TryGetUpdateFolder();
            if (string.IsNullOrWhiteSpace(folder)) return null;

            var p = Path.Combine(folder!, fileName);
            return File.Exists(p) ? p : null;
        }

        private static string? GetOneDriveRoot()
        {
            // gängige Variablen
            var c = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrWhiteSpace(c) && Directory.Exists(c)) return c;

            c = Environment.GetEnvironmentVariable("OneDriveConsumer");
            if (!string.IsNullOrWhiteSpace(c) && Directory.Exists(c)) return c;

            c = Environment.GetEnvironmentVariable("OneDriveCommercial");
            if (!string.IsNullOrWhiteSpace(c) && Directory.Exists(c)) return c;

            // Fallback
            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var alt = Path.Combine(user, "OneDrive");
            return Directory.Exists(alt) ? alt : null;
        }
    }
}
