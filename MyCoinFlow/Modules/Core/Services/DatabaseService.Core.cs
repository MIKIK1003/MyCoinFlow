using ExcelDataReader;
using Microsoft.Data.SqlClient;
using MyCoinFlow.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Transactions;
using System.Text.RegularExpressions;


namespace MyCoinFlow.Services
{
    public partial class DatabaseService
    {
        public List<KontoplanEintrag> LadeKontenplan()
        {
            var eintraege = new List<KontoplanEintrag>();

            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                var command = new SqlCommand(@"
-- Aktiven Zeitraum (falls vorhanden)
WITH Aktiver AS (
    SELECT TOP (1) Id, Startdatum, Enddatum
    FROM Budgetzeitraum
    WHERE IstAktiv = 1
)
SELECT 
    k.Id,
    k.Kontonummer,
    k.Art,
    k.Gruppe,
    k.Untergruppe,
    k.Detail,
    bd.Budgetwert,
    ISNULL(g.Gebucht, 0) AS Gebucht
FROM Kontenplan k
LEFT JOIN Aktiver bz
    ON 1 = 1
LEFT JOIN BudgetDetail bd
    ON bz.Id IS NOT NULL
   AND bd.ZeitraumId = bz.Id
   AND bd.KontoId = k.Id
OUTER APPLY (
    SELECT SUM(x.Wert) AS Gebucht
    FROM (
        -- Zugänge (NachKontoId = dieses Konto) positiv
        SELECT t.NachKontoId AS KontoId, SUM(t.Betrag) AS Wert
        FROM Transaktion t
        WHERE bz.Id IS NOT NULL
          AND t.NachKontoId = k.Id
          AND ISNULL(t.BudgetDatum, t.Datum) >= bz.Startdatum AND ISNULL(t.BudgetDatum, t.Datum) <= bz.Enddatum  -- NEU: BudgetDatum falls gesetzt
        GROUP BY t.NachKontoId
        UNION ALL
        -- Abgänge (VonKontoId = dieses Konto) negativ
        SELECT t.VonKontoId AS KontoId, SUM(-t.Betrag) AS Wert
        FROM Transaktion t
        WHERE bz.Id IS NOT NULL
          AND t.VonKontoId = k.Id
          AND ISNULL(t.BudgetDatum, t.Datum) >= bz.Startdatum AND ISNULL(t.BudgetDatum, t.Datum) <= bz.Enddatum  -- NEU: BudgetDatum falls gesetzt
        GROUP BY t.VonKontoId
    ) x
) g
ORDER BY k.Art, k.Gruppe, k.Untergruppe, k.Kontonummer, k.Detail;
", connection);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    eintraege.Add(new KontoplanEintrag
                    {
                        Id = reader.GetInt32(0),
                        Kontonummer = reader.GetInt32(1),
                        Art = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        Gruppe = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        Untergruppe = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        Detail = reader.IsDBNull(5) ? "" : reader.GetString(5),
                        Budgetwert = reader.IsDBNull(6) ? (decimal?)null : reader.GetDecimal(6),
                        Gebucht = reader.GetDecimal(7)
                    });
                }
            }
            return eintraege;
        }


        public void NeuenKontoplanEintragSpeichern(int kontonummer, string art, string gruppe, string untergruppe, string detail)
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();

            // Prüfung auf vorhandenen Eintrag
            string checkSql = @"SELECT COUNT(*) FROM Kontenplan 
                        WHERE Kontonummer = @Kontonummer AND Art = @Art 
                        AND Gruppe = @Gruppe AND Untergruppe = @Untergruppe AND Detail = @Detail";

            using var checkCmd = new SqlCommand(checkSql, connection);
            checkCmd.Parameters.AddWithValue("@Kontonummer", kontonummer);
            checkCmd.Parameters.AddWithValue("@Art", art);
            checkCmd.Parameters.AddWithValue("@Gruppe", gruppe);
            checkCmd.Parameters.AddWithValue("@Untergruppe", untergruppe);
            checkCmd.Parameters.AddWithValue("@Detail", detail);

            int count = (int)checkCmd.ExecuteScalar();

            if (count > 0)
            {
                // Bereits vorhanden – kein Insert
                return;
            }

            // Eintrag neu speichern
            var insertCmd = new SqlCommand(
                @"INSERT INTO Kontenplan (Kontonummer, Art, Gruppe, Untergruppe, Detail)
          VALUES (@Kontonummer, @Art, @Gruppe, @Untergruppe, @Detail)", connection);

            insertCmd.Parameters.AddWithValue("@Kontonummer", kontonummer);
            insertCmd.Parameters.AddWithValue("@Art", art);
            insertCmd.Parameters.AddWithValue("@Gruppe", gruppe);
            insertCmd.Parameters.AddWithValue("@Untergruppe", untergruppe);
            insertCmd.Parameters.AddWithValue("@Detail", detail);

            insertCmd.ExecuteNonQuery();
        }

        public void KontenplanEintragAktualisieren(int id, int kontonummer, string art, string gruppe, string? untergruppe, string? detail)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                // Korrektes WHERE
                string sql = @"UPDATE Kontenplan 
                       SET Kontonummer = @Kontonummer, 
                           Art = @Art, 
                           Gruppe = @Gruppe, 
                           Untergruppe = @Untergruppe, 
                           Detail = @Detail 
                       WHERE Id = @Id";

                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    command.Parameters.AddWithValue("@Kontonummer", kontonummer);
                    command.Parameters.AddWithValue("@Art", art);
                    command.Parameters.AddWithValue("@Gruppe", gruppe);
                    command.Parameters.AddWithValue("@Untergruppe", (object?)untergruppe ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Detail", (object?)detail ?? DBNull.Value);

                    command.ExecuteNonQuery();
                }
            }
        }

        public void KontenplanEintragLoeschen(int id)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            // 1) Preflight: Wer referenziert dbo.Kontenplan(Id)?
            var refs = GetReferencingCounts("dbo", "Kontenplan", "Id", id);

            // 1a) "Weiche" Verweise (Mappings) zählen
            refs.TryGetValue("dbo.KategorieKontoMapping", out int mappingCount);
            bool hasOtherRefs = refs.Any(kv =>
                !kv.Key.Equals("dbo.KategorieKontoMapping", StringComparison.OrdinalIgnoreCase));

            // 2) Falls Mappings existieren, optional mitlöschen
            if (mappingCount > 0)
            {
                var infoWeitere = hasOtherRefs
                    ? "\n\nHinweis: Es existieren weitere Verweise (z. B. Transaktionen/BudgetDetail). Diese werden NICHT automatisch gelöscht."
                    : "";

                var frage = mappingCount == 1
                    ? $"Zu diesem Konto existiert 1 Mapping in KategorieKontoMapping.\n\nKonto zusammen mit diesem Mapping löschen?{infoWeitere}"
                    : $"Zu diesem Konto existieren {mappingCount} Mappings in KategorieKontoMapping.\n\nKonto zusammen mit diesen Mappings löschen?{infoWeitere}";

                var choice = System.Windows.MessageBox.Show(
                    frage,
                    "Konto & Mappings löschen",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (choice != System.Windows.MessageBoxResult.Yes)
                    return;

                try
                {
                    LoescheKategorieMappingsFuerKonto(id);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Kategorie-Mappings konnten nicht gelöscht werden:\n" + ex.Message,
                        "Löschen fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                // Referenzen neu ermitteln
                refs = GetReferencingCounts("dbo", "Kontenplan", "Id", id);
            }

            // 3) Spezialfall: Adresse.DefaultKontoId darf "weggeschaltet" werden
            refs.TryGetValue("dbo.Adresse", out int adresseCount);

            // Alles außer dbo.Adresse sind harte Blocker
            bool hasHardBlockers = refs.Any(kv => !kv.Key.Equals("dbo.Adresse", StringComparison.OrdinalIgnoreCase));

            if (adresseCount > 0 && !hasHardBlockers)
            {
                // Beispiele anzeigen, damit klar ist welche Adressen gemeint sind
                var beispiele = GetAdressenMitDefaultKonto(id, take: 6);
                var lines = new List<string>();

                lines.Add(adresseCount == 1
                    ? "1 Adresse referenziert dieses Konto als DefaultKontoId."
                    : $"{adresseCount} Adressen referenzieren dieses Konto als DefaultKontoId.");

                if (beispiele.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Beispiele:");
                    foreach (var a in beispiele)
                        lines.Add($"• #{a.Id}: {a.Name}");
                }

                lines.Add("");
                lines.Add("Soll bei diesen Adressen DefaultKontoId auf (leer) gesetzt werden, damit das Konto gelöscht werden kann?");
                lines.Add("Die Adressen selbst werden nicht gelöscht.");

                var choice = System.Windows.MessageBox.Show(
                    string.Join(Environment.NewLine, lines),
                    "DefaultKontoId lösen?",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (choice != System.Windows.MessageBoxResult.Yes)
                    return;

                try
                {
                    NullDefaultKontoInAdressen(id);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("DefaultKontoId konnte bei den Adressen nicht gelöst werden:\n" + ex.Message,
                        "Löschen fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                // Nach dem Lösen erneut prüfen
                refs = GetReferencingCounts("dbo", "Kontenplan", "Id", id);
            }

            // 4) Harte Blocker verbleiben? → blockieren, aber mit Adress-Beispielen falls vorhanden
            if (refs.Count > 0)
            {
                if (refs.TryGetValue("dbo.Adresse", out int adrCnt) && adrCnt > 0)
                {
                    var beispiele = GetAdressenMitDefaultKonto(id, take: 6);

                    var info = new List<string>
            {
                "Zusatzinfo zu dbo.Adresse (DefaultKontoId):",
                adrCnt == 1 ? "• 1 Adresse betroffen" : $"• {adrCnt} Adressen betroffen"
            };

                    if (beispiele.Count > 0)
                    {
                        info.Add("Beispiele:");
                        foreach (var a in beispiele)
                            info.Add($"• #{a.Id}: {a.Name}");
                    }

                    info.Add("");
                    info.Add("Weitere Verweise verhindern das Löschen weiterhin.");

                    System.Windows.MessageBox.Show(
                        string.Join(Environment.NewLine, info),
                        "Hinweis",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }

                ShowDeleteBlockedMessage("Kontenplan-Eintrag", refs);
                return;
            }

            // 5) Konto löschen
            try
            {
                using var del = new SqlCommand("DELETE FROM dbo.Kontenplan WHERE Id = @Id", c);
                del.Parameters.AddWithValue("@Id", id);
                del.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                if (HandleSqlDeleteException(ex, "Kontenplan-Eintrag")) return;

                System.Windows.MessageBox.Show("Kontenplan-Eintrag konnte nicht gelöscht werden:\n" + ex.Message,
                    "Löschen fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        // Liefert ein paar Beispiele der Adressen, die DefaultKontoId = kontoId haben
        private List<(int Id, string Name)> GetAdressenMitDefaultKonto(int kontoId, int take = 6)
        {
            var list = new List<(int, string)>();

            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT TOP (@take) Id, Name
FROM dbo.Adresse
WHERE DefaultKontoId = @id
ORDER BY Name;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@take", take);
            cmd.Parameters.AddWithValue("@id", kontoId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int id = r.GetInt32(0);
                string name = r.IsDBNull(1) ? "(ohne Name)" : r.GetString(1);
                list.Add((id, name));
            }

            return list;
        }

        // Setzt DefaultKontoId auf NULL für alle Adressen, die dieses Konto referenzieren
        private int NullDefaultKontoInAdressen(int kontoId)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"UPDATE dbo.Adresse SET DefaultKontoId = NULL WHERE DefaultKontoId = @id;";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", kontoId);

            return cmd.ExecuteNonQuery();
        }



        // Löscht alle Kategorie→Konto-Mappings für ein Konto (weiche Verknüpfungen).
        // Rückgabewert: Anzahl gelöschter Zeilen.
        private int LoescheKategorieMappingsFuerKonto(int kontoId)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();
            using var cmd = new SqlCommand(
                "DELETE FROM dbo.KategorieKontoMapping WHERE KontoId = @id;", c);
            cmd.Parameters.AddWithValue("@id", kontoId);
            return cmd.ExecuteNonQuery();
        }


        public List<Budgetzeitraum> LadeBudgetzeitraeume()
        {
            List<Budgetzeitraum> zeitraeume = new List<Budgetzeitraum>();

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                string sql = "SELECT Id, Bezeichnung, Startdatum, Enddatum, IstAktiv FROM Budgetzeitraum";

                using (SqlCommand command = new SqlCommand(sql, connection))
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Budgetzeitraum zeitraum = new Budgetzeitraum
                        {
                            Id = reader.GetInt32(0),
                            Bezeichnung = reader.GetString(1),
                            Startdatum = reader.GetDateTime(2),
                            Enddatum = reader.GetDateTime(3),
                            IstAktiv = reader.GetBoolean(4)
                        };

                        zeitraeume.Add(zeitraum);
                    }
                }
            }

            return zeitraeume;
        }

        public int? HoleAktivenBudgetzeitraumId()
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();

            using var cmd = new SqlCommand("SELECT TOP 1 Id FROM Budgetzeitraum WHERE IstAktiv = 1", connection);
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? (int?)null : (int)(result);
        }

        /// <summary>
        /// Liefert alle Konten (Kontenplan) mit Budgetwert für den angegebenen Zeitraum.
        /// Wenn zeitraumId == null, wird der aktive Zeitraum genommen (falls vorhanden).
        /// </summary>
        public List<BudgetKontoRow> LadeBudgetKontenFuerZeitraum(int? zeitraumId = null)
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();

            int? usedId = zeitraumId;
            if (usedId == null)
            {
                usedId = HoleAktivenBudgetzeitraumId();
            }

            // Hinweis: Wenn kein aktiver Zeitraum existiert, liefern wir Budgetwert = NULL für alle Konten.
            string sql = @"
SELECT 
    k.Id AS KontoId,
    k.Kontonummer,
    k.Art,
    k.Gruppe,
    k.Untergruppe,
    k.Detail,
    bd.Budgetwert
FROM Kontenplan k
LEFT JOIN BudgetDetail bd 
    ON bd.KontoId = k.Id
    AND (@ZeitraumId IS NOT NULL AND bd.ZeitraumId = @ZeitraumId)
ORDER BY k.Art, k.Gruppe, k.Untergruppe, k.Kontonummer, k.Detail;
";

            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@ZeitraumId", (object?)usedId ?? DBNull.Value);

            var list = new List<BudgetKontoRow>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new BudgetKontoRow
                {
                    KontoId = reader.GetInt32(0),
                    Kontonummer = reader.GetInt32(1),
                    Art = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Gruppe = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Untergruppe = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Detail = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Budgetwert = reader.IsDBNull(6) ? (decimal?)null : reader.GetDecimal(6)
                });
            }

            return list;
        }

        /// <summary>
        /// Upsert für einen Budgetwert (deckt Insert und Update ab).
        /// </summary>
        public void UpsertBudgetwert(int zeitraumId, int kontoId, decimal? wert)
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();

            // Null => Eintrag löschen, um "kein Wert gesetzt" sauber zu repräsentieren
            if (wert == null)
            {
                using var delCmd = new SqlCommand(
                    "DELETE FROM BudgetDetail WHERE ZeitraumId = @Z AND KontoId = @K", connection);
                delCmd.Parameters.AddWithValue("@Z", zeitraumId);
                delCmd.Parameters.AddWithValue("@K", kontoId);
                delCmd.ExecuteNonQuery();
                return;
            }

            // Upsert
            string checkSql = "SELECT COUNT(*) FROM BudgetDetail WHERE ZeitraumId = @Z AND KontoId = @K";
            using var checkCmd = new SqlCommand(checkSql, connection);
            checkCmd.Parameters.AddWithValue("@Z", zeitraumId);
            checkCmd.Parameters.AddWithValue("@K", kontoId);
            int count = (int)checkCmd.ExecuteScalar();

            if (count > 0)
            {
                using var updateCmd = new SqlCommand(
                    "UPDATE BudgetDetail SET Budgetwert = @W WHERE ZeitraumId = @Z AND KontoId = @K", connection);
                updateCmd.Parameters.AddWithValue("@W", wert.Value);
                updateCmd.Parameters.AddWithValue("@Z", zeitraumId);
                updateCmd.Parameters.AddWithValue("@K", kontoId);
                updateCmd.ExecuteNonQuery();
            }
            else
            {
                using var insertCmd = new SqlCommand(
                    "INSERT INTO BudgetDetail (ZeitraumId, KontoId, Budgetwert) VALUES (@Z, @K, @W)", connection);
                insertCmd.Parameters.AddWithValue("@Z", zeitraumId);
                insertCmd.Parameters.AddWithValue("@K", kontoId);
                insertCmd.Parameters.AddWithValue("@W", wert.Value);
                insertCmd.ExecuteNonQuery();
            }
        }

        public void BudgetzeitraumAktualisieren(int id, string bezeichnung, DateTime startdatum, DateTime enddatum, bool aktiv)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                // Wenn "aktiv" gesetzt ist, zuerst alle anderen deaktivieren
                if (aktiv)
                {
                    string deaktivierenSql = "UPDATE Budgetzeitraum SET IstAktiv = 0";
                    using (SqlCommand deaktivierenCmd = new SqlCommand(deaktivierenSql, connection))
                    {
                        deaktivierenCmd.ExecuteNonQuery();
                    }
                }

                string sql = @"
            UPDATE Budgetzeitraum 
            SET Bezeichnung = @Bez, 
                Startdatum = @Start, 
                Enddatum = @Ende, 
                IstAktiv = @Aktiv 
            WHERE Id = @Id";

                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    command.Parameters.AddWithValue("@Bez", bezeichnung);
                    command.Parameters.AddWithValue("@Start", startdatum);
                    command.Parameters.AddWithValue("@Ende", enddatum);
                    command.Parameters.AddWithValue("@Aktiv", aktiv);

                    command.ExecuteNonQuery();
                }
            }
        }

        public void BudgetzeitraumSpeichern(string bezeichnung, DateTime startdatum, DateTime enddatum, bool IstAktiv)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                if (IstAktiv)
                {
                    string deaktivierenSql = "UPDATE Budgetzeitraum SET IstAktiv = 0";
                    using (SqlCommand deaktivierenCmd = new SqlCommand(deaktivierenSql, connection))
                    {
                        deaktivierenCmd.ExecuteNonQuery();
                    }
                }

                string sql = "INSERT INTO Budgetzeitraum (Bezeichnung, Startdatum, Enddatum, IstAktiv) VALUES (@Bez, @Start, @Ende, @Aktiv)";

                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Bez", bezeichnung);
                    command.Parameters.AddWithValue("@Start", startdatum);
                    command.Parameters.AddWithValue("@Ende", enddatum);
                    command.Parameters.AddWithValue("@Aktiv", IstAktiv);

                    command.ExecuteNonQuery();
                }
            }
        }

        public void BudgetzeitraumLoeschen(int id)
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            connection.Open();

            // 1) Aktiven Status abfragen
            const string chkSql = "SELECT IstAktiv FROM dbo.Budgetzeitraum WHERE Id = @Id";
            using (var chk = new Microsoft.Data.SqlClient.SqlCommand(chkSql, connection))
            {
                chk.Parameters.AddWithValue("@Id", id);
                var v = chk.ExecuteScalar();

                if (v == null || v == DBNull.Value)
                {
                    System.Windows.MessageBox.Show("Der Budgetzeitraum wurde nicht gefunden.",
                        "Löschen nicht möglich", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }

                bool istAktiv = Convert.ToBoolean(v);

                if (istAktiv)
                {
                    // Spezielle Regel: Aktiven Zeitraum nicht löschen
                    System.Windows.MessageBox.Show(
                        "Der aktive Budgetzeitraum kann nicht gelöscht werden.\n\n" +
                        "Bitte zuerst einen anderen Zeitraum aktivieren oder diesen deaktivieren.",
                        "Löschen nicht möglich",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }
            }

            // 2) Löschen (für inaktive Zeiträume)
            try
            {
                const string delSql = "DELETE FROM dbo.Budgetzeitraum WHERE Id = @Id";
                using var del = new Microsoft.Data.SqlClient.SqlCommand(delSql, connection);
                del.Parameters.AddWithValue("@Id", id);
                del.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // Falls wider Erwarten FK-Blockaden existieren, freundlich abfangen
                if (HandleSqlDeleteException(ex, "Budgetzeitraum")) return;

                System.Windows.MessageBox.Show("Budgetzeitraum konnte nicht gelöscht werden:\n" + ex.Message,
                    "Löschen fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                // Kein throw → kein Programmstop
            }
        }

        public Budgetzeitraum? HoleBudgetzeitraum(int id)
        {
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();

            const string sql = "SELECT Id, Bezeichnung, Startdatum, Enddatum, IstAktiv FROM dbo.Budgetzeitraum WHERE Id = @Id";
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new Budgetzeitraum
            {
                Id = r.GetInt32(0),
                Bezeichnung = r.GetString(1),
                Startdatum = r.GetDateTime(2),
                Enddatum = r.GetDateTime(3),
                IstAktiv = r.GetBoolean(4)
            };
        }



        /// public void ArtHinzufuegen(string bezeichnung)
        /// {
        ///    using var connection = new SqlConnection(_connectionString);
        /// <summary>
        /// public void ArtHinzufuegen(string bezeichnung)
        /// </summary> = new SqlCommand("INSERT INTO Art (Bezeichnung) VALUES (@Bez)", connection);
        ///    command.Parameters.AddWithValue("@Bez", bezeichnung);
        ///    command.ExecuteNonQuery();
        /// }

        public List<KontenArt> LadeKontenArten()
        {
            var arten = new List<KontenArt>();

            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                var command = new SqlCommand("SELECT Id, Bezeichnung FROM KontenArt", connection);
                var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    arten.Add(new KontenArt
                    {
                        Id = reader.GetInt32(0),
                        Bezeichnung = reader.GetString(1)
                    });
                }
            }

            return arten;
        }

        public void SpeichereKontenArt(String bezeichnung)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                string sql = "INSERT INTO KontenArt (Bezeichnung) VALUES (@Bez)";
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Bez", bezeichnung);
                    command.ExecuteNonQuery();
                }
            }
        }


        

        public List<KontenGruppe> LadeKontenGruppen()
        {
            var gruppen = new List<KontenGruppe>();

            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                string sql = "SELECT Id, Bezeichnung FROM KontenGruppe";

                using (var command = new SqlCommand(sql, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        gruppen.Add(new KontenGruppe
                        {
                            Id = reader.GetInt32(0),
                            Bezeichnung = reader.GetString(1),
                        });
                    }
                }
            }

            return gruppen;
        }

        public void SpeichereKontenGruppe(string bezeichnung)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                string sql = "INSERT INTO KontenGruppe (Bezeichnung) VALUES (@Bez)";

                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Bez", bezeichnung);
                    command.ExecuteNonQuery();
                }
            }
        }


        public void RenameKontenArt(string oldName, string newName)
        {
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var tx = con.BeginTransaction();
            try
            {
                // Stammdaten
                using (var cmd = new SqlCommand(
                    "UPDATE KontenArt SET Bezeichnung = @new WHERE Bezeichnung = @old;", con, tx))
                {
                    cmd.Parameters.AddWithValue("@new", newName);
                    cmd.Parameters.AddWithValue("@old", oldName);
                    cmd.ExecuteNonQuery();
                }

                // Alle Vorkommen im Kontenplan
                using (var cmd = new SqlCommand(
                    "UPDATE Kontenplan SET Art = @new WHERE Art = @old;", con, tx))
                {
                    cmd.Parameters.AddWithValue("@new", newName);
                    cmd.Parameters.AddWithValue("@old", oldName);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public void RenameKontenGruppe(string oldName, string newName)
        {
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var tx = con.BeginTransaction();
            try
            {
                using (var cmd = new SqlCommand(
                    "UPDATE KontenGruppe SET Bezeichnung = @new WHERE Bezeichnung = @old;", con, tx))
                {
                    cmd.Parameters.AddWithValue("@new", newName);
                    cmd.Parameters.AddWithValue("@old", oldName);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new SqlCommand(
                    "UPDATE Kontenplan SET Gruppe = @new WHERE Gruppe = @old;", con, tx))
                {
                    cmd.Parameters.AddWithValue("@new", newName);
                    cmd.Parameters.AddWithValue("@old", oldName);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public void RenameKontenUnterGruppe(string oldName, string newName)
        {
            using var con = new SqlConnection(_connectionString);
            con.Open();
            using var tx = con.BeginTransaction();
            try
            {
                using (var cmd = new SqlCommand(
                    "UPDATE KontenUnterGruppe SET Bezeichnung = @new WHERE Bezeichnung = @old;", con, tx))
                {
                    cmd.Parameters.AddWithValue("@new", newName);
                    cmd.Parameters.AddWithValue("@old", oldName);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new SqlCommand(
                    "UPDATE Kontenplan SET Untergruppe = @new WHERE Untergruppe = @old;", con, tx))
                {
                    cmd.Parameters.AddWithValue("@new", newName);
                    cmd.Parameters.AddWithValue("@old", oldName);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }


        // --- ERSETZEN ---
        public void LoescheKontenArt(int id)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            // Bezeichnung ermitteln
            string? bez = null;
            using (var get = new SqlCommand("SELECT Bezeichnung FROM dbo.KontenArt WHERE Id=@Id", c))
            {
                get.Parameters.AddWithValue("@Id", id);
                var v = get.ExecuteScalar();
                if (v == null || v == DBNull.Value)
                {
                    System.Windows.MessageBox.Show("Eintrag in KontenArt wurde nicht gefunden.",
                        "Löschen nicht möglich", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }
                bez = Convert.ToString(v);
            }

            // Verwendung im Kontenplan zählen
            int used = 0;
            using (var cnt = new SqlCommand("SELECT COUNT(*) FROM dbo.Kontenplan WHERE Art = @Bez", c))
            {
                cnt.Parameters.AddWithValue("@Bez", bez ?? (object)DBNull.Value);
                used = Convert.ToInt32(cnt.ExecuteScalar() ?? 0);
            }

            if (used > 0)
            {
                var msg = used == 1
                    ? "Diese Art wird in 1 Kontenplan-Zeile verwendet.\n\nArt in dieser Zeile auf (leer) setzen und den Stammdatensatz löschen?"
                    : $"Diese Art wird in {used} Kontenplan-Zeilen verwendet.\n\nArt in diesen Zeilen auf (leer) setzen und den Stammdatensatz löschen?";

                var ask = System.Windows.MessageBox.Show(msg, "Konten-Art löschen",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (ask != System.Windows.MessageBoxResult.Yes) return;

                using var tx = c.BeginTransaction();
                try
                {
                    using (var upd = new SqlCommand("UPDATE dbo.Kontenplan SET Art = N'' WHERE Art = @Bez", c, tx))
                    {
                        upd.Parameters.AddWithValue("@Bez", bez ?? (object)DBNull.Value);
                        upd.ExecuteNonQuery();
                    }
                    using (var del = new SqlCommand("DELETE FROM dbo.KontenArt WHERE Id=@Id", c, tx))
                    {
                        del.Parameters.AddWithValue("@Id", id);
                        del.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    if (HandleSqlDeleteException(ex, "Konten-Art")) return;
                    System.Windows.MessageBox.Show("Konten-Art konnte nicht gelöscht werden:\n" + ex.Message,
                        "Löschen fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                return;
            }

            // Kein Gebrauch → direkt löschen
            try
            {
                using var del = new SqlCommand("DELETE FROM dbo.KontenArt WHERE Id=@Id", c);
                del.Parameters.AddWithValue("@Id", id);
                del.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                if (HandleSqlDeleteException(ex, "Konten-Art")) return;
                System.Windows.MessageBox.Show("Konten-Art konnte nicht gelöscht werden:\n" + ex.Message,
                    "Löschen fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        // --- ERSETZEN ---
        public void LoescheKontenGruppe(int id)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            string? bez = null;
            using (var get = new SqlCommand("SELECT Bezeichnung FROM dbo.KontenGruppe WHERE Id=@Id", c))
            {
                get.Parameters.AddWithValue("@Id", id);
                var v = get.ExecuteScalar();
                if (v == null || v == DBNull.Value)
                {
                    System.Windows.MessageBox.Show("Eintrag in KontenGruppe wurde nicht gefunden.",
                        "Löschen nicht möglich", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }
                bez = Convert.ToString(v);
            }

            int used = 0;
            using (var cnt = new SqlCommand("SELECT COUNT(*) FROM dbo.Kontenplan WHERE Gruppe = @Bez", c))
            {
                cnt.Parameters.AddWithValue("@Bez", bez ?? (object)DBNull.Value);
                used = Convert.ToInt32(cnt.ExecuteScalar() ?? 0);
            }

            if (used > 0)
            {
                var msg = used == 1
                    ? "Diese Gruppe wird in 1 Kontenplan-Zeile verwendet.\n\nGruppe in dieser Zeile auf (leer) setzen und den Stammdatensatz löschen?"
                    : $"Diese Gruppe wird in {used} Kontenplan-Zeilen verwendet.\n\nGruppe in diesen Zeilen auf (leer) setzen und den Stammdatensatz löschen?";

                var ask = System.Windows.MessageBox.Show(msg, "Konten-Gruppe löschen",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (ask != System.Windows.MessageBoxResult.Yes) return;

                using var tx = c.BeginTransaction();
                try
                {
                    using (var upd = new SqlCommand("UPDATE dbo.Kontenplan SET Gruppe = N'' WHERE Gruppe = @Bez", c, tx))
                    {
                        upd.Parameters.AddWithValue("@Bez", bez ?? (object)DBNull.Value);
                        upd.ExecuteNonQuery();
                    }
                    using (var del = new SqlCommand("DELETE FROM dbo.KontenGruppe WHERE Id=@Id", c, tx))
                    {
                        del.Parameters.AddWithValue("@Id", id);
                        del.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    if (HandleSqlDeleteException(ex, "Konten-Gruppe")) return;
                    System.Windows.MessageBox.Show("Konten-Gruppe konnte nicht gelöscht werden:\n" + ex.Message,
                        "Löschen fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                return;
            }

            try
            {
                using var del = new SqlCommand("DELETE FROM dbo.KontenGruppe WHERE Id=@Id", c);
                del.Parameters.AddWithValue("@Id", id);
                del.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                if (HandleSqlDeleteException(ex, "Konten-Gruppe")) return;
                System.Windows.MessageBox.Show("Konten-Gruppe konnte nicht gelöscht werden:\n" + ex.Message,
                    "Löschen fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        // --- ERSETZEN ---
        public void LoescheKontenUnterGruppe(int id)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            string? bez = null;
            using (var get = new SqlCommand("SELECT Bezeichnung FROM dbo.KontenUnterGruppe WHERE Id=@Id", c))
            {
                get.Parameters.AddWithValue("@Id", id);
                var v = get.ExecuteScalar();
                if (v == null || v == DBNull.Value)
                {
                    System.Windows.MessageBox.Show("Eintrag in KontenUnterGruppe wurde nicht gefunden.",
                        "Löschen nicht möglich", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }
                bez = Convert.ToString(v);
            }

            int used = 0;
            using (var cnt = new SqlCommand("SELECT COUNT(*) FROM dbo.Kontenplan WHERE Untergruppe = @Bez", c))
            {
                cnt.Parameters.AddWithValue("@Bez", bez ?? (object)DBNull.Value);
                used = Convert.ToInt32(cnt.ExecuteScalar() ?? 0);
            }

            if (used > 0)
            {
                var msg = used == 1
                    ? "Diese Untergruppe wird in 1 Kontenplan-Zeile verwendet.\n\nUntergruppe in dieser Zeile auf (leer) setzen und den Stammdatensatz löschen?"
                    : $"Diese Untergruppe wird in {used} Kontenplan-Zeilen verwendet.\n\nUntergruppe in diesen Zeilen auf (leer) setzen und den Stammdatensatz löschen?";

                var ask = System.Windows.MessageBox.Show(msg, "Konten-Untergruppe löschen",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (ask != System.Windows.MessageBoxResult.Yes) return;

                using var tx = c.BeginTransaction();
                try
                {
                    using (var upd = new SqlCommand("UPDATE dbo.Kontenplan SET Untergruppe = N'' WHERE Untergruppe = @Bez", c, tx))
                    {
                        upd.Parameters.AddWithValue("@Bez", bez ?? (object)DBNull.Value);
                        upd.ExecuteNonQuery();
                    }
                    using (var del = new SqlCommand("DELETE FROM dbo.KontenUnterGruppe WHERE Id=@Id", c, tx))
                    {
                        del.Parameters.AddWithValue("@Id", id);
                        del.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    if (HandleSqlDeleteException(ex, "Konten-Untergruppe")) return;
                    System.Windows.MessageBox.Show("Konten-Untergruppe konnte nicht gelöscht werden:\n" + ex.Message,
                        "Löschen fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                return;
            }

            try
            {
                using var del = new SqlCommand("DELETE FROM dbo.KontenUnterGruppe WHERE Id=@Id", c);
                del.Parameters.AddWithValue("@Id", id);
                del.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                if (HandleSqlDeleteException(ex, "Konten-Untergruppe")) return;
                System.Windows.MessageBox.Show("Konten-Untergruppe konnte nicht gelöscht werden:\n" + ex.Message,
                    "Löschen fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }



        public List<KontenUnterGruppe> LadeKontenUnterGruppen()
        {
            var untergruppen = new List<KontenUnterGruppe>();

            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                string sql = "SELECT Id, Bezeichnung FROM KontenUnterGruppe";

                using (var command = new SqlCommand(sql, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        untergruppen.Add(new KontenUnterGruppe
                        {
                            Id = reader.GetInt32(0),
                            Bezeichnung = reader.GetString(1),
                        });
                    }
                }
            }

            return untergruppen;
        }

        public void SpeichereKontenUnterGruppe(string bezeichnung)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                string sql = "INSERT INTO KontenUnterGruppe (Bezeichnung) VALUES (@Bez)";
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Bez", bezeichnung);
                    command.ExecuteNonQuery();
                }
            }
        }


        

        // ADRESSEN
        public List<Adresse> LadeAdressen()
        {
            var list = new List<Adresse>();
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT Id, Name, Strasse, PLZ, Ort, Land, Typ, IBAN, Notiz,
       IstBudgetiert, StandardEinnahmenKontoId, DefaultKontoId
FROM Adresse
ORDER BY Name;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Adresse
                {
                    Id = r.GetInt32(0),
                    Name = r.GetString(1),
                    Strasse = r.IsDBNull(2) ? null : r.GetString(2),
                    PLZ = r.IsDBNull(3) ? null : r.GetString(3),
                    Ort = r.IsDBNull(4) ? null : r.GetString(4),
                    Land = r.IsDBNull(5) ? null : r.GetString(5),
                    Typ = r.IsDBNull(6) ? null : r.GetString(6),
                    IBAN = r.IsDBNull(7) ? null : r.GetString(7),
                    Notiz = r.IsDBNull(8) ? null : r.GetString(8),
                    IstBudgetiert = !r.IsDBNull(9) && r.GetBoolean(9),
                    StandardEinnahmenKontoId = r.IsDBNull(10) ? (int?)null : r.GetInt32(10),
                    DefaultKontoId = r.IsDBNull(11) ? (int?)null : r.GetInt32(11)
                });
            }

            return list;
        }


        public Adresse LadeAdresseById(int id)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();
            const string sql = "SELECT Id, Name, Strasse, PLZ, Ort, Land, Typ, IBAN, Notiz, IstBudgetiert, StandardEinnahmenKontoId, DefaultKontoId FROM Adresse WHERE Id=@id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                return new Adresse
                {
                    Id = r.GetInt32(0),
                    Name = r.GetString(1),
                    Strasse = r.IsDBNull(2) ? null : r.GetString(2),
                    PLZ = r.IsDBNull(3) ? null : r.GetString(3),
                    Ort = r.IsDBNull(4) ? null : r.GetString(4),
                    Land = r.IsDBNull(5) ? null : r.GetString(5),
                    Typ = r.IsDBNull(6) ? null : r.GetString(6),
                    IBAN = r.IsDBNull(7) ? null : r.GetString(7),
                    Notiz = r.IsDBNull(8) ? null : r.GetString(8),
                    IstBudgetiert = !r.IsDBNull(9) && r.GetBoolean(9),
                    StandardEinnahmenKontoId = r.IsDBNull(10) ? null : r.GetInt32(10),
                    DefaultKontoId = r.IsDBNull(11) ? (int?)null : r.GetInt32(11)
                };
            }
            throw new Exception($"Adresse {id} nicht gefunden.");
        }

        public Adresse? HoleAdresse(int id)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT Id, Name, Strasse, PLZ, Ort, Land, Typ, IBAN, Notiz,
       IstBudgetiert, StandardEinnahmenKontoId, DefaultKontoId
FROM Adresse
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new Adresse
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                Strasse = r.IsDBNull(2) ? null : r.GetString(2),
                PLZ = r.IsDBNull(3) ? null : r.GetString(3),
                Ort = r.IsDBNull(4) ? null : r.GetString(4),
                Land = r.IsDBNull(5) ? null : r.GetString(5),
                Typ = r.IsDBNull(6) ? null : r.GetString(6),
                IBAN = r.IsDBNull(7) ? null : r.GetString(7),
                Notiz = r.IsDBNull(8) ? null : r.GetString(8),
                IstBudgetiert = !r.IsDBNull(9) && r.GetBoolean(9),
                StandardEinnahmenKontoId = r.IsDBNull(10) ? (int?)null : r.GetInt32(10),
                DefaultKontoId = r.IsDBNull(11) ? (int?)null : r.GetInt32(11)
            };
        }

        public int SpeichereAdresse(Adresse a)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
INSERT INTO Adresse
    (Name, Strasse, PLZ, Ort, Land, Typ, IBAN, Notiz,
     IstBudgetiert, StandardEinnahmenKontoId, DefaultKontoId)
OUTPUT INSERTED.Id
VALUES
    (@Name, @Str, @PLZ, @Ort, @Land, @Typ, @IBAN, @Notiz,
     @Budg, @StdKonto, @DefKonto);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Name", a.Name);
            cmd.Parameters.AddWithValue("@Str", (object?)a.Strasse ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PLZ", (object?)a.PLZ ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Ort", (object?)a.Ort ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Land", (object?)a.Land ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Typ", (object?)a.Typ ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IBAN", (object?)a.IBAN ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notiz", (object?)a.Notiz ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Budg", a.IstBudgetiert);
            cmd.Parameters.AddWithValue("@StdKonto", (object?)a.StandardEinnahmenKontoId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DefKonto", (object?)a.DefaultKontoId ?? DBNull.Value);

            return (int)cmd.ExecuteScalar();
        }

        public void AktualisiereAdresse(Adresse a)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
UPDATE Adresse SET
    Name = @Name,
    Strasse = @Str,
    PLZ = @PLZ,
    Ort = @Ort,
    Land = @Land,
    Typ = @Typ,
    IBAN = @IBAN,
    Notiz = @Notiz,
    IstBudgetiert = @Budg,
    StandardEinnahmenKontoId = @StdKonto,
    DefaultKontoId = @DefKonto
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", a.Id);
            cmd.Parameters.AddWithValue("@Name", a.Name);
            cmd.Parameters.AddWithValue("@Str", (object?)a.Strasse ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PLZ", (object?)a.PLZ ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Ort", (object?)a.Ort ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Land", (object?)a.Land ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Typ", (object?)a.Typ ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IBAN", (object?)a.IBAN ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notiz", (object?)a.Notiz ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Budg", a.IstBudgetiert);
            cmd.Parameters.AddWithValue("@StdKonto", (object?)a.StandardEinnahmenKontoId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DefKonto", (object?)a.DefaultKontoId ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }


        // --- REFERENZ-SCHUTZ: Zentrale Helpers --------------------------------------
        public void LoescheAdresse(int id)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            // Preflight: Welche Tabellen referenzieren dbo.Adresse(Id)?
            // (nutzt den bereits eingefügten Helper GetReferencingCounts)
            var refs = GetReferencingCounts("dbo", "Adresse", "Id", id);

            // Gibt es Aliase?
            refs.TryGetValue("dbo.AdresseAlias", out int aliasCount);
            bool hasOtherRefs = refs.Any(kv => !kv.Key.Equals("dbo.AdresseAlias", StringComparison.OrdinalIgnoreCase));

            // Fall A: Aliase vorhanden
            if (aliasCount > 0)
            {
                // Hinweistext je nach weiteren Verweisen
                var infoWeitere = hasOtherRefs
                    ? "\n\nHinweis: Es existieren weitere Verweise (z. B. Transaktionen). Diese werden NICHT automatisch gelöscht."
                    : "";

                var frage = aliasCount == 1
                    ? $"Zu dieser Adresse existiert 1 Alias.\n\nAdresse zusammen mit diesem Alias löschen?{infoWeitere}"
                    : $"Zu dieser Adresse existieren {aliasCount} Aliase.\n\nAdresse zusammen mit diesen Aliasen löschen?{infoWeitere}";

                var choice = System.Windows.MessageBox.Show(
                    frage,
                    "Adresse & Aliase löschen",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (choice != System.Windows.MessageBoxResult.Yes)
                    return; // Nutzer lehnt ab → ruhig zurück

                // 1) Aliase löschen
                try
                {
                    LoescheAdressAliase(id);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Aliase konnten nicht gelöscht werden:\n" + ex.Message,
                        "Löschen fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                // 2) Referenzen neu ermitteln (Aliase sind weg)
                refs = GetReferencingCounts("dbo", "Adresse", "Id", id);
            }

            // Fall B: Es existieren (nach Alias-Löschung) noch andere Verweise → blockieren
            if (refs.Count > 0)
            {
                // Freundliche Liste der verbleibenden Blocker
                ShowDeleteBlockedMessage("Adresse", refs);
                return;
            }

            // Fall C: Keine Blocker → Adresse löschen
            try
            {
                using var cmd = new SqlCommand("DELETE FROM dbo.Adresse WHERE Id=@Id", c);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // FK 547 & Co. freundlich abfangen (falls sich zwischenzeitlich neue Verweise ergeben haben)
                if (HandleSqlDeleteException(ex, "Adresse")) return;

                System.Windows.MessageBox.Show("Adresse konnte nicht gelöscht werden:\n" + ex.Message,
                    "Löschen fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                // Kein throw → kein Programmstop
            }
        }

        
        // Speichert/überschreibt einen Alias (Unique-Constraint auf (AdresseId, Text) empfohlen)
        public void SpeichereAdressAlias(int adresseId, string text, string modus)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
MERGE AdresseAlias AS tgt
USING (SELECT @AdresseId AS AdresseId, @Text AS Text) AS src
    ON tgt.AdresseId = src.AdresseId AND tgt.Text = src.Text
WHEN MATCHED THEN
    UPDATE SET Modus = @Modus
WHEN NOT MATCHED THEN
    INSERT (AdresseId, Text, Modus) VALUES (@AdresseId, @Text, @Modus);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@AdresseId", adresseId);
            cmd.Parameters.AddWithValue("@Text", text);
            cmd.Parameters.AddWithValue("@Modus", modus ?? "Exact");
            cmd.ExecuteNonQuery();
        }

        // ---------------------------------------------
        // Schema für Adress-Buchungsregeln sicherstellen
        // NEU: optionaler Betragsbereich für präzisere Unterscheidung
        // ---------------------------------------------
        public void EnsureAdressBuchungsregelSchema()
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AdressBuchungsregel' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.AdressBuchungsregel
    (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdressBuchungsregel PRIMARY KEY,
        AdresseId     INT NOT NULL,
        IstEinnahme   BIT NOT NULL,
        TextPattern   NVARCHAR(200) NOT NULL,
        PatternModus  NVARCHAR(20) NOT NULL CONSTRAINT DF_AdressBuchungsregel_Modus DEFAULT('Contains'),
        KontoId       INT NOT NULL,

        -- NEU: optionaler Betragsbereich
        BetragVon     DECIMAL(18,2) NULL,
        BetragBis     DECIMAL(18,2) NULL,

        Prioritaet    INT NOT NULL CONSTRAINT DF_AdressBuchungsregel_Prio DEFAULT(100),
        IstAktiv      BIT NOT NULL CONSTRAINT DF_AdressBuchungsregel_Aktiv DEFAULT(1),
        CreatedAtUtc  DATETIME2 NOT NULL CONSTRAINT DF_AdressBuchungsregel_Created DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_AdressBuchungsregel_Adresse
            FOREIGN KEY (AdresseId) REFERENCES dbo.Adresse(Id),

        CONSTRAINT FK_AdressBuchungsregel_Konto
            FOREIGN KEY (KontoId) REFERENCES dbo.Kontenplan(Id)
    );

    CREATE INDEX IX_AdressBuchungsregel_AdresseId ON dbo.AdressBuchungsregel(AdresseId);
    CREATE INDEX IX_AdressBuchungsregel_KontoId   ON dbo.AdressBuchungsregel(KontoId);
END;

-- Nachrüstung bestehender DBs
IF COL_LENGTH('dbo.AdressBuchungsregel', 'BetragVon') IS NULL
BEGIN
    ALTER TABLE dbo.AdressBuchungsregel
    ADD BetragVon DECIMAL(18,2) NULL;
END;

IF COL_LENGTH('dbo.AdressBuchungsregel', 'BetragBis') IS NULL
BEGIN
    ALTER TABLE dbo.AdressBuchungsregel
    ADD BetragBis DECIMAL(18,2) NULL;
END;

-- NEU: Evidenz-Tracking statt Einzelbeispiel-Fixierung
IF COL_LENGTH('dbo.AdressBuchungsregel', 'BelegAnzahl') IS NULL
BEGIN
    ALTER TABLE dbo.AdressBuchungsregel
    ADD BelegAnzahl INT NOT NULL CONSTRAINT DF_AdressBuchungsregel_BelegAnzahl DEFAULT(1);
END;

IF COL_LENGTH('dbo.AdressBuchungsregel', 'LetzteBestaetigung') IS NULL
BEGIN
    ALTER TABLE dbo.AdressBuchungsregel
    ADD LetzteBestaetigung DATETIME2 NULL;
END;

IF COL_LENGTH('dbo.AdressBuchungsregel', 'IstKonflikt') IS NULL
BEGIN
    ALTER TABLE dbo.AdressBuchungsregel
    ADD IstKonflikt BIT NOT NULL CONSTRAINT DF_AdressBuchungsregel_IstKonflikt DEFAULT(0);
END;

-- NEU: Belege (Evidenz-Historie) je bestätigter Zuordnung.
-- Grundlage für die Regel-Ableitung in LernAdressBuchungsregel().
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AdressBuchungsregelBeleg' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.AdressBuchungsregelBeleg
    (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdressBuchungsregelBeleg PRIMARY KEY,
        AdresseId     INT NOT NULL,
        IstEinnahme   BIT NOT NULL,
        TextPattern   NVARCHAR(200) NOT NULL,
        PatternModus  NVARCHAR(20) NOT NULL CONSTRAINT DF_AdressBuchungsregelBeleg_Modus DEFAULT('Contains'),
        Betrag        DECIMAL(18,2) NOT NULL,
        KontoId       INT NOT NULL,
        ErstelltAmUtc DATETIME2 NOT NULL CONSTRAINT DF_AdressBuchungsregelBeleg_Created DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_AdressBuchungsregelBeleg_Adresse
            FOREIGN KEY (AdresseId) REFERENCES dbo.Adresse(Id),

        CONSTRAINT FK_AdressBuchungsregelBeleg_Konto
            FOREIGN KEY (KontoId) REFERENCES dbo.Kontenplan(Id)
    );

    CREATE INDEX IX_AdressBuchungsregelBeleg_Lookup
        ON dbo.AdressBuchungsregelBeleg(AdresseId, IstEinnahme, TextPattern, PatternModus);
END;
";

            using var cmd = new SqlCommand(sql, c);
            cmd.ExecuteNonQuery();
        }



        // Liefert alle Aliase (Text -> Adresse)
        public List<AdressAlias> LadeAdressAliase()
        {
            var list = new List<AdressAlias>();
            using var c = new SqlConnection(_connectionString);
            c.Open();

            // Beispiel-Tabelle/Spaltennamen: AdresseAlias (Id, AdresseId, Text, Modus)
            const string sql = @"SELECT Id, AdresseId, Text, Modus FROM AdresseAlias;";
            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new AdressAlias(
                    r.GetInt32(0),
                    r.GetInt32(1),
                    r.IsDBNull(2) ? "" : r.GetString(2),
                    r.IsDBNull(3) ? "Exact" : r.GetString(3)
                ));
            }
            return list;
        }



        // --- GELDINSTITUTE -----------------------------
        public List<Geldinstitut> LadeGeldinstitute()
        {
            var list = new List<Geldinstitut>();
            using var c = new SqlConnection(_connectionString);
            c.Open();
            const string sql = @"SELECT Id, Name, BIC, IBAN, KontoNummer, Notiz, Anfangsbestand, Anfangsdatum
                     FROM Geldinstitut
                     ORDER BY Name";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Geldinstitut
                {
                    Id = r.GetInt32(0),
                    Name = r.GetString(1),
                    BIC = r.IsDBNull(2) ? null : r.GetString(2),
                    IBAN = r.IsDBNull(3) ? null : r.GetString(3),
                    KontoNummer = r.IsDBNull(4) ? null : r.GetString(4),
                    Notiz = r.IsDBNull(5) ? null : r.GetString(5),
                    Anfangsbestand = r.IsDBNull(6) ? 0m : r.GetDecimal(6),
                    Anfangsdatum = r.IsDBNull(7) ? (DateTime?)null : r.GetDateTime(7)
                });

            }
            return list;
        }

        public int SpeichereGeldinstitut(Geldinstitut g)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();
            const string sql = @"INSERT INTO Geldinstitut (Name, BIC, IBAN, KontoNummer, Notiz, Anfangsbestand, Anfangsdatum)
                         OUTPUT INSERTED.Id
                         VALUES (@Name, @BIC, @IBAN, @Kto, @Notiz, @Anf, @AnfDat)";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Name", g.Name);
            cmd.Parameters.AddWithValue("@BIC", (object?)g.BIC ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IBAN", (object?)g.IBAN ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Kto", (object?)g.KontoNummer ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notiz", (object?)g.Notiz ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Anf", g.Anfangsbestand);
            cmd.Parameters.AddWithValue("@AnfDat", (object?)g.Anfangsdatum ?? DBNull.Value);
            return (int)cmd.ExecuteScalar();
        }


        public void AktualisiereGeldinstitut(Geldinstitut g)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();
            const string sql = @"UPDATE Geldinstitut SET
                           Name=@Name, BIC=@BIC, IBAN=@IBAN, KontoNummer=@Kto, Notiz=@Notiz,
                           Anfangsbestand=@Anf, Anfangsdatum=@AnfDat
                         WHERE Id=@Id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", g.Id);
            cmd.Parameters.AddWithValue("@Name", g.Name);
            cmd.Parameters.AddWithValue("@BIC", (object?)g.BIC ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IBAN", (object?)g.IBAN ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Kto", (object?)g.KontoNummer ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notiz", (object?)g.Notiz ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Anf", g.Anfangsbestand);
            cmd.Parameters.AddWithValue("@AnfDat", (object?)g.Anfangsdatum ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        // DatabaseService.cs
        // KOMPLETTE METHODE – 1:1 ERSETZEN
        public void LoescheGeldinstitut(int id)
        {
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();

            // Prüfen, ob noch Verweise auf das Geldinstitut existieren (z. B. Transaktion)
            var refs = GetReferencingCounts("dbo", "Geldinstitut", "Id", id);
            if (refs.Count > 0)
            {
                ShowDeleteBlockedMessage("Geldinstitut", refs);
                return; // ruhig zurück – kein Programmstop
            }

            // Keine Blocker → löschen
            try
            {
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                    "DELETE FROM dbo.Geldinstitut WHERE Id=@Id", c);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // Falls im Rennen doch FK greift → freundlich abfangen
                if (HandleSqlDeleteException(ex, "Geldinstitut")) return;

                System.Windows.MessageBox.Show("Geldinstitut konnte nicht gelöscht werden:\n" + ex.Message,
                    "Löschen fehlgeschlagen",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                // kein throw → kein Programmstop
            }
        }

        public List<GeldinstitutSaldo> LadeGeldinstituteMitSaldo(DateTime? abgrenzungsdatum)
        {
            var bis = (abgrenzungsdatum?.Date ?? DateTime.Today);

            var result = new List<GeldinstitutSaldo>();
            var institute = LadeGeldinstitute(); // Stammdaten inkl. Anfangsbestand/-datum

            foreach (var g in institute)
            {
                DateTime? von = g.Anfangsdatum?.Date; // nur ab Anfangsdatum zählen
                var tx = LadeTransaktionenByGeldinstitut(
                    geldinstitutId: g.Id,
                    von: von,
                    bis: bis,
                    minBetrag: null,
                    maxBetrag: null,
                    adresseId: null,
                    kontoId: null
                );

                decimal gebucht = 0m;

                foreach (var t in tx)
                {
                    if (t.NachKontoId.HasValue && !t.VonKontoId.HasValue)
                    {
                        // Bank -> Budgetkonto: Richtung über Nummernkreis der Nach-Seite
                        if (IstEinnahmenKonto(t.NachKontoId.Value))
                            gebucht += t.Betrag;   // Bank +
                        else
                            gebucht -= t.Betrag;   // Bank -
                    }
                    else if (!t.NachKontoId.HasValue)
                    {
                        // Bank-only (keine Nach-Konto-Seite):
                        // Pragmatik wie in der Detailansicht:
                        // - Wenn VonKontoId oder Adresse vorhanden -> Geldfluss *zur* Bank => +
                        // - sonst neutral (0)
                        if (t.VonKontoId.HasValue || t.AdresseId.HasValue)
                            gebucht += t.Betrag;   // Bank +
                                                   // else: gebucht += 0;
                    }
                    else
                    {
                        // Exotisch: beide Budgetseiten gesetzt -> neutral
                        // (kommt praktisch nicht vor, deshalb 0)
                    }
                }

                var saldo = g.Anfangsbestand + gebucht;

                result.Add(new GeldinstitutSaldo
                {
                    Id = g.Id,
                    Name = g.Name,
                    BIC = g.BIC,
                    IBAN = g.IBAN,
                    KontoNummer = g.KontoNummer,
                    Notiz = g.Notiz,
                    Anfangsbestand = g.Anfangsbestand,
                    Anfangsdatum = g.Anfangsdatum,
                    Gebucht = gebucht,
                    Schlussaldo = saldo
                });
            }

            return result.OrderBy(x => x.Name).ToList();
        }




        public int CountCreditCardStaging()
        {
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();
            const string sql = "SELECT COUNT(*) FROM CreditCardImportStaging";
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            try
            {
                var v = cmd.ExecuteScalar();
                return v == null || v == DBNull.Value ? 0 : Convert.ToInt32(v);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) // Invalid object name
            {
                // Tabelle existiert (noch) nicht -> 0 anzeigen
                return 0;
            }
        }

        public int CountBankImportItem()
        {
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();
            const string sql = "SELECT COUNT(*) FROM BankImportItem";
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            try
            {
                var v = cmd.ExecuteScalar();
                return v == null || v == DBNull.Value ? 0 : Convert.ToInt32(v);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) // Invalid object name
            {
                // Tabelle existiert (noch) nicht -> 0 anzeigen
                return 0;
            }
        }



        // -----------Löscht eine importierte BankImportItem-Zeile-------------
        public void DeleteBankImportItem(int id)
        {
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "DELETE FROM dbo.BankImportItem WHERE Id = @id;", c);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ---------- TRANSAKTIONEN ----------

        public void SpeichereTransaktion(DateTime datum, int? vonKontoId, int? nachKontoId,
                         decimal betrag, string? notiz,
                         int? adresseId, int? geldinstitutId,
                         DateTime? budgetDatum) // NEU: optionales BudgetDatum
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"INSERT INTO Transaktion
                 (Datum, BudgetDatum, VonKontoId, NachKontoId, Betrag, Notiz, AdresseId, GeldinstitutId)  -- NEU: BudgetDatum
                 VALUES (@d, @bd, @v, @n, @b, @z, @a, @g)";

            using var cmd = new SqlCommand(sql, c);

            cmd.Parameters.Add(new SqlParameter("@d", System.Data.SqlDbType.Date) { Value = datum.Date });

            // NEU: BudgetDatum Parameter (nullable)
            cmd.Parameters.Add(new SqlParameter("@bd", System.Data.SqlDbType.Date)
            {
                Value = (object?)budgetDatum?.Date ?? DBNull.Value
            });

            var pVon = new SqlParameter("@v", System.Data.SqlDbType.Int);
            pVon.Value = (object?)vonKontoId ?? DBNull.Value;
            cmd.Parameters.Add(pVon);

            var pNach = new SqlParameter("@n", System.Data.SqlDbType.Int);
            pNach.Value = (object?)nachKontoId ?? DBNull.Value;
            cmd.Parameters.Add(pNach);

            var pBetrag = new SqlParameter("@b", System.Data.SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = betrag };
            cmd.Parameters.Add(pBetrag);

            var pNotiz = new SqlParameter("@z", System.Data.SqlDbType.NVarChar, 200) { Value = (object?)notiz ?? DBNull.Value };
            cmd.Parameters.Add(pNotiz);

            var pAdr = new SqlParameter("@a", System.Data.SqlDbType.Int) { Value = (object?)adresseId ?? DBNull.Value };
            cmd.Parameters.Add(pAdr);

            var pBank = new SqlParameter("@g", System.Data.SqlDbType.Int) { Value = (object?)geldinstitutId ?? DBNull.Value };
            cmd.Parameters.Add(pBank);

            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // Debug-Hilfe: zeigt den Grund (FK-Fehler, NOT NULL, etc.)
                System.Windows.MessageBox.Show("Transaktion konnte nicht gespeichert werden:\n" + ex.Message,
                    "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                throw; // optional: weiterwerfen oder hier beenden
            }
        }



        // Neu:Budgetdatum
        public MyCoinFlow.Models.Transaktion? HoleTransaktion(int id)
        {
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT t.Id, t.Datum, t.BudgetDatum, t.VonKontoId, t.NachKontoId,
       t.Betrag, t.Notiz,
       t.AdresseId, a.Name AS AdresseName,
       t.GeldinstitutId, g.Name AS BankName,
       t.ImportQuelle
FROM dbo.Transaktion t
LEFT JOIN dbo.Adresse a      ON a.Id = t.AdresseId
LEFT JOIN dbo.Geldinstitut g ON g.Id = t.GeldinstitutId
WHERE t.Id = @id;";

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new MyCoinFlow.Models.Transaktion
            {
                Id = r.GetInt32(0),
                Datum = r.GetDateTime(1),

                // NEU: BudgetDatum laden (optional)
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
            };
        }

        /// <summary>
        /// DMS-Matching: liefert Transaktionen, deren Betrag exakt (betragsmässig, ohne Vorzeichen)
        /// zum übergebenen Betrag passt und deren Datum im Fenster um das Dokumentdatum liegt.
        /// Transaktionen, die bereits ein Attachment haben, werden ausgeschlossen (kein erneuter
        /// Vorschlag für bereits dokumentierte Buchungen).
        /// </summary>
        public List<Transaktion> FindCandidateTransaktionenForMatch(decimal betrag, DateTime docDatum, int tageVorher, int tageNachher)
        {
            var list = new List<Transaktion>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT t.Id, t.Datum, t.BudgetDatum, t.VonKontoId, t.NachKontoId,
       t.Betrag, t.Notiz,
       t.AdresseId, a.Name AS AdresseName,
       t.GeldinstitutId, g.Name AS BankName,
       t.ImportQuelle
FROM dbo.Transaktion t
LEFT JOIN dbo.Adresse a      ON a.Id = t.AdresseId
LEFT JOIN dbo.Geldinstitut g ON g.Id = t.GeldinstitutId
WHERE ABS(t.Betrag) = ABS(@betrag)
  AND t.Datum BETWEEN @von AND @bis
  AND NOT EXISTS (SELECT 1 FROM dbo.Attachment att WHERE att.TransaktionId = t.Id)
ORDER BY t.Datum DESC;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@betrag", betrag);
            cmd.Parameters.AddWithValue("@von", docDatum.Date.AddDays(-Math.Abs(tageVorher)));
            cmd.Parameters.AddWithValue("@bis", docDatum.Date.AddDays(Math.Abs(tageNachher)));

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Transaktion
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

        /// <summary>
        /// DMS: einfache Suche für das manuelle Zuweisen eines Dokuments zu einer Transaktion
        /// (Betrag/Datumsbereich/Freitext optional, alle kombinierbar). Für den
        /// DmsAssignTransactionDialog-Suchmodus.
        /// </summary>
        public List<Transaktion> SearchTransaktionenForZuordnung(string? text, decimal? betrag, DateTime? von, DateTime? bis, int maxResults = 50)
        {
            var list = new List<Transaktion>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT TOP (@max) t.Id, t.Datum, t.BudgetDatum, t.VonKontoId, t.NachKontoId,
       t.Betrag, t.Notiz,
       t.AdresseId, a.Name AS AdresseName,
       t.GeldinstitutId, g.Name AS BankName,
       t.ImportQuelle
FROM dbo.Transaktion t
LEFT JOIN dbo.Adresse a      ON a.Id = t.AdresseId
LEFT JOIN dbo.Geldinstitut g ON g.Id = t.GeldinstitutId
WHERE (@betrag IS NULL OR ABS(t.Betrag) = ABS(@betrag))
  AND (@von IS NULL OR t.Datum >= @von)
  AND (@bis IS NULL OR t.Datum <= @bis)
  AND (@q IS NULL OR
       t.Notiz LIKE '%' + @q + '%' OR
       a.Name LIKE '%' + @q + '%' OR
       g.Name LIKE '%' + @q + '%')
ORDER BY t.Datum DESC;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@max", maxResults);
            cmd.Parameters.AddWithValue("@betrag", (object?)betrag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@von", (object?)von ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bis", (object?)bis ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@q", string.IsNullOrWhiteSpace(text) ? DBNull.Value : (object)text.Trim());

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Transaktion
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


        // NEU: Gefilterte Transaktionen für ein Geldinstitut
        public List<Transaktion> LadeTransaktionenByGeldinstitut(
    int geldinstitutId,
    DateTime? von = null,
    DateTime? bis = null,
    decimal? minBetrag = null,
    decimal? maxBetrag = null,
    int? adresseId = null,
    int? kontoId = null)
        {
            var list = new List<Transaktion>();
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT t.Id, t.Datum, t.BudgetDatum, t.VonKontoId, t.NachKontoId, t.Betrag, t.Notiz,
       t.AdresseId, a.Name as AdresseName,
       t.GeldinstitutId, g.Name as BankName,
       t.ImportQuelle
FROM Transaktion t
LEFT JOIN Adresse a ON t.AdresseId = a.Id
LEFT JOIN Geldinstitut g ON t.GeldinstitutId = g.Id
WHERE t.GeldinstitutId = @gi
  AND (@von IS NULL OR ISNULL(t.BudgetDatum, t.Datum) >= @von)
AND (@bis IS NULL OR ISNULL(t.BudgetDatum, t.Datum) <= @bis)
  AND (@minB IS NULL OR t.Betrag >= @minB)
  AND (@maxB IS NULL OR t.Betrag <= @maxB)
  AND (@adr IS NULL OR t.AdresseId = @adr)
  AND (@kto IS NULL OR t.VonKontoId = @kto OR t.NachKontoId = @kto)
ORDER BY t.Datum DESC, t.Id DESC;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@gi", geldinstitutId);
            cmd.Parameters.AddWithValue("@von", (object?)von?.Date ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bis", (object?)bis?.Date ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@minB", (object?)minBetrag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@maxB", (object?)maxBetrag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@adr", (object?)adresseId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@kto", (object?)kontoId ?? DBNull.Value);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Transaktion
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

        // ---------- TRANSAKTIONEN UPDATE/DELETE ----------
        public void AktualisiereTransaktion(int id, DateTime datum, int? vonKontoId, int? nachKontoId,
                                            decimal betrag, string? notiz,
                                            int? adresseId, int? geldinstitutId,
                                            DateTime? budgetDatum) // NEU: optionales BudgetDatum
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"UPDATE Transaktion SET
                   Datum=@d,
                   BudgetDatum=@bd,   -- NEU: BudgetDatum speichern
                   VonKontoId=@v,
                   NachKontoId=@n,
                   Betrag=@b,
                   Notiz=@z,
                   AdresseId=@a,
                   GeldinstitutId=@g
                 WHERE Id=@id";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add(new SqlParameter("@id", System.Data.SqlDbType.Int) { Value = id });
            cmd.Parameters.Add(new SqlParameter("@d", System.Data.SqlDbType.Date) { Value = datum.Date });

            // NEU: BudgetDatum Parameter (nullable)
            cmd.Parameters.Add(new SqlParameter("@bd", System.Data.SqlDbType.Date)
            {
                Value = (object?)budgetDatum?.Date ?? DBNull.Value
            });

            cmd.Parameters.Add(new SqlParameter("@v", System.Data.SqlDbType.Int) { Value = (object?)vonKontoId ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@n", System.Data.SqlDbType.Int) { Value = (object?)nachKontoId ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@b", System.Data.SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = betrag });
            cmd.Parameters.Add(new SqlParameter("@z", System.Data.SqlDbType.NVarChar, 200) { Value = (object?)notiz ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@a", System.Data.SqlDbType.Int) { Value = (object?)adresseId ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@g", System.Data.SqlDbType.Int) { Value = (object?)geldinstitutId ?? DBNull.Value });

            cmd.ExecuteNonQuery();
        }


        public void LoescheTransaktion(int id)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            // Guard: Transaktion darf nicht gelöscht werden, wenn sie in einem STWE-Set verwendet wird.
            const string sqlCheck = "SELECT COUNT(1) FROM dbo.StweSet WHERE TransaktionId = @id;";
            int usedCount;
            using (var check = new SqlCommand(sqlCheck, c))
            {
                check.Parameters.Add(new SqlParameter("@id", System.Data.SqlDbType.Int) { Value = id });
                var usedCountObj = check.ExecuteScalar();
                usedCount = usedCountObj == null || usedCountObj == DBNull.Value ? 0 : Convert.ToInt32(usedCountObj);
            }

            if (usedCount > 0)
            {
                // Optional: ein paar Set-Titel anzeigen (damit klar ist, wo es hängt).
                const string sqlTitles = @"
SELECT TOP (5) Id, Titel
FROM dbo.StweSet
WHERE TransaktionId = @id
ORDER BY Id DESC;";

                var lines = new List<string>
        {
            $"Diese Transaktion kann nicht gelöscht werden, weil sie in {usedCount} STWE-Set(s) verwendet wird."
        };

                using (var cmdTitles = new SqlCommand(sqlTitles, c))
                {
                    cmdTitles.Parameters.Add(new SqlParameter("@id", System.Data.SqlDbType.Int) { Value = id });

                    using var r = cmdTitles.ExecuteReader();
                    while (r.Read())
                    {
                        var setId = r.GetInt32(0);
                        var titel = r.IsDBNull(1) ? "" : r.GetString(1);
                        titel = string.IsNullOrWhiteSpace(titel) ? "(ohne Titel)" : titel.Trim();

                        lines.Add($"• Set #{setId}: {titel}");
                    }
                }

                if (usedCount > 5)
                    lines.Add("• …");

                throw new InvalidOperationException(string.Join(Environment.NewLine, lines));
            }

            const string sql = "DELETE FROM Transaktion WHERE Id=@id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add(new SqlParameter("@id", System.Data.SqlDbType.Int) { Value = id });
            cmd.ExecuteNonQuery();
        }

        // --- Defaults für Konto-Vorschläge (Adresse.DefaultKontoId) -----------------

        private static string NormalizeIban(string? iban)
            => string.IsNullOrWhiteSpace(iban) ? "" : iban.Replace(" ", "").ToUpperInvariant();


        /// <summary>
        /// Liefert DefaultKontoId über IBAN-Match (Adresse.IBAN).
        /// </summary>
        public int? HoleDefaultKontoIdByIban(string? counterpartyIban)
        {
            var norm = NormalizeIban(counterpartyIban);
            if (string.IsNullOrEmpty(norm)) return null;

            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT TOP 1 DefaultKontoId
FROM Adresse
WHERE REPLACE(UPPER(ISNULL(IBAN,'')),' ','') = @ibanNorm;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@ibanNorm", norm);
            var v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? (int?)null : Convert.ToInt32(v);
        }



        // KREDITKARTEN MST 250825

        // ===================== KREDITKARTEN =====================

        // ---- Konten-Lookup (für Dialog & Anzeige) ----
        public List<KontoLookup> LadeKontoLookup()
        {
            var list = new List<KontoLookup>();
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT Id, Kontonummer, Art, Gruppe, Untergruppe, Detail
FROM Kontenplan
ORDER BY Kontonummer, Detail;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int id = r.GetInt32(0);
                string nr = r.IsDBNull(1) ? "" : r.GetInt32(1).ToString();
                string art = r.IsDBNull(2) ? "" : r.GetString(2);
                string grp = r.IsDBNull(3) ? "" : r.GetString(3);
                string ug = r.IsDBNull(4) ? "" : r.GetString(4);
                string detail = r.IsDBNull(5) ? "" : r.GetString(5);

                string label = string.IsNullOrWhiteSpace(detail) ? nr : $"{nr} {detail}";
                if (!string.IsNullOrWhiteSpace(art))
                {
                    var tail = art;
                    if (!string.IsNullOrWhiteSpace(grp)) tail += $"/{grp}";
                    if (!string.IsNullOrWhiteSpace(ug)) tail += $"/{ug}";
                    label += $" [{tail}]";
                }

                list.Add(new KontoLookup { Id = id, Anzeige = label });
            }
            return list;
        }

        public List<KontoLookup> LadeKreditkartenKonten()
        {
            // Heuristik: alle Konten, deren Art/Gruppe/Untergruppe/Detail auf „Kreditkarte“ hindeuten
            var list = new List<KontoLookup>();
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT Id, Kontonummer, Art, Gruppe, Untergruppe, Detail
FROM Kontenplan
WHERE 
    (Detail LIKE '%Kreditkart%' OR Gruppe LIKE '%Kreditkart%' OR Untergruppe LIKE '%Kreditkart%' OR Art LIKE '%Kreditkart%')
    OR (Detail LIKE '%Credit%card%' OR Gruppe LIKE '%Credit%card%' OR Untergruppe LIKE '%Credit%card%' OR Art LIKE '%Credit%card%')
ORDER BY Kontonummer, Detail;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int id = r.GetInt32(0);
                string nr = r.IsDBNull(1) ? "" : r.GetInt32(1).ToString();
                string art = r.IsDBNull(2) ? "" : r.GetString(2);
                string grp = r.IsDBNull(3) ? "" : r.GetString(3);
                string ug = r.IsDBNull(4) ? "" : r.GetString(4);
                string detail = r.IsDBNull(5) ? "" : r.GetString(5);

                string label = string.IsNullOrWhiteSpace(detail) ? nr : $"{nr} {detail}";
                if (!string.IsNullOrWhiteSpace(art))
                {
                    var tail = art;
                    if (!string.IsNullOrWhiteSpace(grp)) tail += $"/{grp}";
                    if (!string.IsNullOrWhiteSpace(ug)) tail += $"/{ug}";
                    label += $" [{tail}]";
                }

                list.Add(new KontoLookup { Id = id, Anzeige = label });
            }
            return list;
        }




        // ---- Adresse (nur Name) ----
        public int? FindeOderErzeugeAdresseByName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sel = "SELECT TOP 1 Id FROM Adresse WHERE Name = @n";
            using (var s = new SqlCommand(sel, c))
            {
                s.Parameters.AddWithValue("@n", name.Trim());
                var id = s.ExecuteScalar();
                if (id != null && id != DBNull.Value)
                    return Convert.ToInt32(id);
            }

            const string ins = @"INSERT INTO Adresse (Name) OUTPUT INSERTED.Id VALUES (@n)";
            using (var i = new SqlCommand(ins, c))
            {
                i.Parameters.AddWithValue("@n", name.Trim());
                return (int)i.ExecuteScalar();
            }
        }

        // ---- Import-Hash + Dedupe + Insert ----
        private static string ComputeSha256Hex(string input)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }


        private static string BaueImportHashV2(DateTime datum, decimal betragPositiv,
                                       string? beschreibung, string? haendler,
                                       string? kartennummer, string? debitKredit)
        {
            // Normalisieren
            string N(string? s) => (s ?? "").Trim().ToUpperInvariant();
            var parts = new[]
            {
        datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        betragPositiv.ToString("0.######", CultureInfo.InvariantCulture),
        N(beschreibung), N(haendler), N(kartennummer), N(debitKredit) // <- NEU: Richtung mit in den Hash
    };
            var payload = string.Join("|", parts);

            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
            return BitConverter.ToString(bytes).Replace("-", "");
        }



        public int? SucheTransaktionIdByHash(string hash)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = "SELECT TOP 1 Id FROM Transaktion WHERE ImportHash = @h";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@h", hash);

            var v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? (int?)null : Convert.ToInt32(v);
        }

        public int InsertTransaktionMitImport(DateTime datum, int? vonKontoId, int? nachKontoId,
                                              decimal betragPositiv, string? notiz,
                                              int? adresseId, int? geldinstitutId,
                                              string importQuelle, string importHash)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"INSERT INTO Transaktion
        (Datum, VonKontoId, NachKontoId, Betrag, Notiz, AdresseId, GeldinstitutId, ImportQuelle, ImportHash)
        OUTPUT INSERTED.Id
        VALUES (@d, @v, @n, @b, @z, @a, @g, @q, @h)";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add(new SqlParameter("@d", System.Data.SqlDbType.Date) { Value = datum.Date });
            cmd.Parameters.Add(new SqlParameter("@v", System.Data.SqlDbType.Int) { Value = (object?)vonKontoId ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@n", System.Data.SqlDbType.Int) { Value = (object?)nachKontoId ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@b", System.Data.SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = betragPositiv });
            cmd.Parameters.Add(new SqlParameter("@z", System.Data.SqlDbType.NVarChar, 200) { Value = (object?)notiz ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@a", System.Data.SqlDbType.Int) { Value = (object?)adresseId ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@g", System.Data.SqlDbType.Int) { Value = (object?)geldinstitutId ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@q", System.Data.SqlDbType.NVarChar, 100) { Value = importQuelle });
            cmd.Parameters.Add(new SqlParameter("@h", System.Data.SqlDbType.NVarChar, 64) { Value = importHash });

            return (int)cmd.ExecuteScalar();
        }

        // ---- Excel einlesen (genau deine Spalten) ----
        // ---- Excel/CSV einlesen (genau deine Spalten) ----
        // ---- Excel/CSV einlesen (genau deine Spalten) ----
        public List<CreditCardImportRow> LeseCreditCardExcel(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException(filePath);

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            DataTable t;

            // Diese Header erwarten wir im Rohfile als mögliche Startsignale.
            // Noch VOR dem Mapping suchen wir nach den Source-Headern.
            var erwarteteRohHeader = new[]
            {
        "Datum",
        "Buchungstext",
        "Gegenpartei",
        "Kategorie",
        "Betrag",
        "Konto",
        "Debit/Kredit",
        "Transaktionsdatum",
        "Beschreibung",
        "Händler",
        "Händlerkategorie",
        "Kartennummer"
    };

            // === A) CSV ========================================================
            if (ext == ".csv")
            {
                t = LeseCsvMitAutomatischerHeaderZeile(filePath, erwarteteRohHeader);

                // Mapping anwenden
                t = new CreditCardImportMappingService(this).ApplyMappingIfNeeded(t);
            }
            // === B) Excel ======================================================
            else
            {
                t = LeseExcelMitAutomatischerHeaderZeile(filePath, erwarteteRohHeader);

                // Mapping anwenden
                t = new CreditCardImportMappingService(this).ApplyMappingIfNeeded(t);
            }

            // === C) Master-Spalten prüfen =====================================
            const string COL_DATUM = "Transaktionsdatum";
            const string COL_BESCH = "Beschreibung";
            const string COL_HAEND = "Händler";
            const string COL_KAT = "Händlerkategorie";
            const string COL_BETR = "Betrag";
            const string COL_DK = "Debit/Kredit";
            const string COL_CARD = "Kartennummer"; // optional

            foreach (var col in new[] { COL_DATUM, COL_BESCH, COL_HAEND, COL_KAT, COL_BETR, COL_DK })
            {
                if (!t.Columns.Contains(col))
                    throw new Exception($"Spalte „{col}“ fehlt in der Excel-Datei.");
            }

            // === D) Zeilen lesen ==============================================
            var list = new List<CreditCardImportRow>();
            var ciCH = new CultureInfo("de-CH");

            foreach (DataRow r in t.Rows)
            {
                if (!TryGetDate(r[COL_DATUM], out var datum))
                    continue;

                if (!TryGetDecimal(r[COL_BETR], ciCH, out var betragOriginal))
                    continue;

                var besch = r[COL_BESCH]?.ToString()?.Trim() ?? "";
                var haend = r[COL_HAEND]?.ToString()?.Trim();
                var kat = r[COL_KAT]?.ToString()?.Trim();
                var dk = r[COL_DK]?.ToString()?.Trim() ?? "";
                var card = t.Columns.Contains(COL_CARD) ? r[COL_CARD]?.ToString()?.Trim() : null;

                // Falls Debit/Kredit leer ist, Richtung aus Vorzeichen ableiten.
                if (string.IsNullOrWhiteSpace(dk))
                {
                    if (betragOriginal < 0m)
                        dk = "DEBIT";
                    else if (betragOriginal > 0m)
                        dk = "KREDIT";
                }

                // Betrag intern positiv speichern; Richtung separat über DebitKredit.
                var betragPos = Math.Abs(betragOriginal);

                list.Add(new CreditCardImportRow
                {
                    Datum = datum.Value.Date,
                    Beschreibung = besch,
                    Haendler = string.IsNullOrWhiteSpace(haend) ? null : haend,
                    Kategorie = string.IsNullOrWhiteSpace(kat) ? null : kat,
                    Betrag = betragPos,
                    DebitKredit = dk,
                    Kartennummer = string.IsNullOrWhiteSpace(card) ? null : card
                });
            }

            return list;

            // ================================================================
            // Hilfsmethoden nur für diese Methode lokal gehalten
            // ================================================================

            DataTable LeseCsvMitAutomatischerHeaderZeile(string pfad, string[] erwarteteHeader)
            {
                var alleZeilen = new List<string[]>();

                using (var sr = new StreamReader(pfad, Encoding.Latin1, detectEncodingFromByteOrderMarks: true))
                {
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        alleZeilen.Add(line.Split(';'));
                    }
                }

                if (alleZeilen.Count == 0)
                    return new DataTable();

                int headerIndex = FindeHeaderZeile(alleZeilen, erwarteteHeader);
                if (headerIndex < 0)
                    throw new Exception("Keine gültige Header-Zeile in der CSV-Datei gefunden.");

                var table = new DataTable();

                var headerCells = alleZeilen[headerIndex];
                var verwendeteSpaltennamen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < headerCells.Length; i++)
                {
                    var colName = (headerCells[i] ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(colName))
                        colName = $"Spalte{i + 1}";

                    colName = MacheEindeutigenSpaltennamen(colName, verwendeteSpaltennamen);
                    table.Columns.Add(colName);
                }

                for (int rowIndex = headerIndex + 1; rowIndex < alleZeilen.Count; rowIndex++)
                {
                    var raw = alleZeilen[rowIndex];

                    if (IstLeerzeile(raw))
                        continue;

                    var row = table.NewRow();
                    for (int col = 0; col < table.Columns.Count; col++)
                    {
                        row[col] = col < raw.Length ? (raw[col] ?? string.Empty).Trim() : string.Empty;
                    }

                    table.Rows.Add(row);
                }

                return table;
            }

            DataTable LeseExcelMitAutomatischerHeaderZeile(string pfad, string[] erwarteteHeader)
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                using var fs = File.Open(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(fs);

                var ds = reader.AsDataSet(new ExcelDataReader.ExcelDataSetConfiguration
                {
                    ConfigureDataTable = _ => new ExcelDataReader.ExcelDataTableConfiguration
                    {
                        UseHeaderRow = false
                    }
                });

                if (ds.Tables.Count == 0)
                    return new DataTable();

                var roh = ds.Tables[0];
                if (roh.Rows.Count == 0)
                    return new DataTable();

                var alleZeilen = new List<string[]>();

                foreach (DataRow dr in roh.Rows)
                {
                    var cells = new string[roh.Columns.Count];
                    for (int i = 0; i < roh.Columns.Count; i++)
                    {
                        cells[i] = dr[i]?.ToString()?.Trim() ?? string.Empty;
                    }
                    alleZeilen.Add(cells);
                }

                int headerIndex = FindeHeaderZeile(alleZeilen, erwarteteHeader);
                if (headerIndex < 0)
                    throw new Exception("Keine gültige Header-Zeile in der Excel-Datei gefunden.");

                var table = new DataTable();
                var verwendeteSpaltennamen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var headerCells = alleZeilen[headerIndex];
                for (int i = 0; i < headerCells.Length; i++)
                {
                    var colName = (headerCells[i] ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(colName))
                        colName = $"Spalte{i + 1}";

                    colName = MacheEindeutigenSpaltennamen(colName, verwendeteSpaltennamen);
                    table.Columns.Add(colName);
                }

                for (int rowIndex = headerIndex + 1; rowIndex < alleZeilen.Count; rowIndex++)
                {
                    var raw = alleZeilen[rowIndex];

                    if (IstLeerzeile(raw))
                        continue;

                    var row = table.NewRow();
                    for (int col = 0; col < table.Columns.Count; col++)
                    {
                        row[col] = col < raw.Length ? raw[col] : string.Empty;
                    }

                    table.Rows.Add(row);
                }

                return table;
            }

            int FindeHeaderZeile(List<string[]> zeilen, string[] erwarteteHeader)
            {
                // Regel:
                // Erste Zeile verwenden, die mindestens 2 erwartete Header enthält.
                // Falls keine 2 gefunden werden, als Fallback eine Zeile mit mindestens 1 Treffer.
                int fallbackIndex = -1;

                for (int i = 0; i < zeilen.Count; i++)
                {
                    var row = zeilen[i];
                    int treffer = ZaehleHeaderTreffer(row, erwarteteHeader);

                    if (treffer >= 2)
                        return i;

                    if (treffer >= 1 && fallbackIndex < 0)
                        fallbackIndex = i;
                }

                return fallbackIndex;
            }

            int ZaehleHeaderTreffer(string[] row, string[] erwarteteHeader)
            {
                int treffer = 0;

                foreach (var cell in row)
                {
                    var wert = (cell ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(wert))
                        continue;

                    if (erwarteteHeader.Any(h => string.Equals(h, wert, StringComparison.OrdinalIgnoreCase)))
                        treffer++;
                }

                return treffer;
            }

            bool IstLeerzeile(string[] row)
            {
                foreach (var cell in row)
                {
                    if (!string.IsNullOrWhiteSpace(cell))
                        return false;
                }

                return true;
            }

            string MacheEindeutigenSpaltennamen(string name, HashSet<string> bestehend)
            {
                var basis = name;
                var kandidat = basis;
                int nr = 2;

                while (!bestehend.Add(kandidat))
                {
                    kandidat = $"{basis}_{nr}";
                    nr++;
                }

                return kandidat;
            }

            static bool TryGetDate(object? v, out DateTime? d)
            {
                d = null;

                if (v == null)
                    return false;

                if (v is DateTime dt)
                {
                    d = dt;
                    return true;
                }

                if (DateTime.TryParse(v.ToString(), out var dt2))
                {
                    d = dt2;
                    return true;
                }

                return false;
            }

            static bool TryGetDecimal(object? v, CultureInfo ci, out decimal dec)
            {
                dec = 0m;

                if (v == null)
                    return false;

                if (v is double d)
                {
                    dec = (decimal)d;
                    return true;
                }

                if (v is float f)
                {
                    dec = (decimal)f;
                    return true;
                }

                if (v is decimal dd0)
                {
                    dec = dd0;
                    return true;
                }

                var s = v.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(s))
                    return false;

                if (decimal.TryParse(s, NumberStyles.Any, ci, out var dd))
                {
                    dec = dd;
                    return true;
                }

                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out dd))
                {
                    dec = dd;
                    return true;
                }

                return false;
            }
        }

        // ---- Alles-in-einem: Verbuchen (liefert Kennzahlen) ----
        public (int inserted, int skipped, int duplicates) VerbuchenCcStaging(int batchId, int kreditkartenKontoId, int? geldinstitutId)
        {
            EnsureCreditCardImportAdresseColumn();

            int ins = 0, skip = 0, dup = 0;

            using var c = new SqlConnection(_connectionString);
            c.Open();

            // Nur gemappte Zeilen des Batches
            const string sel = @"
            SELECT Id, Datum, Betrag, DebitKredit, Beschreibung, Haendler, Kategorie, Kartennummer, KontoId, ImportHash, AdresseId
            FROM CreditCardImportStaging
            WHERE BatchId=@b AND KontoId IS NOT NULL
            ORDER BY Datum, Id";

            using var cmd = new SqlCommand(sel, c);
            cmd.Parameters.AddWithValue("@b", batchId);

            var rows = new List<(int Id, DateTime Datum, decimal Betrag, string DK, string? Bez, string? H, string? K, string? Card, int KontoId, string? Hash, int? AdresseId)>();
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    rows.Add((
                        r.GetInt32(0),
                        r.GetDateTime(1),
                        r.GetDecimal(2),
                        r.IsDBNull(3) ? "" : r.GetString(3),
                        r.IsDBNull(4) ? null : r.GetString(4),
                        r.IsDBNull(5) ? null : r.GetString(5),
                        r.IsDBNull(6) ? null : r.GetString(6),
                        r.IsDBNull(7) ? null : r.GetString(7),
                        r.GetInt32(8),
                        r.IsDBNull(9) ? null : r.GetString(9),
                        r.IsDBNull(10) ? (int?)null : r.GetInt32(10)
                    ));
                }
            }

            foreach (var r in rows)
            {
                var dk = (r.DK ?? "").Trim();

                bool isBel = IstBelastung(dk);
                bool isGut = IstGutschrift(dk);

                if (!isBel && !isGut) { skip++; continue; }

                var betrag = Math.Abs(r.Betrag);
                var hash = r.Hash ?? BaueImportHashV2(r.Datum, betrag, r.Bez ?? "", r.H, r.Card, r.DK);

                if (SucheTransaktionIdByHash(hash).HasValue) { dup++; continue; }

                // Richtung:
                //  - Belastung:  KK-/Durchlaufkonto  -> Zielkonto (Ausgabe)
                //  - Gutschrift: Zielkonto           -> KK-/Durchlaufkonto (Rückzahlung)
                int? von = isBel ? kreditkartenKontoId : r.KontoId;
                int? nach = isBel ? r.KontoId : kreditkartenKontoId;

                // Normalfall: Adresse wurde beim Staging/Zuweisen bereits über
                // AdressErkennungService/ZuordnungDialog aufgelöst. Fallback nur
                // für Alt-Zeilen aus der Zeit vor diesem Umbau.
                var adrId = r.AdresseId ?? FindeOderErzeugeAdresseByName(r.H);

                // **WICHTIG**: Keine Bank mehr referenzieren -> GeldinstitutId = null
                // Damit ist es fachlich eine reine Konto->Konto-Buchung und taucht NICHT im Banksaldo auf.
                var tid = InsertTransaktionMitImport(
                    r.Datum,
                    von,
                    nach,
                    betrag,
                    r.Bez,
                    adrId,
                    geldinstitutId: null,              // <-- EINZIGE relevante Änderung
                    importQuelle: "KreditkartenExcel",
                    importHash: hash
                );
                ins++;

                // Archiv + Staging löschen
                using (var a = new SqlCommand(@"
                INSERT INTO CreditCardImportArchive
                (BatchId, Datum, Betrag, DebitKredit, Beschreibung, Haendler, Kategorie, Kartennummer, MappingKey, KontoId, ImportHash, TransaktionId)
                SELECT BatchId, Datum, Betrag, DebitKredit, Beschreibung, Haendler, Kategorie, Kartennummer, MappingKey, KontoId, ImportHash, @tid
                FROM CreditCardImportStaging WHERE Id=@id;

                DELETE FROM CreditCardImportStaging WHERE Id=@id;", c))
                {
                    a.Parameters.AddWithValue("@id", r.Id);
                    a.Parameters.AddWithValue("@tid", tid);
                    a.ExecuteNonQuery();
                }
            }

            return (ins, skip, dup);
        }



        public string BaueMappingSchluessel(string? beschreibung, string? haendler, string? kategorie)
        {
            // 1) Normalisieren
            string Norm(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "";
                s = s.Trim();

                // Datumsangaben entfernen (z.B. 21.08.2025)
                s = Regex.Replace(s, @"\b\d{1,2}\.\d{1,2}\.\d{2,4}\b", "", RegexOptions.CultureInvariant);

                // lange Ziffernfolgen (Referenznummern) raus
                s = Regex.Replace(s, @"\d{5,}", "", RegexOptions.CultureInvariant);

                // Mehrfach-Leerzeichen zu einem
                s = Regex.Replace(s, @"\s+", " ", RegexOptions.CultureInvariant);

                return s.ToUpperInvariant();
            }

            var h = Norm(haendler);
            var k = Norm(kategorie);
            var b = Norm(beschreibung);

            // 2) Reihenfolge: Händler | Kategorie | Beschreibung (stable first)
            // Leere Teile weglassen
            var parts = new[] { h, k, b }.Where(p => !string.IsNullOrEmpty(p));
            return string.Join("|", parts);
        }

        public int? HoleKontoIdFuerMapping(string schluessel)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();
            const string sql = @"SELECT TOP 1 KontoId FROM KategorieKontoMapping
                         WHERE UPPER(LTRIM(RTRIM(Kategorie))) = UPPER(LTRIM(RTRIM(@k)))";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@k", schluessel);
            var v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? (int?)null : Convert.ToInt32(v);
        }

        public void UpsertKategorieMapping(string schluessel, int kontoId)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string check = @"SELECT Id FROM KategorieKontoMapping
                           WHERE UPPER(LTRIM(RTRIM(Kategorie))) = UPPER(LTRIM(RTRIM(@k)))";
            using var ck = new SqlCommand(check, c);
            ck.Parameters.AddWithValue("@k", schluessel);
            var idObj = ck.ExecuteScalar();

            if (idObj != null && idObj != DBNull.Value)
            {
                const string upd = @"UPDATE KategorieKontoMapping SET KontoId=@ko WHERE Id=@id";
                using var u = new SqlCommand(upd, c);
                u.Parameters.AddWithValue("@ko", kontoId);
                u.Parameters.AddWithValue("@id", (int)idObj);
                u.ExecuteNonQuery();
            }
            else
            {
                const string ins = @"INSERT INTO KategorieKontoMapping (Kategorie, KontoId) VALUES (@k, @ko)";
                using var i = new SqlCommand(ins, c);
                i.Parameters.AddWithValue("@k", schluessel);
                i.Parameters.AddWithValue("@ko", kontoId);
                i.ExecuteNonQuery();
            }
        }



        public decimal HoleSaldoFuerKonto(int kontoId, DateTime? bisDatum = null)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT 
    ISNULL((
        SELECT SUM(Betrag) FROM Transaktion 
        WHERE NachKontoId = @K AND (@bis IS NULL OR Datum <= @bis)
    ), 0)
  - ISNULL((
        SELECT SUM(Betrag) FROM Transaktion 
        WHERE VonKontoId  = @K AND (@bis IS NULL OR Datum <= @bis)
    ), 0) AS Saldo;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@K", kontoId);
            cmd.Parameters.AddWithValue("@bis", (object?)bisDatum ?? DBNull.Value);

            var v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? 0m : Convert.ToDecimal(v);
        }

        // ====== CREDITCARD IMPORT: STAGING + ARCHIVE ======

        public int CreateCcBatch(string? sourceFile, int? kreditkartenKontoId, int? geldinstitutId)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();
            const string sql = @"INSERT INTO CreditCardImportBatch(ImportedAt, SourceFile, KreditkartenKontoId, GeldinstitutId)
                         OUTPUT INSERTED.Id
                         VALUES (SYSUTCDATETIME(), @f, @k, @g)";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@f", (object?)sourceFile ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@k", (object?)kreditkartenKontoId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@g", (object?)geldinstitutId ?? DBNull.Value);
            return (int)cmd.ExecuteScalar();
        }

        public (int inserted, int skipped, int duplicates) SaveExcelRowsToStaging(int batchId, IEnumerable<CreditCardImportRow> rows)
        {
            EnsureCreditCardImportAdresseColumn();

            int ins = 0, skip = 0, dup = 0;
            var matcher = new AdressErkennungService();

            using var c = new SqlConnection(_connectionString);
            c.Open();

            foreach (var r in rows)
            {
                // Nur Zeilen mit bekanntem Debit/Kredit – beides zulassen
                if (!IstBelastung(r.DebitKredit) && !IstGutschrift(r.DebitKredit)) { skip++; continue; }

                // Gleiche Erkennung wie beim CAMT-Import: Adresse fuzzy matchen,
                // dann Konto über Sonderregel oder Adress-Standardkonto.
                bool istEinnahme = IstGutschrift(r.DebitKredit);
                var betragAbs = Math.Abs(r.Betrag);
                var (adrId, kontoId) = ResolveCcZeile(matcher, r.Haendler, r.Beschreibung, r.Kategorie, betragAbs, istEinnahme);

                // MappingKey bleibt als Metadatum erhalten (u.a. für das Archiv), wird aber
                // nicht mehr zur automatischen Kontosuche verwendet.
                var key = BaueMappingSchluessel(r.Beschreibung, r.Haendler, r.Kategorie);

                var hash = BaueImportHashV2(r.Datum, betragAbs, r.Beschreibung, r.Haendler, r.Kartennummer, r.DebitKredit);

                // Dedupe über Staging/Archiv/Transaktion
                if (HashExistsAnywhere(c, hash)) { dup++; continue; }

                const string insSql = @"
INSERT INTO CreditCardImportStaging
(BatchId, Datum, Betrag, DebitKredit, Beschreibung, Haendler, Kategorie, Kartennummer, MappingKey, KontoId, AdresseId, ImportHash)
VALUES (@b, @d, @w, @dk, @bez, @h, @kat, @card, @key, @konto, @adr, @hash)";
                using var cmd = new SqlCommand(insSql, c);
                cmd.Parameters.AddWithValue("@b", batchId);
                cmd.Parameters.AddWithValue("@d", r.Datum.Date);
                cmd.Parameters.AddWithValue("@w", betragAbs);
                cmd.Parameters.AddWithValue("@dk", (object?)r.DebitKredit ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@bez", (object?)r.Beschreibung ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@h", (object?)r.Haendler ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@kat", (object?)r.Kategorie ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@card", (object?)r.Kartennummer ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@key", (object?)key ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@konto", (object?)kontoId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@adr", (object?)adrId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@hash", (object?)hash ?? DBNull.Value);
                cmd.ExecuteNonQuery();
                ins++;
            }

            return (ins, skip, dup);

            bool HashExistsAnywhere(SqlConnection conn, string h)
            {
                const string q = @"
SELECT 1 WHERE EXISTS(SELECT 1 FROM CreditCardImportStaging WHERE ImportHash=@h)
        OR EXISTS(SELECT 1 FROM CreditCardImportArchive WHERE ImportHash=@h)
        OR EXISTS(SELECT 1 FROM Transaktion WHERE ImportHash=@h)";
                using var chk = new SqlCommand(q, conn);
                chk.Parameters.AddWithValue("@h", h);
                var v = chk.ExecuteScalar();
                return v != null && v != DBNull.Value;
            }
        }

        // Erweitert die Kreditkarten-Staging-/Archiv-Tabellen um AdresseId,
        // damit derselbe adressbezogene Erkennungs- und Regel-Workflow wie beim
        // CAMT-Bank-Import genutzt werden kann. Idempotent, wie die übrigen
        // Ensure...Schema()-Methoden.
        public void EnsureCreditCardImportAdresseColumn()
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
IF COL_LENGTH('dbo.CreditCardImportStaging', 'AdresseId') IS NULL
BEGIN
    ALTER TABLE dbo.CreditCardImportStaging ADD AdresseId INT NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_CcStaging_Adresse')
BEGIN
    ALTER TABLE dbo.CreditCardImportStaging
        ADD CONSTRAINT FK_CcStaging_Adresse FOREIGN KEY (AdresseId) REFERENCES dbo.Adresse(Id);
END;

IF COL_LENGTH('dbo.CreditCardImportArchive', 'AdresseId') IS NULL
BEGIN
    ALTER TABLE dbo.CreditCardImportArchive ADD AdresseId INT NULL;
END;
";
            using var cmd = new SqlCommand(sql, c);
            cmd.ExecuteNonQuery();
        }

        // ---------------------------------------------
        // Löst für eine Kreditkarten-Zeile Adresse + Konto genau so auf,
        // wie es der CAMT-Bank-Import tut: fuzzy Adress-Matching über
        // AdressErkennungService, dann Sonderregel (AdressBuchungsregel),
        // sonst Standardkonto der Adresse. Keine automatische Neuanlage
        // einer Adresse - das entscheidet der Nutzer im Zuordnungsdialog,
        // genau wie bei CAMT.
        // ---------------------------------------------
        public (int? adresseId, int? kontoId) ResolveCcZeile(AdressErkennungService matcher, string? haendler, string? beschreibung, string? kategorie, decimal betragAbs, bool istEinnahme)
        {
            var name = !string.IsNullOrWhiteSpace(haendler) ? haendler : beschreibung;
            var adrId = matcher.TryMatch(null, name, beschreibung);

            int? kontoId = null;

            if (adrId.HasValue)
            {
                kontoId = ResolveKontoByAdressBuchungsregel(adrId.Value, istEinnahme, beschreibung, kategorie, betragAbs, out _);

                if (!kontoId.HasValue)
                {
                    var adr = HoleAdresse(adrId.Value);
                    if (istEinnahme && adr?.IstBudgetiert == true && adr.StandardEinnahmenKontoId.HasValue)
                        kontoId = adr.StandardEinnahmenKontoId;
                    else if (!istEinnahme && adr?.DefaultKontoId.HasValue == true)
                        kontoId = adr.DefaultKontoId;
                }
            }

            // Fallback: Kategorie-Standardkonto. Greift unabhängig davon, ob eine
            // Adresse erkannt wurde - Kreditkarten-Kategorien sind fix und decken
            // oft schon den Grossteil der Zeilen ab, auch bei unbekanntem Händler.
            if (!kontoId.HasValue)
                kontoId = HoleKontoIdFuerKategorie(kategorie);

            return (adrId, kontoId);
        }

        public List<CreditCardImportRow> LadeCcStaging(int? batchId = null)
        {
            EnsureCreditCardImportAdresseColumn();

            var list = new List<CreditCardImportRow>();
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT s.Id, s.BatchId, s.Datum, s.Betrag, s.DebitKredit, s.Beschreibung, s.Haendler, s.Kategorie,
       s.Kartennummer, s.KontoId, s.AdresseId
FROM CreditCardImportStaging s
WHERE (@b IS NULL OR s.BatchId=@b)
ORDER BY s.Datum, s.Id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@b", (object?)batchId ?? DBNull.Value);

            // Für Anzeige: Konto-/Adress-Labels vorab laden
            var labels = LadeKontoLookup().ToDictionary(x => x.Id, x => x.Anzeige);
            var adressen = LadeAdressen().ToDictionary(a => a.Id, a => a.Name);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var row = new CreditCardImportRow
                {
                    Id = r.GetInt32(0),
                    BatchId = r.GetInt32(1),
                    Datum = r.GetDateTime(2),
                    Betrag = r.GetDecimal(3),
                    DebitKredit = r.IsDBNull(4) ? "" : r.GetString(4),
                    Beschreibung = r.IsDBNull(5) ? "" : r.GetString(5),
                    Haendler = r.IsDBNull(6) ? null : r.GetString(6),
                    Kategorie = r.IsDBNull(7) ? null : r.GetString(7),
                    Kartennummer = r.IsDBNull(8) ? null : r.GetString(8),
                    KontoId = r.IsDBNull(9) ? (int?)null : r.GetInt32(9),
                    AdresseId = r.IsDBNull(10) ? (int?)null : r.GetInt32(10)
                };
                if (row.KontoId.HasValue && labels.TryGetValue(row.KontoId.Value, out var lbl))
                    row.Konto = lbl;
                if (row.AdresseId.HasValue && adressen.TryGetValue(row.AdresseId.Value, out var an))
                    row.Adresse = an;

                list.Add(row);
            }
            return list;
        }

        public void UpdateCcStagingZuordnung(int rowId, int? adresseId, int kontoId)
        {
            EnsureCreditCardImportAdresseColumn();

            using var c = new SqlConnection(_connectionString);
            c.Open();

            using var u = new SqlCommand("UPDATE CreditCardImportStaging SET KontoId=@k, AdresseId=@a WHERE Id=@id", c);
            u.Parameters.AddWithValue("@k", kontoId);
            u.Parameters.AddWithValue("@a", (object?)adresseId ?? DBNull.Value);
            u.Parameters.AddWithValue("@id", rowId);
            u.ExecuteNonQuery();
        }

        
        // Helper in DatabaseService (gleich wie VM-Variante)
        private static bool IstBelastung(string? dk)
        {
            var s = (dk ?? "").Trim().ToUpperInvariant();
            return s is "BELASTUNG" or "DEBIT" or "SOLL" or "CHARGE" or "AUSGABE" or "DEBITO";
        }

        private static bool IstGutschrift(string? dk)
        {
            var s = (dk ?? "").Trim().ToUpperInvariant();
            // deckt unsere Mapping-Ausgaben und übliche Synonyme ab
            return s is "KREDIT" or "GUTSCHRIFT" or "CREDIT" or "CRDT" or "HABEN";
        }



        public (int deletedStaging, int affectedArchive) DeleteCcBatchAndStaging(int batchId)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            // Zähler vorab
            int stagingCount, archiveCount;
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM CreditCardImportStaging WHERE BatchId=@b", c))
            { cmd.Parameters.AddWithValue("@b", batchId); stagingCount = (int)cmd.ExecuteScalar(); }
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM CreditCardImportArchive WHERE BatchId=@b", c))
            { cmd.Parameters.AddWithValue("@b", batchId); archiveCount = (int)cmd.ExecuteScalar(); }

            using var tx = c.BeginTransaction();
            // Archiv-Referenzen loslösen (BatchId NULL setzen, Transaktionen bleiben unberührt)
            using (var up = new SqlCommand("UPDATE CreditCardImportArchive SET BatchId=NULL WHERE BatchId=@b", c, tx))
            { up.Parameters.AddWithValue("@b", batchId); up.ExecuteNonQuery(); }

            // Batch löschen -> Staging wird via ON DELETE CASCADE mit gelöscht
            using (var del = new SqlCommand("DELETE FROM CreditCardImportBatch WHERE Id=@b", c, tx))
            { del.Parameters.AddWithValue("@b", batchId); del.ExecuteNonQuery(); }

            tx.Commit();
            return (stagingCount, archiveCount);
        }


        public void DeleteCcStagingRows(IEnumerable<int> ids)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();
            foreach (var id in ids)
            {
                using var cmd = new SqlCommand("DELETE FROM CreditCardImportStaging WHERE Id=@id", c);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public List<CreditCardBatchInfo> LadeCcBatches(bool nurMitOffenen = true)
        {
            var list = new List<CreditCardBatchInfo>();
            using var c = new SqlConnection(_connectionString);
            c.Open();

            string sql = @"
SELECT b.Id, b.ImportedAt, b.SourceFile,
       ISNULL(s.Offen,0) AS Offen,
       ISNULL(a.Archiviert,0) AS Archiviert
FROM CreditCardImportBatch b
OUTER APPLY (SELECT COUNT(*) AS Offen FROM CreditCardImportStaging s WHERE s.BatchId = b.Id) s
OUTER APPLY (SELECT COUNT(*) AS Archiviert FROM CreditCardImportArchive ar WHERE ar.BatchId = b.Id) a
";

            if (nurMitOffenen) sql += "WHERE ISNULL(s.Offen,0) > 0\n";
            sql += "ORDER BY b.ImportedAt DESC;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new CreditCardBatchInfo
                {
                    Id = r.GetInt32(0),
                    ImportedAt = r.GetDateTime(1),
                    SourceFile = r.IsDBNull(2) ? null : r.GetString(2),
                    Offen = r.GetInt32(3),
                    Archiviert = r.GetInt32(4)
                });
            }
            return list;
        }

        #region Kreditkarten-Header-Mapping (Schemas & FieldMappings)

        // Lädt alle Schemas (Master zuerst)
        public IList<ImportSchema> ImportSchemasGetAll()
        {
            var result = new List<ImportSchema>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
            SELECT Id, Name, IsMaster
            FROM dbo.ImportSchema
            ORDER BY IsMaster DESC, Name ASC;";

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                result.Add(new ImportSchema
                {
                    Id = r.GetInt32(0),
                    Name = r.GetString(1),
                    IsMaster = r.GetBoolean(2)
                });
            }
            return result;
        }

        // Fügt ein Schema ein und gibt es mit Id zurück
        public ImportSchema ImportSchemaInsert(ImportSchema s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (string.IsNullOrWhiteSpace(s.Name))
                throw new ArgumentException("Name darf nicht leer sein.", nameof(s));

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
            INSERT INTO dbo.ImportSchema (Name, IsMaster)
            OUTPUT INSERTED.Id
            VALUES (@name, @isMaster);";

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@name", s.Name.Trim());
            cmd.Parameters.AddWithValue("@isMaster", s.IsMaster);
            s.Id = (int)cmd.ExecuteScalar();
            return s;
        }

        // Löscht ein Schema (Mappings werden dank FK CASCADE mit gelöscht)
        public void ImportSchemaDelete(int schemaId)
        {
            if (schemaId <= 0) throw new ArgumentOutOfRangeException(nameof(schemaId));

            using var c = CreateConnection();
            c.Open();

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "DELETE FROM dbo.ImportSchema WHERE Id=@id;", c);
            cmd.Parameters.AddWithValue("@id", schemaId);
            cmd.ExecuteNonQuery();
        }

        public void ImportSchemaUpdateName(int schemaId, string name)
        {
            if (schemaId <= 0) throw new ArgumentOutOfRangeException(nameof(schemaId));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name darf nicht leer sein.", nameof(name));

            using var c = CreateConnection();
            c.Open();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "UPDATE dbo.ImportSchema SET Name=@name WHERE Id=@id;", c);
            cmd.Parameters.AddWithValue("@id", schemaId);
            cmd.Parameters.AddWithValue("@name", name.Trim());
            cmd.ExecuteNonQuery();
        }

        // Lädt alle FieldMappings zu einem Schema
        public IList<FieldMapping> FieldMappingsGetBySchema(int schemaId)
        {
            if (schemaId <= 0) throw new ArgumentOutOfRangeException(nameof(schemaId));

            var list = new List<FieldMapping>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
                SELECT Id, SchemaId, MasterHeader, SourceHeader, DefaultValue
                FROM dbo.ImportFieldMapping
                WHERE SchemaId=@id
                ORDER BY MasterHeader;";

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", schemaId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new FieldMapping
                {
                    Id = r.GetInt32(0),
                    SchemaId = r.GetInt32(1),
                    MasterHeader = r.GetString(2),
                    SourceHeader = r.GetString(3),
                    // <- HIER: DefaultValue (Spalte 4) sauber auslesen, auch wenn NULL
                    DefaultValue = r.IsDBNull(4) ? null : r.GetString(4)
                });
            }
            return list;
        }


        // Ersetzt die Mappings eines Schemas vollständig (TRANSACTION)
        public void FieldMappingsReplace(int schemaId, List<FieldMapping> items)
        {
            if (schemaId <= 0) throw new ArgumentOutOfRangeException(nameof(schemaId));
            if (items == null) throw new ArgumentNullException(nameof(items));

            // 0) Vorab: Normalisieren, leere/böse Header filtern, Duplikate (pro SourceHeader) entfernen
            static string Norm(string s)
                => new string((s ?? "").Trim().ToLowerInvariant()
                    .Normalize(System.Text.NormalizationForm.FormD)
                    .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    .ToArray())
                   .Normalize(System.Text.NormalizationForm.FormC);

            bool IsBanned(string? h)
                => string.IsNullOrWhiteSpace(h) || Norm(h!).StartsWith("abgeschlossene zahlungen");

            var unique = new Dictionary<string, FieldMapping>();   // key = normalized SourceHeader
            var seenSource = new HashSet<string>();                // für UX_ImportFieldMapping_Schema_Source

            foreach (var m in items)
            {
                var master = (m?.MasterHeader ?? "").Trim();
                var source = (m?.SourceHeader ?? "").Trim();
                var defVal = string.IsNullOrWhiteSpace(m?.DefaultValue) ? null : m!.DefaultValue!.Trim();

                if (string.IsNullOrWhiteSpace(master) || string.IsNullOrWhiteSpace(source))
                    continue;
                if (IsBanned(source)) // "Abgeschlossene Zahlungen" u. ä. ignorieren
                    continue;

                var keySource = Norm(source);
                if (seenSource.Contains(keySource))
                    continue; // gleiche SourceHeader mehrfach: nur den ersten behalten
                seenSource.Add(keySource);

                unique[keySource] = new FieldMapping
                {
                    SchemaId = schemaId,
                    MasterHeader = master,
                    SourceHeader = source,
                    DefaultValue = defVal
                };
            }

            using var c = CreateConnection();
            c.Open();
            using var tx = c.BeginTransaction();

            // 1) löschen
            using (var del = new Microsoft.Data.SqlClient.SqlCommand(
                "DELETE FROM dbo.ImportFieldMapping WHERE SchemaId=@id;", c, tx))
            {
                del.Parameters.AddWithValue("@id", schemaId);
                del.ExecuteNonQuery();
            }

            // 2) einfügen (nur bereinigte, eindeutige Paare)
            foreach (var p in unique.Values)
            {
                using var ins = new Microsoft.Data.SqlClient.SqlCommand(@"
INSERT INTO dbo.ImportFieldMapping (SchemaId, MasterHeader, SourceHeader, DefaultValue)
VALUES (@sid, @master, @source, @def);", c, tx);

                ins.Parameters.AddWithValue("@sid", p.SchemaId);
                ins.Parameters.AddWithValue("@master", p.MasterHeader);
                ins.Parameters.AddWithValue("@source", p.SourceHeader);
                ins.Parameters.AddWithValue("@def", (object?)p.DefaultValue ?? DBNull.Value);

                ins.ExecuteNonQuery();
            }

            tx.Commit();
        }


        // Löscht alle Mappings eines Schemas (ohne Schema zu löschen)
        public void FieldMappingsDeleteBySchema(int schemaId)
        {
            if (schemaId <= 0) throw new ArgumentOutOfRangeException(nameof(schemaId));

            using var c = CreateConnection();
            c.Open();

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "DELETE FROM dbo.ImportFieldMapping WHERE SchemaId=@id;", c);
            cmd.Parameters.AddWithValue("@id", schemaId);
            cmd.ExecuteNonQuery();
        }

        #endregion



        // =================== END KREDITKARTEN ===================

        // Transaktionen für ein bestimmtes Konto (sowohl als Von, als Nach)
        public List<Transaktion> LadeTransaktionenByKonto(
            int kontoId,
            DateTime? von = null,
            DateTime? bis = null,
            decimal? minBetrag = null,
            decimal? maxBetrag = null,
            int? adresseId = null,
            int? geldinstitutId = null)
        {
            var list = new List<Transaktion>();

            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT t.Id,
       t.Datum,
       t.BudgetDatum,
       t.VonKontoId,
       t.NachKontoId,
       t.Betrag,
       t.Notiz,
       t.AdresseId,
       a.Name as AdresseName,
       t.GeldinstitutId,
       g.Name as BankName,
       t.ImportQuelle
FROM Transaktion t
LEFT JOIN Adresse a      ON a.Id = t.AdresseId
LEFT JOIN Geldinstitut g ON g.Id = t.GeldinstitutId
WHERE (t.VonKontoId = @kto OR t.NachKontoId = @kto)
  AND (@von IS NULL OR ISNULL(t.BudgetDatum, t.Datum) >= @von)
  AND (@bis IS NULL OR ISNULL(t.BudgetDatum, t.Datum) <= @bis)
  AND (@minB IS NULL OR t.Betrag >= @minB)
  AND (@maxB IS NULL OR t.Betrag <= @maxB)
  AND (@adr IS NULL OR t.AdresseId = @adr)
  AND (@gi  IS NULL OR t.GeldinstitutId = @gi)
ORDER BY t.Datum DESC, t.Id DESC;";

            using var cmd = new SqlCommand(sql, c);

            cmd.Parameters.AddWithValue("@kto", kontoId);
            cmd.Parameters.AddWithValue("@von", (object?)von?.Date ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bis", (object?)bis?.Date ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@minB", (object?)minBetrag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@maxB", (object?)maxBetrag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@adr", (object?)adresseId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gi", (object?)geldinstitutId ?? DBNull.Value);

            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new Transaktion
                {
                    Id = r.GetInt32(0),

                    Datum = r.GetDateTime(1),

                    BudgetDatum = r.IsDBNull(2)
                        ? (DateTime?)null
                        : r.GetDateTime(2),

                    VonKontoId = r.IsDBNull(3)
                        ? (int?)null
                        : r.GetInt32(3),

                    NachKontoId = r.IsDBNull(4)
                        ? (int?)null
                        : r.GetInt32(4),

                    Betrag = r.GetDecimal(5),

                    Notiz = r.IsDBNull(6)
                        ? null
                        : r.GetString(6),

                    AdresseId = r.IsDBNull(7)
                        ? (int?)null
                        : r.GetInt32(7),

                    AdresseName = r.IsDBNull(8)
                        ? null
                        : r.GetString(8),

                    GeldinstitutId = r.IsDBNull(9)
                        ? (int?)null
                        : r.GetInt32(9),

                    BankName = r.IsDBNull(10)
                        ? null
                        : r.GetString(10),

                    ImportQuelle = r.IsDBNull(11)
                        ? null
                        : r.GetString(11)
                });
            }

            return list;
        }


        // Budgetsumme für ein Konto im Zeitraum (defensiv: 0 wenn Tabelle/Spalten fehlen)
        public decimal LadeBudgetSummeForKonto(int kontoId, DateTime? von, DateTime? bis)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            // Fall A: Kein Datumsfilter -> aktiven Zeitraum verwenden
            if (von == null && bis == null)
            {
                const string sqlAktiv = @"
SELECT ISNULL(SUM(bd.Budgetwert), 0)
FROM Budgetzeitraum bz
JOIN BudgetDetail   bd ON bd.ZeitraumId = bz.Id
WHERE bz.IstAktiv = 1
  AND bd.KontoId   = @K;";

                using var cmdA = new SqlCommand(sqlAktiv, c);
                cmdA.Parameters.AddWithValue("@K", kontoId);
                var v = cmdA.ExecuteScalar();
                return v == null || v == DBNull.Value ? 0m : Convert.ToDecimal(v);
            }

            // Fall B: Datumsfilter vorhanden -> alle überlappenden Zeiträume berücksichtigen
            // Overlap-Bedingung: (bz.Enddatum >= @Von) AND (bz.Startdatum <= @Bis)
            // Wenn nur eines von beiden gesetzt ist, jeweils die andere Seite offen lassen.
            const string sqlOverlap = @"
