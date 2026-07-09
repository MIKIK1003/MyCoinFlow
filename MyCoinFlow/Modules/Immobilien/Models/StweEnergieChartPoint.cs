namespace MyCoinFlow.Models
{
    public class StweEnergieChartPoint
    {
        public string Label { get; set; } = "";   // Q1 2025 etc.
        public decimal RechnungKwh { get; set; }
        public decimal InterneKwh { get; set; }
        public decimal SolarDirektKwh { get; set; }

        public decimal SolarAnteilProzent =>
            InterneKwh <= 0 ? 0 : (SolarDirektKwh / InterneKwh) * 100m;
    }
}
