using Microsoft.Data.SqlClient;
using MyCoinFlow.Models;
using System;
using System.Collections.Generic;

namespace MyCoinFlow.Services
{
    public partial class DatabaseService
    {
        public void EnsureAboSchema()
        {
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
IF OBJECT_ID('dbo.Abo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Abo
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Abo PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL,
        AdresseId INT NULL,
        Periodizitaet NVARCHAR(30) NOT NULL CONSTRAINT DF_Abo_Periodizitaet DEFAULT ('Monatlich'),
        Richtung NVARCHAR(20) NOT NULL CONSTRAINT DF_Abo_Richtung DEFAULT ('Unklar'),
        Kategorie NVARCHAR(80) NOT NULL CONSTRAINT DF_Abo_Kategorie DEFAULT ('Pruefen'),
        ErwarteterBetrag DECIMAL(19,2) NULL,
        BetragToleranzProzent DECIMAL(5,2) NOT NULL CONSTRAINT DF_Abo_Toleranz DEFAULT (10),
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_Abo_Status DEFAULT ('Aktiv'),
        GekuendigtAm DATE NULL,
        KuendigungsfristTage INT NULL,
        VorwarnTage INT NOT NULL CONSTRAINT DF_Abo_VorwarnTage DEFAULT (7),
        ErwartetesKontoId INT NULL,
        WebseiteUrl NVARCHAR(500) NULL,
        Kuendigungsweg NVARCHAR(300) NULL,
        Notiz NVARCHAR(1000) NULL,
        ErstelltAm DATETIME2 NOT NULL CONSTRAINT DF_Abo_ErstelltAm DEFAULT SYSUTCDATETIME(),
        GeaendertAm DATETIME2 NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Abo_Adresse')
   AND OBJECT_ID('dbo.Adresse', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.Abo
    ADD CONSTRAINT FK_Abo_Adresse
        FOREIGN KEY (AdresseId) REFERENCES dbo.Adresse(Id);
END;

IF OBJECT_ID('dbo.AboTransaktion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AboTransaktion
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AboTransaktion PRIMARY KEY,
        AboId INT NOT NULL,
        TransaktionId INT NOT NULL,
        ManuellZugeordnet BIT NOT NULL CONSTRAINT DF_AboTransaktion_Manuell DEFAULT (0),
        IstEinmalig BIT NOT NULL CONSTRAINT DF_AboTransaktion_IstEinmalig DEFAULT (0),
        ErstelltAm DATETIME2 NOT NULL CONSTRAINT DF_AboTransaktion_ErstelltAm DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_AboTransaktion_Abo
            FOREIGN KEY (AboId) REFERENCES dbo.Abo(Id) ON DELETE CASCADE,
        CONSTRAINT FK_AboTransaktion_Transaktion
            FOREIGN KEY (TransaktionId) REFERENCES dbo.Transaktion(Id),
        CONSTRAINT UQ_AboTransaktion_Transaktion UNIQUE (TransaktionId)
    );
END;

IF COL_LENGTH('dbo.Abo', 'KuendigenZum') IS NULL
BEGIN
    ALTER TABLE dbo.Abo
    ADD KuendigenZum DATE NULL;
END;

IF COL_LENGTH('dbo.Abo', 'Kategorie') IS NULL
BEGIN
    ALTER TABLE dbo.Abo
    ADD Kategorie NVARCHAR(80) NOT NULL CONSTRAINT DF_Abo_Kategorie_Migration DEFAULT ('Pruefen');
END;

IF COL_LENGTH('dbo.Abo', 'Kategorie') IS NOT NULL AND COL_LENGTH('dbo.Abo', 'Kategorie') < 160
BEGIN
    ALTER TABLE dbo.Abo ALTER COLUMN Kategorie NVARCHAR(80) NOT NULL;
END;

IF COL_LENGTH('dbo.Abo', 'Richtung') IS NULL
BEGIN
    ALTER TABLE dbo.Abo
    ADD Richtung NVARCHAR(20) NOT NULL CONSTRAINT DF_Abo_Richtung_Migration DEFAULT ('Unklar');
END;

IF COL_LENGTH('dbo.Abo', 'Kuendigungsweg') IS NULL
BEGIN
    ALTER TABLE dbo.Abo
    ADD Kuendigungsweg NVARCHAR(300) NULL;
END;

IF COL_LENGTH('dbo.AboTransaktion', 'IstEinmalig') IS NULL
BEGIN
    ALTER TABLE dbo.AboTransaktion
    ADD IstEinmalig BIT NOT NULL CONSTRAINT DF_AboTransaktion_IstEinmalig_Migration DEFAULT (0);
END;

IF OBJECT_ID('dbo.AboKandidatAusschluss', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AboKandidatAusschluss
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AboKandidatAusschluss PRIMARY KEY,
        AdresseId INT NOT NULL,
        Periodizitaet NVARCHAR(30) NOT NULL,
        ReferenzBetrag DECIMAL(19,2) NOT NULL,
        ErstelltAm DATETIME2 NOT NULL CONSTRAINT DF_AboKandidatAusschluss_ErstelltAm DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_AboKandidatAusschluss_Muster UNIQUE (AdresseId, Periodizitaet, ReferenzBetrag)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AboKandidatAusschluss_AdresseId' AND object_id = OBJECT_ID('dbo.AboKandidatAusschluss'))
BEGIN
    CREATE INDEX IX_AboKandidatAusschluss_AdresseId ON dbo.AboKandidatAusschluss (AdresseId);
END;

IF OBJECT_ID('dbo.AboKategorie', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AboKategorie
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AboKategorie PRIMARY KEY,
        Code NVARCHAR(80) NOT NULL,
        Bezeichnung NVARCHAR(120) NOT NULL,
        Beschreibung NVARCHAR(300) NULL,
        FarbeHex NVARCHAR(20) NOT NULL CONSTRAINT DF_AboKategorie_Farbe DEFAULT ('#5B2DA9'),
        Sortierung INT NOT NULL CONSTRAINT DF_AboKategorie_Sortierung DEFAULT (100),
        IstSystem BIT NOT NULL CONSTRAINT DF_AboKategorie_IstSystem DEFAULT (0),
        IstAktiv BIT NOT NULL CONSTRAINT DF_AboKategorie_IstAktiv DEFAULT (1),
        ErstelltAm DATETIME2 NOT NULL CONSTRAINT DF_AboKategorie_ErstelltAm DEFAULT SYSUTCDATETIME(),
        GeaendertAm DATETIME2 NULL,
        CONSTRAINT UQ_AboKategorie_Code UNIQUE (Code)
    );
END;

DECLARE @Defaults TABLE
(
    Code NVARCHAR(80), Bezeichnung NVARCHAR(120), Beschreibung NVARCHAR(300),
    FarbeHex NVARCHAR(20), Sortierung INT, IstAktiv BIT
);
INSERT INTO @Defaults VALUES
    ('Wohnen', N'Wohnen & Immobilien', N'Miete, Pacht, Nebenkosten und Immobilien', '#2F7D6D', 10, 1),
    ('Versicherung', N'Versicherungen', N'Policen, Krankenkassen und weitere Versicherungen', '#3867A8', 20, 1),
    ('Telekommunikation', N'Telekommunikation & Internet', N'Mobilfunk, Internet und Kommunikationsdienste', '#1B8396', 30, 1),
    ('Mitgliedschaft', N'Mitgliedschaften', N'Vereine, Fitness und wiederkehrende Mitgliedsbeiträge', '#8C5AB5', 40, 1),
    ('Finanzierung', N'Finanzierung & Kredite', N'Leasing, Darlehen, Hypotheken und Kredite', '#A45A44', 50, 1),
    ('SteuernGebuehren', N'Steuern & Gebühren', N'Regelmässige Steuern und öffentliche Gebühren', '#8A6540', 60, 1),
    ('VorsorgeSparen', N'Sparen & Vorsorge', N'Sparpläne, Vorsorge und regelmässige Rücklagen', '#3C7C54', 70, 1),
    ('Dienstleistung', N'Dienstleistungen', N'Regelmässig bezogene Dienstleistungen', '#70628F', 80, 1),
    ('Vertrag', N'Verträge', N'Weitere vertraglich wiederkehrende Zahlungen', '#B06C1F', 90, 1),
    ('SoftwareLizenz', N'Lizenzen & Software', N'Apps, Cloud-Dienste und digitale Lizenzen', '#167E91', 100, 1),
    ('Streaming', N'Streaming', N'Video, Musik, Games und digitale Inhalte', '#684CB9', 110, 1),
    ('Sonstige', N'Sonstige Serien', N'Weitere regelmässige Einnahmen und Ausgaben', '#536274', 900, 1),
    ('Pruefen', N'Noch nicht kategorisiert', N'Bestehende Serien mit offener Einordnung', '#B06C1F', 990, 0);

INSERT INTO dbo.AboKategorie (Code, Bezeichnung, Beschreibung, FarbeHex, Sortierung, IstSystem, IstAktiv)
SELECT d.Code, d.Bezeichnung, d.Beschreibung, d.FarbeHex, d.Sortierung, 1, d.IstAktiv
FROM @Defaults d
WHERE NOT EXISTS (SELECT 1 FROM dbo.AboKategorie k WHERE k.Code = d.Code);";

            using var cmd = new SqlCommand(sql, c);
            cmd.ExecuteNonQuery();
        }

        public List<Abo> AbosLaden()
        {
            EnsureAboSchema();

            var result = new List<Abo>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    ab.Id, ab.Name, ab.AdresseId, a.Name AS AdresseName,
    ab.Periodizitaet, ab.Richtung, ab.Kategorie, k.Bezeichnung AS KategorieBezeichnung,
    ab.ErwarteterBetrag, ab.BetragToleranzProzent,
    ab.Status, ab.GekuendigtAm, ab.KuendigungsfristTage, ab.VorwarnTage,
    ab.ErwartetesKontoId, ab.WebseiteUrl, ab.Kuendigungsweg, ab.Notiz, ab.KuendigenZum
FROM dbo.Abo ab
LEFT JOIN dbo.Adresse a ON a.Id = ab.AdresseId
LEFT JOIN dbo.AboKategorie k ON k.Code = ab.Kategorie
ORDER BY ab.Name;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                var abo = new Abo
                {
                    Id = r.GetInt32(0),
                    Name = r.GetString(1),
                    AdresseId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                    AdresseName = r.IsDBNull(3) ? null : r.GetString(3),
                    Periodizitaet = r.GetString(4),
                    Richtung = r.IsDBNull(5) ? Zahlungsrichtungen.Unklar : r.GetString(5),
                    Kategorie = r.IsDBNull(6) ? AboKategorien.Pruefen : r.GetString(6),
                    KategorieBezeichnung = r.IsDBNull(7) ? null : r.GetString(7),
                    ErwarteterBetrag = r.IsDBNull(8) ? (decimal?)null : r.GetDecimal(8),
                    BetragToleranzProzent = r.GetDecimal(9),
                    Status = r.GetString(10),
                    GekuendigtAm = r.IsDBNull(11) ? (DateTime?)null : r.GetDateTime(11),
                    KuendigungsfristTage = r.IsDBNull(12) ? (int?)null : r.GetInt32(12),
                    VorwarnTage = r.GetInt32(13),
                    ErwartetesKontoId = r.IsDBNull(14) ? (int?)null : r.GetInt32(14),
                    WebseiteUrl = r.IsDBNull(15) ? null : r.GetString(15),
                    Kuendigungsweg = r.IsDBNull(16) ? null : r.GetString(16),
                    Notiz = r.IsDBNull(17) ? null : r.GetString(17),
                    KuendigenZum = r.IsDBNull(18) ? (DateTime?)null : r.GetDateTime(18)
                };

                if (abo.Kategorie == AboKategorien.Pruefen)
                {
                    abo.Kategorie = AboErkennungService.KategorieErmitteln(
                        $"{abo.Name} {abo.AdresseName} {abo.Notiz}");
                    abo.KategorieBezeichnung = AboKategorien.Anzeige(abo.Kategorie);
                }

                result.Add(abo);
            }

            return result;
        }

        public List<AboKategorie> AboKategorienLaden(bool includeInactive = false)
        {
            EnsureAboSchema();
            var result = new List<AboKategorie>();
            using var c = CreateConnection();
            c.Open();
            const string sql = @"
SELECT k.Id, k.Code, k.Bezeichnung, ISNULL(k.Beschreibung, ''), k.FarbeHex,
       k.Sortierung, k.IstSystem, k.IstAktiv,
       (SELECT COUNT(*) FROM dbo.Abo ab WHERE ab.Kategorie = k.Code) AS AnzahlSerien
FROM dbo.AboKategorie k
WHERE @includeInactive = 1 OR k.IstAktiv = 1
ORDER BY k.Sortierung, k.Bezeichnung;";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@includeInactive", includeInactive);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new AboKategorie
                {
                    Id = reader.GetInt32(0),
                    Code = reader.GetString(1),
                    Bezeichnung = reader.GetString(2),
                    Beschreibung = reader.GetString(3),
                    FarbeHex = reader.GetString(4),
                    Sortierung = reader.GetInt32(5),
                    IstSystem = reader.GetBoolean(6),
                    IstAktiv = reader.GetBoolean(7),
                    AnzahlSerien = reader.GetInt32(8)
                });
            }
            return result;
        }

