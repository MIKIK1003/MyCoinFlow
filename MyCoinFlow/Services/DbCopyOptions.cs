namespace MyCoinFlow.Services
{
    /// <summary>
    /// Optionen für das Kopieren von Stammdaten zwischen zwei MyCoinFlow-Datenbanken.
    /// </summary>
    public sealed class DbCopyOptions
    {
        public bool CopyKontenstruktur { get; set; } = true;
        public bool CopyAdressen { get; set; } = true;
        public bool CopyAliase { get; set; } = true;
        public bool CopyGeldinstitute { get; set; } = true;
        public bool CopyImportSchemas { get; set; } = true;
        public bool CopyKategorieKonto { get; set; } = true;

        // NEU: Nummernkreise (NumberRangeRules)
        public bool CopyNumberRanges { get; set; } = true;

        public bool CreateBudgetzeitraum { get; set; } = true;
        public int BudgetYear { get; set; } = System.DateTime.Today.Year;
    }
}
