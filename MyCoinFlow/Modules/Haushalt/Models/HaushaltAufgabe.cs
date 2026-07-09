using System;

namespace MyCoinFlow.Models
{
    public class HaushaltAufgabe
    {
        public int Id { get; set; }

        public int ObjektId { get; set; }
        public string ObjektBezeichnung { get; set; } = "";

        public string Titel { get; set; } = "";
        public string Status { get; set; } = "Offen";

        public DateTime AktivAb { get; set; }
        public DateTime FaelligAm { get; set; }
        public DateTime? ErledigtAm { get; set; }

        public string MicrosoftTaskId { get; set; } = "";

        public bool IstAktiv { get; set; } = true;
        public DateTime ErstelltAm { get; set; }
        public DateTime? GeaendertAm { get; set; }
    }
}