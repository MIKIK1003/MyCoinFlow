namespace MyCoinFlow.Models
{
    public class AdressBuchungsregel
    {
        public int Id { get; set; }
        public int AdresseId { get; set; }
        public bool IstEinnahme { get; set; }

        public string TextPattern { get; set; } = "";
        public string PatternModus { get; set; } = "Contains";

        public int KontoId { get; set; }

        // NEU: Betrag zur Unterscheidung
        public decimal? BetragVon { get; set; }
        public decimal? BetragBis { get; set; }

        public int Prioritaet { get; set; } = 100;
        public bool IstAktiv { get; set; } = true;

        // NEU: Evidenz, aus der die Regel abgeleitet wurde
        public int BelegAnzahl { get; set; } = 1;
        public System.DateTime? LetzteBestaetigung { get; set; }
        public bool IstKonflikt { get; set; }
    }
}