using System;

namespace MyCoinFlow.Models
{
    public class CreditCardBatchInfo
    {
        public int Id { get; set; }
        public DateTime ImportedAt { get; set; }
        public string? SourceFile { get; set; }
        public int Offen { get; set; }       // Anzahl Zeilen in Staging (offen)
        public int Archiviert { get; set; }  // Anzahl im Archiv (nur Info)
        public int Gesamt => Offen + Archiviert;

        public string Anzeige =>
            $"{ImportedAt:yyyy-MM-dd HH:mm} – {SourceFile ?? "(ohne Name)"}  (offen {Offen})";
    }
}
