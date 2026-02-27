using System;

namespace MyCoinFlow.Models
{
    public class Transaktion
    {
        public int Id { get; set; }
        public DateTime Datum { get; set; }

        // NEU: Optionales Budgetdatum (für Abgrenzung im Budget; Bankdatum bleibt Datum)
        public DateTime? BudgetDatum { get; set; }

        public int? VonKontoId { get; set; }
        public int? NachKontoId { get; set; }

        public decimal Betrag { get; set; }
        public string? Notiz { get; set; }

        public int? AdresseId { get; set; }
        public string? AdresseName { get; set; }

        public int? GeldinstitutId { get; set; }
        public string? BankName { get; set; }

        // NEU: für Anzeige-Logik (KK-Detail erkenntlich machen)
        public string? ImportQuelle { get; set; }
    }
}