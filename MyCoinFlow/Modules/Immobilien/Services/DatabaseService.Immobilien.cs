using ExcelDataReader;
using Microsoft.Data.SqlClient;
using MyCoinFlow.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Transactions;
using System.Text.RegularExpressions;


namespace MyCoinFlow.Services
{
    public partial class DatabaseService
    {
        /// <summary>
        /// Legt die Tabellen für das STWE/Liegenschaften-Modul an (idempotent).
        /// Wird beim ersten Klick auf "Liegenschaften" aufgerufen.
        /// </summary>
        public void EnsureStweSchema()
        {
            using var c = CreateConnection();
            c.Open();

            // Minimaler Start (Schritt 1): nur Basis-Tabellen.
            // In Schritt 2/3 erweitern wir um Eigentümerwechsel, Sets, Lines, Schlüssel etc.
            const string sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweLiegenschaft' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweLiegenschaft
    (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StweLiegenschaft PRIMARY KEY,
        Name          NVARCHAR(120) NOT NULL,
        Strasse       NVARCHAR(120) NULL,
        PLZ           NVARCHAR(10)  NULL,
        Ort           NVARCHAR(80)  NULL,
        Notiz         NVARCHAR(400) NULL,
        CreatedAtUtc  DATETIME2 NOT NULL CONSTRAINT DF_StweLiegenschaft_Created DEFAULT SYSUTCDATETIME()
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweEinheit' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweEinheit
    (
        Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StweEinheit PRIMARY KEY,
        LiegenschaftId INT NOT NULL,
        Bezeichnung    NVARCHAR(80) NOT NULL,        -- z.B. 'Whg 3.2', 'Garage G12'
        Typ            NVARCHAR(30) NULL,            -- Wohnung/Garage/Keller/Gewerbe (später)
        MeaPromille    DECIMAL(9,3) NULL,            -- Miteigentumsanteil (‰)
        FlaecheM2      DECIMAL(9,2) NULL,
        Notiz          NVARCHAR(400) NULL,
        CONSTRAINT FK_StweEinheit_Liegenschaft FOREIGN KEY (LiegenschaftId) REFERENCES dbo.StweLiegenschaft(Id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweEigentuemer' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweEigentuemer
    (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StweEigentuemer PRIMARY KEY,
        Name          NVARCHAR(120) NOT NULL,
        Email         NVARCHAR(160) NULL,
        Telefon       NVARCHAR(60)  NULL,
        Notiz         NVARCHAR(400) NULL,
        CreatedAtUtc  DATETIME2 NOT NULL CONSTRAINT DF_StweEigentuemer_Created DEFAULT SYSUTCDATETIME()
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweEinheitEigentum' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweEinheitEigentum
    (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StweEinheitEigentum PRIMARY KEY,
        EinheitId    INT NOT NULL,
        EigentuemerId INT NOT NULL,
        GueltigVon   DATE NOT NULL,
        GueltigBis   DATE NULL,

        CONSTRAINT FK_StweEinheitEigentum_Einheit
            FOREIGN KEY (EinheitId) REFERENCES dbo.StweEinheit(Id),

        CONSTRAINT FK_StweEinheitEigentum_Eigentuemer
            FOREIGN KEY (EigentuemerId) REFERENCES dbo.StweEigentuemer(Id)
    );

    CREATE INDEX IX_StweEinheitEigentum_EinheitId ON dbo.StweEinheitEigentum(EinheitId);
    CREATE INDEX IX_StweEinheitEigentum_EigentuemerId ON dbo.StweEinheitEigentum(EigentuemerId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweSet' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweSet
    (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StweSet PRIMARY KEY,
        LiegenschaftId INT NOT NULL,
        TransaktionId INT NOT NULL,
        Titel         NVARCHAR(160) NULL,
        CreatedAtUtc  DATETIME2 NOT NULL CONSTRAINT DF_StweSet_Created DEFAULT SYSUTCDATETIME(),
        IsClosed      BIT NOT NULL CONSTRAINT DF_StweSet_IsClosed DEFAULT(0),

        CONSTRAINT FK_StweSet_Liegenschaft FOREIGN KEY (LiegenschaftId) REFERENCES dbo.StweLiegenschaft(Id),
        CONSTRAINT FK_StweSet_Transaktion  FOREIGN KEY (TransaktionId)  REFERENCES dbo.Transaktion(Id)
    );

    CREATE INDEX IX_StweSet_LiegenschaftId ON dbo.StweSet(LiegenschaftId);
    CREATE INDEX IX_StweSet_TransaktionId  ON dbo.StweSet(TransaktionId);
END;

-- ------------------------------------------------------------
-- STWE: StweSet -> Referenz auf verwendetes Zählerdaten-Set (ENERGIE)
-- ------------------------------------------------------------
IF COL_LENGTH('dbo.StweSet', 'EnergieZaehlerdatenSetId') IS NULL
BEGIN
    ALTER TABLE dbo.StweSet
    ADD EnergieZaehlerdatenSetId INT NULL;
END;



-- NEU: Set-Typ (Gutschrift/Belastung)
IF COL_LENGTH('dbo.StweSet', 'IsCredit') IS NULL
BEGIN
    ALTER TABLE dbo.StweSet
    ADD IsCredit BIT NOT NULL CONSTRAINT DF_StweSet_IsCredit DEFAULT(0);
END;



IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweSetLine' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweSetLine
    (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StweSetLine PRIMARY KEY,
        SetId        INT NOT NULL,
        EinheitId    INT NULL,
        EigentuemerId INT NULL,
        Schluessel   NVARCHAR(40) NULL,     -- z.B. MEA / Fläche / Fix / Individuell (kommt in Schritt 6)
        Betrag       DECIMAL(18,2) NOT NULL,
        Notiz        NVARCHAR(200) NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_StweSetLine_Created DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_StweSetLine_Set FOREIGN KEY (SetId) REFERENCES dbo.StweSet(Id)
    );

    CREATE INDEX IX_StweSetLine_SetId ON dbo.StweSetLine(SetId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweSchluessel' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweSchluessel
    (
        Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StweSchluessel PRIMARY KEY,
        LiegenschaftId INT NOT NULL,
        Name           NVARCHAR(120) NOT NULL,
        Modus          NVARCHAR(12) NOT NULL, -- 'FIX' | 'MEA'
        CreatedAtUtc   DATETIME2 NOT NULL CONSTRAINT DF_StweSchluessel_Created DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_StweSchluessel_Liegenschaft
            FOREIGN KEY (LiegenschaftId) REFERENCES dbo.StweLiegenschaft(Id)
    );

    CREATE INDEX IX_StweSchluessel_LiegenschaftId ON dbo.StweSchluessel(LiegenschaftId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweSchluesselLine' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweSchluesselLine
    (
        Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StweSchluesselLine PRIMARY KEY,
        SchluesselId INT NOT NULL,
        EigentuemerId INT NOT NULL,
        AnteilProzent DECIMAL(9,4) NOT NULL, -- 0..100

        CONSTRAINT FK_StweSchluesselLine_Schluessel
            FOREIGN KEY (SchluesselId) REFERENCES dbo.StweSchluessel(Id),

        CONSTRAINT FK_StweSchluesselLine_Eigentuemer
            FOREIGN KEY (EigentuemerId) REFERENCES dbo.StweEigentuemer(Id)
    );

    CREATE INDEX IX_StweSchluesselLine_SchluesselId ON dbo.StweSchluesselLine(SchluesselId);
END;


-- ------------------------------------------------------------
-- ENERGIE: Zähler-Stammdaten pro Liegenschaft
-- ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweZaehler' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweZaehler
    (
        Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StweZaehler PRIMARY KEY,
        LiegenschaftId INT NOT NULL,
        Name           NVARCHAR(120) NOT NULL,
        Typ            NVARCHAR(12) NOT NULL,   -- 'DIREKT' | 'ALLG' | 'HEIZ' | 'EVU'
        EinheitId      INT NULL,                -- nur bei DIREKT
        SchluesselId   INT NULL,                -- optional: Verteil-Schlüssel für diesen Zähler
        Notiz          NVARCHAR(200) NULL,
        CreatedAtUtc   DATETIME2 NOT NULL CONSTRAINT DF_StweZaehler_Created DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_StweZaehler_Liegenschaft
            FOREIGN KEY (LiegenschaftId) REFERENCES dbo.StweLiegenschaft(Id),

        CONSTRAINT FK_StweZaehler_Einheit
            FOREIGN KEY (EinheitId) REFERENCES dbo.StweEinheit(Id)        ,
        CONSTRAINT FK_StweZaehler_Schluessel
            FOREIGN KEY (SchluesselId) REFERENCES dbo.StweSchluessel(Id)

    );

    CREATE INDEX IX_StweZaehler_LiegenschaftId ON dbo.StweZaehler(LiegenschaftId);
    CREATE INDEX IX_StweZaehler_EinheitId      ON dbo.StweZaehler(EinheitId);
    CREATE INDEX IX_StweZaehler_SchluesselId   ON dbo.StweZaehler(SchluesselId);

END;


-- ------------------------------------------------------------
-- ENERGIE: Zähler-Verteilzeilen (Eigentümer-Quoten pro Zähler)
-- ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweZaehlerLine' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweZaehlerLine
    (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StweZaehlerLine PRIMARY KEY,
        ZaehlerId      INT NOT NULL,
        EigentuemerId  INT NOT NULL,
        AnteilProzent  DECIMAL(18,6) NOT NULL,

        CONSTRAINT FK_StweZaehlerLine_Zaehler
            FOREIGN KEY (ZaehlerId) REFERENCES dbo.StweZaehler(Id),

        CONSTRAINT FK_StweZaehlerLine_Eigentuemer
            FOREIGN KEY (EigentuemerId) REFERENCES dbo.StweEigentuemer(Id),

        CONSTRAINT UQ_StweZaehlerLine_Zaehler_Eigentuemer
            UNIQUE (ZaehlerId, EigentuemerId)
    );

    CREATE INDEX IX_StweZaehlerLine_ZaehlerId ON dbo.StweZaehlerLine(ZaehlerId);
END;



-- Nachmigration: SchluesselId nachziehen (wenn DB schon existiert)
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweZaehler' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    IF COL_LENGTH('dbo.StweZaehler', 'SchluesselId') IS NULL
    BEGIN
        ALTER TABLE dbo.StweZaehler ADD SchluesselId INT NULL;
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_StweZaehler_Schluessel')
    BEGIN
        ALTER TABLE dbo.StweZaehler WITH CHECK
        ADD CONSTRAINT FK_StweZaehler_Schluessel
        FOREIGN KEY (SchluesselId) REFERENCES dbo.StweSchluessel(Id);
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_StweZaehler_SchluesselId' AND object_id = OBJECT_ID('dbo.StweZaehler'))
    BEGIN
        CREATE INDEX IX_StweZaehler_SchluesselId ON dbo.StweZaehler(SchluesselId);
    END;
END;


-- ------------------------------------------------------------
-- : Zählerstände je Set (Alt/Neu)
-- ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweEnergieSetZaehler' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweEnergieSetZaehler
    (
        Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StweEnergieSetZaehler PRIMARY KEY,
        SetId     INT NOT NULL,
        ZaehlerId INT NOT NULL,
        AltKwh    DECIMAL(18,3) NOT NULL,
        NeuKwh    DECIMAL(18,3) NOT NULL,

        CONSTRAINT FK_StweEnergieSetZaehler_Set
            FOREIGN KEY (SetId) REFERENCES dbo.StweSet(Id),

        CONSTRAINT FK_StweEnergieSetZaehler_Zaehler
            FOREIGN KEY (ZaehlerId) REFERENCES dbo.StweZaehler(Id),

        CONSTRAINT UQ_StweEnergieSetZaehler_Set_Zaehler UNIQUE (SetId, ZaehlerId)
    );

    CREATE INDEX IX_StweEnergieSetZaehler_SetId ON dbo.StweEnergieSetZaehler(SetId);
END;

-- ------------------------------------------------------------
-- ENERGIE: Meta je Set (EVU-kWh und PV-Gutschrift fürs Budgetkonto)
-- ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweEnergieSetMeta' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweEnergieSetMeta
    (
        SetId            INT NOT NULL CONSTRAINT PK_StweEnergieSetMeta PRIMARY KEY,
        EvuKwh           DECIMAL(18,3) NULL,     -- Leistung auf EVU-Rechnung (kWh)
        PvGutschriftChf  DECIMAL(18,2) NULL,     -- nur Budget-Zuordnung / Auswertung
        PvKontoId        INT NULL,               -- dbo.Kontenplan(Id)
        Notiz            NVARCHAR(200) NULL,
        UpdatedAtUtc     DATETIME2 NOT NULL CONSTRAINT DF_StweEnergieSetMeta_Updated DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_StweEnergieSetMeta_Set
            FOREIGN KEY (SetId) REFERENCES dbo.StweSet(Id),

        CONSTRAINT FK_StweEnergieSetMeta_Konto
            FOREIGN KEY (PvKontoId) REFERENCES dbo.Kontenplan(Id)
    );
END;

-- ------------------------------------------------------------
-- STWE: Zählerdaten (Ablesungen) – Header
-- ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweZaehlerdatenSet' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweZaehlerdatenSet
    (
        Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StweZaehlerdatenSet PRIMARY KEY,
        LiegenschaftId   INT NOT NULL,
        ErfasstAm        DATETIME2 NOT NULL,
        RechnungKwhTotal DECIMAL(18,3) NULL,
        GutschriftChf    DECIMAL(18,2) NULL,
        RueckgespeistKwh DECIMAL(18,3) NULL,
        Notiz            NVARCHAR(200) NULL,
        UpdatedAtUtc     DATETIME2 NOT NULL CONSTRAINT DF_StweZaehlerdatenSet_Updated DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_StweZaehlerdatenSet_Liegenschaft
            FOREIGN KEY (LiegenschaftId) REFERENCES dbo.StweLiegenschaft(Id)
    );

    CREATE INDEX IX_StweZaehlerdatenSet_Lid_Am ON dbo.StweZaehlerdatenSet(LiegenschaftId, ErfasstAm DESC, Id DESC);
END;

-- Nachrüstung (bestehende DB): RueckgespeistKwh
IF COL_LENGTH('dbo.StweZaehlerdatenSet', 'RueckgespeistKwh') IS NULL
BEGIN
    ALTER TABLE dbo.StweZaehlerdatenSet
    ADD RueckgespeistKwh DECIMAL(18,3) NULL;
END;


-- ------------------------------------------------------------
-- STWE: Zählerdaten (Ablesungen) – Lines (Neuwerte je Zähler)
-- ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweZaehlerdatenLine' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweZaehlerdatenLine
    (
        Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StweZaehlerdatenLine PRIMARY KEY,
        SetId     INT NOT NULL,
        ZaehlerId INT NOT NULL,
        NeuWert   DECIMAL(18,3) NOT NULL,

        CONSTRAINT FK_StweZaehlerdatenLine_Set
            FOREIGN KEY (SetId) REFERENCES dbo.StweZaehlerdatenSet(Id),

        CONSTRAINT FK_StweZaehlerdatenLine_Zaehler
            FOREIGN KEY (ZaehlerId) REFERENCES dbo.StweZaehler(Id),

        CONSTRAINT UQ_StweZaehlerdatenLine_Set_Zaehler UNIQUE(SetId, ZaehlerId)
    );

    CREATE INDEX IX_StweZaehlerdatenLine_SetId ON dbo.StweZaehlerdatenLine(SetId);
END;

-- ------------------------------------------------------------
-- STWE: Monatswerte je Zähler (nur bei ErfassungsTyp = 1)
-- ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StweZaehlerdatenMonat' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.StweZaehlerdatenMonat
    (
        Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StweZaehlerdatenMonat PRIMARY KEY,
        SetId       INT NOT NULL,
        ZaehlerId   INT NOT NULL,
        MonatIndex  INT NOT NULL,              -- 1..n
        Kwh         DECIMAL(18,3) NOT NULL,

        CONSTRAINT FK_StweZaehlerdatenMonat_Set
            FOREIGN KEY (SetId) REFERENCES dbo.StweZaehlerdatenSet(Id),

        CONSTRAINT FK_StweZaehlerdatenMonat_Zaehler
            FOREIGN KEY (ZaehlerId) REFERENCES dbo.StweZaehler(Id)
    );

    CREATE INDEX IX_StweZaehlerdatenMonat_SetId ON dbo.StweZaehlerdatenMonat(SetId);
END;


-- ------------------------------------------------------------
-- STWE: Erfassungsart (Differenz / Monatswerte)
-- ------------------------------------------------------------
IF COL_LENGTH('dbo.StweZaehlerdatenSet', 'ErfassungsTyp') IS NULL
BEGIN
    ALTER TABLE dbo.StweZaehlerdatenSet
    ADD ErfassungsTyp INT NOT NULL CONSTRAINT DF_StweZaehlerdatenSet_ErfassungsTyp DEFAULT(0);
END;

IF COL_LENGTH('dbo.StweZaehlerdatenSet', 'MonatsAnzahl') IS NULL
BEGIN
    ALTER TABLE dbo.StweZaehlerdatenSet
    ADD MonatsAnzahl INT NULL;
END;

-- ------------------------------------------------------------
-- STWE: FIX-/ENERGIE-Zeilen optional auf Einheit beziehen
-- ------------------------------------------------------------
IF COL_LENGTH('dbo.StweSchluesselLine', 'EinheitId') IS NULL
BEGIN
    ALTER TABLE dbo.StweSchluesselLine
    ADD EinheitId INT NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_StweSchluesselLine_Einheit')
BEGIN
    ALTER TABLE dbo.StweSchluesselLine WITH CHECK
    ADD CONSTRAINT FK_StweSchluesselLine_Einheit
    FOREIGN KEY (EinheitId) REFERENCES dbo.StweEinheit(Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_StweSchluesselLine_EinheitId' AND object_id = OBJECT_ID('dbo.StweSchluesselLine'))
BEGIN
    CREATE INDEX IX_StweSchluesselLine_EinheitId ON dbo.StweSchluesselLine(EinheitId);
END;

IF COL_LENGTH('dbo.StweZaehlerLine', 'EinheitId') IS NULL
BEGIN
    ALTER TABLE dbo.StweZaehlerLine
    ADD EinheitId INT NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_StweZaehlerLine_Einheit')
BEGIN
    ALTER TABLE dbo.StweZaehlerLine WITH CHECK
    ADD CONSTRAINT FK_StweZaehlerLine_Einheit
    FOREIGN KEY (EinheitId) REFERENCES dbo.StweEinheit(Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_StweZaehlerLine_EinheitId' AND object_id = OBJECT_ID('dbo.StweZaehlerLine'))
BEGIN
    CREATE INDEX IX_StweZaehlerLine_EinheitId ON dbo.StweZaehlerLine(EinheitId);
END;



";
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

 
        /// <summary>
        /// Lädt alle Liegenschaften (STWE) für die Übersicht.
        /// </summary>
        public List<MyCoinFlow.Models.StweLiegenschaft> StweLiegenschaftenGetAll()
        {
            EnsureStweSchema();

            var list = new List<MyCoinFlow.Models.StweLiegenschaft>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT Id, Name, Strasse, PLZ, Ort, Notiz
FROM dbo.StweLiegenschaft
ORDER BY Name;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.StweLiegenschaft
                {
                    Id = r.GetInt32(0),
                    Name = r.GetString(1),
                    Strasse = r.IsDBNull(2) ? null : r.GetString(2),
                    PLZ = r.IsDBNull(3) ? null : r.GetString(3),
                    Ort = r.IsDBNull(4) ? null : r.GetString(4),
                    Notiz = r.IsDBNull(5) ? null : r.GetString(5)
                });
            }

            return list;
        }

        /// <summary>
        /// Legt eine neue Liegenschaft an und gibt die neue Id zurück.
        /// </summary>
        public int StweLiegenschaftInsert(MyCoinFlow.Models.StweLiegenschaft l)
        {
            if (l == null) throw new ArgumentNullException(nameof(l));
            if (string.IsNullOrWhiteSpace(l.Name))
                throw new ArgumentException("Name darf nicht leer sein.", nameof(l));

            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.StweLiegenschaft (Name, Strasse, PLZ, Ort, Notiz)
OUTPUT INSERTED.Id
VALUES (@n, @s, @p, @o, @no);";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var pN = cmd.CreateParameter(); pN.ParameterName = "@n"; pN.Value = l.Name.Trim(); cmd.Parameters.Add(pN);
            var pS = cmd.CreateParameter(); pS.ParameterName = "@s"; pS.Value = (object?)l.Strasse ?? DBNull.Value; cmd.Parameters.Add(pS);
            var pP = cmd.CreateParameter(); pP.ParameterName = "@p"; pP.Value = (object?)l.PLZ ?? DBNull.Value; cmd.Parameters.Add(pP);
            var pO = cmd.CreateParameter(); pO.ParameterName = "@o"; pO.Value = (object?)l.Ort ?? DBNull.Value; cmd.Parameters.Add(pO);
            var pNo = cmd.CreateParameter(); pNo.ParameterName = "@no"; pNo.Value = (object?)l.Notiz ?? DBNull.Value; cmd.Parameters.Add(pNo);

            var idObj = cmd.ExecuteScalar();
            return Convert.ToInt32(idObj);
        }

        public List<MyCoinFlow.Models.StweEinheit> StweEinheitenGetByLiegenschaft(int liegenschaftId)
        {
            EnsureStweSchema();

            var list = new List<MyCoinFlow.Models.StweEinheit>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT Id, LiegenschaftId, Bezeichnung, Typ, MeaPromille, FlaecheM2, Notiz
FROM dbo.StweEinheit
WHERE LiegenschaftId = @lid
ORDER BY Bezeichnung;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var p = cmd.CreateParameter();
            p.ParameterName = "@lid";
            p.Value = liegenschaftId;
            cmd.Parameters.Add(p);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.StweEinheit
                {
                    Id = r.GetInt32(0),
                    LiegenschaftId = r.GetInt32(1),
                    Bezeichnung = r.GetString(2),
                    Typ = r.IsDBNull(3) ? null : r.GetString(3),
                    MeaPromille = r.IsDBNull(4) ? (decimal?)null : r.GetDecimal(4),
                    FlaecheM2 = r.IsDBNull(5) ? (decimal?)null : r.GetDecimal(5),
                    Notiz = r.IsDBNull(6) ? null : r.GetString(6),
                });
            }
            return list;
        }

        public int StweEinheitInsert(MyCoinFlow.Models.StweEinheit e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (e.LiegenschaftId <= 0) throw new ArgumentException("LiegenschaftId fehlt.", nameof(e));
            if (string.IsNullOrWhiteSpace(e.Bezeichnung)) throw new ArgumentException("Bezeichnung fehlt.", nameof(e));

            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.StweEinheit (LiegenschaftId, Bezeichnung, Typ, MeaPromille, FlaecheM2, Notiz)
OUTPUT INSERTED.Id
VALUES (@lid, @b, @t, @m, @f, @n);";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var p1 = cmd.CreateParameter(); p1.ParameterName = "@lid"; p1.Value = e.LiegenschaftId; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "@b"; p2.Value = e.Bezeichnung.Trim(); cmd.Parameters.Add(p2);
            var p3 = cmd.CreateParameter(); p3.ParameterName = "@t"; p3.Value = (object?)e.Typ ?? DBNull.Value; cmd.Parameters.Add(p3);
            var p4 = cmd.CreateParameter(); p4.ParameterName = "@m"; p4.Value = (object?)e.MeaPromille ?? DBNull.Value; cmd.Parameters.Add(p4);
            var p5 = cmd.CreateParameter(); p5.ParameterName = "@f"; p5.Value = (object?)e.FlaecheM2 ?? DBNull.Value; cmd.Parameters.Add(p5);
            var p6 = cmd.CreateParameter(); p6.ParameterName = "@n"; p6.Value = (object?)e.Notiz ?? DBNull.Value; cmd.Parameters.Add(p6);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<MyCoinFlow.Models.StweEigentuemer> StweEigentuemerGetAll()
        {
            EnsureStweSchema();

            var list = new List<MyCoinFlow.Models.StweEigentuemer>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT Id, Name, Email, Telefon, Notiz
FROM dbo.StweEigentuemer
ORDER BY Name;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.StweEigentuemer
                {
                    Id = r.GetInt32(0),
                    Name = r.GetString(1),
                    Email = r.IsDBNull(2) ? null : r.GetString(2),
                    Telefon = r.IsDBNull(3) ? null : r.GetString(3),
                    Notiz = r.IsDBNull(4) ? null : r.GetString(4),
                });
            }
            return list;
        }

        public int StweEigentuemerInsert(MyCoinFlow.Models.StweEigentuemer e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (string.IsNullOrWhiteSpace(e.Name))
                throw new ArgumentException("Name darf nicht leer sein.", nameof(e));

            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.StweEigentuemer (Name, Email, Telefon, Notiz)
OUTPUT INSERTED.Id
VALUES (@n, @em, @tel, @no);";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var pN = cmd.CreateParameter(); pN.ParameterName = "@n"; pN.Value = e.Name.Trim(); cmd.Parameters.Add(pN);
            var pE = cmd.CreateParameter(); pE.ParameterName = "@em"; pE.Value = (object?)e.Email ?? DBNull.Value; cmd.Parameters.Add(pE);
            var pT = cmd.CreateParameter(); pT.ParameterName = "@tel"; pT.Value = (object?)e.Telefon ?? DBNull.Value; cmd.Parameters.Add(pT);
            var pN2 = cmd.CreateParameter(); pN2.ParameterName = "@no"; pN2.Value = (object?)e.Notiz ?? DBNull.Value; cmd.Parameters.Add(pN2);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<MyCoinFlow.Models.StweEinheitEigentumRow> StweEinheitEigentumGetByEinheit(int einheitId)
        {
            EnsureStweSchema();

            var list = new List<MyCoinFlow.Models.StweEinheitEigentumRow>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT x.Id, x.EinheitId, x.EigentuemerId, e.Name, x.GueltigVon, x.GueltigBis
FROM dbo.StweEinheitEigentum x
JOIN dbo.StweEigentuemer e ON e.Id = x.EigentuemerId
WHERE x.EinheitId = @eid
ORDER BY x.GueltigVon DESC, e.Name;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            var p = cmd.CreateParameter(); p.ParameterName = "@eid"; p.Value = einheitId; cmd.Parameters.Add(p);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.StweEinheitEigentumRow
                {
                    Id = r.GetInt32(0),
                    EinheitId = r.GetInt32(1),
                    EigentuemerId = r.GetInt32(2),
                    EigentuemerName = r.GetString(3),
                    GueltigVon = r.GetDateTime(4),
                    GueltigBis = r.IsDBNull(5) ? (DateTime?)null : r.GetDateTime(5)
                });
            }
            return list;
        }

        public int StweEinheitEigentumInsert(int einheitId, int eigentuemerId, DateTime gueltigVon, DateTime? gueltigBis)
        {
            EnsureStweSchema();

            if (einheitId <= 0) throw new ArgumentOutOfRangeException(nameof(einheitId));
            if (eigentuemerId <= 0) throw new ArgumentOutOfRangeException(nameof(eigentuemerId));
            if (gueltigBis.HasValue && gueltigBis.Value.Date < gueltigVon.Date)
                throw new ArgumentException("GueltigBis darf nicht vor GueltigVon liegen.");

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.StweEinheitEigentum (EinheitId, EigentuemerId, GueltigVon, GueltigBis)
OUTPUT INSERTED.Id
VALUES (@eid, @oid, @von, @bis);";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var p1 = cmd.CreateParameter(); p1.ParameterName = "@eid"; p1.Value = einheitId; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "@oid"; p2.Value = eigentuemerId; cmd.Parameters.Add(p2);
            var p3 = cmd.CreateParameter(); p3.ParameterName = "@von"; p3.Value = gueltigVon.Date; cmd.Parameters.Add(p3);
            var p4 = cmd.CreateParameter(); p4.ParameterName = "@bis"; p4.Value = (object?)gueltigBis?.Date ?? DBNull.Value; cmd.Parameters.Add(p4);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void StweEinheitEigentumUpdate(int id, int einheitId, int eigentuemerId, DateTime gueltigVon, DateTime? gueltigBis)
        {
            EnsureStweSchema();

            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (einheitId <= 0) throw new ArgumentOutOfRangeException(nameof(einheitId));
            if (eigentuemerId <= 0) throw new ArgumentOutOfRangeException(nameof(eigentuemerId));
            if (gueltigBis.HasValue && gueltigBis.Value.Date < gueltigVon.Date)
                throw new ArgumentException("GueltigBis darf nicht vor GueltigVon liegen.");

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.StweEinheitEigentum SET
    EinheitId     = @eid,
    EigentuemerId = @oid,
    GueltigVon    = @von,
    GueltigBis    = @bis
WHERE Id = @id;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@eid", einheitId);
            cmd.Parameters.AddWithValue("@oid", eigentuemerId);
            cmd.Parameters.AddWithValue("@von", gueltigVon.Date);
            cmd.Parameters.AddWithValue("@bis", (object?)gueltigBis?.Date ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public void StweEinheitEigentumDelete(int id)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            try
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "DELETE FROM dbo.StweEinheitEigentum WHERE Id=@id;";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                if (HandleSqlDeleteException(ex, "Zuordnung")) return;

                System.Windows.MessageBox.Show(
                    "Zuordnung konnte nicht gelöscht werden:\n" + ex.Message,
                    "Löschen fehlgeschlagen",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        public List<MyCoinFlow.Models.Transaktion> StweTransaktionenGetRecent(int top = 500)
        {
            var list = new List<MyCoinFlow.Models.Transaktion>();

            EnsureNumberRangeRulesTable();

            using var c = CreateConnection();
            c.Open();

            var activeId = HoleAktivenBudgetzeitraumId();

            DateTime? start = null;
            DateTime? end = null;

            if (activeId.HasValue)
            {
                var bz = HoleBudgetzeitraum(activeId.Value);
                if (bz != null)
                {
                    start = bz.Startdatum.Date;
                    end = bz.Enddatum.Date;
                }
            }

            var safeTop = top <= 0 ? 500 : top;

            var sql = $@"
SELECT TOP ({safeTop})
    t.Id,
    t.Datum,
    t.BudgetDatum,
    t.VonKontoId,
    t.NachKontoId,
    t.Betrag,
    t.Notiz,
    t.AdresseId,
    a.Name AS AdresseName,
    t.GeldinstitutId,
    g.Name AS BankName,
    t.ImportQuelle
FROM dbo.Transaktion t
LEFT JOIN dbo.Adresse a      ON a.Id = t.AdresseId
LEFT JOIN dbo.Geldinstitut g ON g.Id = t.GeldinstitutId
LEFT JOIN dbo.Kontenplan vk  ON vk.Id = t.VonKontoId
LEFT JOIN dbo.Kontenplan nk  ON nk.Id = t.NachKontoId
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.StweSet s
    WHERE s.TransaktionId = t.Id
)
AND (@von IS NULL OR ISNULL(t.BudgetDatum, t.Datum) >= @von)
AND (@bis IS NULL OR ISNULL(t.BudgetDatum, t.Datum) <= @bis)
AND NOT EXISTS (
    SELECT 1
    FROM dbo.NumberRangeRules nr
    WHERE nr.ExcludeFromStweSets = 1
      AND (
            TRY_CONVERT(INT, vk.Kontonummer) BETWEEN nr.RangeStart AND nr.RangeEnd
         OR TRY_CONVERT(INT, nk.Kontonummer) BETWEEN nr.RangeStart AND nr.RangeEnd
      )
)
ORDER BY t.Datum DESC, t.Id DESC;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var pVon = cmd.CreateParameter();
            pVon.ParameterName = "@von";
            pVon.Value = (object?)start ?? DBNull.Value;
            cmd.Parameters.Add(pVon);

            var pBis = cmd.CreateParameter();
            pBis.ParameterName = "@bis";
            pBis.Value = (object?)end ?? DBNull.Value;
            cmd.Parameters.Add(pBis);

            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.Transaktion
                {
                    Id = r.GetInt32(0),
                    Datum = r.GetDateTime(1),
                    BudgetDatum = r.IsDBNull(2) ? (DateTime?)null : r.GetDateTime(2),
                    VonKontoId = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    NachKontoId = r.IsDBNull(4) ? (int?)null : r.GetInt32(4),
                    Betrag = r.GetDecimal(5),
                    Notiz = r.IsDBNull(6) ? null : r.GetString(6),
                    AdresseId = r.IsDBNull(7) ? (int?)null : r.GetInt32(7),
                    AdresseName = r.IsDBNull(8) ? null : r.GetString(8),
                    GeldinstitutId = r.IsDBNull(9) ? (int?)null : r.GetInt32(9),
                    BankName = r.IsDBNull(10) ? null : r.GetString(10),
                    ImportQuelle = r.IsDBNull(11) ? null : r.GetString(11)
                });
            }

            return list;
        }


        public int StweSetInsert(int liegenschaftId, int transaktionId, string? titel)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            bool isCredit = false;

            try
            {
                const string sql = @"
        SELECT TOP(1)
            CASE 
                -- 1) Bankimport (höchste Priorität)
                WHEN iba.Direction = 'CRDT' THEN 1
                WHEN iba.Direction = 'DBIT' THEN 0

                -- 2) Einnahme über Kontenregel
                WHEN nrr.Richtung = 'Einnahme' AND nrr.IstBudgetkonto = 1 THEN 1

                -- 3) Rückfluss Konto -> Bank
                WHEN t.VonKontoId IS NOT NULL AND t.NachKontoId IS NULL THEN 1

                ELSE 0
            END

        FROM dbo.Transaktion t

        LEFT JOIN dbo.BankImportItemArchive iba 
            ON iba.BookedTransaktionId = t.Id

        LEFT JOIN dbo.Kontenplan kn 
            ON kn.Id = t.NachKontoId

        LEFT JOIN dbo.NumberRangeRules nrr
            ON kn.Kontonummer BETWEEN nrr.RangeStart AND nrr.RangeEnd

        WHERE t.Id = @id;";

                using var cmd = c.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", transaktionId);

                var v = cmd.ExecuteScalar();
                isCredit = v != null && v != DBNull.Value && Convert.ToInt32(v) == 1;
            }
            catch
            {
                isCredit = false;
            }

            using var tx = c.BeginTransaction(System.Data.IsolationLevel.Serializable);

            try
            {
                const string sqlCheck = @"
SELECT TOP (1) Id
FROM dbo.StweSet
WHERE TransaktionId = @tid;";

                using (var cmdCheck = c.CreateCommand())
                {
                    cmdCheck.Transaction = tx;
                    cmdCheck.CommandText = sqlCheck;
                    cmdCheck.Parameters.AddWithValue("@tid", transaktionId);

                    var existing = cmdCheck.ExecuteScalar();
                    if (existing != null && existing != DBNull.Value)
                    {
                        tx.Rollback();
                        throw new InvalidOperationException(
                            $"Diese Transaktion wurde bereits in einem STWE-Set verarbeitet (SetId {Convert.ToInt32(existing)}).");
                    }
                }

                const string sqlInsert = @"
INSERT INTO dbo.StweSet (LiegenschaftId, TransaktionId, Titel, IsCredit)
OUTPUT INSERTED.Id
VALUES (@lid, @tid, @t, @ic);";

                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sqlInsert;

                cmd.Parameters.AddWithValue("@lid", liegenschaftId);
                cmd.Parameters.AddWithValue("@tid", transaktionId);
                cmd.Parameters.AddWithValue("@t", (object?)titel ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ic", isCredit ? 1 : 0);

                var newId = Convert.ToInt32(cmd.ExecuteScalar());

                tx.Commit();
                return newId;
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }




        public List<MyCoinFlow.Models.StweSetRow> StweSetsGetByLiegenschaft(int liegenschaftId)
        {
            EnsureStweSchema();

            var list = new List<MyCoinFlow.Models.StweSetRow>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT 
    s.Id,
    s.LiegenschaftId,
    s.TransaktionId,
    t.Datum,
    t.BudgetDatum

    -- SIGNED Total (Belastung = +, Gutschrift = -)
    CASE WHEN ISNULL(s.IsCredit, 0) = 1 THEN -t.Betrag ELSE t.Betrag END AS BetragSigned,

    COALESCE(NULLIF(s.Titel,''), COALESCE(NULLIF(t.Notiz,''),'(ohne Text)')) AS Titel,
    s.IsClosed,
    ISNULL(s.IsCredit, 0) AS IsCredit,

    ISNULL(x.Verteilt, 0) AS Verteilt,

    (CASE WHEN ISNULL(s.IsCredit, 0) = 1 THEN -t.Betrag ELSE t.Betrag END) - ISNULL(x.Verteilt, 0) AS Rest
FROM dbo.StweSet s
JOIN dbo.Transaktion t ON t.Id = s.TransaktionId
OUTER APPLY (
    SELECT SUM(l.Betrag) AS Verteilt
    FROM dbo.StweSetLine l
    WHERE l.SetId = s.Id
) x
WHERE s.LiegenschaftId = @lid
ORDER BY t.Datum DESC, s.Id DESC;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var p = cmd.CreateParameter();
            p.ParameterName = "@lid";
            p.Value = liegenschaftId;
            cmd.Parameters.Add(p);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.StweSetRow
                {
                    Id = r.GetInt32(0),
                    LiegenschaftId = r.GetInt32(1),
                    TransaktionId = r.GetInt32(2),
                    Datum = r.GetDateTime(3),
                    Betrag = r.GetDecimal(4),
                    Titel = r.GetString(5),
                    IsClosed = r.GetBoolean(6),
                    IsCredit = r.GetBoolean(7),
                    Verteilt = r.GetDecimal(8),
                    Rest = r.GetDecimal(9)
                });
            }

            return list;
        }


        public List<MyCoinFlow.Models.StweSchluessel> StweSchluesselGetByLiegenschaft(int liegenschaftId)
        {
            EnsureStweSchema();

            var list = new List<MyCoinFlow.Models.StweSchluessel>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT Id, LiegenschaftId, Name, Modus
FROM dbo.StweSchluessel
WHERE LiegenschaftId = @lid
ORDER BY Name;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            var p = cmd.CreateParameter(); p.ParameterName = "@lid"; p.Value = liegenschaftId; cmd.Parameters.Add(p);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.StweSchluessel
                {
                    Id = r.GetInt32(0),
                    LiegenschaftId = r.GetInt32(1),
                    Name = r.GetString(2),
                    Modus = r.GetString(3)
                });
            }
            return list;
        }

