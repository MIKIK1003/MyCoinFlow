using System;

namespace MyCoinFlow.Models
{
    public class DmsProcessingLogEntry
    {
        public int Id { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public string? FileName { get; set; }
        public string? Ergebnis { get; set; }

        public string ZeitpunktAnzeige => OccurredAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    }
}
