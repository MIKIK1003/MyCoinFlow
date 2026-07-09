namespace MyCoinFlow.Models
{
    /// <summary>
    /// Repräsentiert einen Budgetzeitraum.
    /// </summary>
    public class Budgetzeitraum
    {
        public int Id { get; set; }
        public string Bezeichnung { get; set; } = string.Empty;
        public DateTime Startdatum { get; set; }
        public DateTime Enddatum { get; set; }
        public bool IstAktiv { get; set; }
    }
}
