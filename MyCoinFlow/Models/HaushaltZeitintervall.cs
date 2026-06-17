using System;

namespace MyCoinFlow.Models
{
    public class HaushaltZeitintervall
    {
        public int Id { get; set; }
        public string Bezeichnung { get; set; } = "";
        public int Tage { get; set; }
        public string Bemerkung { get; set; } = "";
        public bool IstAktiv { get; set; } = true;
        public DateTime ErstelltAm { get; set; }
        public DateTime? GeaendertAm { get; set; }
    }
}