namespace MyCoinFlow.Models
{
    /// <summary>
    /// Benutzerdefinierte Regel für Kontonummern-Bereiche.
    /// Richtung: "Ausgabe" oder "Einnahme"
    /// Bezeichnung: rein informativ (z. B. "Investitionen (Budgetiert)")
    /// IstBudgetkonto: optionales Flag (derzeit ohne Logikverwendung)
    /// </summary>
    public class NumberRangeRule
    {
        public int Id { get; set; }
        public int RangeStart { get; set; }
        public int RangeEnd { get; set; }
        public string Richtung { get; set; } = "Ausgabe";
        public string? Bezeichnung { get; set; } = "Ausgaben (Budgetiert)";
        public bool IstBudgetkonto { get; set; }
    }
}
