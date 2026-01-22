namespace MyCoinFlow.Models
{
    /// <summary>
    /// Stammdaten einer Liegenschaft (STWE).
    /// </summary>
    public class StweLiegenschaft
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string? Strasse { get; set; }

        public string? PLZ { get; set; }

        public string? Ort { get; set; }

        public string? Notiz { get; set; }
    }
}
