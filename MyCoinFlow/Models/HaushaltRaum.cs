using System;

namespace MyCoinFlow.Models
{
    public class HaushaltRaum
    {
        public int Id { get; set; }

        public int? StandortId { get; set; }
        public string StandortBezeichnung { get; set; } = "";
        public string StandortIconKey { get; set; } = "HomeCityOutline";
        public string StandortFarbeKey { get; set; } = "DeepPurple";

        public string Bezeichnung { get; set; } = "";
        public string IconKey { get; set; } = "HomeOutline";
        public string Bemerkung { get; set; } = "";
        public bool IstAktiv { get; set; } = true;
        public DateTime ErstelltAm { get; set; }
        public DateTime? GeaendertAm { get; set; }
    }
}