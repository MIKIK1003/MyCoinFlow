namespace MyCoinFlow.Models
{
    /// <summary>
    /// Verteilzeile pro Zähler (Energie-Verteilung).
    /// Summe AnteilProzent muss 100% ergeben.
    /// </summary>
    public class StweZaehlerLine
    {
        public int Id { get; set; }
        public int ZaehlerId { get; set; }

        public int EigentuemerId { get; set; }
        public string EigentuemerName { get; set; } = "";

        public decimal AnteilProzent { get; set; }
    }
}
