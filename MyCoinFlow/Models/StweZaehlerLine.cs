namespace MyCoinFlow.Models
{
    public class StweZaehlerLine
    {
        public int Id { get; set; }
        public int ZaehlerId { get; set; }

        public int? EinheitId { get; set; }
        public string EinheitBezeichnung { get; set; } = "";

        public int EigentuemerId { get; set; }
        public string EigentuemerName { get; set; } = "";

        public decimal AnteilProzent { get; set; }
    }
}