        public int StweSchluesselInsert(int liegenschaftId, string name, string modus)
        {
            EnsureStweSchema();

            if (liegenschaftId <= 0) throw new ArgumentOutOfRangeException(nameof(liegenschaftId));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name darf nicht leer sein.", nameof(name));

            modus = (modus ?? "").Trim().ToUpperInvariant();

            // NEU: ENERGIE ist jetzt ein erlaubter Modus (zusätzlich zu FIX/MEA)
            if (modus != "FIX" && modus != "MEA" && modus != "ENERGIE")
                throw new ArgumentException("Modus muss FIX, MEA oder ENERGIE sein.", nameof(modus));

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.StweSchluessel (LiegenschaftId, Name, Modus)
OUTPUT INSERTED.Id
VALUES (@lid, @n, @m);";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var p1 = cmd.CreateParameter(); p1.ParameterName = "@lid"; p1.Value = liegenschaftId; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "@n"; p2.Value = name.Trim(); cmd.Parameters.Add(p2);
            var p3 = cmd.CreateParameter(); p3.ParameterName = "@m"; p3.Value = modus; cmd.Parameters.Add(p3);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<MyCoinFlow.Models.StweSchluesselLine> StweSchluesselLinesGet(int schluesselId)
        {
            EnsureStweSchema();

            var list = new List<MyCoinFlow.Models.StweSchluesselLine>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    l.Id,
    l.SchluesselId,

    l.EinheitId,
    ISNULL(u.Bezeichnung, '') AS EinheitBezeichnung,

    l.EigentuemerId,
    e.Name,

    l.AnteilProzent

FROM dbo.StweSchluesselLine l

LEFT JOIN dbo.StweEinheit u
    ON u.Id = l.EinheitId

JOIN dbo.StweEigentuemer e
    ON e.Id = l.EigentuemerId

WHERE l.SchluesselId = @sid

ORDER BY
    CASE WHEN l.EinheitId IS NULL THEN 1 ELSE 0 END,
    u.Bezeichnung,
    e.Name;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var p = cmd.CreateParameter();
            p.ParameterName = "@sid";
            p.Value = schluesselId;
            cmd.Parameters.Add(p);

            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.StweSchluesselLine
                {
                    Id = r.GetInt32(0),
                    SchluesselId = r.GetInt32(1),

                    EinheitId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                    EinheitBezeichnung = r.IsDBNull(3) ? "" : r.GetString(3),

                    EigentuemerId = r.GetInt32(4),
                    EigentuemerName = r.GetString(5),

                    AnteilProzent = r.GetDecimal(6)
                });
            }

