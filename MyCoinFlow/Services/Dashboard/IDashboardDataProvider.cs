using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MyCoinFlow.Services.Dashboard
{
    public enum GroupingDimension { KontenArt, KontenGruppe, KontenUnterGruppe }

    public sealed class DashboardPoint
    {
        public string Label { get; init; } = "";
        public decimal Budget { get; init; }
        public decimal Ist { get; init; }
    }

    public class DashboardData
    {
        public IReadOnlyList<DashboardPoint> Points { get; init; } = [];
        public static DashboardData Empty { get; } = new() { Points = [] };
    }

    /// <summary>Optional: Zeitraumtext für die UI.</summary>
    public interface IWithPeriodInfo
    {
        string PeriodInfo { get; }
    }

    public interface IDashboardDataProvider
    {
        Task<DashboardData> LoadAsync(GroupingDimension dimension, CancellationToken ct = default);
    }
}
