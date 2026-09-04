using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using MyCoinFlow.Import;

namespace MyCoinFlow.Services
{
    public class ImportService
    {
        // Immer die aktuelle Mandanten-DB verwenden (kein Caching!)
        private string ConnectionString => MyCoinFlow.Services.ConnectionStrings.Current;

        // Optionaler Helper, falls du ihn woanders mal brauchst
        private SqlConnection CreateConnection() => new SqlConnection(ConnectionString);

        public int CreateBatch(string sourceFormat, string fileName, byte[]? fileHash, string? accountIban, string? currency)
        {
            using var c = CreateConnection();
            c.Open();

            var sql = @"INSERT INTO BankImportBatch (SourceFormat, FileName, FileHash, AccountIban, Currency)
                        OUTPUT INSERTED.Id
                        VALUES (@f, @n, @h, @iban, @ccy)";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@f", sourceFormat);
            cmd.Parameters.AddWithValue("@n", fileName);
            cmd.Parameters.Add("@h", SqlDbType.VarBinary, 32).Value = (object?)fileHash ?? DBNull.Value;
            cmd.Parameters.AddWithValue("@iban", (object?)accountIban ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ccy", (object?)currency ?? DBNull.Value);
            return (int)cmd.ExecuteScalar();
        }

        public (int inserted, int skipped) UpsertItems(int batchId, IEnumerable<BankImportItem> items)
        {
            int ins = 0, skip = 0;
            using var c = CreateConnection();
            c.Open();
            EnsureStructuredReferenceColumns(c);

            foreach (var it in items)
            {
                var uniq = BuildUniqKey(it);
                const string sqlCheck = @"
SELECT CASE WHEN EXISTS
(
    SELECT 1 FROM BankImportItem WHERE AccountIban = @iban AND UniqKey = @uk
)
OR EXISTS
(
    SELECT 1
    FROM BankImportItemArchive
    WHERE AccountIban = @iban
      AND BookingDate = @bookingDate
      AND Amount = @amount
      AND COALESCE(ServiceRef, N'') = @serviceRef
)
THEN 1 ELSE 0 END;";
                using (var check = new SqlCommand(sqlCheck, c))
                {
                    check.Parameters.AddWithValue("@iban", (object?)it.AccountIban ?? DBNull.Value);
                    check.Parameters.AddWithValue("@uk", uniq);
                    check.Parameters.AddWithValue("@bookingDate", it.BookingDate.Date);
                    check.Parameters.AddWithValue("@amount", it.Amount);
                    check.Parameters.AddWithValue("@serviceRef", it.ServiceRef ?? string.Empty);
                    int count = (int)check.ExecuteScalar();
                    if (count > 0) { skip++; continue; }
                }

                const string sqlIns = @"
INSERT INTO BankImportItem
(BatchId, AccountIban, Currency, BookingDate, ValueDate, Amount, Direction, ServiceRef, StructuredReference, [Text],
 CounterpartyName, CounterpartyIban, Uetr, PurposeCode,
 VorschlagAdresseId, VorschlagNachKontoId, VorschlagVonKontoId, VorschlagGeldinstitutId,
 UniqKey)
VALUES
(@b, @iban, @ccy, @bd, @vd, @amt, @dir, @ref, @structuredRef, @txt,
 @cpn, @cpi, @uetr, @purp,
 @adr, @nach, @von, @gi,
 @uk)";
                using var insCmd = new SqlCommand(sqlIns, c);
                insCmd.Parameters.AddWithValue("@b", batchId);
                insCmd.Parameters.AddWithValue("@iban", (object?)it.AccountIban ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@ccy", (object?)it.Currency ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@bd", it.BookingDate.Date);
                insCmd.Parameters.AddWithValue("@vd", (object?)it.ValueDate?.Date ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@amt", it.Amount);
                insCmd.Parameters.AddWithValue("@dir", it.Direction == KreditDebit.Credit ? "CRDT" : "DBIT");
                insCmd.Parameters.AddWithValue("@ref", (object?)it.ServiceRef ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@structuredRef", (object?)it.StructuredReference ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@txt", (object?)it.Text ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@cpn", (object?)it.CounterpartyName ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@cpi", (object?)it.CounterpartyIban ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@uetr", (object?)it.Uetr ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@purp", (object?)it.PurposeCode ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@adr", (object?)it.VorschlagAdresseId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@nach", (object?)it.VorschlagNachKontoId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@von", (object?)it.VorschlagVonKontoId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@gi", (object?)it.VorschlagGeldinstitutId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@uk", uniq);
                insCmd.ExecuteNonQuery();
                ins++;
            }
            return (ins, skip);
        }

        public List<BankImportItem> LoadPending()
        {
            var list = new List<BankImportItem>();
            using var c = CreateConnection();
            c.Open();
            EnsureStructuredReferenceColumns(c);

            const string sql = @"
SELECT Id, AccountIban, Currency, BookingDate, ValueDate, Amount, Direction, ServiceRef, StructuredReference, [Text],
       CounterpartyName, CounterpartyIban, Uetr, PurposeCode,
       VorschlagAdresseId, VorschlagNachKontoId, VorschlagVonKontoId, VorschlagGeldinstitutId
FROM BankImportItem
WHERE [Status] = 0
ORDER BY BookingDate, Id";
            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new BankImportItem
                {
                    StagingId = r.GetInt32(0),
                    AccountIban = r.IsDBNull(1) ? "" : r.GetString(1),
                    Currency = r.IsDBNull(2) ? "CHF" : r.GetString(2),
                    BookingDate = r.GetDateTime(3),
                    ValueDate = r.IsDBNull(4) ? (DateTime?)null : r.GetDateTime(4),
                    Amount = r.GetDecimal(5),
                    Direction = r.GetString(6) == "CRDT" ? KreditDebit.Credit : KreditDebit.Debit,
                    ServiceRef = r.IsDBNull(7) ? "" : r.GetString(7),
                    StructuredReference = r.IsDBNull(8) ? null : r.GetString(8),
                    Text = r.IsDBNull(9) ? "" : r.GetString(9),
                    CounterpartyName = r.IsDBNull(10) ? null : r.GetString(10),
                    CounterpartyIban = r.IsDBNull(11) ? null : r.GetString(11),
                    Uetr = r.IsDBNull(12) ? null : r.GetString(12),
                    PurposeCode = r.IsDBNull(13) ? null : r.GetString(13),
                    VorschlagAdresseId = r.IsDBNull(14) ? (int?)null : r.GetInt32(14),
                    VorschlagNachKontoId = r.IsDBNull(15) ? (int?)null : r.GetInt32(15),
                    VorschlagVonKontoId = r.IsDBNull(16) ? (int?)null : r.GetInt32(16),
                    VorschlagGeldinstitutId = r.IsDBNull(17) ? (int?)null : r.GetInt32(17)
                });
            }
            return list;
        }

