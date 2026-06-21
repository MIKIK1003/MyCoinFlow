using Microsoft.Data.SqlClient;
using MyCoinFlow.Models;
using System;
using System.Collections.Generic;

namespace MyCoinFlow.Services
{
    public partial class DatabaseService
    {
        public void EnsureHaushaltSchema()
        {
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
IF OBJECT_ID('dbo.HaushaltStandort', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HaushaltStandort
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HaushaltStandort PRIMARY KEY,
        Bezeichnung NVARCHAR(200) NOT NULL,
        IconKey NVARCHAR(100) NOT NULL CONSTRAINT DF_HaushaltStandort_IconKey DEFAULT ('HomeCityOutline'),
        FarbeKey NVARCHAR(100) NOT NULL CONSTRAINT DF_HaushaltStandort_FarbeKey DEFAULT ('DeepPurple'),
        Bemerkung NVARCHAR(1000) NULL,
        IstAktiv BIT NOT NULL CONSTRAINT DF_HaushaltStandort_IstAktiv DEFAULT (1),
        ErstelltAm DATETIME2 NOT NULL CONSTRAINT DF_HaushaltStandort_ErstelltAm DEFAULT SYSUTCDATETIME(),
        GeaendertAm DATETIME2 NULL
    );
END;

IF OBJECT_ID('dbo.HaushaltRaum', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HaushaltRaum
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HaushaltRaum PRIMARY KEY,
        StandortId INT NULL,
        Bezeichnung NVARCHAR(200) NOT NULL,
        IconKey NVARCHAR(100) NOT NULL CONSTRAINT DF_HaushaltRaum_IconKey DEFAULT ('HomeOutline'),
        Bemerkung NVARCHAR(1000) NULL,
        IstAktiv BIT NOT NULL CONSTRAINT DF_HaushaltRaum_IstAktiv DEFAULT (1),
        ErstelltAm DATETIME2 NOT NULL CONSTRAINT DF_HaushaltRaum_ErstelltAm DEFAULT SYSUTCDATETIME(),
        GeaendertAm DATETIME2 NULL,

        CONSTRAINT FK_HaushaltRaum_Standort
            FOREIGN KEY (StandortId) REFERENCES dbo.HaushaltStandort(Id)
    );
END;

IF COL_LENGTH('dbo.HaushaltRaum', 'StandortId') IS NULL
BEGIN
    ALTER TABLE dbo.HaushaltRaum
    ADD StandortId INT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_HaushaltRaum_Standort'
)
BEGIN
    ALTER TABLE dbo.HaushaltRaum
    ADD CONSTRAINT FK_HaushaltRaum_Standort
        FOREIGN KEY (StandortId) REFERENCES dbo.HaushaltStandort(Id);
END;

IF OBJECT_ID('dbo.HaushaltArbeitsanweisung', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HaushaltArbeitsanweisung
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HaushaltArbeitsanweisung PRIMARY KEY,
        Bezeichnung NVARCHAR(200) NOT NULL,
        Beschreibung NVARCHAR(2000) NULL,
        IconKey NVARCHAR(100) NOT NULL CONSTRAINT DF_HaushaltArbeitsanweisung_IconKey DEFAULT ('ClipboardTextOutline'),
        IstAktiv BIT NOT NULL CONSTRAINT DF_HaushaltArbeitsanweisung_IstAktiv DEFAULT (1),
        ErstelltAm DATETIME2 NOT NULL CONSTRAINT DF_HaushaltArbeitsanweisung_ErstelltAm DEFAULT SYSUTCDATETIME(),
        GeaendertAm DATETIME2 NULL
    );
END;

IF OBJECT_ID('dbo.HaushaltZeitintervall', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HaushaltZeitintervall
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HaushaltZeitintervall PRIMARY KEY,
        Bezeichnung NVARCHAR(200) NOT NULL,
        Tage INT NOT NULL,
        Bemerkung NVARCHAR(1000) NULL,
        IstAktiv BIT NOT NULL CONSTRAINT DF_HaushaltZeitintervall_IstAktiv DEFAULT (1),
        ErstelltAm DATETIME2 NOT NULL CONSTRAINT DF_HaushaltZeitintervall_ErstelltAm DEFAULT SYSUTCDATETIME(),
        GeaendertAm DATETIME2 NULL
    );
END;

IF COL_LENGTH('dbo.HaushaltObjekt', 'ArbeitsanweisungId') IS NULL
   AND OBJECT_ID('dbo.HaushaltObjekt', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.HaushaltObjekt
    ADD ArbeitsanweisungId INT NULL;
END;

IF COL_LENGTH('dbo.HaushaltObjekt', 'ZeitintervallId') IS NULL
   AND OBJECT_ID('dbo.HaushaltObjekt', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.HaushaltObjekt
    ADD ZeitintervallId INT NULL;
END;

IF COL_LENGTH('dbo.HaushaltObjekt', 'VorlaufTage') IS NULL
   AND OBJECT_ID('dbo.HaushaltObjekt', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.HaushaltObjekt
    ADD VorlaufTage INT NOT NULL CONSTRAINT DF_HaushaltObjekt_VorlaufTage DEFAULT (0);
END;

IF COL_LENGTH('dbo.HaushaltObjekt', 'LetzteAusfuehrungAm') IS NULL
   AND OBJECT_ID('dbo.HaushaltObjekt', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.HaushaltObjekt
    ADD LetzteAusfuehrungAm DATE NULL;
END;



IF OBJECT_ID('dbo.HaushaltObjektKategorie', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HaushaltObjektKategorie
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HaushaltObjektKategorie PRIMARY KEY,
        Bezeichnung NVARCHAR(200) NOT NULL,
        IconKey NVARCHAR(100) NOT NULL CONSTRAINT DF_HaushaltObjektKategorie_IconKey DEFAULT ('PackageVariantClosed'),
        Bemerkung NVARCHAR(1000) NULL,
        IstAktiv BIT NOT NULL CONSTRAINT DF_HaushaltObjektKategorie_IstAktiv DEFAULT (1),
        ErstelltAm DATETIME2 NOT NULL CONSTRAINT DF_HaushaltObjektKategorie_ErstelltAm DEFAULT SYSUTCDATETIME(),
        GeaendertAm DATETIME2 NULL
    );
END;

IF COL_LENGTH('dbo.HaushaltObjekt', 'KategorieId') IS NULL
   AND OBJECT_ID('dbo.HaushaltObjekt', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.HaushaltObjekt
    ADD KategorieId INT NULL;
END;


IF OBJECT_ID('dbo.HaushaltObjekt', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HaushaltObjekt
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HaushaltObjekt PRIMARY KEY,
        RaumId INT NOT NULL,

        Bezeichnung NVARCHAR(200) NOT NULL,
        Kategorie NVARCHAR(100) NULL,
        IconKey NVARCHAR(100) NOT NULL CONSTRAINT DF_HaushaltObjekt_IconKey DEFAULT ('PackageVariantClosed'),

        Hersteller NVARCHAR(200) NULL,
        Modell NVARCHAR(200) NULL,
        Seriennummer NVARCHAR(200) NULL,
        Kaufdatum DATE NULL,
        Kaufpreis DECIMAL(19,2) NULL,
        Bemerkung NVARCHAR(1000) NULL,

        IstAktiv BIT NOT NULL CONSTRAINT DF_HaushaltObjekt_IstAktiv DEFAULT (1),
        ErstelltAm DATETIME2 NOT NULL CONSTRAINT DF_HaushaltObjekt_ErstelltAm DEFAULT SYSUTCDATETIME(),
        GeaendertAm DATETIME2 NULL,

        CONSTRAINT FK_HaushaltObjekt_Raum
            FOREIGN KEY (RaumId) REFERENCES dbo.HaushaltRaum(Id)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_HaushaltRaum_StandortId'
      AND object_id = OBJECT_ID('dbo.HaushaltRaum')
)
BEGIN
    CREATE INDEX IX_HaushaltRaum_StandortId
    ON dbo.HaushaltRaum(StandortId);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_HaushaltObjekt_RaumId'
      AND object_id = OBJECT_ID('dbo.HaushaltObjekt')
)
BEGIN
    CREATE INDEX IX_HaushaltObjekt_RaumId
    ON dbo.HaushaltObjekt(RaumId);
END;
";

            using var cmd = new SqlCommand(sql, c);
            cmd.ExecuteNonQuery();
        }

        public List<HaushaltStandort> HaushaltStandorteGetAll()
        {
            EnsureHaushaltSchema();

            var list = new List<HaushaltStandort>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    Id,
    Bezeichnung,
    IconKey,
    FarbeKey,
    ISNULL(Bemerkung, '') AS Bemerkung,
    IstAktiv,
    ErstelltAm,
    GeaendertAm
FROM dbo.HaushaltStandort
WHERE IstAktiv = 1
ORDER BY Bezeichnung;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new HaushaltStandort
                {
                    Id = r.GetInt32(0),
                    Bezeichnung = r.GetString(1),
                    IconKey = r.GetString(2),
                    FarbeKey = r.GetString(3),
                    Bemerkung = r.GetString(4),
                    IstAktiv = r.GetBoolean(5),
                    ErstelltAm = r.GetDateTime(6),
                    GeaendertAm = r.IsDBNull(7) ? null : r.GetDateTime(7)
                });
            }

            return list;
        }

        public int HaushaltStandortInsert(HaushaltStandort model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.HaushaltStandort
(
    Bezeichnung,
    IconKey,
    FarbeKey,
    Bemerkung,
    IstAktiv
)
OUTPUT INSERTED.Id
VALUES
(
    @Bezeichnung,
    @IconKey,
    @FarbeKey,
    @Bemerkung,
    @IstAktiv
);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Bezeichnung", model.Bezeichnung.Trim());
            cmd.Parameters.AddWithValue("@IconKey", string.IsNullOrWhiteSpace(model.IconKey) ? "HomeCityOutline" : model.IconKey.Trim());
            cmd.Parameters.AddWithValue("@FarbeKey", string.IsNullOrWhiteSpace(model.FarbeKey) ? "DeepPurple" : model.FarbeKey.Trim());
            cmd.Parameters.AddWithValue("@Bemerkung", string.IsNullOrWhiteSpace(model.Bemerkung) ? DBNull.Value : model.Bemerkung.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void HaushaltStandortUpdate(HaushaltStandort model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.HaushaltStandort
SET
    Bezeichnung = @Bezeichnung,
    IconKey = @IconKey,
    FarbeKey = @FarbeKey,
    Bemerkung = @Bemerkung,
    IstAktiv = @IstAktiv,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", model.Id);
            cmd.Parameters.AddWithValue("@Bezeichnung", model.Bezeichnung.Trim());
            cmd.Parameters.AddWithValue("@IconKey", string.IsNullOrWhiteSpace(model.IconKey) ? "HomeCityOutline" : model.IconKey.Trim());
            cmd.Parameters.AddWithValue("@FarbeKey", string.IsNullOrWhiteSpace(model.FarbeKey) ? "DeepPurple" : model.FarbeKey.Trim());
            cmd.Parameters.AddWithValue("@Bemerkung", string.IsNullOrWhiteSpace(model.Bemerkung) ? DBNull.Value : model.Bemerkung.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            cmd.ExecuteNonQuery();
        }

        public void HaushaltStandortDelete(int id)
        {
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.HaushaltStandort
SET
    IstAktiv = 0,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }

        public List<HaushaltRaum> HaushaltRaeumeGetAll()
        {
            EnsureHaushaltSchema();

            var list = new List<HaushaltRaum>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    r.Id,
    r.StandortId,
    ISNULL(s.Bezeichnung, '') AS StandortBezeichnung,
    ISNULL(s.IconKey, 'HomeCityOutline') AS StandortIconKey,
    ISNULL(s.FarbeKey, 'DeepPurple') AS StandortFarbeKey,
    r.Bezeichnung,
    r.IconKey,
    ISNULL(r.Bemerkung, '') AS Bemerkung,
    r.IstAktiv,
    r.ErstelltAm,
    r.GeaendertAm
FROM dbo.HaushaltRaum r
LEFT JOIN dbo.HaushaltStandort s
    ON s.Id = r.StandortId
WHERE r.IstAktiv = 1
ORDER BY s.Bezeichnung, r.Bezeichnung;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new HaushaltRaum
                {
                    Id = r.GetInt32(0),
                    StandortId = r.IsDBNull(1) ? null : r.GetInt32(1),
                    StandortBezeichnung = r.GetString(2),
                    StandortIconKey = r.GetString(3),
                    StandortFarbeKey = r.GetString(4),
                    Bezeichnung = r.GetString(5),
                    IconKey = r.GetString(6),
                    Bemerkung = r.GetString(7),
                    IstAktiv = r.GetBoolean(8),
                    ErstelltAm = r.GetDateTime(9),
                    GeaendertAm = r.IsDBNull(10) ? null : r.GetDateTime(10)
                });
            }

            return list;
        }

        public int HaushaltRaumInsert(HaushaltRaum model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.HaushaltRaum
(
    StandortId,
    Bezeichnung,
    IconKey,
    Bemerkung,
    IstAktiv
)
OUTPUT INSERTED.Id
VALUES
(
    @StandortId,
    @Bezeichnung,
    @IconKey,
    @Bemerkung,
    @IstAktiv
);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@StandortId", model.StandortId.HasValue ? model.StandortId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Bezeichnung", model.Bezeichnung.Trim());
            cmd.Parameters.AddWithValue("@IconKey", string.IsNullOrWhiteSpace(model.IconKey) ? "HomeOutline" : model.IconKey.Trim());
            cmd.Parameters.AddWithValue("@Bemerkung", string.IsNullOrWhiteSpace(model.Bemerkung) ? DBNull.Value : model.Bemerkung.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }



        public void HaushaltRaumUpdate(HaushaltRaum model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.HaushaltRaum
SET
    StandortId = @StandortId,
    Bezeichnung = @Bezeichnung,
    IconKey = @IconKey,
    Bemerkung = @Bemerkung,
    IstAktiv = @IstAktiv,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", model.Id);
            cmd.Parameters.AddWithValue("@StandortId", model.StandortId.HasValue ? model.StandortId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Bezeichnung", model.Bezeichnung.Trim());
            cmd.Parameters.AddWithValue("@IconKey", string.IsNullOrWhiteSpace(model.IconKey) ? "HomeOutline" : model.IconKey.Trim());
            cmd.Parameters.AddWithValue("@Bemerkung", string.IsNullOrWhiteSpace(model.Bemerkung) ? DBNull.Value : model.Bemerkung.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            cmd.ExecuteNonQuery();
        }

        public void HaushaltRaumDelete(int id)
        {
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.HaushaltRaum
SET
    IstAktiv = 0,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }

        public List<HaushaltArbeitsanweisung> HaushaltArbeitsanweisungenGetAll()
        {
            EnsureHaushaltSchema();

            var list = new List<HaushaltArbeitsanweisung>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    Id,
    Bezeichnung,
    ISNULL(Beschreibung, '') AS Beschreibung,
    IconKey,
    IstAktiv,
    ErstelltAm,
    GeaendertAm
FROM dbo.HaushaltArbeitsanweisung
WHERE IstAktiv = 1
ORDER BY Bezeichnung;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new HaushaltArbeitsanweisung
                {
                    Id = r.GetInt32(0),
                    Bezeichnung = r.GetString(1),
                    Beschreibung = r.GetString(2),
                    IconKey = r.GetString(3),
                    IstAktiv = r.GetBoolean(4),
                    ErstelltAm = r.GetDateTime(5),
                    GeaendertAm = r.IsDBNull(6) ? null : r.GetDateTime(6)
                });
            }

            return list;
        }

        public int HaushaltArbeitsanweisungInsert(HaushaltArbeitsanweisung model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.HaushaltArbeitsanweisung
(
    Bezeichnung,
    Beschreibung,
    IconKey,
    IstAktiv
)
OUTPUT INSERTED.Id
VALUES
(
    @Bezeichnung,
    @Beschreibung,
    @IconKey,
    @IstAktiv
);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Bezeichnung", model.Bezeichnung.Trim());
            cmd.Parameters.AddWithValue("@Beschreibung", string.IsNullOrWhiteSpace(model.Beschreibung) ? DBNull.Value : model.Beschreibung.Trim());
            cmd.Parameters.AddWithValue("@IconKey", string.IsNullOrWhiteSpace(model.IconKey) ? "ClipboardTextOutline" : model.IconKey.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void HaushaltArbeitsanweisungUpdate(HaushaltArbeitsanweisung model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.HaushaltArbeitsanweisung
SET
    Bezeichnung = @Bezeichnung,
    Beschreibung = @Beschreibung,
    IconKey = @IconKey,
    IstAktiv = @IstAktiv,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", model.Id);
            cmd.Parameters.AddWithValue("@Bezeichnung", model.Bezeichnung.Trim());
            cmd.Parameters.AddWithValue("@Beschreibung", string.IsNullOrWhiteSpace(model.Beschreibung) ? DBNull.Value : model.Beschreibung.Trim());
            cmd.Parameters.AddWithValue("@IconKey", string.IsNullOrWhiteSpace(model.IconKey) ? "ClipboardTextOutline" : model.IconKey.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            cmd.ExecuteNonQuery();
        }

        public void HaushaltArbeitsanweisungDelete(int id)
        {
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.HaushaltArbeitsanweisung
SET
    IstAktiv = 0,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }

        public List<HaushaltZeitintervall> HaushaltZeitintervalleGetAll()
        {
            EnsureHaushaltSchema();

            var list = new List<HaushaltZeitintervall>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    Id,
    Bezeichnung,
    Tage,
    ISNULL(Bemerkung, '') AS Bemerkung,
    IstAktiv,
    ErstelltAm,
    GeaendertAm
FROM dbo.HaushaltZeitintervall
WHERE IstAktiv = 1
ORDER BY Tage, Bezeichnung;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new HaushaltZeitintervall
                {
                    Id = r.GetInt32(0),
                    Bezeichnung = r.GetString(1),
                    Tage = r.GetInt32(2),
                    Bemerkung = r.GetString(3),
                    IstAktiv = r.GetBoolean(4),
                    ErstelltAm = r.GetDateTime(5),
                    GeaendertAm = r.IsDBNull(6) ? null : r.GetDateTime(6)
                });
            }

            return list;
        }

        public int HaushaltZeitintervallInsert(HaushaltZeitintervall model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.HaushaltZeitintervall
(
    Bezeichnung,
    Tage,
    Bemerkung,
    IstAktiv
)
OUTPUT INSERTED.Id
VALUES
(
    @Bezeichnung,
    @Tage,
    @Bemerkung,
    @IstAktiv
);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Bezeichnung", model.Bezeichnung.Trim());
            cmd.Parameters.AddWithValue("@Tage", model.Tage);
            cmd.Parameters.AddWithValue("@Bemerkung", string.IsNullOrWhiteSpace(model.Bemerkung) ? DBNull.Value : model.Bemerkung.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void HaushaltZeitintervallUpdate(HaushaltZeitintervall model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.HaushaltZeitintervall
SET
    Bezeichnung = @Bezeichnung,
    Tage = @Tage,
    Bemerkung = @Bemerkung,
    IstAktiv = @IstAktiv,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", model.Id);
            cmd.Parameters.AddWithValue("@Bezeichnung", model.Bezeichnung.Trim());
            cmd.Parameters.AddWithValue("@Tage", model.Tage);
            cmd.Parameters.AddWithValue("@Bemerkung", string.IsNullOrWhiteSpace(model.Bemerkung) ? DBNull.Value : model.Bemerkung.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            cmd.ExecuteNonQuery();
        }

        public void HaushaltZeitintervallDelete(int id)
        {
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.HaushaltZeitintervall
SET
    IstAktiv = 0,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }


        public List<HaushaltObjektKategorie> HaushaltObjektKategorienGetAll()
        {
            EnsureHaushaltSchema();

            var list = new List<HaushaltObjektKategorie>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    Id,
    Bezeichnung,
    IconKey,
    ISNULL(Bemerkung, '') AS Bemerkung,
    IstAktiv,
    ErstelltAm,
    GeaendertAm
FROM dbo.HaushaltObjektKategorie
WHERE IstAktiv = 1
ORDER BY Bezeichnung;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new HaushaltObjektKategorie
                {
                    Id = r.GetInt32(0),
                    Bezeichnung = r.GetString(1),
                    IconKey = r.GetString(2),
                    Bemerkung = r.GetString(3),
                    IstAktiv = r.GetBoolean(4),
                    ErstelltAm = r.GetDateTime(5),
                    GeaendertAm = r.IsDBNull(6) ? null : r.GetDateTime(6)
                });
            }

            return list;
        }

        public int HaushaltObjektKategorieInsert(HaushaltObjektKategorie model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.HaushaltObjektKategorie
(
    Bezeichnung,
    IconKey,
    Bemerkung,
    IstAktiv
)
OUTPUT INSERTED.Id
VALUES
(
    @Bezeichnung,
    @IconKey,
    @Bemerkung,
    @IstAktiv
);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Bezeichnung", model.Bezeichnung.Trim());
            cmd.Parameters.AddWithValue("@IconKey", string.IsNullOrWhiteSpace(model.IconKey) ? "PackageVariantClosed" : model.IconKey.Trim());
            cmd.Parameters.AddWithValue("@Bemerkung", string.IsNullOrWhiteSpace(model.Bemerkung) ? DBNull.Value : model.Bemerkung.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void HaushaltObjektKategorieUpdate(HaushaltObjektKategorie model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.HaushaltObjektKategorie
SET
    Bezeichnung = @Bezeichnung,
    IconKey = @IconKey,
    Bemerkung = @Bemerkung,
    IstAktiv = @IstAktiv,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", model.Id);
            cmd.Parameters.AddWithValue("@Bezeichnung", model.Bezeichnung.Trim());
            cmd.Parameters.AddWithValue("@IconKey", string.IsNullOrWhiteSpace(model.IconKey) ? "PackageVariantClosed" : model.IconKey.Trim());
            cmd.Parameters.AddWithValue("@Bemerkung", string.IsNullOrWhiteSpace(model.Bemerkung) ? DBNull.Value : model.Bemerkung.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            cmd.ExecuteNonQuery();
        }

        public void HaushaltObjektKategorieDelete(int id)
        {
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.HaushaltObjektKategorie
SET
    IstAktiv = 0,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }


        public List<HaushaltObjekt> HaushaltObjekteGetByRaum(int raumId)
        {
            EnsureHaushaltSchema();

            var list = new List<HaushaltObjekt>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    o.Id,
    o.RaumId,
    r.Bezeichnung AS RaumBezeichnung,

    o.KategorieId,
    ISNULL(k.Bezeichnung, ISNULL(o.Kategorie, '')) AS KategorieBezeichnung,
    ISNULL(k.IconKey, o.IconKey) AS KategorieIconKey,

    o.ArbeitsanweisungId,
    ISNULL(a.Bezeichnung, '') AS ArbeitsanweisungBezeichnung,
    ISNULL(a.Beschreibung, '') AS ArbeitsanweisungBeschreibung,

    o.ZeitintervallId,
    ISNULL(z.Bezeichnung, '') AS ZeitintervallBezeichnung,
    z.Tage AS ZeitintervallTage,

    o.VorlaufTage,
    o.LetzteAusfuehrungAm,

    o.Bezeichnung,
    ISNULL(o.Kategorie, '') AS Kategorie,
    o.IconKey,

    ISNULL(o.Hersteller, '') AS Hersteller,
    ISNULL(o.Modell, '') AS Modell,
    ISNULL(o.Seriennummer, '') AS Seriennummer,
    o.Kaufdatum,
    o.Kaufpreis,
    ISNULL(o.Bemerkung, '') AS Bemerkung,

    o.IstAktiv,
    o.ErstelltAm,
    o.GeaendertAm
FROM dbo.HaushaltObjekt o
JOIN dbo.HaushaltRaum r ON r.Id = o.RaumId
LEFT JOIN dbo.HaushaltObjektKategorie k ON k.Id = o.KategorieId
LEFT JOIN dbo.HaushaltArbeitsanweisung a ON a.Id = o.ArbeitsanweisungId
LEFT JOIN dbo.HaushaltZeitintervall z ON z.Id = o.ZeitintervallId
WHERE o.IstAktiv = 1
  AND o.RaumId = @RaumId
ORDER BY o.Bezeichnung;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@RaumId", raumId);

            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new HaushaltObjekt
                {
                    Id = r.GetInt32(0),
                    RaumId = r.GetInt32(1),
                    RaumBezeichnung = r.GetString(2),

                    KategorieId = r.IsDBNull(3) ? null : r.GetInt32(3),
                    KategorieBezeichnung = r.GetString(4),
                    KategorieIconKey = r.GetString(5),

                    ArbeitsanweisungId = r.IsDBNull(6) ? null : r.GetInt32(6),
                    ArbeitsanweisungBezeichnung = r.GetString(7),
                    ArbeitsanweisungBeschreibung = r.GetString(8),

                    ZeitintervallId = r.IsDBNull(9) ? null : r.GetInt32(9),
                    ZeitintervallBezeichnung = r.GetString(10),
                    ZeitintervallTage = r.IsDBNull(11) ? null : r.GetInt32(11),

                    VorlaufTage = r.GetInt32(12),
                    LetzteAusfuehrungAm = r.IsDBNull(13) ? null : r.GetDateTime(13),

                    Bezeichnung = r.GetString(14),
                    Kategorie = r.GetString(15),
                    IconKey = r.GetString(16),

                    Hersteller = r.GetString(17),
                    Modell = r.GetString(18),
                    Seriennummer = r.GetString(19),
                    Kaufdatum = r.IsDBNull(20) ? null : r.GetDateTime(20),
                    Kaufpreis = r.IsDBNull(21) ? null : r.GetDecimal(21),
                    Bemerkung = r.GetString(22),

                    IstAktiv = r.GetBoolean(23),
                    ErstelltAm = r.GetDateTime(24),
                    GeaendertAm = r.IsDBNull(25) ? null : r.GetDateTime(25)
                });
            }

            return list;
        }

        public List<HaushaltObjekt> HaushaltObjekteGetAll()
        {
            EnsureHaushaltSchema();

            var list = new List<HaushaltObjekt>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    o.Id,
    o.RaumId,
    r.Bezeichnung AS RaumBezeichnung,
    o.KategorieId,
    ISNULL(k.Bezeichnung, ISNULL(o.Kategorie, '')) AS KategorieBezeichnung,
    ISNULL(k.IconKey, o.IconKey) AS KategorieIconKey,
    o.Bezeichnung,
    ISNULL(o.Kategorie, '') AS Kategorie,
    o.IconKey,
    ISNULL(o.Hersteller, '') AS Hersteller,
    ISNULL(o.Modell, '') AS Modell,
    ISNULL(o.Seriennummer, '') AS Seriennummer,
    o.Kaufdatum,
    o.Kaufpreis,
    ISNULL(o.Bemerkung, '') AS Bemerkung,
    o.IstAktiv,
    o.ErstelltAm,
    o.GeaendertAm
FROM dbo.HaushaltObjekt o
JOIN dbo.HaushaltRaum r ON r.Id = o.RaumId
LEFT JOIN dbo.HaushaltObjektKategorie k ON k.Id = o.KategorieId
WHERE o.IstAktiv = 1
ORDER BY o.Bezeichnung;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new HaushaltObjekt
                {
                    Id = r.GetInt32(0),
                    RaumId = r.GetInt32(1),
                    RaumBezeichnung = r.GetString(2),
                    KategorieId = r.IsDBNull(3) ? null : r.GetInt32(3),
                    KategorieBezeichnung = r.GetString(4),
                    KategorieIconKey = r.GetString(5),
                    Bezeichnung = r.GetString(6),
                    Kategorie = r.GetString(7),
                    IconKey = r.GetString(8),
                    Hersteller = r.GetString(9),
                    Modell = r.GetString(10),
                    Seriennummer = r.GetString(11),
                    Kaufdatum = r.IsDBNull(12) ? null : r.GetDateTime(12),
                    Kaufpreis = r.IsDBNull(13) ? null : r.GetDecimal(13),
                    Bemerkung = r.GetString(14),
                    IstAktiv = r.GetBoolean(15),
                    ErstelltAm = r.GetDateTime(16),
                    GeaendertAm = r.IsDBNull(17) ? null : r.GetDateTime(17)
                });
            }

            return list;
        }

        public int HaushaltObjektInsert(HaushaltObjekt model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.HaushaltObjekt
(
    RaumId,
    KategorieId,
    ArbeitsanweisungId,
    ZeitintervallId,
    VorlaufTage,
    LetzteAusfuehrungAm,

    Bezeichnung,
    Kategorie,
    IconKey,

    Hersteller,
    Modell,
    Seriennummer,
    Kaufdatum,
    Kaufpreis,
    Bemerkung,
    IstAktiv
)
OUTPUT INSERTED.Id
VALUES
(
    @RaumId,
    @KategorieId,
    @ArbeitsanweisungId,
    @ZeitintervallId,
    @VorlaufTage,
    @LetzteAusfuehrungAm,

    @Bezeichnung,
    @Kategorie,
    @IconKey,

    @Hersteller,
    @Modell,
    @Seriennummer,
    @Kaufdatum,
    @Kaufpreis,
    @Bemerkung,
    @IstAktiv
);";

            using var cmd = new SqlCommand(sql, c);

            cmd.Parameters.AddWithValue("@RaumId", model.RaumId);
            cmd.Parameters.AddWithValue("@KategorieId", model.KategorieId.HasValue ? model.KategorieId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ArbeitsanweisungId", model.ArbeitsanweisungId.HasValue ? model.ArbeitsanweisungId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ZeitintervallId", model.ZeitintervallId.HasValue ? model.ZeitintervallId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@VorlaufTage", model.VorlaufTage);
            cmd.Parameters.AddWithValue("@LetzteAusfuehrungAm", model.LetzteAusfuehrungAm.HasValue ? model.LetzteAusfuehrungAm.Value.Date : DBNull.Value);

            cmd.Parameters.AddWithValue("@Bezeichnung", model.Bezeichnung.Trim());
            cmd.Parameters.AddWithValue("@Kategorie", string.IsNullOrWhiteSpace(model.Kategorie) ? DBNull.Value : model.Kategorie.Trim());
            cmd.Parameters.AddWithValue("@IconKey", string.IsNullOrWhiteSpace(model.IconKey) ? "PackageVariantClosed" : model.IconKey.Trim());

            cmd.Parameters.AddWithValue("@Hersteller", string.IsNullOrWhiteSpace(model.Hersteller) ? DBNull.Value : model.Hersteller.Trim());
            cmd.Parameters.AddWithValue("@Modell", string.IsNullOrWhiteSpace(model.Modell) ? DBNull.Value : model.Modell.Trim());
            cmd.Parameters.AddWithValue("@Seriennummer", string.IsNullOrWhiteSpace(model.Seriennummer) ? DBNull.Value : model.Seriennummer.Trim());
            cmd.Parameters.AddWithValue("@Kaufdatum", model.Kaufdatum.HasValue ? model.Kaufdatum.Value.Date : DBNull.Value);
            cmd.Parameters.AddWithValue("@Kaufpreis", model.Kaufpreis.HasValue ? model.Kaufpreis.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Bemerkung", string.IsNullOrWhiteSpace(model.Bemerkung) ? DBNull.Value : model.Bemerkung.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void HaushaltObjektUpdate(HaushaltObjekt model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.HaushaltObjekt
SET
    RaumId = @RaumId,
    KategorieId = @KategorieId,
    ArbeitsanweisungId = @ArbeitsanweisungId,
    ZeitintervallId = @ZeitintervallId,
    VorlaufTage = @VorlaufTage,
    LetzteAusfuehrungAm = @LetzteAusfuehrungAm,

    Bezeichnung = @Bezeichnung,
    Kategorie = @Kategorie,
    IconKey = @IconKey,

    Hersteller = @Hersteller,
    Modell = @Modell,
    Seriennummer = @Seriennummer,
    Kaufdatum = @Kaufdatum,
    Kaufpreis = @Kaufpreis,
    Bemerkung = @Bemerkung,
    IstAktiv = @IstAktiv,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);

            cmd.Parameters.AddWithValue("@Id", model.Id);
            cmd.Parameters.AddWithValue("@RaumId", model.RaumId);
            cmd.Parameters.AddWithValue("@KategorieId", model.KategorieId.HasValue ? model.KategorieId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ArbeitsanweisungId", model.ArbeitsanweisungId.HasValue ? model.ArbeitsanweisungId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ZeitintervallId", model.ZeitintervallId.HasValue ? model.ZeitintervallId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@VorlaufTage", model.VorlaufTage);
            cmd.Parameters.AddWithValue("@LetzteAusfuehrungAm", model.LetzteAusfuehrungAm.HasValue ? model.LetzteAusfuehrungAm.Value.Date : DBNull.Value);

            cmd.Parameters.AddWithValue("@Bezeichnung", model.Bezeichnung.Trim());
            cmd.Parameters.AddWithValue("@Kategorie", string.IsNullOrWhiteSpace(model.Kategorie) ? DBNull.Value : model.Kategorie.Trim());
            cmd.Parameters.AddWithValue("@IconKey", string.IsNullOrWhiteSpace(model.IconKey) ? "PackageVariantClosed" : model.IconKey.Trim());

            cmd.Parameters.AddWithValue("@Hersteller", string.IsNullOrWhiteSpace(model.Hersteller) ? DBNull.Value : model.Hersteller.Trim());
            cmd.Parameters.AddWithValue("@Modell", string.IsNullOrWhiteSpace(model.Modell) ? DBNull.Value : model.Modell.Trim());
            cmd.Parameters.AddWithValue("@Seriennummer", string.IsNullOrWhiteSpace(model.Seriennummer) ? DBNull.Value : model.Seriennummer.Trim());
            cmd.Parameters.AddWithValue("@Kaufdatum", model.Kaufdatum.HasValue ? model.Kaufdatum.Value.Date : DBNull.Value);
            cmd.Parameters.AddWithValue("@Kaufpreis", model.Kaufpreis.HasValue ? model.Kaufpreis.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Bemerkung", string.IsNullOrWhiteSpace(model.Bemerkung) ? DBNull.Value : model.Bemerkung.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            cmd.ExecuteNonQuery();
        }


        public void HaushaltObjektLetzteAusfuehrungSetzen(int objektId, DateTime datum)
        {
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.HaushaltObjekt
SET
    LetzteAusfuehrungAm = @Datum,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", objektId);
            cmd.Parameters.AddWithValue("@Datum", datum.Date);

            cmd.ExecuteNonQuery();
        }


        public void HaushaltObjektDelete(int id)
        {
            EnsureHaushaltSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.HaushaltObjekt
SET
    IstAktiv = 0,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }
    }
}