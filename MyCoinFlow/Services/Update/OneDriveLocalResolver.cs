using System;
using System.IO;

namespace MyCoinFlow.Services.Update
{
    /// <summary>
    /// Findet den lokalen OneDrive-Root und liefert den Pfad zur version.json,
    /// z. B. ...\OneDrive\Documents\MyCoinFlowUpdate\version.json
    /// (berücksichtigt auch "Dokumente" als lokalisierte Anzeige).
    /// </summary>
    internal static class OneDriveLocalResolver
    {
        public static string? TryGetReleaseFeedLocalPath()
        {
            var root = GetOneDriveRoot();
            if (string.IsNullOrWhiteSpace(root)) return null;

            var candidates = new[]
            {
                Path.Combine(root, "Documents",  "MyCoinFlowUpdate", "version.json"),
                Path.Combine(root, "Dokumente",  "MyCoinFlowUpdate", "version.json"),
            };

            foreach (var p in candidates)
                if (File.Exists(p)) return p;

            return null;
        }

        private static string? GetOneDriveRoot()
        {
            var c = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrWhiteSpace(c) && Directory.Exists(c)) return c;

            c = Environment.GetEnvironmentVariable("OneDriveConsumer");
            if (!string.IsNullOrWhiteSpace(c) && Directory.Exists(c)) return c;

            c = Environment.GetEnvironmentVariable("OneDriveCommercial");
            if (!string.IsNullOrWhiteSpace(c) && Directory.Exists(c)) return c;

            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var alt = Path.Combine(user, "OneDrive");
            return Directory.Exists(alt) ? alt : null;
        }
    }
}
