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
                    Bezeichnung = r.GetString(3),
                    Kategorie = r.GetString(4),
                    IconKey = r.GetString(5),
                    Hersteller = r.GetString(6),
                    Modell = r.GetString(7),
                    Seriennummer = r.GetString(8),
                    Kaufdatum = r.IsDBNull(9) ? null : r.GetDateTime(9),
                    Kaufpreis = r.IsDBNull(10) ? null : r.GetDecimal(10),
                    Bemerkung = r.GetString(11),
                    IstAktiv = r.GetBoolean(12),
                    ErstelltAm = r.GetDateTime(13),
                    GeaendertAm = r.IsDBNull(14) ? null : r.GetDateTime(14)
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