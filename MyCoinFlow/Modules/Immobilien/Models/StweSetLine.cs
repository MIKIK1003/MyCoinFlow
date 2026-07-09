namespace MyCoinFlow.Models
{
    public class StweSetLine
    {
        public int Id { get; set; }
        public int SetId { get; set; }

        public int? EinheitId { get; set; }
        public int? EigentuemerId { get; set; }

        public string? Schluessel { get; set; }
        public decimal Betrag { get; set; }

        public string? Notiz { get; set; }
    }
}
