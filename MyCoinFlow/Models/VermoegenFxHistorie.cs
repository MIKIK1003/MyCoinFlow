using System;

namespace MyCoinFlow.Models
{
    public class VermoegenFxHistorie
    {
        public int Id { get; set; }

        public string VonWaehrung { get; set; } = "";
        public string NachWaehrung { get; set; } = "";

        public DateTime KursDatum { get; set; }
        public decimal Kurs { get; set; }

        public string Quelle { get; set; } = "";
        public DateTime ErfasstAm { get; set; }
    }
}