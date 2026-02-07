namespace MyCoinFlow.Models
{
    public class StweZaehlerdatenSet
    {
        public int Id { get; set; }
        public int LiegenschaftId { get; set; }

        public System.DateTime ErfasstAm { get; set; }

        public decimal? RechnungKwhTotal { get; set; }     // kWh aus Rechnung (manuell)
        public decimal? GutschriftChf { get; set; }        // optional (nur Statistik/Report)

        // NEU: zurückgespeiste kWh (statistisch / Auswertung)
        public decimal? RueckgespeistKwh { get; set; }     // kWh zurück ins Netz (manuell)

        public string? Notiz { get; set; }
    }
}
