using System;

namespace MyCoinFlow.Models
{
    public class StweSetRow
    {
        public int Id { get; set; }
        public int LiegenschaftId { get; set; }
        public int TransaktionId { get; set; }

        public DateTime Datum { get; set; }
        public decimal Betrag { get; set; }
        public string Titel { get; set; } = "";
        public bool IsClosed { get; set; }

        public decimal Verteilt { get; set; }
        public decimal Rest { get; set; }
    }
}
