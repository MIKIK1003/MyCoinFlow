namespace MyCoinFlow.Models
{
    public class VermoegenGeldinstitutAuswahl
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string IBAN { get; set; } = "";

        public string AnzeigeText =>
            string.IsNullOrWhiteSpace(IBAN)
                ? Name
                : $"{Name} – {IBAN}";
    }
}