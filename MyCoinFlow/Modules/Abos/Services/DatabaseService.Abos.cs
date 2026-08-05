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
        ErwarteterBetrag DECIMAL(19,2) NULL,
        BetragToleranzProzent DECIMAL(5,2) NOT NULL CONSTRAINT DF_Abo_Toleranz DEFAULT (10),
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_Abo_Status DEFAULT ('Aktiv'),
        GekuendigtAm DATE NULL,
        KuendigungsfristTage INT NULL,
        VorwarnTage INT NOT NULL CONSTRAINT DF_Abo_VorwarnTage DEFAULT (7),
        ErwartetesKontoId INT NULL,
        WebseiteUrl NVARCHAR(500) NULL,
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
END;";

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
    ab.Periodizitaet, ab.ErwarteterBetrag, ab.BetragToleranzProzent,
    ab.Status, ab.GekuendigtAm, ab.KuendigungsfristTage, ab.VorwarnTage,
    ab.ErwartetesKontoId, ab.WebseiteUrl, ab.Notiz, ab.KuendigenZum
FROM dbo.Abo ab
LEFT JOIN dbo.Adresse a ON a.Id = ab.AdresseId
ORDER BY ab.Name;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                result.Add(new Abo
                {
                    Id = r.GetInt32(0),
                    Name = r.GetString(1),
                    AdresseId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                    AdresseName = r.IsDBNull(3) ? null : r.GetString(3),
                    Periodizitaet = r.GetString(4),
                    ErwarteterBetrag = r.IsDBNull(5) ? (decimal?)null : r.GetDecimal(5),
                    BetragToleranzProzent = r.GetDecimal(6),
                    Status = r.GetString(7),
                    GekuendigtAm = r.IsDBNull(8) ? (DateTime?)null : r.GetDateTime(8),
                    KuendigungsfristTage = r.IsDBNull(9) ? (int?)null : r.GetInt32(9),
                    VorwarnTage = r.GetInt32(10),
                    ErwartetesKontoId = r.IsDBNull(11) ? (int?)null : r.GetInt32(11),
                    WebseiteUrl = r.IsDBNull(12) ? null : r.GetString(12),
                    Notiz = r.IsDBNull(13) ? null : r.GetString(13),
                    KuendigenZum = r.IsDBNull(14) ? (DateTime?)null : r.GetDateTime(14)
                });
            }

            return result;
        }

        public int AboInsert(Abo abo)
        {
            EnsureAboSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.Abo
    (Name, AdresseId, Periodizitaet, ErwarteterBetrag, BetragToleranzProzent,
     Status, GekuendigtAm, KuendigungsfristTage, VorwarnTage,
     ErwartetesKontoId, WebseiteUrl, Notiz, KuendigenZum)
VALUES
    (@name, @adr, @per, @betrag, @tol,
     @status, @gek, @frist, @vorwarn,
     @konto, @url, @notiz, @kzum);
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
    ErwarteterBetrag = @betrag,
    BetragToleranzProzent = @tol,
    Status = @status,
    GekuendigtAm = @gek,
    KuendigungsfristTage = @frist,
    VorwarnTage = @vorwarn,
    ErwartetesKontoId = @konto,
    WebseiteUrl = @url,
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
            cmd.Parameters.AddWithValue("@betrag", (object?)abo.ErwarteterBetrag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tol", abo.BetragToleranzProzent);
            cmd.Parameters.AddWithValue("@status", abo.Status);
            cmd.Parameters.AddWithValue("@gek", (object?)abo.GekuendigtAm ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@frist", (object?)abo.KuendigungsfristTage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vorwarn", abo.VorwarnTage);
            cmd.Parameters.AddWithValue("@konto", (object?)abo.ErwartetesKontoId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@url", (object?)abo.WebseiteUrl ?? DBNull.Value);
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
        public void AboTransaktionZuordnen(int aboId, int transaktionId, bool manuell)
        {
            EnsureAboSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
DELETE FROM dbo.AboTransaktion WHERE TransaktionId = @tid;
INSERT INTO dbo.AboTransaktion (AboId, TransaktionId, ManuellZugeordnet)
VALUES (@aid, @tid, @man);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@aid", aboId);
            cmd.Parameters.AddWithValue("@tid", transaktionId);
            cmd.Parameters.AddWithValue("@man", manuell);
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
    t.Notiz, at.ManuellZugeordnet
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
                    ManuellZugeordnet = r.GetBoolean(9)
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
    }
}
