using System;

namespace MyCoinFlow.Models
{
    public class DmsDocument
    {
        public int Id { get; set; }
        public int? TransaktionId { get; set; }
        public string? EntityType { get; set; }
        public int? EntityId { get; set; }

        public string? Titel { get; set; }
        public string? Kategorie { get; set; }

        public string FileName { get; set; } = "";
        public string? OriginalName { get; set; }
        public string FolderRel { get; set; } = "";
        public long? SizeBytes { get; set; }
        public DateTime ImportedAtUtc { get; set; }
        public string? OcrStatus { get; set; }

        public string TitelAnzeige => !string.IsNullOrWhiteSpace(Titel) ? Titel : FileName;

        public string VerknuepftMitAnzeige => EntityType switch
        {
            "Transaktion" => $"Transaktion #{EntityId ?? TransaktionId}",
            null => "–",
            _ => $"{EntityType} #{EntityId}"
        };

        public string SizeDisplay => SizeBytes.HasValue
            ? (SizeBytes.Value >= 1024 * 1024
                ? $"{(SizeBytes.Value / (1024.0 * 1024.0)):0.0} MB"
                : $"{(SizeBytes.Value / 1024.0):0} KB")
            : "";
    }
}
