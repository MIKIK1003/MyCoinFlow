namespace MyCoinFlow.Models
{
    /// <summary>
    /// Benutzerdefinierte Regel für Kontonummern-Bereiche.
    /// Richtung: "Ausgabe", "Einnahme" oder "Neutral"
    /// Bezeichnung: rein informativ (z. B. "Investitionen (Budgetiert)")
    /// IstBudgetkonto: optionales Flag
    /// ExcludeFromStweSets: Konten dieses Nummernkreises sollen in STWE-Set-Auswahlen nicht erscheinen.
    /// </summary>
    public class NumberRangeRule
    {
        public int Id { get; set; }
        public int RangeStart { get; set; }
        public int RangeEnd { get; set; }
        public string Richtung { get; set; } = "Ausgabe";
        public string? Bezeichnung { get; set; } = "Ausgaben (Budgetiert)";
        public bool IstBudgetkonto { get; set; }
        public bool ExcludeFromStweSets { get; set; }
    }
}