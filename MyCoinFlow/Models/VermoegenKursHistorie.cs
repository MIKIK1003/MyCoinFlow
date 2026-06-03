using System;

namespace MyCoinFlow.Models
{
    public class VermoegenKursHistorie
    {
        public int Id { get; set; }
        public int PositionId { get; set; }
        public DateTime KursDatum { get; set; }
        public decimal Kurs { get; set; }
        public string Quelle { get; set; } = "";
        public DateTime ErfasstAm { get; set; }
    }
}