namespace MyCoinFlow.Models
{
    public class Adresse
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Strasse { get; set; }
        public string? PLZ { get; set; }
        public string? Ort { get; set; }
        public string? Land { get; set; }
        public string? Typ { get; set; }     // z.B. Lieferant, Dienstleister, Kunde
        public string? IBAN { get; set; }    // Bankverbindung der Adresse
        public string? Notiz { get; set; }
        public bool IstBudgetiert { get; set; }
        public int? StandardEinnahmenKontoId { get; set; }
        public int? DefaultKontoId { get; set; }

    }
}
