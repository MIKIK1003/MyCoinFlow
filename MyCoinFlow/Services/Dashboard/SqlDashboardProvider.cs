using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services.Dashboard
{
    /// <summary>
    /// Liefert Dashboard-Daten (Budget & IST) gruppiert nach Art/Gruppe/Untergruppe aus deiner SQL-DB.
    /// </summary>
    public sealed class SqlDashboardProvider : IDashboardDataProvider, IWithPeriodInfo
    {
        private readonly Func<SqlConnection> _connectionFactory;

        public SqlDashboardProvider(Func<SqlConnection> connectionFactory)
            => _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public string PeriodInfo { get; private set; } = "Zeitraum: unbekannt";

        public async Task<DashboardData> LoadAsync(GroupingDimension dimension, CancellationToken ct = default)
        {
            using var con = _connectionFactory();
            await con.OpenAsync(ct).ConfigureAwait(false);

            // 1) Aktiver Zeitraum, sonst jüngster, sonst aktuelles Jahr
            var period = await GetActiveOrLatestPeriodAsync(con, ct).ConfigureAwait(false);
            DateTime startIncl = period?.Start ?? new DateTime(DateTime.Today.Year, 1, 1);
            DateTime endIncl = period?.Ende ?? DateTime.Today;
            DateTime endExcl = endIncl.Date.AddDays(1);

            PeriodInfo = period is null
                ? $"Zeitraum: {startIncl:dd.MM.yyyy}–{endIncl:dd.MM.yyyy} (kein aktiver Budgetzeitraum)"
                : $"Zeitraum: {period!.Start:dd.MM.yyyy}–{period!.Ende:dd.MM.yyyy} (aktiv)";

            // 2) Spalte in Kontenplan je Dimension
            var labelColumn = dimension switch
            {
                GroupingDimension.KontenArt => "Art",
                GroupingDimension.KontenGruppe => "Gruppe",
                GroupingDimension.KontenUnterGruppe => "Untergruppe",
                _ => throw new ArgumentOutOfRangeException(nameof(dimension))
            };

            // 3) Budget je Label (nur wenn Zeitraum vorhanden)
            var budgetByLabel = period?.Id is int zId
                ? await LoadBudgetByLabelAsync(con, zId, labelColumn, ct).ConfigureAwait(false)
                : new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            // 4) IST je Label (Summe Transaktion.Betrag für NachKontoId im Zeitraum)
            var istByLabel = await LoadIstByLabelAsync(con, startIncl, endExcl, labelColumn, ct).ConfigureAwait(false);

            // 5) Vereinen & Punkte
            var labels = new SortedSet<string>(budgetByLabel.Keys, StringComparer.OrdinalIgnoreCase);
            labels.UnionWith(istByLabel.Keys);

            var points = new List<DashboardPoint>(labels.Count);
            foreach (var l in labels)
            {
                budgetByLabel.TryGetValue(l, out var b);
                istByLabel.TryGetValue(l, out var i);
                points.Add(new DashboardPoint
                {
                    Label = string.IsNullOrWhiteSpace(l) ? "(ohne Zuordnung)" : l,
                    Budget = Math.Round(b, 2, MidpointRounding.AwayFromZero),
                    Ist = Math.Round(i, 2, MidpointRounding.AwayFromZero)
                });
            }

            return new DashboardData { Points = points };
        }

        // ---------------- SQL-Hilfen ----------------

        private sealed class PeriodRow
        {
            public int Id { get; init; }
            public DateTime Start { get; init; }
            public DateTime Ende { get; init; }
        }

        private static async Task<PeriodRow?> GetActiveOrLatestPeriodAsync(SqlConnection con, CancellationToken ct)
        {
            const string sqlActive = @"
SELECT TOP (1) Id, Startdatum, Enddatum
FROM Budgetzeitraum
WHERE IstAktiv = 1
ORDER BY Startdatum DESC;";
            await using (var cmd = new SqlCommand(sqlActive, con))
            await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    return new PeriodRow
                    {
                        Id = r.GetInt32(0),
                        Start = r.GetDateTime(1).Date,
                        Ende = r.GetDateTime(2).Date
                    };
                }
            }

            const string sqlLatest = @"
SELECT TOP (1) Id, Startdatum, Enddatum
FROM Budgetzeitraum
ORDER BY Startdatum DESC;";
            await using (var cmd = new SqlCommand(sqlLatest, con))
            await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    return new PeriodRow
                    {
                        Id = r.GetInt32(0),
                        Start = r.GetDateTime(1).Date,
                        Ende = r.GetDateTime(2).Date
                    };
                }
            }

            return null;
        }

        private static async Task<Dictionary<string, decimal>> LoadBudgetByLabelAsync(
            SqlConnection con, int zeitraumId, string labelColumn, CancellationToken ct)
        {
            const string tpl = @"
SELECT 
    COALESCE(NULLIF(kp.{0}, ''), '(ohne Zuordnung)') AS Label,
    SUM(CAST(bd.Budgetwert AS decimal(18,2)))       AS Budget
FROM BudgetDetail bd
JOIN Kontenplan   kp ON kp.Id = bd.KontoId
WHERE bd.ZeitraumId = @z
GROUP BY COALESCE(NULLIF(kp.{0}, ''), '(ohne Zuordnung)')
ORDER BY Label;";
            var sql = string.Format(CultureInfo.InvariantCulture, tpl, labelColumn);

            var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.Add(new SqlParameter("@z", SqlDbType.Int) { Value = zeitraumId });

            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                dict[r.GetString(0)] = r.IsDBNull(1) ? 0m : r.GetDecimal(1);
            }
            return dict;
        }

        private static async Task<Dictionary<string, decimal>> LoadIstByLabelAsync(
            SqlConnection con, DateTime startIncl, DateTime endExcl, string labelColumn, CancellationToken ct)
        {
            const string tpl = @"
SELECT 
    COALESCE(NULLIF(kp.{0}, ''), '(ohne Zuordnung)') AS Label,
    SUM(CAST(t.Betrag AS decimal(18,2)))            AS Ist
FROM Transaktion t
JOIN Kontenplan  kp ON kp.Id = t.NachKontoId
WHERE t.Datum >= @s
  AND t.Datum <  @e
GROUP BY COALESCE(NULLIF(kp.{0}, ''), '(ohne Zuordnung)')
ORDER BY Label;";
            var sql = string.Format(CultureInfo.InvariantCulture, tpl, labelColumn);

            var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.Add(new SqlParameter("@s", SqlDbType.DateTime2) { Value = startIncl });
            cmd.Parameters.Add(new SqlParameter("@e", SqlDbType.DateTime2) { Value = endExcl });

            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                dict[r.GetString(0)] = r.IsDBNull(1) ? 0m : r.GetDecimal(1);
            }
            return dict;
        }
    }
}
