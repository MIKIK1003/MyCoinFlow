using System;
using System.Text.Json.Serialization;

namespace MyCoinFlow.Services.Update
{
    /// <summary>
    /// Struktur der version.json im OneDrive.
    /// </summary>
    public sealed class AppVersionInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.1";

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        /// <summary>
        /// Direkter Download-Link zur Setup.exe (OneDrive "Link zu dieser Datei").
        /// </summary>
        [JsonPropertyName("fileUrl")]
        public string FileUrl { get; set; } = string.Empty;

        /// <summary>
        /// Optional: SHA256 der Setup-Datei (Absicherung).
        /// </summary>
        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }
    }
}
