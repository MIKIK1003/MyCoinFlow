namespace MyCoinFlow.Models
{
    public class StweSchluessel
    {
        public int Id { get; set; }
        public int LiegenschaftId { get; set; }

        public string Name { get; set; } = "";
        public string Modus { get; set; } = "FIX"; // "FIX" | "MEA"
    }
}
