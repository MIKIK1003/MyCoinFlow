// Datei: Models/AdressAlias.cs
namespace MyCoinFlow.Models
{
    /// <summary>
    /// Alias für die Adress-Erkennung.
    /// Entspricht der DB-Tabelle: AdresseAlias (Id, AdresseId, Text, Modus).
    /// </summary>
    public class AdressAlias
    {
        public int Id { get; set; }
        public int AdresseId { get; set; }
        public string Text { get; set; } = string.Empty;   // Muster / Alias-Text
        public string Modus { get; set; } = "Exact";       // Exact | StartsWith | EndsWith | Contains

        public AdressAlias() { }

        public AdressAlias(int id, int adresseId, string text, string modus)
        {
            Id = id;
            AdresseId = adresseId;
            Text = text ?? string.Empty;
            Modus = string.IsNullOrWhiteSpace(modus) ? "Exact" : modus;
        }
    }
}
