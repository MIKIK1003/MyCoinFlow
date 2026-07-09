using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MyCoinFlow.Services
{
    // Nur für Design/Notfälle – gibt fixe Demo-Daten zurück.
    public sealed class InMemoryDemoDashboardProvider : IDashboardDataProvider
    {
        public Task<DashboardData> LoadAsync(
            GroupingDimension dimension,
            IReadOnlyList<NumberRange> ranges,
            CancellationToken ct = default)
        {
            var d = new DashboardData
            {
                Points = new List<DashboardPoint>
                {
                    new DashboardPoint { Label = "A", Budget = 1200m, Ist = 900m },
                    new DashboardPoint { Label = "B", Budget = 800m,  Ist = 950m },
                    new DashboardPoint { Label = "C", Budget = 300m,  Ist = 200m }
                }
            };
            return Task.FromResult(d);
        }
    }
}
