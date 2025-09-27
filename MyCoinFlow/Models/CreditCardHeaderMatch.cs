namespace MyCoinFlow.Models
{
    /// <summary>
    /// Beschreibt ein Import-Schema für Kreditkarten-Header (z. B. "Master", "Visa 2025", "Amex CSV").
    /// </summary>
    public class ImportSchema
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsMaster { get; set; }
    }

    /// <summary>
    /// Ordnet einen Master-Header einem Headernamen aus der Quell-Datei zu.
    /// </summary>
    public class FieldMapping
    {
        public int Id { get; set; }
        public int SchemaId { get; set; }
        public string MasterHeader { get; set; } = "";
        public string SourceHeader { get; set; } = "";
        public string? DefaultValue { get; set; }

    }
}