        /// <summary>Persistiert Vorschlags-Felder für ein Staging-Item.</summary>
        public void UpdateSuggestions(int stagingId, int? adresseId, int? nachKontoId, int? vonKontoId, int? geldinstitutId)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"
UPDATE BankImportItem SET
    VorschlagAdresseId = @adr,
    VorschlagNachKontoId = @nach,
    VorschlagVonKontoId = @von,
    VorschlagGeldinstitutId = @gi
WHERE Id = @id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", stagingId);
            cmd.Parameters.AddWithValue("@adr", (object?)adresseId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@nach", (object?)nachKontoId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@von", (object?)vonKontoId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gi", (object?)geldinstitutId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public void MoveToArchive(int sourceItemId, int? transaktionId, string reason)
        {
            using var c = CreateConnection();
            c.Open();
            EnsureStructuredReferenceColumns(c);
            using var tx = c.BeginTransaction();

            const string insArc = @"
INSERT INTO BankImportItemArchive
(SourceItemId, BatchId, AccountIban, Currency, BookingDate, ValueDate, Amount, Direction, ServiceRef, StructuredReference, [Text],
 CounterpartyName, CounterpartyIban, Uetr, PurposeCode,
 VorschlagAdresseId, VorschlagNachKontoId, VorschlagVonKontoId, VorschlagGeldinstitutId,
 BookedTransaktionId, ArchiveReason)
SELECT i.Id, i.BatchId, i.AccountIban, i.Currency, i.BookingDate, i.ValueDate, i.Amount, i.Direction, i.ServiceRef, i.StructuredReference, i.[Text],
       i.CounterpartyName, i.CounterpartyIban, i.Uetr, i.PurposeCode,
       i.VorschlagAdresseId, i.VorschlagNachKontoId, i.VorschlagVonKontoId, i.VorschlagGeldinstitutId,
       @tId, @reason
FROM BankImportItem i
WHERE i.Id = @id;";

            using (var cmd1 = new SqlCommand(insArc, c, tx))
            {
                cmd1.Parameters.AddWithValue("@id", sourceItemId);
                cmd1.Parameters.AddWithValue("@tId", (object?)transaktionId ?? DBNull.Value);
                cmd1.Parameters.AddWithValue("@reason", reason);
                cmd1.ExecuteNonQuery();
            }

            using (var del = new SqlCommand("DELETE FROM BankImportItem WHERE Id = @id", c, tx))
            {
                del.Parameters.AddWithValue("@id", sourceItemId);
                del.ExecuteNonQuery();
            }

            tx.Commit();
        }

        public static byte[] ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var fs = System.IO.File.OpenRead(filePath);
            return sha.ComputeHash(fs);
        }

        public static string BuildUniqKey(BankImportItem it)
        {
            var d = it.BookingDate.ToString("yyyyMMdd");
            var a = it.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            var r = it.ServiceRef ?? "";
            return $"{d}|{a}|{r}";
        }

        private static void EnsureStructuredReferenceColumns(SqlConnection connection)
        {
            const string sql = @"
IF COL_LENGTH(N'dbo.BankImportItem', N'StructuredReference') IS NULL
    ALTER TABLE dbo.BankImportItem ADD StructuredReference nvarchar(80) NULL;
IF COL_LENGTH(N'dbo.BankImportItemArchive', N'StructuredReference') IS NULL
    ALTER TABLE dbo.BankImportItemArchive ADD StructuredReference nvarchar(80) NULL;";
            using var command = new SqlCommand(sql, connection);
            command.ExecuteNonQuery();
        }
    }
}
