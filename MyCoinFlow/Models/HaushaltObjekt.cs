using System;

namespace MyCoinFlow.Models
{
    public class HaushaltObjekt
    {
        public int Id { get; set; }
        public int RaumId { get; set; }
        public string RaumBezeichnung { get; set; } = "";

        public string Bezeichnung { get; set; } = "";
        public string Kategorie { get; set; } = "";
        public string IconKey { get; set; } = "PackageVariantClosed";

        public string Hersteller { get; set; } = "";
        public string Modell { get; set; } = "";
        public string Seriennummer { get; set; } = "";
        public DateTime? Kaufdatum { get; set; }
        public decimal? Kaufpreis { get; set; }
        public string Bemerkung { get; set; } = "";

        public bool IstAktiv { get; set; } = true;
        public DateTime ErstelltAm { get; set; }
        public DateTime? GeaendertAm { get; set; }
    }
}