        public int AboKategorieInsert(AboKategorie category)
        {
            EnsureAboSchema();
            using var c = CreateConnection();
            c.Open();
            const string sql = @"
INSERT INTO dbo.AboKategorie
    (Code, Bezeichnung, Beschreibung, FarbeHex, Sortierung, IstSystem, IstAktiv)
VALUES
    (@code, @name, @description, @color, @sort, 0, @active);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            using var cmd = new SqlCommand(sql, c);
            category.Code = string.IsNullOrWhiteSpace(category.Code)
                ? "Custom_" + Guid.NewGuid().ToString("N")
                : category.Code.Trim();
            FillAboCategoryParams(cmd, category);
            return (int)cmd.ExecuteScalar()!;
        }

        public void AboKategorieUpdate(AboKategorie category)
        {
            EnsureAboSchema();
            using var c = CreateConnection();
            c.Open();
            const string sql = @"
UPDATE dbo.AboKategorie SET
    Bezeichnung = @name,
    Beschreibung = @description,
    FarbeHex = @color,
    Sortierung = @sort,
    IstAktiv = @active,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @id;";
            using var cmd = new SqlCommand(sql, c);
            FillAboCategoryParams(cmd, category);
            cmd.Parameters.AddWithValue("@id", category.Id);
            cmd.ExecuteNonQuery();
        }

