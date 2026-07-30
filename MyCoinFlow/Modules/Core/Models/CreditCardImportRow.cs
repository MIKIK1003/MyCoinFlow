namespace MyCoinFlow.Models
{
    public class CreditCardImportRow
    {
        public int Id { get; set; }            // NEU: Staging-Id
        public int BatchId { get; set; }       // NEU: Batch-Id

        public DateTime Datum { get; set; }
        public string Beschreibung { get; set; } = "";
        public string? Haendler { get; set; }
        public string? Kategorie { get; set; }
        public decimal Betrag { get; set; }
        public string DebitKredit { get; set; } = "";
        public string? Kartennummer { get; set; }

        public int? KontoId { get; set; }
        public string? Konto { get; set; }     // Anzeige

        public int? AdresseId { get; set; }
        public string? Adresse { get; set; }   // Anzeige
    }
}
