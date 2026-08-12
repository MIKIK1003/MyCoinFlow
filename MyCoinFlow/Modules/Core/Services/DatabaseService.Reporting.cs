using Microsoft.Data.SqlClient;
using MyCoinFlow.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace MyCoinFlow.Services
{
    public partial class DatabaseService
    {
        public List<Transaktion> LadeTransaktionenFuerBericht(
            IReadOnlyCollection<int> kontoIds,
            DateTime von,
            DateTime bis)
        {
            var ids = kontoIds
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            if (ids.Length == 0)
                return new List<Transaktion>();

            using var connection = new SqlConnection(_connectionString);
            connection.Open();

            var parameterNames = ids.Select((_, index) => $"@K{index}").ToArray();
            var inClause = string.Join(",", parameterNames);

            var sql = $@"
SELECT t.Id,
       t.Datum,
       t.BudgetDatum,
       t.VonKontoId,
       t.NachKontoId,
       t.Betrag,
       t.Notiz,
       t.AdresseId,
       t.GeldinstitutId,
       t.ImportQuelle
FROM dbo.Transaktion t
WHERE (t.VonKontoId IN ({inClause}) OR t.NachKontoId IN ({inClause}))
  AND ISNULL(t.BudgetDatum, t.Datum) >= @Von
  AND ISNULL(t.BudgetDatum, t.Datum) < DATEADD(DAY, 1, @Bis)
ORDER BY ISNULL(t.BudgetDatum, t.Datum), t.Id;";

            using var command = new SqlCommand(sql, connection);
            for (var index = 0; index < ids.Length; index++)
                command.Parameters.Add(parameterNames[index], SqlDbType.Int).Value = ids[index];

            command.Parameters.Add("@Von", SqlDbType.Date).Value = von.Date;
            command.Parameters.Add("@Bis", SqlDbType.Date).Value = bis.Date;

            var result = new List<Transaktion>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new Transaktion
                {
                    Id = reader.GetInt32(0),
                    Datum = reader.GetDateTime(1),
                    BudgetDatum = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                    VonKontoId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    NachKontoId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    Betrag = reader.GetDecimal(5),
                    Notiz = reader.IsDBNull(6) ? null : reader.GetString(6),
                    AdresseId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    GeldinstitutId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    ImportQuelle = reader.IsDBNull(9) ? null : reader.GetString(9)
                });
            }

            return result;
        }

        public void AktualisiereBudgetwerteTransaktional(
            int zeitraumId,
            IReadOnlyCollection<BudgetwertAenderung> aenderungen)
        {
            if (zeitraumId <= 0)
                throw new ArgumentOutOfRangeException(nameof(zeitraumId));

            var werte = aenderungen
                .Where(a => a.KontoId > 0)
                .GroupBy(a => a.KontoId)
                .Select(g => g.Last())
                .ToArray();

            if (werte.Length == 0)
                return;
            if (werte.Any(a => a.NeuerWert < 0m))
                throw new ArgumentException("Budgetwerte dürfen nicht negativ sein.", nameof(aenderungen));

            using var connection = new SqlConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                const string sql = @"
UPDATE dbo.BudgetDetail
SET Budgetwert = @Wert
WHERE ZeitraumId = @ZeitraumId AND KontoId = @KontoId;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.BudgetDetail (ZeitraumId, KontoId, Budgetwert)
    VALUES (@ZeitraumId, @KontoId, @Wert);
END;";

                using var command = new SqlCommand(sql, connection, transaction);
                var zeitraumParameter = command.Parameters.Add("@ZeitraumId", SqlDbType.Int);
                var kontoParameter = command.Parameters.Add("@KontoId", SqlDbType.Int);
                var wertParameter = command.Parameters.Add("@Wert", SqlDbType.Decimal);
                wertParameter.Precision = 18;
                wertParameter.Scale = 2;

                zeitraumParameter.Value = zeitraumId;
                foreach (var wert in werte)
                {
                    kontoParameter.Value = wert.KontoId;
                    wertParameter.Value = Math.Round(wert.NeuerWert, 2, MidpointRounding.AwayFromZero);
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
