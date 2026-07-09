using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MyCoinFlow.Services
{
    public enum GroupingDimension
    {
        Art = 0,
        Gruppe = 1,
        Untergruppe = 2
    }

    public readonly record struct NumberRange(int Von, int Bis);

    public sealed class DashboardPoint
    {
        public string Label { get; set; } = "";
        public decimal Budget { get; set; }
        public decimal Ist { get; set; }

        // optional (nicht für die Anzeige nötig, aber nützlich)
        public string? Art { get; set; }
        public string? Gruppe { get; set; }
        public string? Untergruppe { get; set; }
        public int? Kontonummer { get; set; }
        public int KontoId { get; set; }
    }

    public sealed class DashboardData
    {
        public List<DashboardPoint> Points { get; set; } = new();
        public decimal BudgetTotal => Points.Sum(p => p.Budget);
        public decimal IstTotal => Points.Sum(p => p.Ist);
    }

    public interface IDashboardDataProvider
    {
        Task<DashboardData> LoadAsync(
            GroupingDimension dimension,
            IReadOnlyList<NumberRange> ranges,
            CancellationToken ct = default);
    }
}
