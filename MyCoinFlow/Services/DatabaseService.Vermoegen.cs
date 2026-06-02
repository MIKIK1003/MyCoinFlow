using Microsoft.Data.SqlClient;
using MyCoinFlow.Models;
using System;
using System.Collections.Generic;

namespace MyCoinFlow.Services
{
    public partial class DatabaseService
    {
        public void EnsureVermoegenSchema()
        {
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
IF OBJECT_ID('dbo.VermoegenDepot', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.VermoegenDepot
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VermoegenDepot PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL,
        Institut NVARCHAR(200) NULL,
        Waehrung NVARCHAR(10) NOT NULL CONSTRAINT DF_VermoegenDepot_Waehrung DEFAULT ('CHF'),
        IstAktiv BIT NOT NULL CONSTRAINT DF_VermoegenDepot_IstAktiv DEFAULT (1),
        ErstelltAm DATETIME2 NOT NULL CONSTRAINT DF_VermoegenDepot_ErstelltAm DEFAULT SYSUTCDATETIME()
    );
END;

IF COL_LENGTH('dbo.VermoegenDepot', 'GeldinstitutId') IS NULL
BEGIN
    ALTER TABLE dbo.VermoegenDepot
    ADD GeldinstitutId INT NULL;
END;

IF OBJECT_ID('dbo.VermoegenPosition', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.VermoegenPosition
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VermoegenPosition PRIMARY KEY,
        DepotId INT NOT NULL,

        Titel NVARCHAR(250) NOT NULL,
        ISIN NVARCHAR(20) NULL,
        Anlageklasse NVARCHAR(50) NOT NULL CONSTRAINT DF_VermoegenPosition_Anlageklasse DEFAULT ('Aktie'),

        Anzahl DECIMAL(28,8) NOT NULL,
        Einstandspreis DECIMAL(19,6) NOT NULL,
        EinstandDatum DATE NULL,

        AktuellerKurs DECIMAL(19,6) NULL,
        KursDatum DATE NULL,

        Notiz NVARCHAR(500) NULL,
        IstAktiv BIT NOT NULL CONSTRAINT DF_VermoegenPosition_IstAktiv DEFAULT (1),
        ErstelltAm DATETIME2 NOT NULL CONSTRAINT DF_VermoegenPosition_ErstelltAm DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_VermoegenPosition_Depot
            FOREIGN KEY (DepotId) REFERENCES dbo.VermoegenDepot(Id)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_VermoegenPosition_DepotId'
      AND object_id = OBJECT_ID('dbo.VermoegenPosition')
)
BEGIN
    CREATE INDEX IX_VermoegenPosition_DepotId
    ON dbo.VermoegenPosition(DepotId);
END;
";
            using var cmd = new SqlCommand(sql, c);
            cmd.ExecuteNonQuery();
        }

        public List<VermoegenDepot> VermoegenDepotsGetAll()
        {
            EnsureVermoegenSchema();

            var list = new List<VermoegenDepot>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    d.Id,
    d.GeldinstitutId,
    ISNULL(g.Name, '') AS GeldinstitutName,
    d.Name,
    d.Institut,
    d.Waehrung,
    d.IstAktiv
FROM dbo.VermoegenDepot d
LEFT JOIN dbo.Geldinstitut g ON g.Id = d.GeldinstitutId
ORDER BY d.Name;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new VermoegenDepot
                {
                    Id = r.GetInt32(0),
                    GeldinstitutId = r.IsDBNull(1) ? null : r.GetInt32(1),
                    GeldinstitutName = r.GetString(2),
                    Name = r.GetString(3),
                    Institut = r.IsDBNull(4) ? "" : r.GetString(4),
                    Waehrung = r.GetString(5),
                    IstAktiv = r.GetBoolean(6)
                });
            }

