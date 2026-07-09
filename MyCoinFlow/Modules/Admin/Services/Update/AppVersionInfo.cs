using System.Text.Json.Serialization;

namespace MyCoinFlow.Services.Update
{
    /// <summary>
    /// Schema der version.json (Release-Feed).
    /// </summary>
    public sealed class AppVersionInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";   // bewusst leer -> vermeidet falsche Defaults

        [JsonPropertyName("notes")]
        public string? Notes { get; set; } = null;

        /// <summary>Direkter Download-Link zur Setup.exe (kann leer bleiben, wenn lokaler Fallback genutzt wird).</summary>
        [JsonPropertyName("fileUrl")]
        public string FileUrl { get; set; } = "";

        /// <summary>Optional: SHA256 der Setup-Datei.</summary>
        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; } = null;

        /// <summary>Hilfsflag für UI/Validierung.</summary>
        [JsonIgnore]
        public bool HasVersion => !string.IsNullOrWhiteSpace(Version);
    }
}
