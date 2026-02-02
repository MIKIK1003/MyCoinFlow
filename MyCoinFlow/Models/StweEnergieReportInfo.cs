namespace MyCoinFlow.Models
{
    /// <summary>
    /// Numerische Energie-Grundlage für den STWE-Bericht.
    /// Diese Werte sind rein lesend/auswertend. Formatierung erfolgt im Bericht.
    /// </summary>
    public class StweEnergieReportInfo
    {
        public int LiegenschaftId { get; set; }

        public int ZaehlerdatenSetId { get; set; }
        public System.DateTime ZaehlerdatenSetDatum { get; set; }
        public string? ZaehlerdatenSetNotiz { get; set; }

        public int? VorherigesZaehlerdatenSetId { get; set; }
        public System.DateTime? VorherigesZaehlerdatenSetDatum { get; set; }

        /// <summary>kWh total auf der Rechnung (manuell im Zählerdaten-Set erfasst)</summary>
        public decimal RechnungKwhTotal { get; set; }

        /// <summary>Optional: Gutschrift CHF (statistisch)</summary>
        public decimal? GutschriftChf { get; set; }

        /// <summary>Summe interne kWh-Diffs (nur DIREKT/ALLG/HEIZ; ohne EVU)</summary>
        public decimal InterneKwhTotal { get; set; }

        /// <summary>PV-Direktverbrauch (inkl. Batterieverschiebung), in kWh</summary>
        public decimal SolarDirektKwh { get; set; }

        /// <summary>Preis pro kWh gemäss Rechnung: Betrag / RechnungKwhTotal</summary>
        public decimal PreisProKwh { get; set; }

        /// <summary>Kontrollwert: RechnungKwhTotal / InterneKwhTotal (1.0 = passt exakt)</summary>
        public decimal Scale { get; set; }
    }
}
