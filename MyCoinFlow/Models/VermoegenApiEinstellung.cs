namespace MyCoinFlow.Models
{
    public class VermoegenApiEinstellung
    {
        public int Id { get; set; }
        public string ApiProvider { get; set; } = "EODHD";
        public string ApiKey { get; set; } = "";
        public bool Aktiv { get; set; } = true;
    }
}