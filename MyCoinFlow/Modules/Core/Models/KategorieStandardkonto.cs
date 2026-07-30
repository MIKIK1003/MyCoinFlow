namespace MyCoinFlow.Models
{
    // Vom User gepflegte Standard-Zuordnung Kategorie -> Konto für den
    // Kreditkarten-Import. Ergänzt die adressbasierte Erkennung um einen
    // zweiten, robusteren Fallback: Kreditkarten-Kategorien sind fix und
    // überschaubar, anders als die stark wechselnden Händlernamen.
    public class KategorieStandardkonto
    {
        public int Id { get; set; }
        public string Kategorie { get; set; } = "";
        public int? KontoId { get; set; }
        public string? KontoAnzeige { get; set; }
    }
}
