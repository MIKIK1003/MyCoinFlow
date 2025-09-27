using System;
using System.Threading.Tasks;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Kopiert Grunddaten von Source-DB nach Target-DB (LocalDB). Arbeitet transaktional.
    /// Zieltabellen werden nur befüllt, wenn sie leer sind.
    /// </summary>
    public sealed class DbCopyService
    {
        private const string MasterConn = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Initial Catalog=master;";

        public async Task CopyAsync(string sourceDb, string targetDb, DbCopyOptions opt, bool createTargetIfMissing)
        {
            if (string.IsNullOrWhiteSpace(sourceDb)) throw new ArgumentException("Source-DB fehlt.");
            if (string.IsNullOrWhiteSpace(targetDb)) throw new ArgumentException("Target-DB fehlt.");
            sourceDb = sourceDb.Trim();
            targetDb = targetDb.Trim();

            if (createTargetIfMissing && !await DbExistsAsync(targetDb))
            {
                var prov = new DbProvisioner();
                await prov.CreateDatabaseAsync(targetDb, null);
                await prov.CloneSchemaFromTemplateAsync("MyCoinFlowDB", targetDb); // Schema-only
            }

            var csTarget = $@"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Initial Catalog={targetDb};";
            await using var conn = new SqlConnection(csTarget);
            await conn.OpenAsync();
            DbTransaction tx = await conn.BeginTransactionAsync();

            try
            {
                // Kontenstruktur
                if (opt.CopyKontenstruktur)
                {
                    await Exec(conn, tx, $@"
IF OBJECT_ID(N'[{sourceDb}].dbo.KontenArt','U') IS NOT NULL
AND OBJECT_ID(N'dbo.KontenArt','U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM dbo.KontenArt)
BEGIN
    SET IDENTITY_INSERT dbo.KontenArt ON;
    INSERT INTO dbo.KontenArt (Id, Bezeichnung)
    SELECT Id, Bezeichnung FROM [{sourceDb}].dbo.KontenArt;
    SET IDENTITY_INSERT dbo.KontenArt OFF;
END");

                    await Exec(conn, tx, $@"
IF OBJECT_ID(N'[{sourceDb}].dbo.KontenGruppe','U') IS NOT NULL
AND OBJECT_ID(N'dbo.KontenGruppe','U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM dbo.KontenGruppe)
BEGIN
    SET IDENTITY_INSERT dbo.KontenGruppe ON;
    INSERT INTO dbo.KontenGruppe (Id, Bezeichnung)
    SELECT Id, Bezeichnung FROM [{sourceDb}].dbo.KontenGruppe;
    SET IDENTITY_INSERT dbo.KontenGruppe OFF;
END");

                    await Exec(conn, tx, $@"
IF OBJECT_ID(N'[{sourceDb}].dbo.KontenUnterGruppe','U') IS NOT NULL
AND OBJECT_ID(N'dbo.KontenUnterGruppe','U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM dbo.KontenUnterGruppe)
BEGIN
    SET IDENTITY_INSERT dbo.KontenUnterGruppe ON;
    INSERT INTO dbo.KontenUnterGruppe (Id, Bezeichnung)
    SELECT Id, Bezeichnung FROM [{sourceDb}].dbo.KontenUnterGruppe;
    SET IDENTITY_INSERT dbo.KontenUnterGruppe OFF;
END");

                    await Exec(conn, tx, $@"
IF OBJECT_ID(N'[{sourceDb}].dbo.Kontenplan','U') IS NOT NULL
AND OBJECT_ID(N'dbo.Kontenplan','U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM dbo.Kontenplan)
BEGIN
    SET IDENTITY_INSERT dbo.Kontenplan ON;
    INSERT INTO dbo.Kontenplan (Id, Kontonummer, Art, Gruppe, Untergruppe, Detail)
    SELECT Id, Kontonummer, Art, Gruppe, Untergruppe, Detail
    FROM   [{sourceDb}].dbo.Kontenplan;
    SET IDENTITY_INSERT dbo.Kontenplan OFF;
END");
                }

                // Adressen
                if (opt.CopyAdressen)
                {
                    await Exec(conn, tx, $@"
IF OBJECT_ID(N'[{sourceDb}].dbo.Adresse','U') IS NOT NULL
AND OBJECT_ID(N'dbo.Adresse','U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM dbo.Adresse)
BEGIN
    SET IDENTITY_INSERT dbo.Adresse ON;
    INSERT INTO dbo.Adresse (Id, Name, Strasse, PLZ, Ort, Land, Typ, Notiz, IBAN, DefaultKontoId, IstBudgetiert, StandardEinnahmenKontoId)
    SELECT Id, Name, Strasse, PLZ, Ort, Land, Typ, Notiz, IBAN, DefaultKontoId, IstBudgetiert, StandardEinnahmenKontoId
    FROM   [{sourceDb}].dbo.Adresse;
    SET IDENTITY_INSERT dbo.Adresse OFF;
END");
                }

                // Aliase
                if (opt.CopyAliase)
                {
                    await Exec(conn, tx, $@"
DECLARE @srcAlias bit = CASE WHEN OBJECT_ID(N'[{sourceDb}].dbo.AdresseAlias','U') IS NOT NULL THEN 1 ELSE 0 END;
DECLARE @srcAlt   bit = CASE WHEN OBJECT_ID(N'[{sourceDb}].dbo.AdressAlias','U')  IS NOT NULL THEN 1 ELSE 0 END;

IF (OBJECT_ID(N'dbo.AdresseAlias','U') IS NOT NULL) AND NOT EXISTS(SELECT 1 FROM dbo.AdresseAlias)
BEGIN
    SET IDENTITY_INSERT dbo.AdresseAlias ON;

    IF @srcAlias = 1
        INSERT INTO dbo.AdresseAlias (Id, AdresseId, Text, Modus)
        SELECT Id, AdresseId, Text, Modus FROM [{sourceDb}].dbo.AdresseAlias;
    ELSE IF @srcAlt = 1
        INSERT INTO dbo.AdresseAlias (Id, AdresseId, Text, Modus)
        SELECT Id, AdresseId, Text, Modus FROM [{sourceDb}].dbo.AdressAlias;

    SET IDENTITY_INSERT dbo.AdresseAlias OFF;
END");
                }

                // Geldinstitute
                if (opt.CopyGeldinstitute)
                {
                    await Exec(conn, tx, $@"
IF OBJECT_ID(N'[{sourceDb}].dbo.Geldinstitut','U') IS NOT NULL
AND OBJECT_ID(N'dbo.Geldinstitut','U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM dbo.Geldinstitut)
BEGIN
    SET IDENTITY_INSERT dbo.Geldinstitut ON;
    INSERT INTO dbo.Geldinstitut (Id, Name, BIC, IBAN, KontoNummer, Notiz, Anfangsbestand, Anfangsdatum)
    SELECT Id, Name, BIC, IBAN, KontoNummer, Notiz, CAST(0 AS DECIMAL(18,2)), NULL
    FROM   [{sourceDb}].dbo.Geldinstitut;
    SET IDENTITY_INSERT dbo.Geldinstitut OFF;
END");
                }

                // Import-Schema & Mapping (nur wenn Tabellen existieren)
                if (opt.CopyImportSchemas)
                {
                    await Exec(conn, tx, $@"
IF OBJECT_ID(N'[{sourceDb}].dbo.ImportSchema','U') IS NOT NULL
AND OBJECT_ID(N'dbo.ImportSchema','U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM dbo.ImportSchema)
BEGIN
    DECLARE @hasIsMaster bit =
      CASE WHEN EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='ImportSchema' AND COLUMN_NAME='IsMaster') THEN 1 ELSE 0 END;

    IF @hasIsMaster = 1
    BEGIN
        DECLARE @srcHasIsMaster bit =
          CASE WHEN EXISTS(SELECT 1 FROM [{sourceDb}].INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='ImportSchema' AND COLUMN_NAME='IsMaster') THEN 1 ELSE 0 END;

        SET IDENTITY_INSERT dbo.ImportSchema ON;
        IF @srcHasIsMaster = 1
            INSERT INTO dbo.ImportSchema (Id, Name, IsMaster)
            SELECT Id, Name, IsMaster FROM [{sourceDb}].dbo.ImportSchema;
        ELSE
            INSERT INTO dbo.ImportSchema (Id, Name, IsMaster)
            SELECT Id, Name, CAST(0 AS bit) FROM [{sourceDb}].dbo.ImportSchema;
        SET IDENTITY_INSERT dbo.ImportSchema OFF;
    END
    ELSE
    BEGIN
        SET IDENTITY_INSERT dbo.ImportSchema ON;
        INSERT INTO dbo.ImportSchema (Id, Name)
        SELECT Id, Name FROM [{sourceDb}].dbo.ImportSchema;
        SET IDENTITY_INSERT dbo.ImportSchema OFF;
    END
END");

                    await Exec(conn, tx, $@"
IF OBJECT_ID(N'[{sourceDb}].dbo.ImportFieldMapping','U') IS NOT NULL
AND OBJECT_ID(N'dbo.ImportFieldMapping','U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM dbo.ImportFieldMapping)
BEGIN
    SET IDENTITY_INSERT dbo.ImportFieldMapping ON;
    INSERT INTO dbo.ImportFieldMapping (Id, SchemaId, MasterHeader, SourceHeader, DefaultValue)
    SELECT Id, SchemaId, MasterHeader, SourceHeader, DefaultValue
    FROM   [{sourceDb}].dbo.ImportFieldMapping;
    SET IDENTITY_INSERT dbo.ImportFieldMapping OFF;
END");
                }

                // Budgetzeitraum
                if (opt.CreateBudgetzeitraum)
                {
                    var start = new DateTime(opt.BudgetYear, 1, 1);
                    var end = new DateTime(opt.BudgetYear, 12, 31);
                    var bez = $"Jahr {opt.BudgetYear}";

                    await Exec(conn, tx, @"
UPDATE dbo.Budgetzeitraum SET IstAktiv = 0;
IF NOT EXISTS(SELECT 1 FROM dbo.Budgetzeitraum WHERE Bezeichnung=@bez)
    INSERT INTO dbo.Budgetzeitraum (Bezeichnung, Startdatum, Enddatum, IstAktiv)
    VALUES (@bez, @start, @end, 1);",
                    new SqlParameter("@bez", bez),
                    new SqlParameter("@start", start),
                    new SqlParameter("@end", end));
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // helpers
        private static async Task Exec(SqlConnection conn, DbTransaction tx, string sql, params SqlParameter[] p)
        {
            var sqlTx = tx as SqlTransaction ?? throw new InvalidOperationException("Erwartete SqlTransaction.");
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = sqlTx;
            cmd.CommandText = sql;
            if (p is { Length: > 0 }) cmd.Parameters.AddRange(p);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<bool> DbExistsAsync(string dbName)
        {
            await using var conn = new SqlConnection(MasterConn);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT DB_ID(@n)";
            cmd.Parameters.AddWithValue("@n", dbName);
            var id = await cmd.ExecuteScalarAsync();
            return id != null && id != DBNull.Value;
        }
    }
}