        public void AboKategorieDelete(int id)
        {
            EnsureAboSchema();
            using var c = CreateConnection();
            c.Open();
            using var transaction = c.BeginTransaction();
            try
            {
                string code;
                bool isSystem;
                using (var find = new SqlCommand("SELECT Code, IstSystem FROM dbo.AboKategorie WHERE Id = @id;", c, transaction))
                {
                    find.Parameters.AddWithValue("@id", id);
                    using var reader = find.ExecuteReader();
                    if (!reader.Read())
                        return;
                    code = reader.GetString(0);
                    isSystem = reader.GetBoolean(1);
                }
                if (isSystem)
                    throw new InvalidOperationException("Vordefinierte Kategorien können umbenannt, aber nicht gelöscht werden.");

                using (var reassign = new SqlCommand("UPDATE dbo.Abo SET Kategorie = @fallback WHERE Kategorie = @code;", c, transaction))
                {
                    reassign.Parameters.AddWithValue("@fallback", AboKategorien.Sonstige);
                    reassign.Parameters.AddWithValue("@code", code);
                    reassign.ExecuteNonQuery();
                }
                using (var delete = new SqlCommand("DELETE FROM dbo.AboKategorie WHERE Id = @id;", c, transaction))
                {
                    delete.Parameters.AddWithValue("@id", id);
                    delete.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static void FillAboCategoryParams(SqlCommand cmd, AboKategorie category)
        {
            cmd.Parameters.AddWithValue("@code", category.Code);
            cmd.Parameters.AddWithValue("@name", category.Bezeichnung.Trim());
            cmd.Parameters.AddWithValue("@description", string.IsNullOrWhiteSpace(category.Beschreibung) ? DBNull.Value : category.Beschreibung.Trim());
            cmd.Parameters.AddWithValue("@color", category.FarbeHex.Trim());
            cmd.Parameters.AddWithValue("@sort", category.Sortierung);
            cmd.Parameters.AddWithValue("@active", category.IstAktiv);
        }

        public int AboInsert(Abo abo)
        {
            EnsureAboSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.Abo
    (Name, AdresseId, Periodizitaet, Richtung, Kategorie, ErwarteterBetrag, BetragToleranzProzent,
     Status, GekuendigtAm, KuendigungsfristTage, VorwarnTage,
     ErwartetesKontoId, WebseiteUrl, Kuendigungsweg, Notiz, KuendigenZum)
VALUES
    (@name, @adr, @per, @richtung, @kat, @betrag, @tol,
     @status, @gek, @frist, @vorwarn,
     @konto, @url, @kweg, @notiz, @kzum);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            using var cmd = new SqlCommand(sql, c);
            FillAboParams(cmd, abo);

            return (int)cmd.ExecuteScalar()!;
        }

        public void AboUpdate(Abo abo)
        {
            EnsureAboSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.Abo SET
    Name = @name,
    AdresseId = @adr,
    Periodizitaet = @per,
    Richtung = @richtung,
    Kategorie = @kat,
    ErwarteterBetrag = @betrag,
    BetragToleranzProzent = @tol,
    Status = @status,
    GekuendigtAm = @gek,
    KuendigungsfristTage = @frist,
    VorwarnTage = @vorwarn,
    ErwartetesKontoId = @konto,
    WebseiteUrl = @url,
    Kuendigungsweg = @kweg,
    Notiz = @notiz,
    KuendigenZum = @kzum,
    GeaendertAm = SYSUTCDATETIME()
WHERE Id = @id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", abo.Id);
            FillAboParams(cmd, abo);

            cmd.ExecuteNonQuery();
        }

        private static void FillAboParams(SqlCommand cmd, Abo abo)
        {
            cmd.Parameters.AddWithValue("@name", abo.Name);
            cmd.Parameters.AddWithValue("@adr", (object?)abo.AdresseId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@per", abo.Periodizitaet);
            cmd.Parameters.AddWithValue("@richtung", abo.Richtung);
            cmd.Parameters.AddWithValue("@kat", abo.Kategorie);
            cmd.Parameters.AddWithValue("@betrag", (object?)abo.ErwarteterBetrag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tol", abo.BetragToleranzProzent);
            cmd.Parameters.AddWithValue("@status", abo.Status);
            cmd.Parameters.AddWithValue("@gek", (object?)abo.GekuendigtAm ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@frist", (object?)abo.KuendigungsfristTage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vorwarn", abo.VorwarnTage);
            cmd.Parameters.AddWithValue("@konto", (object?)abo.ErwartetesKontoId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@url", (object?)abo.WebseiteUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@kweg", (object?)abo.Kuendigungsweg ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@notiz", (object?)abo.Notiz ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@kzum", (object?)abo.KuendigenZum ?? DBNull.Value);
        }

        public void AboDelete(int aboId)
        {
            EnsureAboSchema();

            using var c = CreateConnection();
            c.Open();

            // Zuordnungen fallen per ON DELETE CASCADE mit.
            using var cmd = new SqlCommand("DELETE FROM dbo.Abo WHERE Id = @id;", c);
            cmd.Parameters.AddWithValue("@id", aboId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Ordnet eine Transaktion einem Abo zu. Eine Transaktion kann nur zu EINEM Abo
        /// gehören; eine bestehende Zuordnung wird vorher entfernt.
        /// </summary>
        public void AboTransaktionZuordnen(int aboId, int transaktionId, bool manuell, bool einmalig = false)
        {
            EnsureAboSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
DELETE FROM dbo.AboTransaktion WHERE TransaktionId = @tid;
INSERT INTO dbo.AboTransaktion (AboId, TransaktionId, ManuellZugeordnet, IstEinmalig)
VALUES (@aid, @tid, @man, @einmalig);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@aid", aboId);
            cmd.Parameters.AddWithValue("@tid", transaktionId);
            cmd.Parameters.AddWithValue("@man", manuell);
            cmd.Parameters.AddWithValue("@einmalig", einmalig);
            cmd.ExecuteNonQuery();
        }

        public void AboTransaktionEinmaligSetzen(int aboId, int transaktionId, bool istEinmalig)
        {
            EnsureAboSchema();
            using var c = CreateConnection();
            c.Open();
            using var cmd = new SqlCommand(@"
UPDATE dbo.AboTransaktion
SET IstEinmalig = @einmalig
WHERE AboId = @aid AND TransaktionId = @tid;", c);
            cmd.Parameters.AddWithValue("@aid", aboId);
            cmd.Parameters.AddWithValue("@tid", transaktionId);
            cmd.Parameters.AddWithValue("@einmalig", istEinmalig);
            cmd.ExecuteNonQuery();
        }

        public void AboTransaktionEntfernen(int aboId, int transaktionId)
        {
            EnsureAboSchema();

            using var c = CreateConnection();
            c.Open();

            using var cmd = new SqlCommand(
                "DELETE FROM dbo.AboTransaktion WHERE AboId = @aid AND TransaktionId = @tid;", c);
            cmd.Parameters.AddWithValue("@aid", aboId);
            cmd.Parameters.AddWithValue("@tid", transaktionId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Lädt alle zugeordneten Zahlungen aller Abos in einem Rutsch
        /// (für Grid-Aggregate und Detailliste).
        /// </summary>
        public List<AboZahlung> AboZahlungenLaden()
        {
            EnsureAboSchema();

            var result = new List<AboZahlung>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    at.AboId, t.Id, t.Datum, t.Betrag,
    t.VonKontoId, t.NachKontoId,
    a.Name AS AdresseName, g.Name AS BankName,
    t.Notiz, at.ManuellZugeordnet, at.IstEinmalig
FROM dbo.AboTransaktion at
INNER JOIN dbo.Transaktion t ON t.Id = at.TransaktionId
LEFT JOIN dbo.Adresse a      ON a.Id = t.AdresseId
LEFT JOIN dbo.Geldinstitut g ON g.Id = t.GeldinstitutId
ORDER BY t.Datum DESC, t.Id DESC;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                result.Add(new AboZahlung
                {
                    AboId = r.GetInt32(0),
                    TransaktionId = r.GetInt32(1),
                    Datum = r.GetDateTime(2),
                    Betrag = r.GetDecimal(3),
                    VonKontoId = r.IsDBNull(4) ? (int?)null : r.GetInt32(4),
                    NachKontoId = r.IsDBNull(5) ? (int?)null : r.GetInt32(5),
                    AdresseName = r.IsDBNull(6) ? null : r.GetString(6),
                    BankName = r.IsDBNull(7) ? null : r.GetString(7),
                    Notiz = r.IsDBNull(8) ? null : r.GetString(8),
                    ManuellZugeordnet = r.GetBoolean(9),
                    IstEinmalig = r.GetBoolean(10)
                });
            }

            return result;
        }

        /// <summary>
        /// Alle Transaktionen mit erkannter Adresse (Basis für die Abo-Erkennung
        /// und die automatische Zuordnung neuer Zahlungen).
        /// </summary>
        public List<Transaktion> AboLadeTransaktionenMitAdresse()
        {
            EnsureAboSchema();

            var result = new List<Transaktion>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    t.Id, t.Datum, t.Betrag,
    t.AdresseId, a.Name AS AdresseName,
    t.VonKontoId, t.NachKontoId,
    t.GeldinstitutId, t.Notiz
FROM dbo.Transaktion t
INNER JOIN dbo.Adresse a ON a.Id = t.AdresseId
ORDER BY t.AdresseId, t.Datum;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                result.Add(new Transaktion
                {
                    Id = r.GetInt32(0),
                    Datum = r.GetDateTime(1),
                    Betrag = r.GetDecimal(2),
                    AdresseId = r.GetInt32(3),
                    AdresseName = r.GetString(4),
                    VonKontoId = r.IsDBNull(5) ? (int?)null : r.GetInt32(5),
                    NachKontoId = r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
                    GeldinstitutId = r.IsDBNull(7) ? (int?)null : r.GetInt32(7),
                    Notiz = r.IsDBNull(8) ? null : r.GetString(8)
                });
            }

            return result;
        }

        /// <summary>
        /// Alle Transaktionen im Zeitraum, die noch KEINEM Abo zugeordnet sind
        /// (Basis für die Lücken-Suche; bewusst auch Transaktionen ohne Adresse).
        /// </summary>
        public List<Transaktion> AboLadeNichtZugeordneteTransaktionen(DateTime von, DateTime bis)
        {
            EnsureAboSchema();

            var result = new List<Transaktion>();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT
    t.Id, t.Datum, t.Betrag,
    t.AdresseId, a.Name AS AdresseName,
    t.VonKontoId, t.NachKontoId,
    t.GeldinstitutId, g.Name AS BankName,
    t.Notiz
FROM dbo.Transaktion t
LEFT JOIN dbo.Adresse a      ON a.Id = t.AdresseId
LEFT JOIN dbo.Geldinstitut g ON g.Id = t.GeldinstitutId
WHERE t.Datum >= @von
  AND t.Datum <= @bis
  AND NOT EXISTS (SELECT 1 FROM dbo.AboTransaktion at WHERE at.TransaktionId = t.Id)
ORDER BY t.Datum;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@von", von.Date);
            cmd.Parameters.AddWithValue("@bis", bis.Date);

            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                result.Add(new Transaktion
                {
                    Id = r.GetInt32(0),
                    Datum = r.GetDateTime(1),
                    Betrag = r.GetDecimal(2),
                    AdresseId = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    AdresseName = r.IsDBNull(4) ? null : r.GetString(4),
                    VonKontoId = r.IsDBNull(5) ? (int?)null : r.GetInt32(5),
                    NachKontoId = r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
                    GeldinstitutId = r.IsDBNull(7) ? (int?)null : r.GetInt32(7),
                    BankName = r.IsDBNull(8) ? null : r.GetString(8),
                    Notiz = r.IsDBNull(9) ? null : r.GetString(9)
                });
            }

            return result;
        }

        /// <summary>
        /// Setzt das Buchungskonto einer Transaktion um (für die Konto-Bereinigung im Abo-Modul).
        /// Bei Bankimporten ist die Aufwandsseite NachKontoId; nur wenn diese leer ist,
        /// wird stattdessen VonKontoId gesetzt.
        /// </summary>
        public void AboSetzeBuchungsKonto(int transaktionId, int kontoId)
        {
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.Transaktion
SET NachKontoId = CASE WHEN NachKontoId IS NOT NULL THEN @konto ELSE NachKontoId END,
    VonKontoId  = CASE WHEN NachKontoId IS NULL     THEN @konto ELSE VonKontoId  END
WHERE Id = @id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", transaktionId);
            cmd.Parameters.AddWithValue("@konto", kontoId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Ids aller Transaktionen, die bereits einem Abo zugeordnet sind.</summary>
        public HashSet<int> AboZugeordneteTransaktionIds()
        {
            EnsureAboSchema();

            var result = new HashSet<int>();

            using var c = CreateConnection();
            c.Open();

            using var cmd = new SqlCommand("SELECT TransaktionId FROM dbo.AboTransaktion;", c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
                result.Add(r.GetInt32(0));

            return result;
        }

        /// <summary>Dauerhaft abgewählte Kandidatenmuster des aktuellen Mandanten.</summary>
        public List<AboKandidatAusschluss> AboKandidatAusschluesseLaden()
        {
            EnsureAboSchema();
            var result = new List<AboKandidatAusschluss>();

            using var c = CreateConnection();
            c.Open();
            using var cmd = new SqlCommand(@"
SELECT AdresseId, Periodizitaet, ReferenzBetrag
FROM dbo.AboKandidatAusschluss;", c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                result.Add(new AboKandidatAusschluss
                {
                    AdresseId = r.GetInt32(0),
                    Periodizitaet = r.GetString(1),
                    ReferenzBetrag = r.GetDecimal(2)
                });
            }

            return result;
        }

        /// <summary>
        /// Merkt abgewählte Kandidaten dauerhaft. Betragsänderungen bis 20 Prozent
        /// werden als dasselbe Muster behandelt und erzeugen keinen neuen Eintrag.
        /// </summary>
        public void AboKandidatenIgnorieren(IEnumerable<AboKandidat> kandidaten)
        {
            EnsureAboSchema();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.AboKandidatAusschluss
    WHERE AdresseId = @adresseId
      AND ABS(ReferenzBetrag - @betrag) <=
          CASE WHEN ABS(@betrag) * 0.20 > 1.00 THEN ABS(@betrag) * 0.20 ELSE 1.00 END
)
BEGIN
    INSERT INTO dbo.AboKandidatAusschluss (AdresseId, Periodizitaet, ReferenzBetrag)
    VALUES (@adresseId, @periodizitaet, @betrag);
END;";

            foreach (var kandidat in kandidaten)
            {
                using var cmd = new SqlCommand(sql, c);
                cmd.Parameters.AddWithValue("@adresseId", kandidat.AdresseId);
                cmd.Parameters.AddWithValue("@periodizitaet", kandidat.Periodizitaet);
                cmd.Parameters.AddWithValue("@betrag", kandidat.MedianBetrag);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
