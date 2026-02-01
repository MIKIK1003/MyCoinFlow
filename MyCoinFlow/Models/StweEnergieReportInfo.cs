namespace MyCoinFlow.Models
{
    /// <summary>
    /// Numerische Energie-Grundlage für den STWE-Bericht (rein lesend).
    /// Formatierung erfolgt im Report/Printer.
    /// </summary>
    public class StweEnergieReportInfo
    {
        public int LiegenschaftId { get; set; }

        public int ZaehlerdatenSetId { get; set; }
        public System.DateTime ZaehlerdatenSetDatum { get; set; }
        public string? ZaehlerdatenSetNotiz { get; set; }

        public int? VorherigesZaehlerdatenSetId { get; set; }
        public System.DateTime? VorherigesZaehlerdatenSetDatum { get; set; }

        public decimal RechnungKwhTotal { get; set; }
        public decimal? GutschriftChf { get; set; }
        
        /// <summary>Summe der internen kWh-Differenzen (aus den Zählerständen Set vs. Vor-Set)</summary>
        public decimal InterneKwhTotal { get; set; }
        public decimal SolarDirektKwh { get; set; }

        /// <summary>Preis pro kWh gemäss Rechnung: Betrag / RechnungKwhTotal</summary>
        public decimal PreisProKwh { get; set; }

        /// <summary>
        /// Skalierungsfaktor, um die aus internen kWh berechneten Beträge exakt auf den Rechnungsbetrag zu bringen.
        /// 1.0 bedeutet "passt exakt".
        /// </summary>
        public decimal Scale { get; set; }
    }
}
