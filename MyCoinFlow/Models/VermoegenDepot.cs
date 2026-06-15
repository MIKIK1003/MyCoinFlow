namespace MyCoinFlow.Models
{
    public class VermoegenDepot
    {
        public int Id { get; set; }

        public int? GeldinstitutId { get; set; }
        public string GeldinstitutName { get; set; } = "";

        public string Name { get; set; } = "";
        public string Institut { get; set; } = "";
        public string Waehrung { get; set; } = "CHF";

        public bool IstAktiv { get; set; } = true;

        public bool IstStandard { get; set; }
    }
}