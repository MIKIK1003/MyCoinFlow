using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using MyCoinFlow.Services;
using System;
using System.Linq;

namespace MyCoinFlow.ViewModels
{
    public class StweEnergieChartViewModel
    {
        public ISeries[] SerienKwh { get; }
        public ISeries[] SerienSolarPct { get; }

        public Axis[] XAchse { get; }
        public Axis[] YAchseKwh { get; }
        public Axis[] YAchsePct { get; }

        public StweEnergieChartViewModel(DatabaseService db, int liegenschaftId, DateTime? von, DateTime? bis)
        {
            var data = db.StweEnergieChartGet(liegenschaftId, von, bis);

            SerienKwh = new ISeries[]
            {
                new ColumnSeries<decimal> { Name = "Rechnung kWh", Values = data.Select(x => x.RechnungKwh).ToArray() },
                new ColumnSeries<decimal> { Name = "Interne kWh", Values = data.Select(x => x.InterneKwh).ToArray() },
                new ColumnSeries<decimal> { Name = "Solar direkt kWh", Values = data.Select(x => x.SolarDirektKwh).ToArray() }
            };

            SerienSolarPct = new ISeries[]
            {
                new LineSeries<decimal> { Name = "Solar-Anteil %", Values = data.Select(x => x.SolarAnteilProzent).ToArray() }
            };

            XAchse = new[] { new Axis { Labels = data.Select(x => x.Label).ToArray() } };
            YAchseKwh = new[] { new Axis { Name = "kWh" } };
            YAchsePct = new[] { new Axis { Name = "%", MinLimit = 0, MaxLimit = 100 } };
        }
    }
}
