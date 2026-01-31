namespace MyCoinFlow.Models
{
    public class StweZaehlerdatenLine
    {
        public int Id { get; set; }
        public int SetId { get; set; }
        public int ZaehlerId { get; set; }
        public decimal NeuWert { get; set; } // absoluter Zählerstand (kWh)
    }
}
