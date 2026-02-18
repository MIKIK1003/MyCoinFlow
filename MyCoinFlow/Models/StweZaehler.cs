namespace MyCoinFlow.Models
{
    /// <summary>
    /// Stammdaten: Stromzähler pro Liegenschaft.
    ///
    /// Typ bleibt (noch) bestehen für Anzeige/Legacy,
    /// die Verteil-Logik läuft neu über SchluesselId.
    /// </summary>
    public class StweZaehler
    {
        public int Id { get; set; }
        public int LiegenschaftId { get; set; }

        public string Name { get; set; } = "";

        /// <summary>
        /// DIREKT | ALLG | HEIZ | EVU (nur Anzeige/Legacy)
        /// </summary>
        public string Typ { get; set; } = "";

        /// <summary>
        /// Nur bei DIREKT gesetzt (Legacy/Info).
        /// </summary>
        public int? EinheitId { get; set; }

        /// <summary>
        /// NEU: Zugewiesener Schlüssel für die Energie-Verteilung (optional, aber für Energie-Sets künftig Pflicht).
        /// </summary>
        public int? SchluesselId { get; set; }

        public string? Notiz { get; set; }
    }
}
