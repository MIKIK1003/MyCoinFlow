using System;

namespace MyCoinFlow.Models
{
    public class HaushaltStandort
    {
        public int Id { get; set; }
        public string Bezeichnung { get; set; } = "";
        public string IconKey { get; set; } = "HomeCityOutline";
        public string FarbeKey { get; set; } = "DeepPurple";
        public string Bemerkung { get; set; } = "";
        public bool IstAktiv { get; set; } = true;
        public DateTime ErstelltAm { get; set; }
        public DateTime? GeaendertAm { get; set; }
    }
}