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

        public int Prioritaet { get; set; } = 100;
        public bool IstAktiv { get; set; } = true;
    }
}