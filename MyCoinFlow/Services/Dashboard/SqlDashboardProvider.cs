using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace MyCoinFlow.Services
{
    public sealed class SqlDashboardProvider : IDashboardDataProvider
    {
        private readonly Func<SqlConnection> _createConnection;

        // Gib hier deine Connection-Factory rein (z. B. new DatabaseService().CreateConnection)
        public SqlDashboardProvider(Func<SqlConnection> createConnection)
            => _createConnection = createConnection ?? throw new ArgumentNullException(nameof(createConnection));

        public async Task<DashboardData> LoadAsync(
            GroupingDimension dimension,
            IReadOnlyList<NumberRange> ranges,
            CancellationToken ct = default)
        {
            using var con = _createConnection();
            await con.OpenAsync(ct);

            string keyExpr = dimension switch
            {
                GroupingDimension.Art => "ISNULL(k.Art, N'(ohne)')",
                GroupingDimension.Gruppe => "ISNULL(k.Gruppe, N'(ohne)')",
                GroupingDimension.Untergruppe => "ISNULL(k.Untergruppe, N'(ohne)')",
                _ => "ISNULL(k.Art, N'(ohne)')"
            };

            // Range-Filter dynamisch
            string rangeWhere = "";
            if (ranges != null && ranges.Count > 0)
            {
                var parts = new List<string>();
                for (int i = 0; i < ranges.Count; i++)
                    parts.Add($"(k.Kontonummer BETWEEN @v{i} AND @b{i})");
                rangeWhere = " AND (" + string.Join(" OR ", parts) + ")";
            }
            else
            {
                // Wenn "Keine" gewählt -> keine Daten zurück
                rangeWhere = " AND 1=0";
            }

            string sql = $@"
WITH Aktiver AS (
    SELECT TOP(1) Id, Startdatum, Enddatum
    FROM Budgetzeitraum
    WHERE IstAktiv = 1
)
SELECT 
    Label = {keyExpr},
    Budget = SUM(ISNULL(bd.Budgetwert, 0)),
    Ist    = SUM(ISNULL(agg.Gebucht, 0))
FROM Kontenplan k
LEFT JOIN Aktiver bz ON 1=1
LEFT JOIN BudgetDetail bd
    ON bz.Id IS NOT NULL AND bd.ZeitraumId = bz.Id AND bd.KontoId = k.Id
OUTER APPLY (
    SELECT SUM(x.Wert) AS Gebucht
    FROM (
        SELECT SUM(t.Betrag) AS Wert
        FROM Transaktion t
        WHERE t.NachKontoId = k.Id
          AND (bz.Id IS NULL OR (t.Datum >= bz.Startdatum AND t.Datum <= bz.Enddatum))
        UNION ALL
        SELECT SUM(-t.Betrag) AS Wert
        FROM Transaktion t
        WHERE t.VonKontoId = k.Id
          AND (bz.Id IS NULL OR (t.Datum >= bz.Startdatum AND t.Datum <= bz.Enddatum))
    ) x
) agg
WHERE 1=1 {rangeWhere}
GROUP BY {keyExpr}
ORDER BY Label;";

            using var cmd = new SqlCommand(sql, con) { CommandType = CommandType.Text };

            if (ranges != null)
            {
                for (int i = 0; i < ranges.Count; i++)
                {
                    cmd.Parameters.AddWithValue($"@v{i}", ranges[i].Von);
                    cmd.Parameters.AddWithValue($"@b{i}", ranges[i].Bis);
                }
            }

            var data = new DashboardData();

            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                data.Points.Add(new DashboardPoint
                {
                    Label = r.GetString(0),
                    Budget = r.IsDBNull(1) ? 0m : r.GetDecimal(1),
                    Ist = r.IsDBNull(2) ? 0m : r.GetDecimal(2)
                });
            }

            return data;
        }
    }
}
