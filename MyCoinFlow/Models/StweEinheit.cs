namespace MyCoinFlow.Models
{
    public class StweEinheit
    {
        public int Id { get; set; }
        public int LiegenschaftId { get; set; }

        public string Bezeichnung { get; set; } = "";   // z.B. "Whg 3.2"
        public string? Typ { get; set; }                 // Wohnung/Garage/…
        public decimal? MeaPromille { get; set; }        // ‰
        public decimal? FlaecheM2 { get; set; }          // m2
        public string? Notiz { get; set; }
    }
}
