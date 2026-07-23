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

        // NEU: erkanntes/angenommenes Dokumentdatum (Basis fürs Fälligkeits-Tracking).
        public DateTime? DokumentDatum { get; set; }

        // NEU: Garantieschein-Kennzeichnung.
        public bool IstGarantieschein { get; set; }
        public DateTime? GarantieAblaufDatum { get; set; }

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

        // ---------------- Fälligkeits-Tracking (30 Tage ab Dokumentdatum) ----------------
        // Nur relevant, solange das Dokument noch keiner Transaktion zugeordnet ist – sobald
        // verknüpft, gilt die Rechnung als erledigt (siehe VerknuepftMitAnzeige).

        public DateTime? FaelligkeitsDatum => (EntityType == null && DokumentDatum.HasValue)
            ? DokumentDatum.Value.AddDays(30)
            : null;

        public bool IstUeberfaellig => FaelligkeitsDatum.HasValue && FaelligkeitsDatum.Value < DateTime.Today;

        public string FaelligAnzeige => FaelligkeitsDatum.HasValue
            ? FaelligkeitsDatum.Value.ToString("dd.MM.yyyy")
            : "";

        // ---------------- Garantie ----------------

        public bool IstGarantieAbgelaufen => IstGarantieschein && GarantieAblaufDatum.HasValue
            && GarantieAblaufDatum.Value < DateTime.Today;

        public bool IstGarantieBaldAblaufend => IstGarantieschein && GarantieAblaufDatum.HasValue
            && !IstGarantieAbgelaufen && GarantieAblaufDatum.Value <= DateTime.Today.AddDays(30);

        public string GarantieAnzeige => !IstGarantieschein
            ? ""
            : GarantieAblaufDatum.HasValue
                ? (IstGarantieAbgelaufen ? $"abgelaufen ({GarantieAblaufDatum.Value:dd.MM.yyyy})" : GarantieAblaufDatum.Value.ToString("dd.MM.yyyy"))
                : "Garantie (ohne Datum)";
    }
}
