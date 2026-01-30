namespace MyCoinFlow.Models
{
    /// <summary>
    /// Stammdaten: Stromzähler pro Liegenschaft.
    /// Typ:
    ///  - DIREKT: interner Wohnungszähler (EinheitId muss gesetzt sein)
    ///  - ALLG  : Allgemeinstrom (EinheitId muss leer sein)
    ///  - HEIZ  : Heizung/Wärmepumpe (EinheitId muss leer sein)
    ///  - EVU   : Hauptzähler EVU (nur Analyse/Report, EinheitId muss leer sein)
    /// </summary>
    public class StweZaehler
    {
        public int Id { get; set; }
        public int LiegenschaftId { get; set; }

        public string Name { get; set; } = "";

        /// <summary>
        /// DIREKT | ALLG | HEIZ | EVU
        /// </summary>
        public string Typ { get; set; } = "";

        /// <summary>
        /// Nur bei DIREKT gesetzt.
        /// </summary>
        public int? EinheitId { get; set; }

        public string? Notiz { get; set; }
    }
}
