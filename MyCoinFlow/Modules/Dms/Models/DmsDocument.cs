using System;
using System.ComponentModel;
using System.Globalization;

namespace MyCoinFlow.Models
{
    public class DmsDocument : INotifyPropertyChanged
    {
        // Nur IstNeu meldet Änderungen (das Neu-Icon soll beim Anklicken sofort
        // verschwinden, ohne die ganze Liste neu zu laden).
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _istNeu;

        /// <summary>Frisch importiert und noch nie angeklickt (GesehenAm in der DB ist NULL).</summary>
        public bool IstNeu
        {
            get => _istNeu;
            set
            {
                if (_istNeu == value) return;
                _istNeu = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IstNeu)));
            }
        }

        public int Id { get; set; }
        public int? TransaktionId { get; set; }
        public string? EntityType { get; set; }
        public int? EntityId { get; set; }

        public string? Titel { get; set; }
        public string? Kategorie { get; set; }

        /// <summary>Frei erfassbarer Beschreibungstext (im Bearbeiten-Dialog gepflegt).</summary>
        public string? Beschreibung { get; set; }

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

        // NEU: Adresse/Betrag der verknüpften Transaktion (nur befüllt, wenn EntityType=Transaktion).
        public decimal? TransBetrag { get; set; }
        public string? TransAdresseName { get; set; }

        // NEU: am Dokument selbst hinterlegte Adresse (für Dokumente ohne Zahlung,
        // z.B. Bankdokumente oder Verträge).
        public int? AdresseId { get; set; }
        public string? EigeneAdresseName { get; set; }

        // NEU: aus dem Dokumenttext erkannter Rechnungsbetrag (bester Betragskandidat).
        public decimal? ErkannterBetrag { get; set; }

        public string TitelAnzeige => !string.IsNullOrWhiteSpace(Titel) ? Titel : FileName;

        // Eigene Zuordnung hat Vorrang (was der Benutzer erfasst, wird auch angezeigt);
        // sonst die Adresse der verknüpften Transaktion.
        public string AdresseAnzeige => EigeneAdresseName ?? TransAdresseName ?? "";

        // Verknüpft: Betrag der Transaktion (verbindlich). Frei: erkannter Betrag
        // aus der Texterkennung, als solcher gekennzeichnet.
        public string BetragAnzeige => TransBetrag.HasValue
            ? TransBetrag.Value.ToString("N2", CultureInfo.CurrentCulture)
            : ErkannterBetrag.HasValue
                ? ErkannterBetrag.Value.ToString("N2", CultureInfo.CurrentCulture) + " ?"
                : "";

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
