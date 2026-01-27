using System;

namespace MyCoinFlow.Models
{
    public class StweSetRow
    {
        public int Id { get; set; }
        public int LiegenschaftId { get; set; }
        public int TransaktionId { get; set; }

        public DateTime Datum { get; set; }

        // Wichtig: Dieses Feld wird ab jetzt im DB-Select als SIGNED geladen:
        // Belastung = +Betrag, Gutschrift = -Betrag
        public decimal Betrag { get; set; }

        public string Titel { get; set; } = "";

        public bool IsClosed { get; set; }

        // NEU: True = Gutschrift/Einzahlung/Rückvergütung -> Verteilzeilen NEGATIV
        public bool IsCredit { get; set; }

        public decimal Verteilt { get; set; }
        public decimal Rest { get; set; }
    }
}