            return list;
        }

        public int VermoegenDepotInsert(VermoegenDepot model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureVermoegenSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.VermoegenDepot
(
    GeldinstitutId,
    Name,
    Institut,
    Waehrung,
    IstAktiv
)
OUTPUT INSERTED.Id
VALUES
(
    @GeldinstitutId,
    @Name,
    @Institut,
    @Waehrung,
    @IstAktiv
);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@GeldinstitutId", model.GeldinstitutId.HasValue ? model.GeldinstitutId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Name", model.Name.Trim());
            cmd.Parameters.AddWithValue("@Institut", string.IsNullOrWhiteSpace(model.Institut) ? DBNull.Value : model.Institut.Trim());
            cmd.Parameters.AddWithValue("@Waehrung", string.IsNullOrWhiteSpace(model.Waehrung) ? "CHF" : model.Waehrung.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<VermoegenPosition> VermoegenPositionenGetAll()
        {
            EnsureVermoegenSchema();

            var list = new List<VermoegenPosition>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    p.Id,
    p.DepotId,
    d.Name AS DepotName,
    p.Titel,
    p.ISIN,
    p.Anlageklasse,
    p.Anzahl,
    p.Einstandspreis,
    p.EinstandDatum,
    p.AktuellerKurs,
    p.KursDatum,
    p.Notiz,
    p.IstAktiv
FROM dbo.VermoegenPosition p
JOIN dbo.VermoegenDepot d ON d.Id = p.DepotId
ORDER BY d.Name, p.Titel;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new VermoegenPosition
                {
                    Id = r.GetInt32(0),
                    DepotId = r.GetInt32(1),
                    DepotName = r.GetString(2),
                    Titel = r.GetString(3),
                    ISIN = r.IsDBNull(4) ? "" : r.GetString(4),
                    Anlageklasse = r.GetString(5),
                    Anzahl = r.GetDecimal(6),
                    Einstandspreis = r.GetDecimal(7),
                    EinstandDatum = r.IsDBNull(8) ? null : r.GetDateTime(8),
                    AktuellerKurs = r.IsDBNull(9) ? null : r.GetDecimal(9),
                    KursDatum = r.IsDBNull(10) ? null : r.GetDateTime(10),
                    Notiz = r.IsDBNull(11) ? "" : r.GetString(11),
                    IstAktiv = r.GetBoolean(12)
                });
            }

            return list;
        }

        public void VermoegenDepotUpdate(VermoegenDepot model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureVermoegenSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.VermoegenDepot
SET
    GeldinstitutId = @GeldinstitutId,
    Name = @Name,
    Institut = @Institut,
    Waehrung = @Waehrung,
    IstAktiv = @IstAktiv
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", model.Id);
            cmd.Parameters.AddWithValue("@GeldinstitutId", model.GeldinstitutId.HasValue ? model.GeldinstitutId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Name", model.Name.Trim());
            cmd.Parameters.AddWithValue("@Institut", string.IsNullOrWhiteSpace(model.Institut) ? DBNull.Value : model.Institut.Trim());
            cmd.Parameters.AddWithValue("@Waehrung", string.IsNullOrWhiteSpace(model.Waehrung) ? "CHF" : model.Waehrung.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            cmd.ExecuteNonQuery();
        }

        public void VermoegenDepotDelete(int id)
        {
            EnsureVermoegenSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.VermoegenDepot
SET IstAktiv = 0
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }

        public List<VermoegenGeldinstitutAuswahl> VermoegenGeldinstituteGetForAuswahl()
        {
            var list = new List<VermoegenGeldinstitutAuswahl>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT Id, Name, IBAN
FROM dbo.Geldinstitut
ORDER BY Name;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new VermoegenGeldinstitutAuswahl
                {
                    Id = r.GetInt32(0),
                    Name = r.IsDBNull(1) ? "" : r.GetString(1),
                    IBAN = r.IsDBNull(2) ? "" : r.GetString(2)
                });
            }

            return list;
        }

        public int VermoegenPositionInsert(VermoegenPosition model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureVermoegenSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.VermoegenPosition
(
    DepotId,
    Titel,
    ISIN,
    Anlageklasse,
    Anzahl,
    Einstandspreis,
    EinstandDatum,
    AktuellerKurs,
    KursDatum,
    Notiz,
    IstAktiv
)
OUTPUT INSERTED.Id
VALUES
(
    @DepotId,
    @Titel,
    @ISIN,
    @Anlageklasse,
    @Anzahl,
    @Einstandspreis,
    @EinstandDatum,
    @AktuellerKurs,
    @KursDatum,
    @Notiz,
    @IstAktiv
);";

            using var cmd = new SqlCommand(sql, c);

            cmd.Parameters.AddWithValue("@DepotId", model.DepotId);
            cmd.Parameters.AddWithValue("@Titel", model.Titel.Trim());
            cmd.Parameters.AddWithValue("@ISIN", string.IsNullOrWhiteSpace(model.ISIN) ? DBNull.Value : model.ISIN.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("@Anlageklasse", string.IsNullOrWhiteSpace(model.Anlageklasse) ? "Aktie" : model.Anlageklasse.Trim());
            cmd.Parameters.AddWithValue("@Anzahl", model.Anzahl);
            cmd.Parameters.AddWithValue("@Einstandspreis", model.Einstandspreis);
            cmd.Parameters.AddWithValue("@EinstandDatum", model.EinstandDatum.HasValue ? model.EinstandDatum.Value.Date : DBNull.Value);
            cmd.Parameters.AddWithValue("@AktuellerKurs", model.AktuellerKurs.HasValue ? model.AktuellerKurs.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@KursDatum", model.KursDatum.HasValue ? model.KursDatum.Value.Date : DBNull.Value);
            cmd.Parameters.AddWithValue("@Notiz", string.IsNullOrWhiteSpace(model.Notiz) ? DBNull.Value : model.Notiz.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void VermoegenPositionUpdate(VermoegenPosition model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureVermoegenSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.VermoegenPosition
SET
    DepotId = @DepotId,
    Titel = @Titel,
    ISIN = @ISIN,
    Anlageklasse = @Anlageklasse,
    Anzahl = @Anzahl,
    Einstandspreis = @Einstandspreis,
    EinstandDatum = @EinstandDatum,
    AktuellerKurs = @AktuellerKurs,
    KursDatum = @KursDatum,
    Notiz = @Notiz,
    IstAktiv = @IstAktiv
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);

            cmd.Parameters.AddWithValue("@Id", model.Id);
            cmd.Parameters.AddWithValue("@DepotId", model.DepotId);
            cmd.Parameters.AddWithValue("@Titel", model.Titel.Trim());
            cmd.Parameters.AddWithValue("@ISIN", string.IsNullOrWhiteSpace(model.ISIN) ? DBNull.Value : model.ISIN.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("@Anlageklasse", string.IsNullOrWhiteSpace(model.Anlageklasse) ? "Aktie" : model.Anlageklasse.Trim());
            cmd.Parameters.AddWithValue("@Anzahl", model.Anzahl);
            cmd.Parameters.AddWithValue("@Einstandspreis", model.Einstandspreis);
            cmd.Parameters.AddWithValue("@EinstandDatum", model.EinstandDatum.HasValue ? model.EinstandDatum.Value.Date : DBNull.Value);
            cmd.Parameters.AddWithValue("@AktuellerKurs", model.AktuellerKurs.HasValue ? model.AktuellerKurs.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@KursDatum", model.KursDatum.HasValue ? model.KursDatum.Value.Date : DBNull.Value);
            cmd.Parameters.AddWithValue("@Notiz", string.IsNullOrWhiteSpace(model.Notiz) ? DBNull.Value : model.Notiz.Trim());
            cmd.Parameters.AddWithValue("@IstAktiv", model.IstAktiv);

            cmd.ExecuteNonQuery();
        }

        public void VermoegenPositionDelete(int id)
        {
            EnsureVermoegenSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.VermoegenPosition
SET IstAktiv = 0
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }


    }

}