            return list;
        }

        public List<StweZaehlerLine> StweZaehlerLinesGet(int zaehlerId)
        {
            EnsureStweSchema();

            var list = new List<StweZaehlerLine>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    l.Id,
    l.ZaehlerId,

    l.EinheitId,
    ISNULL(u.Bezeichnung, '') AS EinheitBezeichnung,

    l.EigentuemerId,
    o.Name,

    l.AnteilProzent

FROM dbo.StweZaehlerLine l

LEFT JOIN dbo.StweEinheit u
    ON u.Id = l.EinheitId

JOIN dbo.StweEigentuemer o
    ON o.Id = l.EigentuemerId

WHERE l.ZaehlerId = @zid

ORDER BY
    CASE WHEN l.EinheitId IS NULL THEN 1 ELSE 0 END,
    u.Bezeichnung,
    o.Name;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@zid", zaehlerId);

            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new StweZaehlerLine
                {
                    Id = r.GetInt32(0),
                    ZaehlerId = r.GetInt32(1),

                    EinheitId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                    EinheitBezeichnung = r.IsDBNull(3) ? "" : r.GetString(3),

                    EigentuemerId = r.GetInt32(4),
                    EigentuemerName = r.IsDBNull(5) ? "" : r.GetString(5),

                    AnteilProzent = r.GetDecimal(6)
                });
            }

            return list;
        }



        public void StweZaehlerLinesReplace(int zaehlerId, List<(int? EinheitId, int EigentuemerId, decimal AnteilProzent)> lines)
        {
            EnsureStweSchema();

            if (zaehlerId <= 0)
                throw new ArgumentException("zaehlerId muss > 0 sein.", nameof(zaehlerId));

            if (lines == null)
                throw new ArgumentNullException(nameof(lines));

            using var c = CreateConnection();
            c.Open();

            using var tx = c.BeginTransaction();

            try
            {
                using (var cmd = c.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM dbo.StweZaehlerLine WHERE ZaehlerId = @zid;";
                    cmd.Parameters.AddWithValue("@zid", zaehlerId);
                    cmd.ExecuteNonQuery();
                }

                foreach (var (einheitId, eigentuemerId, anteilProzent) in lines)
                {
                    using var cmd = c.CreateCommand();
                    cmd.Transaction = tx;

                    cmd.CommandText = @"
INSERT INTO dbo.StweZaehlerLine
(
    ZaehlerId,
    EinheitId,
    EigentuemerId,
    AnteilProzent
)
VALUES
(
    @zid,
    @einheitId,
    @eigentuemerId,
    @anteilProzent
);";

                    cmd.Parameters.AddWithValue("@zid", zaehlerId);
                    cmd.Parameters.AddWithValue("@einheitId", (object?)einheitId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@eigentuemerId", eigentuemerId);
                    cmd.Parameters.AddWithValue("@anteilProzent", anteilProzent);

                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }



        public void StweSchluesselLinesReplace(int schluesselId, List<(int? EinheitId, int EigentuemerId, decimal AnteilProzent)> lines)
        {
            EnsureStweSchema();

            if (schluesselId <= 0)
                throw new ArgumentOutOfRangeException(nameof(schluesselId));

            lines ??= new();

            using var c = CreateConnection();
            c.Open();

            using var tx = c.BeginTransaction();

            try
            {
                using (var del = c.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM dbo.StweSchluesselLine WHERE SchluesselId = @sid;";
                    del.Parameters.AddWithValue("@sid", schluesselId);
                    del.ExecuteNonQuery();
                }

                foreach (var (einheitId, eigentuemerId, anteilProzent) in lines)
                {
                    using var ins = c.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = @"
INSERT INTO dbo.StweSchluesselLine
(
    SchluesselId,
    EinheitId,
    EigentuemerId,
    AnteilProzent
)
VALUES
(
    @sid,
    @einheitId,
    @eigentuemerId,
    @anteilProzent
);";

                    ins.Parameters.AddWithValue("@sid", schluesselId);
                    ins.Parameters.AddWithValue("@einheitId", (object?)einheitId ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@eigentuemerId", eigentuemerId);
                    ins.Parameters.AddWithValue("@anteilProzent", anteilProzent);

                    ins.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public void StweSetLineInsert(int setId, int? einheitId, int? eigentuemerId, string schluessel, decimal betrag)
        {
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.StweSetLine (SetId, EinheitId, EigentuemerId, Schluessel, Betrag)
VALUES (@sid, @eid, @oid, @s, @b);";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var p1 = cmd.CreateParameter(); p1.ParameterName = "@sid"; p1.Value = setId; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "@eid"; p2.Value = (object?)einheitId ?? DBNull.Value; cmd.Parameters.Add(p2);
            var p3 = cmd.CreateParameter(); p3.ParameterName = "@oid"; p3.Value = (object?)eigentuemerId ?? DBNull.Value; cmd.Parameters.Add(p3);
            var p4 = cmd.CreateParameter(); p4.ParameterName = "@s"; p4.Value = schluessel; cmd.Parameters.Add(p4);
            var p5 = cmd.CreateParameter(); p5.ParameterName = "@b"; p5.Value = betrag; cmd.Parameters.Add(p5);

            cmd.ExecuteNonQuery();
        }

        public List<MyCoinFlow.Models.StweSetLine> StweSetLinesGet(int setId)
        {
            EnsureStweSchema();

            var list = new List<MyCoinFlow.Models.StweSetLine>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT Id, SetId, EinheitId, EigentuemerId, Schluessel, Betrag, Notiz
FROM dbo.StweSetLine
WHERE SetId = @sid
ORDER BY Id;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            var p = cmd.CreateParameter(); p.ParameterName = "@sid"; p.Value = setId; cmd.Parameters.Add(p);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.StweSetLine
                {
                    Id = r.GetInt32(0),
                    SetId = r.GetInt32(1),
                    EinheitId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                    EigentuemerId = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    Schluessel = r.IsDBNull(4) ? null : r.GetString(4),
                    Betrag = r.GetDecimal(5),
                    Notiz = r.IsDBNull(6) ? null : r.GetString(6)
                });
            }
            return list;
        }

        public void StweSetLinesDeleteBySet(int setId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM dbo.StweSetLine WHERE SetId = @sid;";
            var p = cmd.CreateParameter(); p.ParameterName = "@sid"; p.Value = setId; cmd.Parameters.Add(p);
            cmd.ExecuteNonQuery();
        }

        // ------------------------------------------------------------
        // ENERGIE: Zähler (Stammdaten) + Set-Zählerstände + Meta
        // ------------------------------------------------------------

        public List<StweZaehler> StweZaehlerGetByLiegenschaft(int liegenschaftId)
        {
            EnsureStweSchema();

            var list = new List<StweZaehler>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT Id, LiegenschaftId, Name, Typ, EinheitId, SchluesselId, Notiz
FROM dbo.StweZaehler
WHERE LiegenschaftId = @lid
ORDER BY Typ, Name, Id;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@lid", liegenschaftId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new StweZaehler
                {
                    Id = r.GetInt32(0),
                    LiegenschaftId = r.GetInt32(1),
                    Name = r.GetString(2),
                    Typ = r.GetString(3),
                    EinheitId = r.IsDBNull(4) ? (int?)null : r.GetInt32(4),
                    SchluesselId = r.IsDBNull(5) ? (int?)null : r.GetInt32(5),
                    Notiz = r.IsDBNull(6) ? null : r.GetString(6)
                });
            }

            return list;
        }

        public int StweZaehlerInsert(StweZaehler z)
        {
            EnsureStweSchema();

            if (z == null) throw new ArgumentNullException(nameof(z));
            if (z.LiegenschaftId <= 0) throw new ArgumentException("LiegenschaftId fehlt.", nameof(z));
            if (string.IsNullOrWhiteSpace(z.Name)) throw new ArgumentException("Name darf nicht leer sein.", nameof(z));
            if (string.IsNullOrWhiteSpace(z.Typ)) throw new ArgumentException("Typ darf nicht leer sein.", nameof(z));

            z.Typ = z.Typ.Trim().ToUpperInvariant();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.StweZaehler (LiegenschaftId, Name, Typ, EinheitId, SchluesselId, Notiz)
OUTPUT INSERTED.Id
VALUES (@lid, @n, @t, @eid, @sid, @no);";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@lid", z.LiegenschaftId);
            cmd.Parameters.AddWithValue("@n", z.Name.Trim());
            cmd.Parameters.AddWithValue("@t", z.Typ);
            cmd.Parameters.AddWithValue("@eid", (object?)z.EinheitId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sid", (object?)z.SchluesselId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@no", (object?)z.Notiz ?? DBNull.Value);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void StweZaehlerUpdate(StweZaehler z)
        {
            EnsureStweSchema();

            if (z == null) throw new ArgumentNullException(nameof(z));
            if (z.Id <= 0) throw new ArgumentException("Id fehlt.", nameof(z));
            if (z.LiegenschaftId <= 0) throw new ArgumentException("LiegenschaftId fehlt.", nameof(z));
            if (string.IsNullOrWhiteSpace(z.Name)) throw new ArgumentException("Name darf nicht leer sein.", nameof(z));
            if (string.IsNullOrWhiteSpace(z.Typ)) throw new ArgumentException("Typ darf nicht leer sein.", nameof(z));

            z.Typ = z.Typ.Trim().ToUpperInvariant();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.StweZaehler SET
    LiegenschaftId = @lid,
    Name           = @n,
    Typ            = @t,
    EinheitId      = @eid,
    SchluesselId   = @sid,
    Notiz          = @no
WHERE Id = @id;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", z.Id);
            cmd.Parameters.AddWithValue("@lid", z.LiegenschaftId);
            cmd.Parameters.AddWithValue("@n", z.Name.Trim());
            cmd.Parameters.AddWithValue("@t", z.Typ);
            cmd.Parameters.AddWithValue("@eid", (object?)z.EinheitId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sid", (object?)z.SchluesselId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@no", (object?)z.Notiz ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }


        public bool StweZaehlerUsedInEnergieSets(int zaehlerId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"SELECT TOP(1) 1 FROM dbo.StweEnergieSetZaehler WHERE ZaehlerId = @id;";
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", zaehlerId);

            var v = cmd.ExecuteScalar();
            return v != null && v != DBNull.Value;
        }

        public void StweZaehlerDelete(int zaehlerId)
        {
            EnsureStweSchema();

            if (zaehlerId <= 0)
                throw new ArgumentException("zaehlerId muss > 0 sein.", nameof(zaehlerId));

            using var c = CreateConnection();
            c.Open();

            using var tx = c.BeginTransaction();

            try
            {
                // 1) Abhängige Zählerdaten-Lines löschen
                using (var cmd = c.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
DELETE FROM dbo.StweZaehlerdatenLine
WHERE ZaehlerId = @id;";
                    cmd.Parameters.AddWithValue("@id", zaehlerId);
                    cmd.ExecuteNonQuery();
                }


                // 0) Zähler-Verteilzeilen löschen
                using (var cmd = c.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM dbo.StweZaehlerLine WHERE ZaehlerId = @id;";
                    cmd.Parameters.AddWithValue("@id", zaehlerId);
                    cmd.ExecuteNonQuery();
                }




                // Optional/defensiv: falls es weitere Tabellen gibt, die direkt auf ZaehlerId zeigen,
                // dann hier ebenfalls vor dem Stamm löschen.

                // 2) Zähler-Stamm löschen
                using (var cmd = c.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
DELETE FROM dbo.StweZaehler
WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@id", zaehlerId);

                    var affected = cmd.ExecuteNonQuery();
                    if (affected != 1)
                        throw new InvalidOperationException($"Zähler (Id={zaehlerId}) konnte nicht gelöscht werden (affected={affected}).");
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { /* ignore */ }
                throw;
            }
        }


        public List<(int ZaehlerId, decimal AltKwh, decimal NeuKwh)> StweEnergieZaehlerGetBySet(int setId)
        {
            EnsureStweSchema();

            var list = new List<(int, decimal, decimal)>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT ZaehlerId, AltKwh, NeuKwh
FROM dbo.StweEnergieSetZaehler
WHERE SetId = @sid
ORDER BY ZaehlerId;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@sid", setId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((r.GetInt32(0), r.GetDecimal(1), r.GetDecimal(2)));
            }

            return list;
        }

        public Dictionary<int, decimal> StweEnergieLastNeuStaendeGet(int liegenschaftId, DateTime stichtag)
        {
            EnsureStweSchema();

            var result = new Dictionary<int, decimal>();
            using var c = CreateConnection();
            c.Open();

            // Letzter erfasster Neu-KWh Stand je Zähler (nur aus Sets derselben Liegenschaft),
            // aber nur aus Sets, deren Transaktionsdatum vor dem Stichtag liegt.
            // Damit füllen wir bei einer neuen Rechnung automatisch "Alt" vor.
            const string sql = @"
;WITH x AS
(
    SELECT
        ez.ZaehlerId,
        ez.NeuKwh,
        t.Datum,
        ROW_NUMBER() OVER (PARTITION BY ez.ZaehlerId ORDER BY t.Datum DESC, s.Id DESC) AS rn
    FROM dbo.StweEnergieSetZaehler ez
    JOIN dbo.StweSet s         ON s.Id = ez.SetId
    JOIN dbo.Transaktion t     ON t.Id = s.TransaktionId
    WHERE s.LiegenschaftId = @lid
      AND t.Datum < @d
)
SELECT ZaehlerId, NeuKwh
FROM x
WHERE rn = 1
ORDER BY ZaehlerId;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@lid", liegenschaftId);
            cmd.Parameters.AddWithValue("@d", stichtag.Date);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var zaehlerId = r.GetInt32(0);
                var neuKwh = r.GetDecimal(1);

                // Defensive: pro ZaehlerId nur einmal.
                if (!result.ContainsKey(zaehlerId))
                    result.Add(zaehlerId, neuKwh);
            }

            return result;
        }



        public void StweEnergieZaehlerReplace(int setId, List<(int ZaehlerId, decimal AltKwh, decimal NeuKwh)> rows)
        {
            EnsureStweSchema();

            if (setId <= 0) throw new ArgumentOutOfRangeException(nameof(setId));
            rows ??= new();

            using var c = CreateConnection();
            c.Open();
            using var tx = c.BeginTransaction();

            try
            {
                using (var del = c.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM dbo.StweEnergieSetZaehler WHERE SetId = @sid;";
                    del.Parameters.AddWithValue("@sid", setId);
                    del.ExecuteNonQuery();
                }

                foreach (var (zaehlerId, alt, neu) in rows)
                {
                    using var ins = c.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = @"
INSERT INTO dbo.StweEnergieSetZaehler (SetId, ZaehlerId, AltKwh, NeuKwh)
VALUES (@sid, @zid, @a, @n);";

                    ins.Parameters.AddWithValue("@sid", setId);
                    ins.Parameters.AddWithValue("@zid", zaehlerId);
                    ins.Parameters.AddWithValue("@a", alt);
                    ins.Parameters.AddWithValue("@n", neu);

                    ins.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public (decimal? EvuKwh, decimal? PvGutschriftChf, int? PvKontoId, string? Notiz) StweEnergieMetaGet(int setId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT EvuKwh, PvGutschriftChf, PvKontoId, Notiz
FROM dbo.StweEnergieSetMeta
WHERE SetId = @sid;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@sid", setId);

            using var r = cmd.ExecuteReader();
            if (!r.Read())
                return (null, null, null, null);

            return (
                r.IsDBNull(0) ? (decimal?)null : r.GetDecimal(0),
                r.IsDBNull(1) ? (decimal?)null : r.GetDecimal(1),
                r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                r.IsDBNull(3) ? null : r.GetString(3)
            );
        }

        public void StweEnergieMetaUpsert(int setId, decimal? evuKwh, decimal? pvGutschriftChf, int? pvKontoId, string? notiz)
        {
            EnsureStweSchema();

            if (setId <= 0) throw new ArgumentOutOfRangeException(nameof(setId));

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.StweEnergieSetMeta WHERE SetId = @sid)
BEGIN
    UPDATE dbo.StweEnergieSetMeta SET
        EvuKwh          = @e,
        PvGutschriftChf = @p,
        PvKontoId       = @k,
        Notiz           = @n,
        UpdatedAtUtc    = SYSUTCDATETIME()
    WHERE SetId = @sid;
END
ELSE
BEGIN
    INSERT INTO dbo.StweEnergieSetMeta (SetId, EvuKwh, PvGutschriftChf, PvKontoId, Notiz)
    VALUES (@sid, @e, @p, @k, @n);
END;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@sid", setId);
            cmd.Parameters.AddWithValue("@e", (object?)evuKwh ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@p", (object?)pvGutschriftChf ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@k", (object?)pvKontoId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@n", (object?)notiz ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }






        public void StweSetLineInsert(int setId, int? einheitId, int? eigentuemerId, string schluessel, decimal betrag, string? notiz = null)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.StweSetLine (SetId, EinheitId, EigentuemerId, Schluessel, Betrag, Notiz)
VALUES (@sid, @eid, @oid, @s, @b, @n);";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var p1 = cmd.CreateParameter(); p1.ParameterName = "@sid"; p1.Value = setId; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "@eid"; p2.Value = (object?)einheitId ?? DBNull.Value; cmd.Parameters.Add(p2);
            var p3 = cmd.CreateParameter(); p3.ParameterName = "@oid"; p3.Value = (object?)eigentuemerId ?? DBNull.Value; cmd.Parameters.Add(p3);
            var p4 = cmd.CreateParameter(); p4.ParameterName = "@s"; p4.Value = schluessel; cmd.Parameters.Add(p4);
            var p5 = cmd.CreateParameter(); p5.ParameterName = "@b"; p5.Value = betrag; cmd.Parameters.Add(p5);
            var p6 = cmd.CreateParameter(); p6.ParameterName = "@n"; p6.Value = (object?)notiz ?? DBNull.Value; cmd.Parameters.Add(p6);

            cmd.ExecuteNonQuery();
        }

        public int? StweEigentuemerGetByEinheitAtDate(int einheitId, DateTime stichtag)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT TOP 1 EigentuemerId
FROM dbo.StweEinheitEigentum
WHERE EinheitId = @eid
  AND GueltigVon <= @d
  AND (GueltigBis IS NULL OR GueltigBis >= @d)
ORDER BY GueltigVon DESC, Id DESC;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var p1 = cmd.CreateParameter(); p1.ParameterName = "@eid"; p1.Value = einheitId; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "@d"; p2.Value = stichtag.Date; cmd.Parameters.Add(p2);

            var obj = cmd.ExecuteScalar();
            if (obj == null || obj == DBNull.Value) return null;

            return Convert.ToInt32(obj);
        }

        public void StweSetSetClosed(int setId, bool isClosed)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE dbo.StweSet SET IsClosed = @c WHERE Id = @id;";
            var p1 = cmd.CreateParameter(); p1.ParameterName = "@c"; p1.Value = isClosed ? 1 : 0; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "@id"; p2.Value = setId; cmd.Parameters.Add(p2);

            cmd.ExecuteNonQuery();
        }

        public void StweSetSetIsCredit(int setId, bool isCredit)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            // Defensive: Closed schützt auch DB-seitig
            using (var chk = c.CreateCommand())
            {
                chk.CommandText = "SELECT IsClosed FROM dbo.StweSet WHERE Id=@id;";
                chk.Parameters.AddWithValue("@id", setId);
                var v = chk.ExecuteScalar();
                if (v != null && v != DBNull.Value && Convert.ToBoolean(v))
                {
                    System.Windows.MessageBox.Show(
                        "Dieses Set ist geschlossen. Bitte zuerst „Wieder öffnen“.",
                        "Set-Typ ändern nicht möglich",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }
            }

            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE dbo.StweSet SET IsCredit = @x WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", setId);
            cmd.Parameters.AddWithValue("@x", isCredit ? 1 : 0);
            cmd.ExecuteNonQuery();
        }




        public void StweSetUpdateTitel(int setId, string? titel)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            // Defensive: Closed-Schutz auch DB-seitig
            using (var chk = c.CreateCommand())
            {
                chk.CommandText = "SELECT IsClosed FROM dbo.StweSet WHERE Id=@id;";
                chk.Parameters.AddWithValue("@id", setId);
                var v = chk.ExecuteScalar();
                if (v == null || v == DBNull.Value) return;

                if (Convert.ToBoolean(v))
                {
                    System.Windows.MessageBox.Show(
                        "Dieses Set ist geschlossen. Bitte zuerst „Wieder öffnen“.",
                        "Titel ändern nicht möglich",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }
            }

            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE dbo.StweSet SET Titel=@t WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", setId);
            cmd.Parameters.AddWithValue("@t", (object?)titel ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public void StweSetDelete(int setId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            // Defensive: Closed-Schutz auch DB-seitig
            using (var chk = c.CreateCommand())
            {
                chk.CommandText = "SELECT IsClosed FROM dbo.StweSet WHERE Id=@id;";
                chk.Parameters.AddWithValue("@id", setId);
                var v = chk.ExecuteScalar();
                if (v == null || v == DBNull.Value) return;

                if (Convert.ToBoolean(v))
                {
                    System.Windows.MessageBox.Show(
                        "Dieses Set ist geschlossen. Bitte zuerst „Wieder öffnen“.",
                        "Löschen nicht möglich",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }
            }

            using var tx = c.BeginTransaction();
            try
            {
                using (var delLines = c.CreateCommand())
                {
                    delLines.Transaction = tx;
                    delLines.CommandText = "DELETE FROM dbo.StweSetLine WHERE SetId=@id;";
                    delLines.Parameters.AddWithValue("@id", setId);
                    delLines.ExecuteNonQuery();
                }

                using (var delSet = c.CreateCommand())
                {
                    delSet.Transaction = tx;
                    delSet.CommandText = "DELETE FROM dbo.StweSet WHERE Id=@id;";
                    delSet.Parameters.AddWithValue("@id", setId);
                    delSet.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }

                if (HandleSqlDeleteException(ex, "Set")) return;

                System.Windows.MessageBox.Show(
                    "Set konnte nicht gelöscht werden:\n" + ex.Message,
                    "Löschen fehlgeschlagen",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }


        public List<MyCoinFlow.Models.StweOwnerSummaryRow> StweReportOwnerSummary(
int liegenschaftId, DateTime? von, DateTime? bis)
        {
            EnsureStweSchema();

            var list = new List<MyCoinFlow.Models.StweOwnerSummaryRow>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    o.Id AS EigentuemerId,
    o.Name AS EigentuemerName,

    -- WICHTIG: Vorzeichen-Normalisierung über IsCredit
    SUM(
        CASE 
            WHEN ISNULL(s.IsCredit, 0) = 1 
                THEN -ABS(l.Betrag)
            ELSE ABS(l.Betrag)
        END
    ) AS Summe

FROM dbo.StweSetLine l
JOIN dbo.StweSet s           ON s.Id = l.SetId
JOIN dbo.Transaktion t       ON t.Id = s.TransaktionId
JOIN dbo.StweEigentuemer o   ON o.Id = l.EigentuemerId

WHERE s.LiegenschaftId = @lid
  AND (@von IS NULL OR ISNULL(t.BudgetDatum, t.Datum) >= @von)
  AND (@bis IS NULL OR ISNULL(t.BudgetDatum, t.Datum) <= @bis)

GROUP BY o.Id, o.Name
ORDER BY o.Name;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var p1 = cmd.CreateParameter(); p1.ParameterName = "@lid"; p1.Value = liegenschaftId; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "@von"; p2.Value = (object?)von?.Date ?? DBNull.Value; cmd.Parameters.Add(p2);
            var p3 = cmd.CreateParameter(); p3.ParameterName = "@bis"; p3.Value = (object?)bis?.Date ?? DBNull.Value; cmd.Parameters.Add(p3);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.StweOwnerSummaryRow
                {
                    EigentuemerId = r.GetInt32(0),
                    EigentuemerName = r.GetString(1),
                    Summe = r.IsDBNull(2) ? 0m : r.GetDecimal(2)
                });
            }

            return list;
        }

        public List<MyCoinFlow.Models.StweOwnerSummaryRow> StweReportOwnerSummaryNr2Ausgaben(
    int liegenschaftId, DateTime? von, DateTime? bis)
        {
            EnsureStweSchema();

            var list = new List<MyCoinFlow.Models.StweOwnerSummaryRow>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
    SELECT
        o.Id AS EigentuemerId,
        o.Name AS EigentuemerName,

        -- Vorzeichen-Korrektur (identisch zur anderen Methode)
        SUM(
            CASE 
                WHEN ISNULL(s.IsCredit, 0) = 1 
                    THEN CASE 
                            WHEN l.Betrag < 0 THEN l.Betrag 
                            ELSE -l.Betrag 
                         END
                ELSE CASE 
                        WHEN l.Betrag < 0 THEN -l.Betrag 
                        ELSE l.Betrag 
                     END
            END
        ) AS Summe

    FROM dbo.StweSetLine l
    JOIN dbo.StweSet s           ON s.Id = l.SetId
    JOIN dbo.Transaktion t       ON t.Id = s.TransaktionId
    JOIN dbo.StweEigentuemer o   ON o.Id = l.EigentuemerId
    LEFT JOIN dbo.Kontenplan kv  ON kv.Id = t.VonKontoId
    LEFT JOIN dbo.Kontenplan kn  ON kn.Id = t.NachKontoId

    WHERE s.LiegenschaftId = @lid
      AND (@von IS NULL OR t.Datum >= @von)
      AND (@bis IS NULL OR t.Datum <= @bis)
      AND (
            (kn.Kontonummer BETWEEN @kStart AND @kEnd)
         OR (kv.Kontonummer BETWEEN @kStart AND @kEnd)
      )

    GROUP BY o.Id, o.Name
    ORDER BY o.Name;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var p1 = cmd.CreateParameter(); p1.ParameterName = "@lid"; p1.Value = liegenschaftId; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "@von"; p2.Value = (object?)von?.Date ?? DBNull.Value; cmd.Parameters.Add(p2);
            var p3 = cmd.CreateParameter(); p3.ParameterName = "@bis"; p3.Value = (object?)bis?.Date ?? DBNull.Value; cmd.Parameters.Add(p3);

            var p4 = cmd.CreateParameter(); p4.ParameterName = "@kStart"; p4.Value = 20000; cmd.Parameters.Add(p4);
            var p5 = cmd.CreateParameter(); p5.ParameterName = "@kEnd"; p5.Value = 29999; cmd.Parameters.Add(p5);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.StweOwnerSummaryRow
                {
                    EigentuemerId = r.GetInt32(0),
                    EigentuemerName = r.GetString(1),
                    Summe = r.IsDBNull(2) ? 0m : r.GetDecimal(2)
                });
            }

            return list;
        }


        public List<MyCoinFlow.Models.StweOwnerDetailRow> StweReportOwnerDetails(
int liegenschaftId, int eigentuemerId, DateTime? von, DateTime? bis)
        {
            EnsureStweSchema();

            var list = new List<MyCoinFlow.Models.StweOwnerDetailRow>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    t.Datum,
    s.Id AS SetId,
    COALESCE(NULLIF(s.Titel,''), COALESCE(NULLIF(t.Notiz,''),'(ohne Text)')) AS Titel,
    l.Schluessel,
    l.Notiz,

    CASE 
        WHEN ISNULL(s.IsCredit, 0) = 1 
            THEN CASE 
                    WHEN l.Betrag < 0 THEN l.Betrag 
                    ELSE -l.Betrag 
                 END
        ELSE CASE 
                WHEN l.Betrag < 0 THEN -l.Betrag 
                ELSE l.Betrag 
             END
    END AS Betrag

FROM dbo.StweSetLine l
JOIN dbo.StweSet s      ON s.Id = l.SetId
JOIN dbo.Transaktion t  ON t.Id = s.TransaktionId

WHERE s.LiegenschaftId = @lid
  AND l.EigentuemerId = @oid
  AND (@von IS NULL OR ISNULL(t.BudgetDatum, t.Datum) >= @von)
  AND (@bis IS NULL OR ISNULL(t.BudgetDatum, t.Datum) <= @bis)

ORDER BY t.Datum DESC, s.Id DESC, l.Id ASC;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            var p1 = cmd.CreateParameter(); p1.ParameterName = "@lid"; p1.Value = liegenschaftId; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "@oid"; p2.Value = eigentuemerId; cmd.Parameters.Add(p2);
            var p3 = cmd.CreateParameter(); p3.ParameterName = "@von"; p3.Value = (object?)von?.Date ?? DBNull.Value; cmd.Parameters.Add(p3);
            var p4 = cmd.CreateParameter(); p4.ParameterName = "@bis"; p4.Value = (object?)bis?.Date ?? DBNull.Value; cmd.Parameters.Add(p4);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.StweOwnerDetailRow
                {
                    Datum = r.GetDateTime(0),
                    SetId = r.GetInt32(1),
                    Titel = r.GetString(2),
                    Schluessel = r.IsDBNull(3) ? null : r.GetString(3),
                    Notiz = r.IsDBNull(4) ? null : r.GetString(4),
                    Betrag = r.IsDBNull(5) ? 0m : r.GetDecimal(5)
                });
            }

            return list;
        }


        public List<StweOriginalTransaktionRow> StweReportOriginalTransaktionen(int liegenschaftId, DateTime? von, DateTime? bis)
        {
            EnsureStweSchema();

            var list = new List<StweOriginalTransaktionRow>();

            const string sql = @"
SELECT DISTINCT
    t.Id            AS TransaktionsId,
    t.Datum         AS Datum,
    CASE WHEN ISNULL(s.IsCredit, 0) = 1 THEN -t.Betrag ELSE t.Betrag END AS BetragSigned,
    t.Notiz         AS Notiz
FROM dbo.StweSet s
INNER JOIN dbo.Transaktion t ON t.Id = s.TransaktionId
WHERE s.LiegenschaftId = @LiegenschaftId
  AND (@Von IS NULL OR ISNULL(t.BudgetDatum, t.Datum) >= @Von)
  AND (@Bis IS NULL OR ISNULL(t.BudgetDatum, t.Datum) <= @Bis)
ORDER BY t.Datum DESC, t.Id DESC;";

            using var con = new SqlConnection(_connectionString);
            con.Open();

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@LiegenschaftId", liegenschaftId);
            cmd.Parameters.AddWithValue("@Von", (object?)von?.Date ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Bis", (object?)bis?.Date ?? DBNull.Value);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new StweOriginalTransaktionRow
                {
                    TransaktionsId = r.GetInt32(0),
                    Datum = r.GetDateTime(1),
                    Betrag = r.GetDecimal(2),
                    Notiz = r.IsDBNull(3) ? null : r.GetString(3)
                });
            }

            return list;
        }

        public void StweLiegenschaftUpdate(MyCoinFlow.Models.StweLiegenschaft l)
        {
            if (l == null) throw new ArgumentNullException(nameof(l));
            if (l.Id <= 0) throw new ArgumentException("Id fehlt.", nameof(l));
            if (string.IsNullOrWhiteSpace(l.Name))
                throw new ArgumentException("Name darf nicht leer sein.", nameof(l));

            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.StweLiegenschaft SET
    Name    = @n,
    Strasse = @s,
    PLZ     = @p,
    Ort     = @o,
    Notiz   = @no
WHERE Id = @id;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            cmd.Parameters.AddWithValue("@id", l.Id);
            cmd.Parameters.AddWithValue("@n", l.Name.Trim());
            cmd.Parameters.AddWithValue("@s", (object?)l.Strasse ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@p", (object?)l.PLZ ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@o", (object?)l.Ort ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@no", (object?)l.Notiz ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public bool StweLiegenschaftHasSets(int liegenschaftId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT TOP(1) 1
FROM dbo.StweSet
WHERE LiegenschaftId = @id;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", liegenschaftId);

            var v = cmd.ExecuteScalar();
            return v != null && v != DBNull.Value;
        }

        public void StweLiegenschaftDelete(int liegenschaftId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            // HARTE REGEL: Löschen nur wenn keine Sets existieren
            if (StweLiegenschaftHasSets(liegenschaftId))
            {
                System.Windows.MessageBox.Show(
                    "Diese Liegenschaft kann nicht gelöscht werden,\n" +
                    "weil bereits Sets (STWE-Aufteilungen) existieren.",
                    "Löschen nicht möglich",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            try
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "DELETE FROM dbo.StweLiegenschaft WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", liegenschaftId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                if (HandleSqlDeleteException(ex, "Liegenschaft"))
                    return;

                System.Windows.MessageBox.Show(
                    "Liegenschaft konnte nicht gelöscht werden:\n" + ex.Message,
                    "Löschen fehlgeschlagen",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
        public void StweEinheitUpdate(MyCoinFlow.Models.StweEinheit e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (e.Id <= 0) throw new ArgumentException("Id fehlt.", nameof(e));
            if (e.LiegenschaftId <= 0) throw new ArgumentException("LiegenschaftId fehlt.", nameof(e));
            if (string.IsNullOrWhiteSpace(e.Bezeichnung)) throw new ArgumentException("Bezeichnung fehlt.", nameof(e));

            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.StweEinheit SET
    LiegenschaftId = @lid,
    Bezeichnung    = @b,
    Typ            = @t,
    MeaPromille    = @m,
    FlaecheM2      = @f,
    Notiz          = @n
WHERE Id = @id;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            cmd.Parameters.AddWithValue("@id", e.Id);
            cmd.Parameters.AddWithValue("@lid", e.LiegenschaftId);
            cmd.Parameters.AddWithValue("@b", e.Bezeichnung.Trim());
            cmd.Parameters.AddWithValue("@t", (object?)e.Typ ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@m", (object?)e.MeaPromille ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@f", (object?)e.FlaecheM2 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@n", (object?)e.Notiz ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public bool StweEinheitHasEigentum(int einheitId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"SELECT TOP(1) 1 FROM dbo.StweEinheitEigentum WHERE EinheitId = @id;";
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", einheitId);

            var v = cmd.ExecuteScalar();
            return v != null && v != DBNull.Value;
        }

        public bool StweEinheitUsedInSetLines(int einheitId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"SELECT TOP(1) 1 FROM dbo.StweSetLine WHERE EinheitId = @id;";
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", einheitId);

            var v = cmd.ExecuteScalar();
            return v != null && v != DBNull.Value;
        }

        public void StweEinheitDelete(int einheitId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            // HARTE REGELN:
            // 1) Keine Eigentums-Zuordnungen
            if (StweEinheitHasEigentum(einheitId))
            {
                System.Windows.MessageBox.Show(
                    "Diese Einheit kann nicht gelöscht werden,\n" +
                    "weil noch Eigentums-Zuordnungen existieren.",
                    "Löschen nicht möglich",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            // 2) Nicht in STWE-Sets verwendet
            if (StweEinheitUsedInSetLines(einheitId))
            {
                System.Windows.MessageBox.Show(
                    "Diese Einheit kann nicht gelöscht werden,\n" +
                    "weil sie in STWE-Sets (SetLines) verwendet wird.",
                    "Löschen nicht möglich",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            try
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "DELETE FROM dbo.StweEinheit WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", einheitId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                if (HandleSqlDeleteException(ex, "Einheit")) return;

                System.Windows.MessageBox.Show(
                    "Einheit konnte nicht gelöscht werden:\n" + ex.Message,
                    "Löschen fehlgeschlagen",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        public void StweEigentuemerUpdate(MyCoinFlow.Models.StweEigentuemer e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (e.Id <= 0) throw new ArgumentException("Id fehlt.", nameof(e));
            if (string.IsNullOrWhiteSpace(e.Name))
                throw new ArgumentException("Name darf nicht leer sein.", nameof(e));

            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.StweEigentuemer SET
    Name    = @n,
    Email   = @em,
    Telefon = @tel,
    Notiz   = @no
WHERE Id = @id;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            cmd.Parameters.AddWithValue("@id", e.Id);
            cmd.Parameters.AddWithValue("@n", e.Name.Trim());
            cmd.Parameters.AddWithValue("@em", (object?)e.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tel", (object?)e.Telefon ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@no", (object?)e.Notiz ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public bool StweEigentuemerHasEigentumZuordnungen(int eigentuemerId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"SELECT TOP(1) 1 FROM dbo.StweEinheitEigentum WHERE EigentuemerId = @id;";
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", eigentuemerId);

            var v = cmd.ExecuteScalar();
            return v != null && v != DBNull.Value;
        }

        public bool StweEigentuemerUsedInSetLines(int eigentuemerId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"SELECT TOP(1) 1 FROM dbo.StweSetLine WHERE EigentuemerId = @id;";
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", eigentuemerId);

            var v = cmd.ExecuteScalar();
            return v != null && v != DBNull.Value;
        }

        public void StweEigentuemerDelete(int eigentuemerId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            // HARTE REGEL 1: keine Zuordnungen
            if (StweEigentuemerHasEigentumZuordnungen(eigentuemerId))
            {
                System.Windows.MessageBox.Show(
                    "Dieser Eigentümer kann nicht gelöscht werden,\n" +
                    "weil noch Eigentums-Zuordnungen existieren.",
                    "Löschen nicht möglich",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            // HARTE REGEL 2: nicht in Sets verwendet
            if (StweEigentuemerUsedInSetLines(eigentuemerId))
            {
                System.Windows.MessageBox.Show(
                    "Dieser Eigentümer kann nicht gelöscht werden,\n" +
                    "weil er in STWE-Sets (SetLines) verwendet wird.",
                    "Löschen nicht möglich",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            try
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "DELETE FROM dbo.StweEigentuemer WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", eigentuemerId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                if (HandleSqlDeleteException(ex, "Eigentümer")) return;

                System.Windows.MessageBox.Show(
                    "Eigentümer konnte nicht gelöscht werden:\n" + ex.Message,
                    "Löschen fehlgeschlagen",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        public void StweSchluesselDelete(int schluesselId)
        {
            EnsureStweSchema();

            if (schluesselId <= 0)
                throw new ArgumentException("schluesselId muss > 0 sein.", nameof(schluesselId));

            using var c = CreateConnection();
            c.Open();

            using var tx = c.BeginTransaction();

            try
            {
                // 1) Prüfen, ob dieser Schlüssel bei Zählern verwendet wird (nur wenn Spalte existiert)
                using (var cmd = c.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
IF COL_LENGTH('dbo.StweZaehler', 'SchluesselId') IS NOT NULL
BEGIN
    SELECT COUNT(1) FROM dbo.StweZaehler WHERE SchluesselId = @sid;
END
ELSE
BEGIN
    SELECT 0;
END";
                    var p = cmd.CreateParameter(); p.ParameterName = "@sid"; p.Value = schluesselId; cmd.Parameters.Add(p);

                    var usedCount = Convert.ToInt32(cmd.ExecuteScalar());
                    if (usedCount > 0)
                        throw new InvalidOperationException("Dieser Schlüssel kann nicht gelöscht werden, weil mindestens ein Zähler darauf verweist. Bitte zuerst beim Zähler einen anderen Schlüssel wählen.");
                }

                // 2) Schlüsselzeilen löschen (FK-Abhängigkeit)
                using (var cmd = c.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM dbo.StweSchluesselLine WHERE SchluesselId = @sid;";
                    var p = cmd.CreateParameter(); p.ParameterName = "@sid"; p.Value = schluesselId; cmd.Parameters.Add(p);
                    cmd.ExecuteNonQuery();
                }

                // 3) Schlüssel selbst löschen
                using (var cmd = c.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM dbo.StweSchluessel WHERE Id = @sid;";
                    var p = cmd.CreateParameter(); p.ParameterName = "@sid"; p.Value = schluesselId; cmd.Parameters.Add(p);

                    var affected = cmd.ExecuteNonQuery();
                    if (affected != 1)
                        throw new InvalidOperationException("Schlüssel konnte nicht gelöscht werden (nicht gefunden oder bereits gelöscht).");
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { /* ignore */ }
                throw;
            }
        }


        public void StweSchluesselRename(int schluesselId, string newName)
        {
            EnsureStweSchema();

            if (schluesselId <= 0) throw new ArgumentOutOfRangeException(nameof(schluesselId));
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("Name darf nicht leer sein.", nameof(newName));

            using var c = CreateConnection();
            c.Open();
            using var tx = c.BeginTransaction();

            try
            {
                // 1) alten Namen + LiegenschaftId holen
                int liegenschaftId;
                string oldName;

                using (var cmd = c.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT LiegenschaftId, Name FROM dbo.StweSchluessel WHERE Id=@id;";
                    cmd.Parameters.AddWithValue("@id", schluesselId);

                    using var r = cmd.ExecuteReader();
                    if (!r.Read())
                    {
                        System.Windows.MessageBox.Show("Schlüssel wurde nicht gefunden.",
                            "Umbenennen nicht möglich",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                        tx.Rollback();
                        return;
                    }

                    liegenschaftId = r.GetInt32(0);
                    oldName = r.GetString(1);
                }

                var trimmed = newName.Trim();

                // 2) Duplikat-Check innerhalb derselben Liegenschaft
                using (var chk = c.CreateCommand())
                {
                    chk.Transaction = tx;
                    chk.CommandText = @"
SELECT TOP(1) 1
FROM dbo.StweSchluessel
WHERE LiegenschaftId = @lid
  AND UPPER(LTRIM(RTRIM(Name))) = UPPER(LTRIM(RTRIM(@n)))
  AND Id <> @id;";
                    chk.Parameters.AddWithValue("@lid", liegenschaftId);
                    chk.Parameters.AddWithValue("@n", trimmed);
                    chk.Parameters.AddWithValue("@id", schluesselId);

                    var v = chk.ExecuteScalar();
                    if (v != null && v != DBNull.Value)
                    {
                        System.Windows.MessageBox.Show(
                            "Ein Schlüssel mit dieser Bezeichnung existiert bereits in dieser Liegenschaft.",
                            "Umbenennen nicht möglich",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                        tx.Rollback();
                        return;
                    }
                }

                // 3) Schlüsselstamm umbenennen
                using (var upd = c.CreateCommand())
                {
                    upd.Transaction = tx;
                    upd.CommandText = "UPDATE dbo.StweSchluessel SET Name=@n WHERE Id=@id;";
                    upd.Parameters.AddWithValue("@n", trimmed);
                    upd.Parameters.AddWithValue("@id", schluesselId);
                    upd.ExecuteNonQuery();
                }

                // 4) Optional: bereits gespeicherte SetLines mit altem Schlüsseltext mitziehen
                // (weil StweSetLine.Schluessel ein Textfeld ist und oft den Schlüssel-Namen enthält)
                using (var upd2 = c.CreateCommand())
                {
                    upd2.Transaction = tx;
                    upd2.CommandText = @"
UPDATE l
SET l.Schluessel = @new
FROM dbo.StweSetLine l
JOIN dbo.StweSet s ON s.Id = l.SetId
WHERE s.LiegenschaftId = @lid
  AND l.Schluessel = @old;";
                    upd2.Parameters.AddWithValue("@new", trimmed);
                    upd2.Parameters.AddWithValue("@lid", liegenschaftId);
                    upd2.Parameters.AddWithValue("@old", oldName);
                    upd2.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public bool StweSetGetIsCredit(int setId)
        {
            EnsureStweSchema();
            using var c = CreateConnection();
            c.Open();

            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT IsCredit FROM dbo.StweSet WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", setId);
            var v = cmd.ExecuteScalar();
            return v != null && v != DBNull.Value && Convert.ToBoolean(v);
        }

        public void StweSetFlipCreditAndLines(int setId, bool isCredit)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            // Defensive: Closed schützen
            using (var chk = c.CreateCommand())
            {
                chk.CommandText = "SELECT IsClosed FROM dbo.StweSet WHERE Id=@id;";
                chk.Parameters.AddWithValue("@id", setId);
                var v = chk.ExecuteScalar();
                if (v != null && v != DBNull.Value && Convert.ToBoolean(v))
                {
                    System.Windows.MessageBox.Show(
                        "Dieses Set ist geschlossen. Bitte zuerst „Wieder öffnen“.",
                        "Set-Typ ändern nicht möglich",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }
            }

            using var tx = c.BeginTransaction();
            try
            {
                // 1) Set-Typ setzen
                using (var upd = c.CreateCommand())
                {
                    upd.Transaction = tx;
                    upd.CommandText = "UPDATE dbo.StweSet SET IsCredit = @x WHERE Id = @id;";
                    upd.Parameters.AddWithValue("@id", setId);
                    upd.Parameters.AddWithValue("@x", isCredit ? 1 : 0);
                    upd.ExecuteNonQuery();
                }

                // 2) Falls bereits Zeilen existieren -> Beträge spiegeln
                using (var flip = c.CreateCommand())
                {
                    flip.Transaction = tx;
                    flip.CommandText = @"
UPDATE dbo.StweSetLine
SET Betrag = -Betrag
WHERE SetId = @id;";
                    flip.Parameters.AddWithValue("@id", setId);
                    flip.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }

                System.Windows.MessageBox.Show(
                    "Set-Typ konnte nicht geändert werden:\n" + ex.Message,
                    "Fehler",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        public List<MyCoinFlow.Models.StweSetRow> StweSetsGetByLiegenschaft(int liegenschaftId, DateTime? von, DateTime? bis)
        {
            EnsureStweSchema();

            var list = new List<MyCoinFlow.Models.StweSetRow>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT 
    s.Id,
    s.LiegenschaftId,
    s.TransaktionId,
    t.Datum,
    t.BudgetDatum,

    -- SIGNED Total (Belastung = +, Gutschrift = -)
    CASE WHEN ISNULL(s.IsCredit, 0) = 1 THEN -t.Betrag ELSE t.Betrag END AS BetragSigned,

    COALESCE(NULLIF(s.Titel,''), COALESCE(NULLIF(t.Notiz,''),'(ohne Text)')) AS Titel,
    s.IsClosed,
    ISNULL(s.IsCredit, 0) AS IsCredit,

    ISNULL(x.Verteilt, 0) AS Verteilt,

    (CASE WHEN ISNULL(s.IsCredit, 0) = 1 THEN -t.Betrag ELSE t.Betrag END) - ISNULL(x.Verteilt, 0) AS Rest
FROM dbo.StweSet s
JOIN dbo.Transaktion t ON t.Id = s.TransaktionId
OUTER APPLY (
    SELECT SUM(l.Betrag) AS Verteilt
    FROM dbo.StweSetLine l
    WHERE l.SetId = s.Id
) x
WHERE s.LiegenschaftId = @lid
  AND (@von IS NULL OR ISNULL(t.BudgetDatum, t.Datum) >= @von)
  AND (@bis IS NULL OR ISNULL(t.BudgetDatum, t.Datum) <= @bis)
ORDER BY ISNULL(t.BudgetDatum, t.Datum) DESC, s.Id DESC;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            cmd.Parameters.AddWithValue("@lid", liegenschaftId);
            cmd.Parameters.AddWithValue("@von", (object?)von?.Date ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bis", (object?)bis?.Date ?? DBNull.Value);

            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.StweSetRow
                {
                    Id = r.GetInt32(0),
                    LiegenschaftId = r.GetInt32(1),
                    TransaktionId = r.GetInt32(2),
                    Datum = r.GetDateTime(3),
                    BudgetDatum = r.IsDBNull(4) ? (DateTime?)null : r.GetDateTime(4),
                    Betrag = r.GetDecimal(5),
                    Titel = r.GetString(6),
                    IsClosed = r.GetBoolean(7),
                    IsCredit = r.GetBoolean(8),
                    Verteilt = r.GetDecimal(9),
                    Rest = r.GetDecimal(10)
                });
            }

            return list;
        }




        // ------------------------------------------------------------
        // STWE: Zählerdaten-Sets (Ablesungen)
        // ------------------------------------------------------------

        public List<StweZaehlerdatenSet> StweZaehlerdatenSetsGetByLiegenschaft(int liegenschaftId)
        {
            EnsureStweSchema();

            var list = new List<StweZaehlerdatenSet>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
        SELECT Id, LiegenschaftId, ErfasstAm, RechnungKwhTotal, GutschriftChf, RueckgespeistKwh, Notiz, ErfassungsTyp, MonatsAnzahl
        FROM dbo.StweZaehlerdatenSet
        WHERE LiegenschaftId = @lid
        ORDER BY ErfasstAm DESC, Id DESC;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@lid", liegenschaftId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new StweZaehlerdatenSet
                {
                    Id = r.GetInt32(0),
                    LiegenschaftId = r.GetInt32(1),
                    ErfasstAm = r.GetDateTime(2),
                    RechnungKwhTotal = r.IsDBNull(3) ? (decimal?)null : r.GetDecimal(3),
                    GutschriftChf = r.IsDBNull(4) ? (decimal?)null : r.GetDecimal(4),
                    RueckgespeistKwh = r.IsDBNull(5) ? (decimal?)null : r.GetDecimal(5),
                    Notiz = r.IsDBNull(6) ? null : r.GetString(6),
                    ErfassungsTyp = r.IsDBNull(7) ? 0 : r.GetInt32(7),
                    MonatsAnzahl = r.IsDBNull(8) ? (int?)null : r.GetInt32(8)
                });
            }

            return list;
        }

        public int StweZaehlerdatenSetInsert(StweZaehlerdatenSet m)
        {
            EnsureStweSchema();

            if (m == null) throw new ArgumentNullException(nameof(m));
            if (m.LiegenschaftId <= 0) throw new ArgumentException("LiegenschaftId fehlt.", nameof(m));
            if (m.ErfasstAm == default) throw new ArgumentException("ErfasstAm fehlt.", nameof(m));

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.StweZaehlerdatenSet
(
    LiegenschaftId,
    ErfasstAm,
    RechnungKwhTotal,
    GutschriftChf,
    RueckgespeistKwh,
    Notiz,
    ErfassungsTyp,
    MonatsAnzahl
)
OUTPUT INSERTED.Id
VALUES
(
    @lid,
    @am,
    @rk,
    @gc,
    @rkwh,
    @n,
    @typ,
    @ma
);";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@lid", m.LiegenschaftId);
            cmd.Parameters.AddWithValue("@am", m.ErfasstAm);
            cmd.Parameters.AddWithValue("@rk", (object?)m.RechnungKwhTotal ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gc", (object?)m.GutschriftChf ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rkwh", (object?)m.RueckgespeistKwh ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@n", (object?)m.Notiz ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@typ", m.ErfassungsTyp);
            cmd.Parameters.AddWithValue("@ma", (object?)m.MonatsAnzahl ?? DBNull.Value);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void StweZaehlerdatenSetUpdate(StweZaehlerdatenSet m)
        {
            EnsureStweSchema();

            if (m == null) throw new ArgumentNullException(nameof(m));
            if (m.Id <= 0) throw new ArgumentException("Id fehlt.", nameof(m));

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.StweZaehlerdatenSet SET
    ErfasstAm        = @am,
    RechnungKwhTotal = @rk,
    GutschriftChf    = @gc,
    RueckgespeistKwh = @rkwh,
    Notiz            = @n,
    ErfassungsTyp    = @typ,
    MonatsAnzahl     = @ma,
    UpdatedAtUtc     = SYSUTCDATETIME()
WHERE Id = @id;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", m.Id);
            cmd.Parameters.AddWithValue("@am", m.ErfasstAm);
            cmd.Parameters.AddWithValue("@rk", (object?)m.RechnungKwhTotal ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gc", (object?)m.GutschriftChf ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rkwh", (object?)m.RueckgespeistKwh ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@n", (object?)m.Notiz ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@typ", m.ErfassungsTyp);
            cmd.Parameters.AddWithValue("@ma", (object?)m.MonatsAnzahl ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }


        public void StweZaehlerdatenSetDelete(int id)
        {
            EnsureStweSchema();
            if (id <= 0) return;

            using var c = CreateConnection();
            c.Open();
            using var tx = c.BeginTransaction();

            try
            {
                using (var delMonate = c.CreateCommand())
                {
                    delMonate.Transaction = tx;
                    delMonate.CommandText = "DELETE FROM dbo.StweZaehlerdatenMonat WHERE SetId = @id;";
                    delMonate.Parameters.AddWithValue("@id", id);
                    delMonate.ExecuteNonQuery();
                }

                using (var delLines = c.CreateCommand())
                {
                    delLines.Transaction = tx;
                    delLines.CommandText = "DELETE FROM dbo.StweZaehlerdatenLine WHERE SetId = @id;";
                    delLines.Parameters.AddWithValue("@id", id);
                    delLines.ExecuteNonQuery();
                }

                using (var delSet = c.CreateCommand())
                {
                    delSet.Transaction = tx;
                    delSet.CommandText = "DELETE FROM dbo.StweZaehlerdatenSet WHERE Id = @id;";
                    delSet.Parameters.AddWithValue("@id", id);
                    delSet.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public List<StweZaehlerdatenLine> StweZaehlerdatenLinesGetBySet(int setId)
        {
            EnsureStweSchema();

            var list = new List<StweZaehlerdatenLine>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT Id, SetId, ZaehlerId, NeuWert
FROM dbo.StweZaehlerdatenLine
WHERE SetId = @sid
ORDER BY ZaehlerId;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@sid", setId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new StweZaehlerdatenLine
                {
                    Id = r.GetInt32(0),
                    SetId = r.GetInt32(1),
                    ZaehlerId = r.GetInt32(2),
                    NeuWert = r.GetDecimal(3)
                });
            }

            return list;
        }

        public void StweZaehlerdatenLinesReplace(int setId, List<(int ZaehlerId, decimal NeuWert)> lines)
        {
            EnsureStweSchema();

            if (setId <= 0) throw new ArgumentOutOfRangeException(nameof(setId));
            lines ??= new();

            using var c = CreateConnection();
            c.Open();
            using var tx = c.BeginTransaction();

            try
            {
                using (var del = c.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM dbo.StweZaehlerdatenLine WHERE SetId = @sid;";
                    del.Parameters.AddWithValue("@sid", setId);
                    del.ExecuteNonQuery();
                }

                foreach (var (zaehlerId, neu) in lines)
                {
                    using var ins = c.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = @"
INSERT INTO dbo.StweZaehlerdatenLine (SetId, ZaehlerId, NeuWert)
VALUES (@sid, @zid, @nw);";
                    ins.Parameters.AddWithValue("@sid", setId);
                    ins.Parameters.AddWithValue("@zid", zaehlerId);
                    ins.Parameters.AddWithValue("@nw", neu);
                    ins.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public void StweZaehlerdatenMonateReplace(int setId, List<(int ZaehlerId, int MonatIndex, decimal Kwh)> monate)
        {
            EnsureStweSchema();

            if (setId <= 0) throw new ArgumentOutOfRangeException(nameof(setId));
            monate ??= new();

            using var c = CreateConnection();
            c.Open();
            using var tx = c.BeginTransaction();

            try
            {
                using (var del = c.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM dbo.StweZaehlerdatenMonat WHERE SetId = @sid;";
                    del.Parameters.AddWithValue("@sid", setId);
                    del.ExecuteNonQuery();
                }

                foreach (var (zaehlerId, monatIndex, kwh) in monate)
                {
                    using var ins = c.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = @"
INSERT INTO dbo.StweZaehlerdatenMonat (SetId, ZaehlerId, MonatIndex, Kwh)
VALUES (@sid, @zid, @mid, @kwh);";
                    ins.Parameters.AddWithValue("@sid", setId);
                    ins.Parameters.AddWithValue("@zid", zaehlerId);
                    ins.Parameters.AddWithValue("@mid", monatIndex);
                    ins.Parameters.AddWithValue("@kwh", kwh);
                    ins.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public List<(int ZaehlerId, int MonatIndex, decimal Kwh)> StweZaehlerdatenMonateGetBySet(int setId)
        {
            EnsureStweSchema();

            var list = new List<(int ZaehlerId, int MonatIndex, decimal Kwh)>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT ZaehlerId, MonatIndex, Kwh
FROM dbo.StweZaehlerdatenMonat
WHERE SetId = @sid
ORDER BY ZaehlerId, MonatIndex;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@sid", setId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((
                    r.GetInt32(0),
                    r.GetInt32(1),
                    r.GetDecimal(2)
                ));
            }

            return list;
        }


        /// <summary>
        /// Liefert das direkt vorherige Zählerdaten-Set (nach ErfasstAm) innerhalb derselben Liegenschaft.
        /// </summary>
        public StweZaehlerdatenSet? StweZaehlerdatenGetPreviousSet(int liegenschaftId, DateTime currentErfasstAm, int currentId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT TOP(1) Id, LiegenschaftId, ErfasstAm, RechnungKwhTotal, GutschriftChf, RueckgespeistKwh, Notiz
FROM dbo.StweZaehlerdatenSet
WHERE LiegenschaftId = @lid
  AND (ErfasstAm < @am OR (ErfasstAm = @am AND Id < @id))
ORDER BY ErfasstAm DESC, Id DESC;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@lid", liegenschaftId);
            cmd.Parameters.AddWithValue("@am", currentErfasstAm);
            cmd.Parameters.AddWithValue("@id", currentId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new StweZaehlerdatenSet
            {
                Id = r.GetInt32(0),
                LiegenschaftId = r.GetInt32(1),
                ErfasstAm = r.GetDateTime(2),
                RechnungKwhTotal = r.IsDBNull(3) ? (decimal?)null : r.GetDecimal(3),
                GutschriftChf = r.IsDBNull(4) ? (decimal?)null : r.GetDecimal(4),
                RueckgespeistKwh = r.IsDBNull(5) ? (decimal?)null : r.GetDecimal(5),
                Notiz = r.IsDBNull(6) ? null : r.GetString(6)
            };
        }

        public int? StweSetGetEnergieZaehlerdatenSetId(int setId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT EnergieZaehlerdatenSetId FROM dbo.StweSet WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", setId);

            var v = cmd.ExecuteScalar();
            if (v == null || v == DBNull.Value) return null;
            return Convert.ToInt32(v);
        }

        public void StweSetUpdateEnergieZaehlerdatenSetId(int setId, int? zaehlerdatenSetId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE dbo.StweSet SET EnergieZaehlerdatenSetId = @zid WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", setId);
            cmd.Parameters.AddWithValue("@zid", (object?)zaehlerdatenSetId ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }
        public StweEnergieReportInfo? StweEnergieReportInfoGet(int liegenschaftId, System.DateTime transaktionsDatum, decimal setTotalSigned)
        {
            EnsureStweSchema();
            if (liegenschaftId <= 0) return null;

            // Neustes Zählerdaten-Set <= Transaktionsdatum
            var sets = StweZaehlerdatenSetsGetByLiegenschaft(liegenschaftId)
                .Where(z => z.ErfasstAm.Date <= transaktionsDatum.Date)
                .OrderByDescending(z => z.ErfasstAm)
                .ThenByDescending(z => z.Id)
                .ToList();

            var cur = sets.FirstOrDefault();
            if (cur == null) return null;

            if (!cur.RechnungKwhTotal.HasValue || cur.RechnungKwhTotal.Value <= 0m)
                return null;

            var prev = StweZaehlerdatenGetPreviousSet(liegenschaftId, cur.ErfasstAm, cur.Id);

            // Stammdaten: ZaehlerId -> Typ
            var zaehlerTyp = StweZaehlerGetByLiegenschaft(liegenschaftId)
                .ToDictionary(z => z.Id, z => (z.Typ ?? "").Trim().ToUpperInvariant());

            // Lines lesen
            var curLines = StweZaehlerdatenLinesGetBySet(cur.Id);
            var prevLines = prev != null ? StweZaehlerdatenLinesGetBySet(prev.Id) : new System.Collections.Generic.List<StweZaehlerdatenLine>();
            var prevDict = prevLines.ToDictionary(x => x.ZaehlerId, x => x.NeuWert);

            // Interne kWh: nur DIREKT/ALLG/HEIZ (EVU ignorieren)
            decimal interneKwh = 0m;
            foreach (var c in curLines)
            {
                if (!zaehlerTyp.TryGetValue(c.ZaehlerId, out var typ))
                    continue;

                if (typ != "DIREKT" && typ != "ALLG" && typ != "HEIZ")
                    continue;

                prevDict.TryGetValue(c.ZaehlerId, out var alt);
                var diff = c.NeuWert - alt;
                if (diff > 0m) interneKwh += diff;
            }

            var rechnungKwh = cur.RechnungKwhTotal.Value;

            // PV-Direktverbrauch: interne kWh minus Rechnung kWh (nie negativ)
            var solarDirekt = interneKwh - rechnungKwh;
            if (solarDirekt < 0m) solarDirekt = 0m;

            // Preis pro kWh gemäss Rechnung
            var preis = setTotalSigned / rechnungKwh;

            // Kontrollwert: Rechnung/Intern (nur falls intern > 0)
            var scale = interneKwh <= 0m ? 1m : (rechnungKwh / interneKwh);

            return new StweEnergieReportInfo
            {
                LiegenschaftId = liegenschaftId,

                ZaehlerdatenSetId = cur.Id,
                ZaehlerdatenSetDatum = cur.ErfasstAm,
                ZaehlerdatenSetNotiz = cur.Notiz,

                VorherigesZaehlerdatenSetId = prev?.Id,
                VorherigesZaehlerdatenSetDatum = prev?.ErfasstAm,

                RechnungKwhTotal = rechnungKwh,
                GutschriftChf = cur.GutschriftChf,

                InterneKwhTotal = interneKwh,
                SolarDirektKwh = solarDirekt,

                PreisProKwh = preis,
                Scale = scale
            };
        }

        public decimal StweSetSumBetragByEnergieZaehlerdatenSetId(int liegenschaftId, int zaehlerdatenSetId, DateTime? von, DateTime? bis)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            // Betrag und Datum kommen aus dbo.Transaktion (StweSet hat nur TransaktionId)
            var sql = @"
SELECT ISNULL(SUM(t.Betrag), 0)
FROM dbo.StweSet s
JOIN dbo.Transaktion t ON t.Id = s.TransaktionId
WHERE s.LiegenschaftId = @lid
  AND s.EnergieZaehlerdatenSetId = @zid";

            if (von.HasValue) sql += " AND t.Datum >= @von";
            if (bis.HasValue) sql += " AND t.Datum <= @bis";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            cmd.Parameters.AddWithValue("@lid", liegenschaftId);
            cmd.Parameters.AddWithValue("@zid", zaehlerdatenSetId);

            if (von.HasValue) cmd.Parameters.AddWithValue("@von", von.Value.Date);
            if (bis.HasValue) cmd.Parameters.AddWithValue("@bis", bis.Value.Date);

            var v = cmd.ExecuteScalar();
            return Convert.ToDecimal(v);
        }

        public IList<StweEnergieChartPoint> StweEnergieChartGet(int liegenschaftId, DateTime? von, DateTime? bis)
        {
            EnsureStweSchema();

            var result = new List<StweEnergieChartPoint>();

            var sets = StweZaehlerdatenSetsGetByLiegenschaft(liegenschaftId)
                .OrderBy(z => z.ErfasstAm)
                .ToList();

            if (von.HasValue)
                sets = sets.Where(z => z.ErfasstAm.Date >= von.Value.Date).ToList();
            if (bis.HasValue)
                sets = sets.Where(z => z.ErfasstAm.Date <= bis.Value.Date).ToList();

            foreach (var z in sets)
            {
                // Summe der Rechnungsbeträge, die explizit diesem Zählerdaten-Set zugeordnet sind
                var betrag = StweSetSumBetragByEnergieZaehlerdatenSetId(
                    liegenschaftId, z.Id, von, bis);

                var info = StweEnergieReportInfoGet(liegenschaftId, z.ErfasstAm, betrag);
                if (info == null) continue;

                result.Add(new StweEnergieChartPoint
                {
                    Label = $"{z.ErfasstAm:MM.yyyy}",
                    RechnungKwh = info.RechnungKwhTotal,
                    InterneKwh = info.InterneKwhTotal,
                    SolarDirektKwh = info.SolarDirektKwh
                });
            }

            return result;
        }


    }
}
