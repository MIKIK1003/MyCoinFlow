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
        Valor NVARCHAR(50) NULL,
        Symbol NVARCHAR(50) NULL,
        Boerse NVARCHAR(50) NULL,
        Waehrung NVARCHAR(10) NOT NULL CONSTRAINT DF_VermoegenPosition_Waehrung DEFAULT ('CHF'),
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

IF COL_LENGTH('dbo.VermoegenPosition', 'Valor') IS NULL
BEGIN
    ALTER TABLE dbo.VermoegenPosition
    ADD Valor NVARCHAR(50) NULL;
END;

IF COL_LENGTH('dbo.VermoegenPosition', 'Symbol') IS NULL
BEGIN
    ALTER TABLE dbo.VermoegenPosition
    ADD Symbol NVARCHAR(50) NULL;
END;

IF COL_LENGTH('dbo.VermoegenPosition', 'Boerse') IS NULL
BEGIN
    ALTER TABLE dbo.VermoegenPosition
    ADD Boerse NVARCHAR(50) NULL;
END;

IF COL_LENGTH('dbo.VermoegenPosition', 'Waehrung') IS NULL
BEGIN
    ALTER TABLE dbo.VermoegenPosition
    ADD Waehrung NVARCHAR(10) NOT NULL
        CONSTRAINT DF_VermoegenPosition_Waehrung DEFAULT ('CHF');
END;

IF OBJECT_ID('dbo.VermoegenEinstellung', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.VermoegenEinstellung
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VermoegenEinstellung PRIMARY KEY,
        ApiProvider NVARCHAR(50) NOT NULL CONSTRAINT DF_VermoegenEinstellung_ApiProvider DEFAULT ('EODHD'),
        ApiKey NVARCHAR(500) NULL,
        Aktiv BIT NOT NULL CONSTRAINT DF_VermoegenEinstellung_Aktiv DEFAULT (1),
        ErstelltAm DATETIME2 NOT NULL CONSTRAINT DF_VermoegenEinstellung_ErstelltAm DEFAULT SYSUTCDATETIME()
    );

    INSERT INTO dbo.VermoegenEinstellung (ApiProvider, ApiKey, Aktiv)
    VALUES ('EODHD', NULL, 1);
END;

IF OBJECT_ID('dbo.VermoegenKursHistorie', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.VermoegenKursHistorie
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VermoegenKursHistorie PRIMARY KEY,
        PositionId INT NOT NULL,
        KursDatum DATE NOT NULL,
        Kurs DECIMAL(19,6) NOT NULL,
        Quelle NVARCHAR(50) NOT NULL,
        ErfasstAm DATETIME2 NOT NULL CONSTRAINT DF_VermoegenKursHistorie_ErfasstAm DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_VermoegenKursHistorie_Position
            FOREIGN KEY (PositionId) REFERENCES dbo.VermoegenPosition(Id)
    );
END;

