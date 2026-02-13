using System;
using System.Threading.Tasks;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services
{
    public sealed class DbCopyService
    {
        public async Task CopyAsync(string sourceDb, string targetDb, DbCopyOptions opt, bool createTargetIfMissing)
        {
            if (string.IsNullOrWhiteSpace(sourceDb)) throw new ArgumentException("Source-DB fehlt.");
            if (string.IsNullOrWhiteSpace(targetDb)) throw new ArgumentException("Target-DB fehlt.");
            sourceDb = sourceDb.Trim();
            targetDb = targetDb.Trim();

            if (createTargetIfMissing && !await DbExistsAsync(targetDb))
            {
                var mand = new MandantService();
                await mand.CreateEmptyFromTemplateAsync(targetDb);
            }

            var csTarget = new SqlConnectionStringBuilder(ConnectionStrings.Master)
            {
                InitialCatalog = targetDb,
                IntegratedSecurity = true,
                Encrypt = false,
                TrustServerCertificate = true
            }.ConnectionString;

            await using var conn = new SqlConnection(csTarget);
            await conn.OpenAsync();
            DbTransaction tx = await conn.BeginTransactionAsync();

            try
            {
                // --- (deine bisherigen Blöcke: Kontenstruktur/Adressen/Aliase/Geldinstitute/Nummernkreise/Budgetzeitraum) ---
                // Ich lasse sie unverändert, nur Import-Schema & Mapping ist jetzt robust.

                // Kontenstruktur (unverändert)
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

                // Adressen (unverändert)
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

                // Aliase (unverändert)
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

                // Geldinstitute (unverändert)
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

                // ✅ FIX: Import-Schema & Mapping FK-sicher kopieren
                if (opt.CopyImportSchemas)
                {
                    await CopyImportSchemasAndMappingsAsync(conn, tx, sourceDb);
                }

                // Nummernkreise (unverändert)
                if (opt.CopyNumberRanges)
                {
                    await Exec(conn, tx, @"
IF OBJECT_ID(N'dbo.NumberRangeRules','U') IS NULL
BEGIN
    CREATE TABLE [dbo].[NumberRangeRules](
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [RangeStart] INT NOT NULL,
        [RangeEnd]   INT NOT NULL,
        [Richtung]   NVARCHAR(12) NOT NULL CHECK ([Richtung] IN (N'Ausgabe', N'Einnahme')),
        [Bezeichnung] NVARCHAR(64) NULL,
        [IstBudgetkonto] BIT NOT NULL CONSTRAINT DF_NumberRangeRules_IstBudgetkonto DEFAULT(0),
        CONSTRAINT CK_NumberRangeRules_Range CHECK ([RangeStart] <= [RangeEnd])
    );
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_NumberRangeRules_Range' AND object_id = OBJECT_ID(N'dbo.NumberRangeRules'))
        CREATE INDEX IX_NumberRangeRules_Range ON [dbo].[NumberRangeRules]([RangeStart],[RangeEnd]);
END");

                    await Exec(conn, tx, $@"
IF OBJECT_ID(N'[{sourceDb}].dbo.NumberRangeRules','U') IS NOT NULL
AND OBJECT_ID(N'dbo.NumberRangeRules','U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM dbo.NumberRangeRules)
BEGIN
    SET IDENTITY_INSERT dbo.NumberRangeRules ON;
    INSERT INTO dbo.NumberRangeRules (Id, RangeStart, RangeEnd, Richtung, Bezeichnung, IstBudgetkonto)
    SELECT Id, RangeStart, RangeEnd, Richtung, Bezeichnung, IstBudgetkonto
    FROM   [{sourceDb}].dbo.NumberRangeRules;
    SET IDENTITY_INSERT dbo.NumberRangeRules OFF;
END");
                }

                // Budgetzeitraum (unverändert)
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

        private static async Task CopyImportSchemasAndMappingsAsync(SqlConnection conn, DbTransaction tx, string sourceDb)
        {
            // Wir erstellen im Ziel eine Mapping-Tabelle: SourceSchemaId -> TargetSchemaId (per Name gematcht)
            // Dann fügen wir ImportFieldMapping mit gemappten SchemaIds ein.
            var sql = $@"
IF OBJECT_ID(N'[{sourceDb}].dbo.ImportSchema','U') IS NULL OR OBJECT_ID(N'[{sourceDb}].dbo.ImportFieldMapping','U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.ImportSchema','U') IS NULL OR OBJECT_ID(N'dbo.ImportFieldMapping','U') IS NULL
    RETURN;

-- Temp-Map
IF OBJECT_ID('tempdb..#SchemaMap') IS NOT NULL DROP TABLE #SchemaMap;
CREATE TABLE #SchemaMap (
    SourceId INT NOT NULL,
    TargetId INT NOT NULL
);

-- 1) Stelle sicher, dass alle Schema-Namen aus Source in Target existieren
--    (Target kann bereits Schemas haben, IDs können abweichen)
INSERT INTO dbo.ImportSchema (Name{(HasColumn(conn, tx, "ImportSchema", "IsMaster") ? ", IsMaster" : "")})
SELECT s.Name{(HasColumn(conn, tx, "ImportSchema", "IsMaster") ? ", CAST(0 AS bit)" : "")}
FROM   [{sourceDb}].dbo.ImportSchema s
WHERE  NOT EXISTS (SELECT 1 FROM dbo.ImportSchema t WHERE t.Name = s.Name);

-- 2) Map SourceId -> TargetId über Name
INSERT INTO #SchemaMap (SourceId, TargetId)
SELECT s.Id, t.Id
FROM   [{sourceDb}].dbo.ImportSchema s
JOIN   dbo.ImportSchema t ON t.Name = s.Name;

-- 3) Insert ImportFieldMapping (nur wenn Ziel leer)
IF NOT EXISTS (SELECT 1 FROM dbo.ImportFieldMapping)
BEGIN
    SET IDENTITY_INSERT dbo.ImportFieldMapping ON;

    INSERT INTO dbo.ImportFieldMapping (Id, SchemaId, MasterHeader, SourceHeader, DefaultValue)
    SELECT m.Id,
           map.TargetId,
           m.MasterHeader,
           m.SourceHeader,
           m.DefaultValue
    FROM   [{sourceDb}].dbo.ImportFieldMapping m
    JOIN   #SchemaMap map ON map.SourceId = m.SchemaId;

    SET IDENTITY_INSERT dbo.ImportFieldMapping OFF;
END
";

            await Exec(conn, tx, sql);
        }

        // helper: check column existence inside current transaction
        private static bool HasColumn(SqlConnection conn, DbTransaction tx, string table, string column)
        {
            var sqlTx = tx as SqlTransaction ?? throw new InvalidOperationException("Erwartete SqlTransaction.");
            using var cmd = conn.CreateCommand();
            cmd.Transaction = sqlTx;
            cmd.CommandText = @"
SELECT 1
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME=@t AND COLUMN_NAME=@c";
            cmd.Parameters.AddWithValue("@t", table);
            cmd.Parameters.AddWithValue("@c", column);
            var r = cmd.ExecuteScalar();
            return r != null;
        }

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
            await using var conn = new SqlConnection(ConnectionStrings.Master);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT DB_ID(@n)";
            cmd.Parameters.AddWithValue("@n", dbName);
            var id = await cmd.ExecuteScalarAsync();
            return id != null && id != DBNull.Value;
        }
    }
}
