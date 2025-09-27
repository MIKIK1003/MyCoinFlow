using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MyCoinFlow.Services.Dashboard
{
    public sealed class InMemoryDemoDashboardProvider : IDashboardDataProvider, IWithPeriodInfo
    {
        private readonly Random _rnd = new();

        public string PeriodInfo { get; private set; } = "Zeitraum: aktiv (Demo)";

        public Task<DashboardData> LoadAsync(GroupingDimension dimension, CancellationToken ct = default)
        {
            // Einfache, stabile Demo-Daten – damit die UI sofort funktioniert.
            // Labels orientieren sich an Art/Gruppe/Untergruppe.
            var labels = dimension switch
            {
                GroupingDimension.KontenArt => new[] { "Einnahmen", "Ausgaben" },
                GroupingDimension.KontenGruppe => new[] { "Wohnen", "Mobilität", "Essen", "Freizeit", "Sonstiges" },
                _ => new[] { "Miete", "Strom", "ÖV", "Auto", "Restaurant", "Supermarkt" }
            };

            // Budget als „Plan“, IST mit kleiner Streuung – negative IST vermeiden (Vergleich Budget vs Betrag in Beträgen > 0).
            var points = new List<DashboardPoint>();
            foreach (var label in labels)
            {
                var budget = Math.Round((decimal)_rnd.Next(200, 1600), 2);
                var ist = Math.Round(budget * (decimal)(0.8 + _rnd.NextDouble() * 0.6), 2);
                points.Add(new DashboardPoint { Label = label, Budget = budget, Ist = ist });
            }

            PeriodInfo = "Zeitraum: aktiv (Demo)";
            return Task.FromResult(new DashboardData { Points = points });
        }
    }
}