IF OBJECT_ID('dbo.VermoegenFxHistorie', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.VermoegenFxHistorie
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VermoegenFxHistorie PRIMARY KEY,
        VonWaehrung NVARCHAR(10) NOT NULL,
        NachWaehrung NVARCHAR(10) NOT NULL,
        KursDatum DATE NOT NULL,
        Kurs DECIMAL(19,8) NOT NULL,
        Quelle NVARCHAR(50) NOT NULL,
        ErfasstAm DATETIME2 NOT NULL CONSTRAINT DF_VermoegenFxHistorie_ErfasstAm DEFAULT SYSUTCDATETIME()
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_VermoegenKursHistorie_Position_Datum'
      AND object_id = OBJECT_ID('dbo.VermoegenKursHistorie')
)
BEGIN
    CREATE UNIQUE INDEX UX_VermoegenKursHistorie_Position_Datum
    ON dbo.VermoegenKursHistorie(PositionId, KursDatum);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_VermoegenFxHistorie_Paar_Datum'
      AND object_id = OBJECT_ID('dbo.VermoegenFxHistorie')
)
BEGIN
    CREATE UNIQUE INDEX UX_VermoegenFxHistorie_Paar_Datum
    ON dbo.VermoegenFxHistorie(VonWaehrung, NachWaehrung, KursDatum);
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
    ISNULL(p.Valor, '') AS Valor,
    ISNULL(p.Symbol, '') AS Symbol,
    ISNULL(p.Boerse, '') AS Boerse,
    ISNULL(p.Waehrung, 'CHF') AS Waehrung,
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
                    Valor = r.GetString(5),
                    Symbol = r.GetString(6),
                    Boerse = r.GetString(7),
                    Waehrung = r.GetString(8),
                    Anlageklasse = r.GetString(9),
                    Anzahl = r.GetDecimal(10),
                    Einstandspreis = r.GetDecimal(11),
                    EinstandDatum = r.IsDBNull(12) ? null : r.GetDateTime(12),
                    AktuellerKurs = r.IsDBNull(13) ? null : r.GetDecimal(13),
                    KursDatum = r.IsDBNull(14) ? null : r.GetDateTime(14),
                    Notiz = r.IsDBNull(15) ? "" : r.GetString(15),
                    IstAktiv = r.GetBoolean(16)
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
    Valor,
    Symbol,
    Boerse,
    Waehrung,
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
    @Valor,
    @Symbol,
    @Boerse,
    @Waehrung,
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
            cmd.Parameters.AddWithValue("@Valor", string.IsNullOrWhiteSpace(model.Valor) ? DBNull.Value : model.Valor.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("@Symbol", string.IsNullOrWhiteSpace(model.Symbol) ? DBNull.Value : model.Symbol.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("@Boerse", string.IsNullOrWhiteSpace(model.Boerse) ? DBNull.Value : model.Boerse.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("@Waehrung", string.IsNullOrWhiteSpace(model.Waehrung) ? "CHF" : model.Waehrung.Trim().ToUpperInvariant());
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
    Valor = @Valor,
    Symbol = @Symbol,
    Boerse = @Boerse,
    Waehrung = @Waehrung,
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
            cmd.Parameters.AddWithValue("@Valor", string.IsNullOrWhiteSpace(model.Valor) ? DBNull.Value : model.Valor.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("@Symbol", string.IsNullOrWhiteSpace(model.Symbol) ? DBNull.Value : model.Symbol.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("@Boerse", string.IsNullOrWhiteSpace(model.Boerse) ? DBNull.Value : model.Boerse.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("@Waehrung", string.IsNullOrWhiteSpace(model.Waehrung) ? "CHF" : model.Waehrung.Trim().ToUpperInvariant());
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

        public VermoegenApiEinstellung VermoegenApiEinstellungGet()
        {
            EnsureVermoegenSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT TOP 1
    Id,
    ApiProvider,
    ApiKey,
    Aktiv
FROM dbo.VermoegenEinstellung
ORDER BY Id;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            if (r.Read())
            {
                return new VermoegenApiEinstellung
                {
                    Id = r.GetInt32(0),
                    ApiProvider = r.IsDBNull(1) ? "EODHD" : r.GetString(1),
                    ApiKey = r.IsDBNull(2) ? "" : r.GetString(2),
                    Aktiv = r.GetBoolean(3)
                };
            }

            return new VermoegenApiEinstellung();
        }

        public void VermoegenApiEinstellungSave(VermoegenApiEinstellung model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            EnsureVermoegenSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.VermoegenEinstellung WHERE Id = @Id)
BEGIN
    UPDATE dbo.VermoegenEinstellung
    SET
        ApiProvider = @ApiProvider,
        ApiKey = @ApiKey,
        Aktiv = @Aktiv
    WHERE Id = @Id;
END
ELSE
BEGIN
    INSERT INTO dbo.VermoegenEinstellung (ApiProvider, ApiKey, Aktiv)
    VALUES (@ApiProvider, @ApiKey, @Aktiv);
END;";

            using var cmd = new SqlCommand(sql, c);

            cmd.Parameters.AddWithValue("@Id", model.Id);
            cmd.Parameters.AddWithValue("@ApiProvider", string.IsNullOrWhiteSpace(model.ApiProvider) ? "EODHD" : model.ApiProvider.Trim());
            cmd.Parameters.AddWithValue("@ApiKey", string.IsNullOrWhiteSpace(model.ApiKey) ? DBNull.Value : model.ApiKey.Trim());
            cmd.Parameters.AddWithValue("@Aktiv", model.Aktiv);

            cmd.ExecuteNonQuery();
        }

        public void VermoegenPositionKursUpdate(int id, decimal aktuellerKurs, DateTime kursDatum)
        {
            EnsureVermoegenSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.VermoegenPosition
SET
    AktuellerKurs = @AktuellerKurs,
    KursDatum = @KursDatum
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@AktuellerKurs", aktuellerKurs);
            cmd.Parameters.AddWithValue("@KursDatum", kursDatum.Date);

            cmd.ExecuteNonQuery();
        }

        public void VermoegenKursHistorieInsertIfMissing(int positionId, DateTime kursDatum, decimal kurs, string quelle)
        {
            EnsureVermoegenSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
IF NOT EXISTS (
    SELECT 1
    FROM dbo.VermoegenKursHistorie
    WHERE PositionId = @PositionId
      AND KursDatum = @KursDatum
)
BEGIN
    INSERT INTO dbo.VermoegenKursHistorie
    (
        PositionId,
        KursDatum,
        Kurs,
        Quelle
    )
    VALUES
    (
        @PositionId,
        @KursDatum,
        @Kurs,
        @Quelle
    );
END;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@PositionId", positionId);
            cmd.Parameters.AddWithValue("@KursDatum", kursDatum.Date);
            cmd.Parameters.AddWithValue("@Kurs", kurs);
            cmd.Parameters.AddWithValue("@Quelle", string.IsNullOrWhiteSpace(quelle) ? "Unbekannt" : quelle.Trim());

            cmd.ExecuteNonQuery();
        }

        public List<VermoegenKursHistorie> VermoegenKursHistorieGetByPosition(int positionId)
        {
            EnsureVermoegenSchema();

            var list = new List<VermoegenKursHistorie>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    Id,
    PositionId,
    KursDatum,
    Kurs,
    Quelle,
    ErfasstAm
FROM dbo.VermoegenKursHistorie
WHERE PositionId = @PositionId
ORDER BY KursDatum DESC;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@PositionId", positionId);

            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new VermoegenKursHistorie
                {
                    Id = r.GetInt32(0),
                    PositionId = r.GetInt32(1),
                    KursDatum = r.GetDateTime(2),
                    Kurs = r.GetDecimal(3),
                    Quelle = r.IsDBNull(4) ? "" : r.GetString(4),
                    ErfasstAm = r.GetDateTime(5)
                });
            }

            return list;
        }

        public void VermoegenFxHistorieInsertIfMissing(
    string vonWaehrung,
    string nachWaehrung,
    DateTime kursDatum,
    decimal kurs,
    string quelle)
        {
            EnsureVermoegenSchema();

            var von = string.IsNullOrWhiteSpace(vonWaehrung)
                ? ""
                : vonWaehrung.Trim().ToUpperInvariant();

            var nach = string.IsNullOrWhiteSpace(nachWaehrung)
                ? ""
                : nachWaehrung.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(von) || string.IsNullOrWhiteSpace(nach))
                return;

            if (kurs <= 0)
                return;

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
IF NOT EXISTS (
    SELECT 1
    FROM dbo.VermoegenFxHistorie
    WHERE VonWaehrung = @VonWaehrung
      AND NachWaehrung = @NachWaehrung
      AND KursDatum = @KursDatum
)
BEGIN
    INSERT INTO dbo.VermoegenFxHistorie
    (
        VonWaehrung,
        NachWaehrung,
        KursDatum,
        Kurs,
        Quelle
    )
    VALUES
    (
        @VonWaehrung,
        @NachWaehrung,
        @KursDatum,
        @Kurs,
        @Quelle
    );
END;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@VonWaehrung", von);
            cmd.Parameters.AddWithValue("@NachWaehrung", nach);
            cmd.Parameters.AddWithValue("@KursDatum", kursDatum.Date);
            cmd.Parameters.AddWithValue("@Kurs", kurs);
            cmd.Parameters.AddWithValue("@Quelle", string.IsNullOrWhiteSpace(quelle) ? "Unbekannt" : quelle.Trim());

            cmd.ExecuteNonQuery();
        }

        public VermoegenFxHistorie? VermoegenFxHistorieGetLatest(
            string vonWaehrung,
            string nachWaehrung)
        {
            EnsureVermoegenSchema();

            var von = string.IsNullOrWhiteSpace(vonWaehrung)
                ? ""
                : vonWaehrung.Trim().ToUpperInvariant();

            var nach = string.IsNullOrWhiteSpace(nachWaehrung)
                ? ""
                : nachWaehrung.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(von) || string.IsNullOrWhiteSpace(nach))
                return null;

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT TOP 1
    Id,
    VonWaehrung,
    NachWaehrung,
    KursDatum,
    Kurs,
    Quelle,
    ErfasstAm
FROM dbo.VermoegenFxHistorie
WHERE VonWaehrung = @VonWaehrung
  AND NachWaehrung = @NachWaehrung
ORDER BY KursDatum DESC;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@VonWaehrung", von);
            cmd.Parameters.AddWithValue("@NachWaehrung", nach);

            using var r = cmd.ExecuteReader();

            if (!r.Read())
                return null;

            return new VermoegenFxHistorie
            {
                Id = r.GetInt32(0),
                VonWaehrung = r.GetString(1),
                NachWaehrung = r.GetString(2),
                KursDatum = r.GetDateTime(3),
                Kurs = r.GetDecimal(4),
                Quelle = r.GetString(5),
                ErfasstAm = r.GetDateTime(6)
            };
        }

    }

}