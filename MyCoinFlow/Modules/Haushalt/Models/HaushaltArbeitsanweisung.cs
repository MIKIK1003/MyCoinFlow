using System;

namespace MyCoinFlow.Models
{
    public class HaushaltArbeitsanweisung
    {
        public int Id { get; set; }
        public string Bezeichnung { get; set; } = "";
        public string Beschreibung { get; set; } = "";
        public string IconKey { get; set; } = "ClipboardTextOutline";
        public bool IstAktiv { get; set; } = true;
        public DateTime ErstelltAm { get; set; }
        public DateTime? GeaendertAm { get; set; }
    }
}