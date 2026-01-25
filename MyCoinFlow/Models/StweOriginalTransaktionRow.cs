using System;

namespace MyCoinFlow.Models
{
    public sealed class StweOriginalTransaktionRow
    {
        public int TransaktionsId { get; set; }
        public DateTime Datum { get; set; }
        public decimal Betrag { get; set; }
        public string? Notiz { get; set; }
    }
}
