namespace MyCoinFlow.Services
{
    public sealed class DbCopyOptions
    {
        public bool CopyKontenstruktur { get; set; } = true;
        public bool CopyAdressen { get; set; } = true;
        public bool CopyAliase { get; set; } = true;
        public bool CopyGeldinstitute { get; set; } = true;
        public bool CopyImportSchemas { get; set; } = true;
        public bool CopyKategorieKonto { get; set; } = true;
        public bool CreateBudgetzeitraum { get; set; } = true;
        public int BudgetYear { get; set; } = System.DateTime.Today.Year;
    }
}
