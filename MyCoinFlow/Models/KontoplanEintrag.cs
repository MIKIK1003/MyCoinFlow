public class KontoplanEintrag
{
    public int Id { get; set; }
    public int Kontonummer { get; set; }
    public string Art { get; set; } = string.Empty;
    public string Gruppe { get; set; } = string.Empty;
    public string Untergruppe { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public decimal? Budgetwert { get; set; }   // aus BudgetDetail (kann null sein)
    public decimal Gebucht { get; set; }       // Summe im aktiven Zeitraum (Ein- minus Ausgänge)

    // Komfort: Differenz = Budget - Gebucht
    public decimal Differenz => (Budgetwert ?? 0m) - Gebucht;
}
