using System;

namespace MyCoinFlow.Models
{
    public class StweOwnerDetailRow
    {
        public DateTime Datum { get; set; }
        public int SetId { get; set; }
        public int TransaktionId { get; set; }

        public string Titel { get; set; } = "";
        public string? Schluessel { get; set; }
        public string? Notiz { get; set; }

        public decimal Betrag { get; set; }
    }
}