SELECT ISNULL(SUM(bd.Budgetwert), 0)
FROM Budgetzeitraum bz
JOIN BudgetDetail   bd ON bd.ZeitraumId = bz.Id
WHERE bd.KontoId = @K
  AND (@Von IS NULL OR bz.Enddatum   >= @Von)
  AND (@Bis IS NULL OR bz.Startdatum <= @Bis);";

            using var cmdB = new SqlCommand(sqlOverlap, c);
            cmdB.Parameters.AddWithValue("@K", kontoId);
            cmdB.Parameters.AddWithValue("@Von", (object?)von?.Date ?? DBNull.Value);
            cmdB.Parameters.AddWithValue("@Bis", (object?)bis?.Date ?? DBNull.Value);

            var val = cmdB.ExecuteScalar();
            return val == null || val == DBNull.Value ? 0m : Convert.ToDecimal(val);
        }

        public bool IstEinnahmenKonto(int kontoId)
        {
            // 1) Regel per Nummernblock prüfen
            int? knr = HoleKontonummerByKontoId(kontoId);
            if (knr.HasValue)
            {
                var regel = FindeRegelFuerKontonummer(knr.Value);
                if (regel != null)
                    return string.Equals(regel.Richtung, "Einnahme", StringComparison.OrdinalIgnoreCase);
            }

            // 2) Fallback: Text-Heuristik
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();
            const string sql = @"
SELECT UPPER(ISNULL(Art,'')) AS Art,
       UPPER(ISNULL(Gruppe,'')) AS Gruppe,
       UPPER(ISNULL(Untergruppe,'')) AS Untergruppe,
       UPPER(ISNULL(Detail,'')) AS Detail
FROM Kontenplan
WHERE Id = @Id";
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", kontoId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return false;

            string text = $"{r["Art"]} {r["Gruppe"]} {r["Untergruppe"]} {r["Detail"]}";
            string[] incomeKeys = { "EINNAHM", "ERTRAG", "ERTRAEG", "ERLÖS", "ERLOS", "ERLOES", "REVENUE", "INCOME" };
            foreach (var k in incomeKeys)
                if (text.Contains(k, StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }

        /// <summary>
        /// Liefert true, wenn die Transaktion für das angefragte Konto als "Ausgabe" angezeigt werden soll.
        /// Standard: Von=Konto -> Ausgabe, Nach=Konto -> Einnahme.
        /// Spezialfall: KreditkartenExcel (Detailverteilung) -> auf Nicht-Einnahmenkonten immer Ausgabe.
        /// </summary>
        public bool IstAusgabeFuerKonto(int kontoId, Transaktion t)
        {
            // KK-Detailbuchung? Dann auf Kosten-/Budgetkonten immer als Ausgabe zeigen.
            if (string.Equals(t.ImportQuelle, "KreditkartenExcel", StringComparison.OrdinalIgnoreCase))
            {
                if (!IstEinnahmenKonto(kontoId)) return true;  // Budget-/Kostenkonten: Ausgabe
                                                               // Einnahmenkonten: normale Logik
            }

            // Standarddarstellung:
            if (t.VonKontoId == kontoId) return true;   // Abgang = Ausgabe
            if (t.NachKontoId == kontoId) return false; // Zugang = Einnahme

            // Fallback: Wenn das Konto "Einnahmenkonto" ist -> Einnahme, sonst Ausgabe
            return !IstEinnahmenKonto(kontoId);
        }










        public void EnsureNumberRangeRulesTable()
        {
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();

            const string createSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[NumberRangeRules]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[NumberRangeRules](
        [Id]             INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [RangeStart]     INT NOT NULL,
        [RangeEnd]       INT NOT NULL,
        [Richtung]       NVARCHAR(12) NOT NULL,
        [Bezeichnung]    NVARCHAR(64) NULL,
        [IstBudgetkonto] BIT NOT NULL CONSTRAINT DF_NumberRangeRules_IstBudgetkonto DEFAULT(0),
        [ExcludeFromStweSets] BIT NOT NULL CONSTRAINT DF_NumberRangeRules_ExcludeFromStweSets DEFAULT(0),
        CONSTRAINT CK_NumberRangeRules_Range CHECK ([RangeStart] <= [RangeEnd])
    );
END";
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(createSql, c))
                cmd.ExecuteNonQuery();

            bool hasBezeichnung;
            using (var chk = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT CASE WHEN COL_LENGTH('dbo.NumberRangeRules','Bezeichnung') IS NULL THEN 0 ELSE 1 END", c))
            {
                var v = chk.ExecuteScalar();
                hasBezeichnung = v != null && v != DBNull.Value && Convert.ToInt32(v) == 1;
            }

            if (!hasBezeichnung)
            {
                using (var alter = new Microsoft.Data.SqlClient.SqlCommand(
                    "ALTER TABLE dbo.NumberRangeRules ADD [Bezeichnung] NVARCHAR(64) NULL;", c))
                {
                    alter.ExecuteNonQuery();
                }

                using (var fill = new Microsoft.Data.SqlClient.SqlCommand(@"
UPDATE dbo.NumberRangeRules
SET Bezeichnung = CASE WHEN Richtung = N'Einnahme' 
                       THEN N'Einnahmen (Budgetiert)' 
                       ELSE N'Ausgaben (Budgetiert)' END
WHERE Bezeichnung IS NULL;", c))
                {
                    fill.ExecuteNonQuery();
                }
            }

            bool hasExcludeFromStweSets;
            using (var chk = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT CASE WHEN COL_LENGTH('dbo.NumberRangeRules','ExcludeFromStweSets') IS NULL THEN 0 ELSE 1 END", c))
            {
                var v = chk.ExecuteScalar();
                hasExcludeFromStweSets = v != null && v != DBNull.Value && Convert.ToInt32(v) == 1;
            }

            if (!hasExcludeFromStweSets)
            {
                using var alter = new Microsoft.Data.SqlClient.SqlCommand(@"
ALTER TABLE dbo.NumberRangeRules
ADD [ExcludeFromStweSets] BIT NOT NULL 
    CONSTRAINT DF_NumberRangeRules_ExcludeFromStweSets DEFAULT(0);", c);

                alter.ExecuteNonQuery();
            }

            EnsureNumberRangeRulesAllowNeutral(c);
        }

        /// <summary>
        /// Sucht vorhandene CHECK-Constraints auf dbo.NumberRangeRules.Richtung,
        /// droppt sie und legt einen sauberen Constraint mit 'Neutral' neu an (idempotent).
        /// </summary>
        private static void EnsureNumberRangeRulesAllowNeutral(Microsoft.Data.SqlClient.SqlConnection c)
        {
            // alle CHECK-Constraints der Tabelle ermitteln
            var names = new List<string>();
            using (var q = new Microsoft.Data.SqlClient.SqlCommand(@"
SELECT cc.name
FROM sys.check_constraints cc
JOIN sys.tables t ON cc.parent_object_id = t.object_id
WHERE t.name = N'NumberRangeRules' AND OBJECT_DEFINITION(cc.object_id) LIKE N'%Richtung%';", c))
            {
                using var r = q.ExecuteReader();
                while (r.Read()) names.Add(r.GetString(0));
            }

            // vorhandene Richtungs-Checks entfernen (Name ist systemgeneriert, daher dynamisch droppen)
            foreach (var n in names)
            {
                using var drop = new Microsoft.Data.SqlClient.SqlCommand(
                    $"ALTER TABLE dbo.NumberRangeRules DROP CONSTRAINT [{n}];", c);
                drop.ExecuteNonQuery();
            }

            // neuen, klar benannten Constraint mit Neutral anlegen
            using (var add = new Microsoft.Data.SqlClient.SqlCommand(@"
ALTER TABLE dbo.NumberRangeRules WITH CHECK
ADD CONSTRAINT CK_NumberRangeRules_Richtung_Allowed
CHECK ([Richtung] IN (N'Ausgabe', N'Einnahme', N'Neutral'));
ALTER TABLE dbo.NumberRangeRules CHECK CONSTRAINT CK_NumberRangeRules_Richtung_Allowed;", c))
            {
                add.ExecuteNonQuery();
            }
        }

        public System.Collections.Generic.List<NumberRangeRule> LadeNummernRegeln()
        {
            EnsureNumberRangeRulesTable();

            var list = new System.Collections.Generic.List<NumberRangeRule>();

            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT 
    Id, 
    RangeStart, 
    RangeEnd, 
    Richtung, 
    Bezeichnung, 
    IstBudgetkonto,
    ExcludeFromStweSets
FROM dbo.NumberRangeRules
ORDER BY RangeStart, RangeEnd;";

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                list.Add(new NumberRangeRule
                {
                    Id = r.GetInt32(0),
                    RangeStart = r.GetInt32(1),
                    RangeEnd = r.GetInt32(2),
                    Richtung = r.GetString(3),
                    Bezeichnung = r.IsDBNull(4) ? null : r.GetString(4),
                    IstBudgetkonto = r.GetBoolean(5),
                    ExcludeFromStweSets = r.GetBoolean(6)
                });
            }

            return list;
        }

        public int SpeichereNummernRegel(NumberRangeRule rule)
        {
            EnsureNumberRangeRulesTable();

            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
INSERT INTO dbo.NumberRangeRules 
(
    RangeStart, 
    RangeEnd, 
    Richtung, 
    Bezeichnung, 
    IstBudgetkonto,
    ExcludeFromStweSets
)
OUTPUT INSERTED.Id
VALUES 
(
    @s, 
    @e, 
    @r, 
    @b, 
    @flag,
    @excludeFromStweSets
);";

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@s", rule.RangeStart);
            cmd.Parameters.AddWithValue("@e", rule.RangeEnd);
            cmd.Parameters.AddWithValue("@r", rule.Richtung);
            cmd.Parameters.AddWithValue("@b", (object?)rule.Bezeichnung ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@flag", rule.IstBudgetkonto);
            cmd.Parameters.AddWithValue("@excludeFromStweSets", rule.ExcludeFromStweSets);

            return (int)cmd.ExecuteScalar();
        }

        public void AktualisiereNummernRegel(NumberRangeRule rule)
        {
            EnsureNumberRangeRulesTable();

            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
UPDATE dbo.NumberRangeRules
SET 
    RangeStart = @s, 
    RangeEnd = @e, 
    Richtung = @r, 
    Bezeichnung = @b, 
    IstBudgetkonto = @flag,
    ExcludeFromStweSets = @excludeFromStweSets
WHERE Id = @id;";

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@s", rule.RangeStart);
            cmd.Parameters.AddWithValue("@e", rule.RangeEnd);
            cmd.Parameters.AddWithValue("@r", rule.Richtung);
            cmd.Parameters.AddWithValue("@b", (object?)rule.Bezeichnung ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@flag", rule.IstBudgetkonto);
            cmd.Parameters.AddWithValue("@excludeFromStweSets", rule.ExcludeFromStweSets);
            cmd.Parameters.AddWithValue("@id", rule.Id);

            cmd.ExecuteNonQuery();
        }

        public void LoescheNummernRegel(int id)
        {
            EnsureNumberRangeRulesTable();
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();
            const string sql = @"DELETE FROM dbo.NumberRangeRules WHERE Id=@id";
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }


        public int? HoleKontonummerByKontoId(int kontoId)
        {
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();
            const string sql = @"SELECT Kontonummer FROM dbo.Kontenplan WHERE Id=@id";
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", kontoId);
            var v = cmd.ExecuteScalar();
            if (v == null || v == DBNull.Value) return null;
            return Convert.ToInt32(v);
        }

        public NumberRangeRule? FindeRegelFuerKontonummer(int kontonummer)
        {
            EnsureNumberRangeRulesTable();
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();
            const string sql = @"
SELECT TOP 1 Id, RangeStart, RangeEnd, Richtung, Bezeichnung, IstBudgetkonto
FROM dbo.NumberRangeRules
WHERE @nr BETWEEN RangeStart AND RangeEnd
ORDER BY (RangeEnd - RangeStart) ASC, RangeStart ASC";
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@nr", kontonummer);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new NumberRangeRule
            {
                Id = r.GetInt32(0),
                RangeStart = r.GetInt32(1),
                RangeEnd = r.GetInt32(2),
                Richtung = r.GetString(3),
                Bezeichnung = r.IsDBNull(4) ? null : r.GetString(4),
                IstBudgetkonto = r.GetBoolean(5)
            };
        }

        public bool NumberRangeRulesHasBezeichnung()
        {
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();
            const string sql = "SELECT CASE WHEN COL_LENGTH('dbo.NumberRangeRules','Bezeichnung') IS NULL THEN 0 ELSE 1 END";
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            var v = cmd.ExecuteScalar();
            return v != null && v != DBNull.Value && Convert.ToInt32(v) == 1;
        }

        private bool NumberRangeRulesHasExcludeFromStweSets()
        {
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT CASE WHEN COL_LENGTH('dbo.NumberRangeRules','ExcludeFromStweSets') IS NULL THEN 0 ELSE 1 END", c);

            var v = cmd.ExecuteScalar();
            return v != null && v != DBNull.Value && Convert.ToInt32(v) == 1;
        }


        public void AssertNumberRangeRulesSchema()
        {
            EnsureNumberRangeRulesTable();

            if (!NumberRangeRulesHasBezeichnung())
                throw new Exception("Spalte 'Bezeichnung' fehlt weiterhin in dbo.NumberRangeRules. Prüfe Datenbank/Connection und Rechte.");

            if (!NumberRangeRulesHasExcludeFromStweSets())
                throw new Exception("Spalte 'ExcludeFromStweSets' fehlt weiterhin in dbo.NumberRangeRules. Prüfe Datenbank/Connection und Rechte.");
        }

        // Löscht alle Aliase, die auf eine Adresse zeigen.
        // Rückgabewert: Anzahl gelöschter Zeilen.
        private int LoescheAdressAliase(int adresseId)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();
            using var cmd = new SqlCommand("DELETE FROM dbo.AdresseAlias WHERE AdresseId = @Id", c);
            cmd.Parameters.AddWithValue("@Id", adresseId);
            return cmd.ExecuteNonQuery();
        }


        // ---------------------------------------------
        // NEU: Transaktionen nach Adresse laden (mit Filtern)
        // ---------------------------------------------

        public List<Transaktion> LadeTransaktionenByAdresse(
            int adresseId,
            DateTime? von = null,
            DateTime? bis = null,
            decimal? minBetrag = null,
            decimal? maxBetrag = null,
            int? kontoId = null,
            int? geldinstitutId = null)
        {
            var result = new List<Transaktion>();

            if (adresseId <= 0)
                return result;

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            const string sql = @"
SELECT 
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
FROM Transaktion t
LEFT JOIN Adresse a      ON a.Id = t.AdresseId
LEFT JOIN Geldinstitut g ON g.Id = t.GeldinstitutId
WHERE t.AdresseId = @adr
  AND (@von IS NULL OR ISNULL(t.BudgetDatum, t.Datum) >= @von)
  AND (@bis IS NULL OR ISNULL(t.BudgetDatum, t.Datum) <= @bis)
  AND (@minB IS NULL OR t.Betrag >= @minB)
  AND (@maxB IS NULL OR t.Betrag <= @maxB)
  AND (@kto IS NULL OR t.VonKontoId = @kto OR t.NachKontoId = @kto)
  AND (@gi  IS NULL OR t.GeldinstitutId = @gi)
ORDER BY t.Datum DESC, t.Id DESC;";

            using var cmd = new SqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@adr", adresseId);
            cmd.Parameters.AddWithValue("@von", (object?)von?.Date ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bis", (object?)bis?.Date ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@minB", (object?)minBetrag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@maxB", (object?)maxBetrag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@kto", (object?)kontoId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gi", (object?)geldinstitutId ?? DBNull.Value);

            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                var t = new Transaktion
                {
                    Id = r.GetInt32(0),

                    Datum = r.GetDateTime(1),

                    BudgetDatum = r.IsDBNull(2)
                        ? (DateTime?)null
                        : r.GetDateTime(2),

                    VonKontoId = r.IsDBNull(3)
                        ? (int?)null
                        : r.GetInt32(3),

                    NachKontoId = r.IsDBNull(4)
                        ? (int?)null
                        : r.GetInt32(4),

                    Betrag = r.GetDecimal(5),

                    Notiz = r.IsDBNull(6)
                        ? null
                        : r.GetString(6),

                    AdresseId = r.IsDBNull(7)
                        ? (int?)null
                        : r.GetInt32(7),

                    AdresseName = r.IsDBNull(8)
                        ? null
                        : r.GetString(8),

                    GeldinstitutId = r.IsDBNull(9)
                        ? (int?)null
                        : r.GetInt32(9),

                    BankName = r.IsDBNull(10)
                        ? null
                        : r.GetString(10),

                    ImportQuelle = r.IsDBNull(11)
                        ? null
                        : r.GetString(11)
                };

                result.Add(t);
            }

            return result;
        }


        // Sorgt dafür, dass es genau EIN Master-Schema gibt (Name = "Master").
        // Legt es nur an, wenn es fehlt. Mehrfach-Aufruf ist unkritisch.
        public int EnsureImportMasterSchemaExists()
        {
            const string selectSql = @"SELECT Id FROM ImportSchema WHERE Name = @name";
            const string insertSql = @"INSERT INTO ImportSchema (Name, IsMaster)
                               VALUES (@name, 1);
                               SELECT CAST(SCOPE_IDENTITY() AS INT);";
            try
            {
                using var conn = CreateConnection(); // wie in deinen anderen DB-Methoden
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = selectSql;
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@name";
                    p.Value = "Master";
                    cmd.Parameters.Add(p);

                    var idObj = cmd.ExecuteScalar();
                    if (idObj != null && idObj != DBNull.Value)
                        return Convert.ToInt32(idObj);
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = insertSql;
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@name";
                    p.Value = "Master";
                    cmd.Parameters.Add(p);
                    var newIdObj = cmd.ExecuteScalar();
                    return Convert.ToInt32(newIdObj);
                }
            }
            catch
            {
                // Falls parallel bereits angelegt: erneut abfragen (idempotent)
                using var conn2 = CreateConnection();
                conn2.Open();
                using var cmd2 = conn2.CreateCommand();
                cmd2.CommandText = selectSql;
                var p2 = cmd2.CreateParameter();
                p2.ParameterName = "@name";
                p2.Value = "Master";
                cmd2.Parameters.Add(p2);
                var idObj2 = cmd2.ExecuteScalar();
                if (idObj2 != null && idObj2 != DBNull.Value)
                    return Convert.ToInt32(idObj2);

                throw;
            }
        }

        public List<Transaktion> SucheTransaktionen(string? term, DateTime? vonDatum, DateTime? bisDatum, string? addressTerm)
        {
            var rawTokens = (term ?? string.Empty)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToArray();

            var numTokens = new List<int>();
            var txtTokens = new List<string>();
            foreach (var tok in rawTokens)
            {
                if (int.TryParse(tok, out var n)) numTokens.Add(n);
                else txtTokens.Add(tok);
            }

            using var c = CreateConnection();
            c.Open();

            var sb = new System.Text.StringBuilder(@"
SELECT DISTINCT
       t.Id, t.Datum, t.BudgetDatum, t.VonKontoId, t.NachKontoId,
       t.Betrag, t.Notiz,
       t.AdresseId, a.Name AS AdresseName,
       t.GeldinstitutId, g.Name AS BankName,
       t.ImportQuelle
FROM dbo.Transaktion t
LEFT JOIN dbo.Adresse        a  ON t.AdresseId      = a.Id
LEFT JOIN dbo.Geldinstitut   g  ON t.GeldinstitutId = g.Id
LEFT JOIN dbo.Attachment     att ON att.TransaktionId = t.Id
LEFT JOIN dbo.AttachmentText at  ON at.AttachmentId  = att.Id
/* NEU: Kontenplan für Von/Nach – damit Kontonummern gefiltert werden können */
LEFT JOIN dbo.Kontenplan kv ON kv.Id = t.VonKontoId
LEFT JOIN dbo.Kontenplan kn ON kn.Id = t.NachKontoId
WHERE 1=1
");

            if (vonDatum.HasValue) sb.AppendLine("  AND ISNULL(t.BudgetDatum, t.Datum) >= @von"); // NEU
            if (bisDatum.HasValue) sb.AppendLine("  AND ISNULL(t.BudgetDatum, t.Datum) <= @bis"); // NEU
            if (!string.IsNullOrWhiteSpace(addressTerm))
                sb.AppendLine("  AND a.Name LIKE @addr COLLATE Latin1_General_CI_AI");

            for (int i = 0; i < txtTokens.Count; i++)
            {
                sb.Append(@"
  AND (
       t.Notiz      LIKE @q" + i + @" COLLATE Latin1_General_CI_AI
    OR a.Name       LIKE @q" + i + @" COLLATE Latin1_General_CI_AI
    OR g.Name       LIKE @q" + i + @" COLLATE Latin1_General_CI_AI
    OR att.FileName LIKE @q" + i + @" COLLATE Latin1_General_CI_AI
    OR at.[Text]    LIKE @q" + i + @" COLLATE Latin1_General_CI_AI
  )");
            }

            for (int j = 0; j < numTokens.Count; j++)
            {
                sb.Append(@"
  AND (
       kv.Kontonummer = @n" + j + @" 
    OR kn.Kontonummer = @n" + j + @"
  )");
            }

            sb.AppendLine("\nORDER BY t.Datum DESC;");

            using var cmd = new SqlCommand(sb.ToString(), c);
            if (vonDatum.HasValue) cmd.Parameters.AddWithValue("@von", vonDatum.Value.Date);
            if (bisDatum.HasValue) cmd.Parameters.AddWithValue("@bis", bisDatum.Value.Date);
            if (!string.IsNullOrWhiteSpace(addressTerm)) cmd.Parameters.AddWithValue("@addr", "%" + addressTerm.Trim() + "%");

            for (int i = 0; i < txtTokens.Count; i++)
                cmd.Parameters.AddWithValue("@q" + i, "%" + txtTokens[i] + "%");
            for (int j = 0; j < numTokens.Count; j++)
                cmd.Parameters.AddWithValue("@n" + j, numTokens[j]);

            var list = new List<Transaktion>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Transaktion
                {
                    Id = r.GetInt32(0),
                    Datum = r.GetDateTime(1),

                    BudgetDatum = r.IsDBNull(2) ? (DateTime?)null : r.GetDateTime(2), // NEU

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

        /// <summary>
        /// Liefert (TotalAttachments, FileNameHits, OcrTextHits) für eine Transaktion,
        /// wobei ein Attachment als Treffer zählt, wenn es bei mind. EINEM Token matched.
        /// </summary>
        public (int total, int fileHits, int textHits)
            GetAttachmentHitCountsForTokens(int transaktionId, IEnumerable<string> tokens)
        {
            var toks = (tokens ?? Array.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            using var c = CreateConnection();
            c.Open();

            // Dynamisch OR-Bedingungen je Token aufbauen
            var sb = new System.Text.StringBuilder(@"
SELECT 
  COUNT(DISTINCT att.Id) AS Total,
  SUM(CASE WHEN (");
            if (toks.Length == 0) sb.Append("1=0");
            for (int i = 0; i < toks.Length; i++)
            {
                if (i > 0) sb.Append(" OR ");
                sb.Append("att.FileName LIKE @f" + i + " COLLATE Latin1_General_CI_AI");
            }
            sb.Append(@") THEN 1 ELSE 0 END) AS FileHits,
  SUM(CASE WHEN (");
            if (toks.Length == 0) sb.Append("1=0");
            for (int i = 0; i < toks.Length; i++)
            {
                if (i > 0) sb.Append(" OR ");
                sb.Append("at.[Text] LIKE @t" + i + " COLLATE Latin1_General_CI_AI");
            }
            sb.Append(@") THEN 1 ELSE 0 END) AS TextHits
FROM dbo.Attachment att
LEFT JOIN dbo.AttachmentText at ON at.AttachmentId = att.Id
WHERE att.TransaktionId = @id;");

            using var cmd = new SqlCommand(sb.ToString(), c);
            cmd.Parameters.AddWithValue("@id", transaktionId);
            for (int i = 0; i < toks.Length; i++)
                cmd.Parameters.AddWithValue("@f" + i, "%" + toks[i] + "%");
            for (int i = 0; i < toks.Length; i++)
                cmd.Parameters.AddWithValue("@t" + i, "%" + toks[i] + "%");

            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                int total = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0));
                int fileHits = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1));
                int textHits = r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2));
                return (total, fileHits, textHits);
            }
            return (0, 0, 0);
        }

        public bool KontoHatBuchungenImZeitraumByKontonummer(int kontonummer, DateTime? von, DateTime? bis)
        {
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT TOP(1) 1
FROM dbo.Transaktion t
LEFT JOIN dbo.Kontenplan kv ON kv.Id = t.VonKontoId
LEFT JOIN dbo.Kontenplan kn ON kn.Id = t.NachKontoId
WHERE (@von IS NULL OR ISNULL(t.BudgetDatum, t.Datum) >= @von)  -- NEU: BudgetDatum falls gesetzt
  AND (@bis IS NULL OR ISNULL(t.BudgetDatum, t.Datum) <= @bis)  -- NEU: BudgetDatum falls gesetzt
  AND (kv.Kontonummer = @knr OR kn.Kontonummer = @knr);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@knr", kontonummer);
            cmd.Parameters.AddWithValue("@von", (object?)von ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bis", (object?)bis ?? DBNull.Value);
            var o = cmd.ExecuteScalar();
            return o != null && o != DBNull.Value;
        }


        public void EnsureTransaktionBudgetDatumColumn()
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(ConnectionStrings.Current);
            conn.Open();

            const string sql = @"
IF COL_LENGTH('dbo.Transaktion', 'BudgetDatum') IS NULL
BEGIN
    ALTER TABLE dbo.Transaktion
    ADD BudgetDatum date NULL;
END
";

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
            cmd.CommandTimeout = 30;
            cmd.ExecuteNonQuery();
        }


        // Buchnungsregeln beim Bankimport

        public List<AdressBuchungsregel> LadeAdressBuchungsregeln(int adresseId, bool istEinnahme)
        {
            EnsureAdressBuchungsregelSchema();

            var list = new List<AdressBuchungsregel>();

            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT
    Id,
    AdresseId,
    IstEinnahme,
    TextPattern,
    PatternModus,
    KontoId,
    BetragVon,
    BetragBis,
    Prioritaet,
    IstAktiv,
    BelegAnzahl,
    LetzteBestaetigung,
    IstKonflikt
FROM dbo.AdressBuchungsregel
WHERE AdresseId = @aid
  AND IstEinnahme = @ein
  AND IstAktiv = 1
ORDER BY Prioritaet ASC, Id ASC;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@aid", adresseId);
            cmd.Parameters.AddWithValue("@ein", istEinnahme);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new AdressBuchungsregel
                {
                    Id = r.GetInt32(0),
                    AdresseId = r.GetInt32(1),
                    IstEinnahme = r.GetBoolean(2),
                    TextPattern = r.IsDBNull(3) ? "" : r.GetString(3),
                    PatternModus = r.IsDBNull(4) ? "Contains" : r.GetString(4),
                    KontoId = r.GetInt32(5),

                    BetragVon = r.IsDBNull(6) ? (decimal?)null : r.GetDecimal(6),
                    BetragBis = r.IsDBNull(7) ? (decimal?)null : r.GetDecimal(7),

                    Prioritaet = r.IsDBNull(8) ? 100 : r.GetInt32(8),
                    IstAktiv = !r.IsDBNull(9) && r.GetBoolean(9),

                    BelegAnzahl = r.IsDBNull(10) ? 1 : r.GetInt32(10),
                    LetzteBestaetigung = r.IsDBNull(11) ? (DateTime?)null : r.GetDateTime(11),
                    IstKonflikt = !r.IsDBNull(12) && r.GetBoolean(12)
                });
            }

            return list;
        }

        // ---------------------------------------------
        // Konto über adressbezogene Buchungsregeln auflösen.
        // Gemeinsame Logik für CAMT-Bank-Import (BankImportViewModel) und
        // Kreditkarten-Import, damit beide Importwege dieselben gelernten
        // Regeln nutzen. Es wird exakt dieselbe Regeltext-Bildung verwendet
        // wie beim Anlernen im ZuordnungDialog (BuildAliasCandidate dort).
        // ---------------------------------------------
        public int? ResolveKontoByAdressBuchungsregel(int adresseId, bool istEinnahme, string? text, string? serviceRef, decimal betrag, out bool istKonflikt)
        {
            istKonflikt = false;

            if (adresseId <= 0)
                return null;

            var regeln = LadeAdressBuchungsregeln(adresseId, istEinnahme);
            if (regeln == null || regeln.Count == 0)
                return null;

            var regelSuchtext = BuildAdressBuchungsregelCandidate(text, serviceRef);
            if (string.IsNullOrWhiteSpace(regelSuchtext))
                regelSuchtext = !string.IsNullOrWhiteSpace(text)
                    ? text!.Trim()
                    : (string.IsNullOrWhiteSpace(serviceRef) ? null : serviceRef!.Trim());

            if (string.IsNullOrWhiteSpace(regelSuchtext))
                return null;

            var textNorm = NormalizeBookingRuleText(regelSuchtext);
            var betragAbs = Math.Abs(betrag);

            var treffer = regeln
                .Where(r => !string.IsNullOrWhiteSpace(r.TextPattern))
                .Where(r => BuchungsregelTextPasst(r.TextPattern, r.PatternModus, textNorm))
                .Where(r => BuchungsregelBetragPasst(r, betragAbs))
                .OrderBy(r => r.Prioritaet)
                .ThenBy(r => r.Id)
                .ToList();

            AdressBuchungsregel? gewaehlt = null;

            if (treffer.Count == 1)
            {
                gewaehlt = treffer[0];
            }
            else if (treffer.Count > 1)
            {
                var bestePrioritaet = treffer[0].Prioritaet;
                var top = treffer.Where(x => x.Prioritaet == bestePrioritaet).ToList();

                if (top.Count == 1)
                    gewaehlt = top[0];
            }

            if (gewaehlt == null)
                return null;

            istKonflikt = gewaehlt.IstKonflikt;
            return gewaehlt.KontoId;
        }

        private static string? BuildAdressBuchungsregelCandidate(string? text, string? serviceRef)
        {
            var src = !string.IsNullOrWhiteSpace(text) ? text : (serviceRef ?? "");
            if (string.IsNullOrWhiteSpace(src)) return null;

            // IBANs / lange Nummern entfernen
            string t = Regex.Replace(src, @"[A-Z]{2}\d{2}[A-Z0-9]{4,}", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b\d{5,}\b", " ");

            // Wörter extrahieren (>=3 Zeichen)
            var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "RECHNUNG","REFERENZ","ZAHLUNG","GEBUEHR","KARTENZAHLUNG","BELASTUNG",
        "GUTSCHRIFT","MITTEILUNG","VALUTA","SEPA","SWIFT","UETR","CHF","EUR","USD",
        "VISA","MASTERCARD","TWINT","POSTFINANCE","UBS","CS","BANK","KONTO","IBAN"
    };

            var words = Regex.Matches(t.ToUpperInvariant(), @"[A-ZÄÖÜ0-9]{3,}")
                             .Cast<Match>()
                             .Select(m => m.Value)
                             .Where(w => !stop.Contains(w))
                             .ToList();

            if (words.Count == 0) return null;

            var picks = words.Take(4)
                             .Select(w => w.Length <= 5 ? w : w.Substring(0, 5))
                             .ToList();

            var code = string.Join("-", picks);

            if (code.Replace("-", "").Length < 8)
            {
                var fallback = words.OrderByDescending(w => w.Length)
                                    .Take(2)
                                    .Select(w => w.Length <= 6 ? w : w.Substring(0, 6));

                code = string.Join("-", fallback);
            }

            return code;
        }

        private static bool BuchungsregelTextPasst(string patternRaw, string? modusRaw, string textNorm)
        {
            var pattern = NormalizeBookingRuleText(patternRaw);
            if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(textNorm))
                return false;

            var modus = string.IsNullOrWhiteSpace(modusRaw) ? "Contains" : modusRaw.Trim();

            return modus switch
            {
                "Exact" => string.Equals(textNorm, pattern, StringComparison.Ordinal),
                "StartsWith" => textNorm.StartsWith(pattern, StringComparison.Ordinal),
                "EndsWith" => textNorm.EndsWith(pattern, StringComparison.Ordinal),
                "PrefixSeq" => PrefixSeqMatchForBookingRule(pattern, textNorm),
                _ => textNorm.Contains(pattern, StringComparison.Ordinal)
            };
        }

        // Kein Bereich gesetzt -> Betrag immer passend
        private static bool BuchungsregelBetragPasst(AdressBuchungsregel regel, decimal betragAbs)
        {
            if (regel == null)
                return false;

            if (!regel.BetragVon.HasValue && !regel.BetragBis.HasValue)
                return true;

            if (regel.BetragVon.HasValue && betragAbs < regel.BetragVon.Value)
                return false;

            if (regel.BetragBis.HasValue && betragAbs > regel.BetragBis.Value)
                return false;

            return true;
        }

        private static bool PrefixSeqMatchForBookingRule(string patternNorm, string textNorm)
        {
            var parts = patternNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            var tokens = textNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int pos = 0;

            foreach (var p in parts)
            {
                bool found = false;

                while (pos < tokens.Length)
                {
                    if (tokens[pos].StartsWith(p, StringComparison.Ordinal))
                    {
                        found = true;
                        pos++;
                        break;
                    }
                    pos++;
                }

                if (!found)
                    return false;
            }

            return true;
        }

        private static string NormalizeBookingRuleText(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var up = raw.ToUpperInvariant();
            var cleaned = Regex.Replace(up, @"[^A-Z0-9]+", " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned;
        }

        // ---------------------------------------------
        // Neuen Beleg für eine Adress-Buchungsregel aufzeichnen und
        // die aktive Regel daraus neu ableiten.
        //
        // Ersetzt das frühere Verhalten, bei dem eine Regel auf den
        // Betrag der einen gelernten Buchung exakt fixiert wurde
        // (BetragVon == BetragBis == dieser eine Betrag). Dadurch griff
        // die Regel bei praktisch keiner folgenden Buchung mehr, weil
        // sich Beträge zur selben Gegenpartei fast immer unterscheiden.
        //
        // Stattdessen wird jede Bestätigung als Beleg gespeichert und
        // die Regel aus allen bisherigen Belegen zu diesem
        // (AdresseId, IstEinnahme, TextPattern, PatternModus) neu berechnet:
        //   - Ein einziges beobachtetes Konto  -> Regel ohne Betragsfilter.
        //   - Mehrere Konten, Beträge überlappen sich nicht
        //                                       -> je Konto eine Regel mit
        //                                          dem beobachteten Betragsband.
        //   - Mehrere Konten, Beträge überlappen sich (echter Konflikt)
        //                                       -> Mehrheitskonto bleibt aktiv,
        //                                          aber als IstKonflikt markiert,
        //                                          damit die Oberfläche den
        //                                          Treffer als unsicher kennzeichnet
        //                                          statt ihn stillschweigend zu übernehmen.
        // ---------------------------------------------
        public void LernAdressBuchungsregel(int adresseId, bool istEinnahme, string textPattern, string patternModus, int kontoId, decimal betrag, int prioritaet = 100)
        {
            if (adresseId <= 0) throw new ArgumentOutOfRangeException(nameof(adresseId));
            if (kontoId <= 0) throw new ArgumentOutOfRangeException(nameof(kontoId));

            var pattern = (textPattern ?? "").Trim();
            if (string.IsNullOrWhiteSpace(pattern))
                throw new ArgumentException("TextPattern fehlt.", nameof(textPattern));

            var modus = string.IsNullOrWhiteSpace(patternModus) ? "Contains" : patternModus.Trim();
            var betragAbs = Math.Abs(betrag);

            using var c = new SqlConnection(_connectionString);
            c.Open();
            using var tx = c.BeginTransaction();

            try
            {
                using (var insertBeleg = new SqlCommand(@"
INSERT INTO dbo.AdressBuchungsregelBeleg (AdresseId, IstEinnahme, TextPattern, PatternModus, Betrag, KontoId)
VALUES (@AdresseId, @IstEinnahme, @TextPattern, @PatternModus, @Betrag, @KontoId);", c, tx))
                {
                    insertBeleg.Parameters.AddWithValue("@AdresseId", adresseId);
                    insertBeleg.Parameters.AddWithValue("@IstEinnahme", istEinnahme);
                    insertBeleg.Parameters.AddWithValue("@TextPattern", pattern);
                    insertBeleg.Parameters.AddWithValue("@PatternModus", modus);
                    insertBeleg.Parameters.AddWithValue("@Betrag", betragAbs);
                    insertBeleg.Parameters.AddWithValue("@KontoId", kontoId);
                    insertBeleg.ExecuteNonQuery();
                }

                var belege = new List<(int KontoId, decimal Betrag)>();
                using (var loadBelege = new SqlCommand(@"
SELECT KontoId, Betrag
FROM dbo.AdressBuchungsregelBeleg
WHERE AdresseId = @AdresseId AND IstEinnahme = @IstEinnahme
  AND TextPattern = @TextPattern AND PatternModus = @PatternModus;", c, tx))
                {
                    loadBelege.Parameters.AddWithValue("@AdresseId", adresseId);
                    loadBelege.Parameters.AddWithValue("@IstEinnahme", istEinnahme);
                    loadBelege.Parameters.AddWithValue("@TextPattern", pattern);
                    loadBelege.Parameters.AddWithValue("@PatternModus", modus);

                    using var r = loadBelege.ExecuteReader();
                    while (r.Read())
                        belege.Add((r.GetInt32(0), r.GetDecimal(1)));
                }

                using (var deleteAlt = new SqlCommand(@"
DELETE FROM dbo.AdressBuchungsregel
WHERE AdresseId = @AdresseId AND IstEinnahme = @IstEinnahme
  AND TextPattern = @TextPattern AND PatternModus = @PatternModus;", c, tx))
                {
                    deleteAlt.Parameters.AddWithValue("@AdresseId", adresseId);
                    deleteAlt.Parameters.AddWithValue("@IstEinnahme", istEinnahme);
                    deleteAlt.Parameters.AddWithValue("@TextPattern", pattern);
                    deleteAlt.Parameters.AddWithValue("@PatternModus", modus);
                    deleteAlt.ExecuteNonQuery();
                }

                var gruppen = belege
                    .GroupBy(b => b.KontoId)
                    .Select(g => new
                    {
                        KontoId = g.Key,
                        Anzahl = g.Count(),
                        Min = g.Min(x => x.Betrag),
                        Max = g.Max(x => x.Betrag)
                    })
                    .OrderByDescending(g => g.Anzahl)
                    .ToList();

                void InsertRegel(int gKontoId, decimal? von, decimal? bis, int anzahl, int prio, bool konflikt)
                {
                    using var insertRegel = new SqlCommand(@"
INSERT INTO dbo.AdressBuchungsregel
    (AdresseId, IstEinnahme, TextPattern, PatternModus, KontoId, BetragVon, BetragBis, Prioritaet, IstAktiv, BelegAnzahl, LetzteBestaetigung, IstKonflikt)
VALUES
    (@AdresseId, @IstEinnahme, @TextPattern, @PatternModus, @KontoId, @BetragVon, @BetragBis, @Prioritaet, 1, @BelegAnzahl, SYSUTCDATETIME(), @IstKonflikt);", c, tx);

                    insertRegel.Parameters.AddWithValue("@AdresseId", adresseId);
                    insertRegel.Parameters.AddWithValue("@IstEinnahme", istEinnahme);
                    insertRegel.Parameters.AddWithValue("@TextPattern", pattern);
                    insertRegel.Parameters.AddWithValue("@PatternModus", modus);
                    insertRegel.Parameters.AddWithValue("@KontoId", gKontoId);
                    insertRegel.Parameters.AddWithValue("@BetragVon", (object?)von ?? DBNull.Value);
                    insertRegel.Parameters.AddWithValue("@BetragBis", (object?)bis ?? DBNull.Value);
                    insertRegel.Parameters.AddWithValue("@Prioritaet", prio);
                    insertRegel.Parameters.AddWithValue("@BelegAnzahl", anzahl);
                    insertRegel.Parameters.AddWithValue("@IstKonflikt", konflikt);
                    insertRegel.ExecuteNonQuery();
                }

                if (gruppen.Count == 1)
                {
                    // Ein einziges Konto über alle bisher beobachteten Beträge:
                    // Regel gilt betragsunabhängig.
                    InsertRegel(gruppen[0].KontoId, null, null, gruppen[0].Anzahl, prioritaet, konflikt: false);
                }
                else
                {
                    bool overlap = false;
                    for (int i = 0; i < gruppen.Count && !overlap; i++)
                        for (int j = i + 1; j < gruppen.Count; j++)
                            if (gruppen[i].Min <= gruppen[j].Max && gruppen[j].Min <= gruppen[i].Max)
                            {
                                overlap = true;
                                break;
                            }

                    if (!overlap)
                    {
                        // Beträge trennen die Konten sauber -> je Konto ein Betragsband.
                        int prio = prioritaet;
                        foreach (var g in gruppen.OrderBy(g => g.Min))
                        {
                            InsertRegel(g.KontoId, g.Min, g.Max, g.Anzahl, prio, konflikt: false);
                            prio += 1;
                        }
                    }
                    else
                    {
                        // Echter Konflikt: gleicher Text, überlappende Beträge, unterschiedliche Konten.
                        // Mehrheitskonto bleibt aktiv, aber als unsicher markiert statt blind zu raten.
                        var mehrheit = gruppen[0];
                        var gesamtAnzahl = gruppen.Sum(g => g.Anzahl);
                        InsertRegel(mehrheit.KontoId, null, null, gesamtAnzahl, prioritaet, konflikt: true);
                    }
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        // ---------------------------------------------
        // Konto-Schnellwahl: je Benutzer frei wählbare Konten,
        // die im Zuordnungsdialog als Klick-Buttons erscheinen,
        // statt sie jedes Mal aus dem Dropdown suchen zu müssen.
        // ---------------------------------------------
        public void EnsureKontoSchnellwahlSchema()
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'KontoSchnellwahl' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.KontoSchnellwahl
    (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_KontoSchnellwahl PRIMARY KEY,
        Username     NVARCHAR(64) NOT NULL,
        KontoId      INT NOT NULL,
        Reihenfolge  INT NOT NULL CONSTRAINT DF_KontoSchnellwahl_Reihenfolge DEFAULT(100),

        CONSTRAINT UQ_KontoSchnellwahl_User_Konto UNIQUE (Username, KontoId),
        CONSTRAINT FK_KontoSchnellwahl_Konto FOREIGN KEY (KontoId) REFERENCES dbo.Kontenplan(Id)
    );

    CREATE INDEX IX_KontoSchnellwahl_Username ON dbo.KontoSchnellwahl(Username);
END;
";
            using var cmd = new SqlCommand(sql, c);
            cmd.ExecuteNonQuery();
        }

        public List<int> LadeKontoSchnellwahl(string username)
        {
            EnsureKontoSchnellwahlSchema();

            var list = new List<int>();
            if (string.IsNullOrWhiteSpace(username)) return list;

            using var c = new SqlConnection(_connectionString);
            c.Open();

            using var cmd = new SqlCommand(@"
SELECT KontoId
FROM dbo.KontoSchnellwahl
WHERE Username = @Username
ORDER BY Reihenfolge ASC, Id ASC;", c);
            cmd.Parameters.AddWithValue("@Username", username.Trim());

            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(r.GetInt32(0));

            return list;
        }

        // Ersetzt die komplette Schnellwahl-Liste des Benutzers (Reihenfolge = Position in der übergebenen Liste).
        public void SpeichereKontoSchnellwahl(string username, IEnumerable<int> kontoIds)
        {
            EnsureKontoSchnellwahlSchema();

            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username fehlt.", nameof(username));
            var name = username.Trim();

            using var c = new SqlConnection(_connectionString);
            c.Open();
            using var tx = c.BeginTransaction();

            try
            {
                using (var del = new SqlCommand("DELETE FROM dbo.KontoSchnellwahl WHERE Username = @Username;", c, tx))
                {
                    del.Parameters.AddWithValue("@Username", name);
                    del.ExecuteNonQuery();
                }

                int pos = 0;
                foreach (var kontoId in kontoIds.Distinct())
                {
                    using var ins = new SqlCommand(@"
INSERT INTO dbo.KontoSchnellwahl (Username, KontoId, Reihenfolge)
VALUES (@Username, @KontoId, @Reihenfolge);", c, tx);
                    ins.Parameters.AddWithValue("@Username", name);
                    ins.Parameters.AddWithValue("@KontoId", kontoId);
                    ins.Parameters.AddWithValue("@Reihenfolge", pos);
                    ins.ExecuteNonQuery();
                    pos++;
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        // ---------------------------------------------
        // Verwaltung gelernter Zuordnungen (Aliase + Buchungsregeln),
        // damit Fehlanlernungen über die Oberfläche gefunden und entfernt
        // werden können statt direkt in der DB.
        // ---------------------------------------------
        public class AdressAliasAnzeige
        {
            public int Id { get; set; }
            public int AdresseId { get; set; }
            public string AdresseName { get; set; } = "";
            public string Text { get; set; } = "";
            public string Modus { get; set; } = "";
        }

        public List<AdressAliasAnzeige> LadeAdressAliaseMitNamen()
        {
            var adressen = LadeAdressen().ToDictionary(a => a.Id, a => a.Name);

            return LadeAdressAliase()
                .Select(a => new AdressAliasAnzeige
                {
                    Id = a.Id,
                    AdresseId = a.AdresseId,
                    AdresseName = adressen.TryGetValue(a.AdresseId, out var n) ? n : $"(Adresse #{a.AdresseId})",
                    Text = a.Text,
                    Modus = a.Modus
                })
                .OrderBy(a => a.AdresseName).ThenBy(a => a.Text)
                .ToList();
        }

        public void LoescheAdressAlias(int id)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();
            using var cmd = new SqlCommand("DELETE FROM dbo.AdresseAlias WHERE Id=@id;", c);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public class AdressBuchungsregelAnzeige
        {
            public int Id { get; set; }
            public string AdresseName { get; set; } = "";
            public bool IstEinnahme { get; set; }
            public string TextPattern { get; set; } = "";
            public string PatternModus { get; set; } = "";
            public string KontoAnzeige { get; set; } = "";
            public decimal? BetragVon { get; set; }
            public decimal? BetragBis { get; set; }
            public int BelegAnzahl { get; set; }
            public bool IstKonflikt { get; set; }
        }

        public List<AdressBuchungsregelAnzeige> LadeAlleAdressBuchungsregelnMitNamen()
        {
            EnsureAdressBuchungsregelSchema();

            var adressen = LadeAdressen().ToDictionary(a => a.Id, a => a.Name);
            var konten = LadeKontoLookup().ToDictionary(k => k.Id, k => k.Anzeige);

            var list = new List<AdressBuchungsregelAnzeige>();

            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT Id, AdresseId, IstEinnahme, TextPattern, PatternModus, KontoId, BetragVon, BetragBis, BelegAnzahl, IstKonflikt
FROM dbo.AdressBuchungsregel
WHERE IstAktiv = 1
ORDER BY AdresseId, TextPattern;";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var adresseId = r.GetInt32(1);
                var kontoId = r.GetInt32(5);

                list.Add(new AdressBuchungsregelAnzeige
                {
                    Id = r.GetInt32(0),
                    AdresseName = adressen.TryGetValue(adresseId, out var an) ? an : $"(Adresse #{adresseId})",
                    IstEinnahme = r.GetBoolean(2),
                    TextPattern = r.IsDBNull(3) ? "" : r.GetString(3),
                    PatternModus = r.IsDBNull(4) ? "Contains" : r.GetString(4),
                    KontoAnzeige = konten.TryGetValue(kontoId, out var ka) ? ka : $"(Konto #{kontoId})",
                    BetragVon = r.IsDBNull(6) ? (decimal?)null : r.GetDecimal(6),
                    BetragBis = r.IsDBNull(7) ? (decimal?)null : r.GetDecimal(7),
                    BelegAnzahl = r.IsDBNull(8) ? 1 : r.GetInt32(8),
                    IstKonflikt = !r.IsDBNull(9) && r.GetBoolean(9)
                });
            }

            return list;
        }

        // Löscht eine gelernte Buchungsregel UND die zugrunde liegenden Belege
        // für dieses Konto. Ohne das Entfernen der Belege würde die Regel beim
        // nächsten Anlernen eines ähnlichen Falls aus der Beleghistorie sofort
        // wieder abgeleitet werden - das Löschen wäre nur oberflächlich.
        public void LoescheAdressBuchungsregel(int id)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();
            using var tx = c.BeginTransaction();

            try
            {
                int adresseId, kontoId;
                bool istEinnahme;
                string textPattern, patternModus;

                using (var sel = new SqlCommand(
                    "SELECT AdresseId, IstEinnahme, TextPattern, PatternModus, KontoId FROM dbo.AdressBuchungsregel WHERE Id=@id;", c, tx))
                {
                    sel.Parameters.AddWithValue("@id", id);
                    using var r = sel.ExecuteReader();
                    if (!r.Read())
                    {
                        tx.Rollback();
                        return;
                    }
                    adresseId = r.GetInt32(0);
                    istEinnahme = r.GetBoolean(1);
                    textPattern = r.IsDBNull(2) ? "" : r.GetString(2);
                    patternModus = r.IsDBNull(3) ? "Contains" : r.GetString(3);
                    kontoId = r.GetInt32(4);
                }

                using (var delBeleg = new SqlCommand(@"
DELETE FROM dbo.AdressBuchungsregelBeleg
WHERE AdresseId=@a AND IstEinnahme=@e AND TextPattern=@t AND PatternModus=@m AND KontoId=@k;", c, tx))
                {
                    delBeleg.Parameters.AddWithValue("@a", adresseId);
                    delBeleg.Parameters.AddWithValue("@e", istEinnahme);
                    delBeleg.Parameters.AddWithValue("@t", textPattern);
                    delBeleg.Parameters.AddWithValue("@m", patternModus);
                    delBeleg.Parameters.AddWithValue("@k", kontoId);
                    delBeleg.ExecuteNonQuery();
                }

                using (var delRegel = new SqlCommand("DELETE FROM dbo.AdressBuchungsregel WHERE Id=@id;", c, tx))
                {
                    delRegel.Parameters.AddWithValue("@id", id);
                    delRegel.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        // ---------------------------------------------
        // Kategorie-Standardkonto: fester, vom Nutzer gepflegter Fallback
        // für den Kreditkarten-Import. Kreditkarten-Anbieter liefern eine
        // kleine, feste Menge an Kategorien (Kartennummer/Händler dagegen
        // wechseln pro Buchung) - darum eigenständige Tabelle statt der
        // alten, mehrdeutigen KategorieKontoMapping (komposiver Schlüssel).
        // ---------------------------------------------
        public void EnsureKategorieStandardkontoSchema()
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'KategorieStandardkonto' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.KategorieStandardkonto
    (
        Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_KategorieStandardkonto PRIMARY KEY,
        Kategorie NVARCHAR(200) NOT NULL,
        KontoId   INT NULL,

        CONSTRAINT UQ_KategorieStandardkonto_Kategorie UNIQUE (Kategorie),
        CONSTRAINT FK_KategorieStandardkonto_Konto FOREIGN KEY (KontoId) REFERENCES dbo.Kontenplan(Id)
    );
END;
";
            using var cmd = new SqlCommand(sql, c);
            cmd.ExecuteNonQuery();
        }

        public List<KategorieStandardkonto> LadeKategorieStandardkonten()
        {
            EnsureKategorieStandardkontoSchema();

            var konten = LadeKontoLookup().ToDictionary(k => k.Id, k => k.Anzeige);
            var list = new List<KategorieStandardkonto>();

            using var c = new SqlConnection(_connectionString);
            c.Open();

            using var cmd = new SqlCommand(
                "SELECT Id, Kategorie, KontoId FROM dbo.KategorieStandardkonto ORDER BY Kategorie;", c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var kontoId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2);
                list.Add(new KategorieStandardkonto
                {
                    Id = r.GetInt32(0),
                    Kategorie = r.IsDBNull(1) ? "" : r.GetString(1),
                    KontoId = kontoId,
                    KontoAnzeige = kontoId.HasValue && konten.TryGetValue(kontoId.Value, out var a) ? a : null
                });
            }

            return list;
        }

        // Fügt Kategorien ohne Konto hinzu, die noch nicht existieren (z. B. beim
        // Einlesen einer Musterdatei). Bereits vorhandene Kategorien (mit oder
        // ohne Konto) bleiben unverändert.
        public void SeedKategorienOhneKonto(IEnumerable<string> kategorien)
        {
            EnsureKategorieStandardkontoSchema();

            using var c = new SqlConnection(_connectionString);
            c.Open();

            foreach (var roh in kategorien.Select(k => (k ?? "").Trim()).Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                const string sql = @"
IF NOT EXISTS (SELECT 1 FROM dbo.KategorieStandardkonto WHERE UPPER(LTRIM(RTRIM(Kategorie))) = UPPER(LTRIM(RTRIM(@k))))
BEGIN
    INSERT INTO dbo.KategorieStandardkonto (Kategorie, KontoId) VALUES (@k, NULL);
END;";
                using var cmd = new SqlCommand(sql, c);
                cmd.Parameters.AddWithValue("@k", roh);
                cmd.ExecuteNonQuery();
            }
        }

        public void SpeichereKategorieStandardkonten(IEnumerable<(int Id, int? KontoId)> zeilen)
        {
            EnsureKategorieStandardkontoSchema();

            using var c = new SqlConnection(_connectionString);
            c.Open();
            using var tx = c.BeginTransaction();

            try
            {
                foreach (var z in zeilen)
                {
                    using var cmd = new SqlCommand("UPDATE dbo.KategorieStandardkonto SET KontoId=@k WHERE Id=@id;", c, tx);
                    cmd.Parameters.AddWithValue("@k", (object?)z.KontoId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@id", z.Id);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public int? HoleKontoIdFuerKategorie(string? kategorie)
        {
            if (string.IsNullOrWhiteSpace(kategorie))
                return null;

            EnsureKategorieStandardkontoSchema();

            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT KontoId FROM dbo.KategorieStandardkonto
WHERE UPPER(LTRIM(RTRIM(Kategorie))) = UPPER(LTRIM(RTRIM(@k))) AND KontoId IS NOT NULL;";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@k", kategorie.Trim());
            var v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? (int?)null : Convert.ToInt32(v);
        }
    }
}
