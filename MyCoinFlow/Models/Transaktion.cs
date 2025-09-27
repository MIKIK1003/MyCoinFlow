namespace MyCoinFlow.Models
{
    public class Transaktion
    {
        public int Id { get; set; }
        public DateTime Datum { get; set; }

        public int? VonKontoId { get; set; }   // nullable
        public int? NachKontoId { get; set; }  // nullable

        public decimal Betrag { get; set; }
        public string? Notiz { get; set; }

        public int? AdresseId { get; set; }
        public string? AdresseName { get; set; }

        public int? GeldinstitutId { get; set; }
        public string? BankName { get; set; }
    }
}
