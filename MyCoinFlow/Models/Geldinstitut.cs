namespace MyCoinFlow.Models
{
    public class Geldinstitut
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? BIC { get; set; }
        public string? IBAN { get; set; }
        public string? KontoNummer { get; set; }
        public string? Notiz { get; set; }
        public decimal Anfangsbestand { get; set; }    // z.B. 1500.00
        public DateTime? Anfangsdatum { get; set; }    // z.B. 01.01.2025 (optional)

    }
}
