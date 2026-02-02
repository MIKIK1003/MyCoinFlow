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

    /// <summary>
    /// Repräsentiert einen Eintrag aus der Kontenplan-Tabelle.
    /// Diese Klasse steht im Models-Ordner, wird hier verwendet.
    /// </summary>
    public class DatabaseService : ICreditCardImportRepository
    {
        // Verbindung zur Datenbank
        private string _connectionString => MyCoinFlow.Services.ConnectionStrings.Current;


        public Microsoft.Data.SqlClient.SqlConnection CreateConnection()
        {
            // Liefert eine GESCHLOSSENE Verbindung. Öffnen/Schließen übernimmt der Provider.
            return new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
        }



        /// <summary>
        /// Prüft testweise, ob die Datenbankverbindung funktioniert.
        /// </summary>
        public bool TestConnection()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    connection.Open();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler beim Verbinden mit der Datenbank: {ex.Message}");
                return false;
            }
        }

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
          AND t.Datum >= bz.Startdatum AND t.Datum <= bz.Enddatum
        GROUP BY t.NachKontoId
        UNION ALL
        -- Abgänge (VonKontoId = dieses Konto) negativ
        SELECT t.VonKontoId AS KontoId, SUM(-t.Betrag) AS Wert
        FROM Transaktion t
        WHERE bz.Id IS NOT NULL
          AND t.VonKontoId = k.Id
          AND t.Datum >= bz.Startdatum AND t.Datum <= bz.Enddatum
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
            //    (nutzt die bereits bei Adresse eingeführten Helper: GetReferencingCounts / ShowDeleteBlockedMessage)
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
                    return; // Nutzer lehnt ab → ruhig zurück

                // Mappings löschen
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

            // 3) Harte Blocker verbleiben? (Transaktion.VonKontoId/NachKontoId, BudgetDetail.KontoId, …)
            if (refs.Count > 0)
            {
                ShowDeleteBlockedMessage("Kontenplan-Eintrag", refs);
                return;
            }

            // 4) Konto löschen
            try
            {
                using var del = new SqlCommand("DELETE FROM dbo.Kontenplan WHERE Id = @Id", c);
                del.Parameters.AddWithValue("@Id", id);
                del.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // FK-Fehler freundlich abfangen (falls Race-Condition o. ä.)
                if (HandleSqlDeleteException(ex, "Kontenplan-Eintrag")) return;

                System.Windows.MessageBox.Show("Kontenplan-Eintrag konnte nicht gelöscht werden:\n" + ex.Message,
                    "Löschen fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                // kein throw → kein Programmstop
            }
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


        public void BudgetwertSpeichern(int zeitraumId, int kontoId, decimal wert)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                // Existiert bereits ein Eintrag?
                string sqlCheck = "SELECT COUNT(*) FROM BudgetDetail WHERE ZeitraumId = @ZeitraumId AND KontoId = @KontoId";
                using (var checkCmd = new SqlCommand(sqlCheck, connection))
                {
                    checkCmd.Parameters.AddWithValue("@ZeitraumId", zeitraumId);
                    checkCmd.Parameters.AddWithValue("@KontoId", kontoId);
                    int count = (int)checkCmd.ExecuteScalar();

                    if (count > 0)
                    {
                        // Update
                        string sqlUpdate = "UPDATE BudgetDetail SET Budgetwert = @Wert WHERE ZeitraumId = @ZeitraumId AND KontoId = @KontoId";
                        using (var updateCmd = new SqlCommand(sqlUpdate, connection))
                        {
                            updateCmd.Parameters.AddWithValue("@Wert", wert);
                            updateCmd.Parameters.AddWithValue("@ZeitraumId", zeitraumId);
                            updateCmd.Parameters.AddWithValue("@KontoId", kontoId);
                            updateCmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        // Insert
                        string sqlInsert = "INSERT INTO BudgetDetail (ZeitraumId, KontoId, Budgetwert) VALUES (@ZeitraumId, @KontoId, @Wert)";
                        using (var insertCmd = new SqlCommand(sqlInsert, connection))
                        {
                            insertCmd.Parameters.AddWithValue("@ZeitraumId", zeitraumId);
                            insertCmd.Parameters.AddWithValue("@KontoId", kontoId);
                            insertCmd.Parameters.AddWithValue("@Wert", wert);
                            insertCmd.ExecuteNonQuery();
                        }
                    }
                }
            }
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

        public void AktualisiereKontenArt(int id, string bezeichnung)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                string sql = "UPDATE KontenArt SET Bezeichnung = @Bez WHERE Id = @Id";

                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
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

        public void AktualisiereKontenGruppe(int id, string bezeichnung)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                const string sql = "UPDATE KontenGruppe SET Bezeichnung = @Bez WHERE Id = @Id";
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
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

        public void AktualisiereKontenUnterGruppe(int id, string bezeichnung)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                const string sql = "UPDATE KontenUnterGruppe SET Bezeichnung = @Bez WHERE Id = @Id";
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
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
        private Dictionary<string, int> GetReferencingCounts(string schemaName, string tableName, string pkColumn, int id)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();

            // 1) Alle FKs finden, die auf (schema.table.pkColumn) zeigen
            string fkQuery = @"
SELECT 
    schR.name  AS RefSchema,
    tR.name    AS RefTable,
    cR.name    AS RefColumn
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.tables tP   ON fk.referenced_object_id = tP.object_id
JOIN sys.columns cP ON cP.object_id = tP.object_id AND cP.column_id = fkc.referenced_column_id
JOIN sys.tables tR   ON fk.parent_object_id = tR.object_id
JOIN sys.columns cR ON cR.object_id = tR.object_id AND cR.column_id = fkc.parent_column_id
JOIN sys.schemas schP ON schP.schema_id = tP.schema_id
JOIN sys.schemas schR ON schR.schema_id = tR.schema_id
WHERE schP.name = @pSchema AND tP.name = @pTable AND cP.name = @pPk;";

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(fkQuery, c);
            cmd.Parameters.AddWithValue("@pSchema", schemaName);
            cmd.Parameters.AddWithValue("@pTable", tableName);
            cmd.Parameters.AddWithValue("@pPk", pkColumn);

            var refs = new List<(string RefSchema, string RefTable, string RefColumn)>();
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                    refs.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
            }

            // 2) Für jede referenzierende Tabelle zählen, wie viele Zeilen den PK-Wert benutzen
            foreach (var (refSchema, refTable, refCol) in refs)
            {
                string countSql = $@"SELECT COUNT(*) FROM [{refSchema}].[{refTable}] WHERE [{refCol}] = @id;";
                using var countCmd = new Microsoft.Data.SqlClient.SqlCommand(countSql, c);
                countCmd.Parameters.AddWithValue("@id", id);
                var v = countCmd.ExecuteScalar();
                int cnt = (v == null || v == DBNull.Value) ? 0 : Convert.ToInt32(v);
                if (cnt > 0) result[$"{refSchema}.{refTable}"] = cnt;
            }

            return result;
        }

        private bool ShowDeleteBlockedMessage(string objektBezeichnung, Dictionary<string, int> refs)
        {
            var lines = new List<string> { $"{objektBezeichnung} kann nicht gelöscht werden, weil noch Verweise existieren:" };
            foreach (var kv in refs.OrderByDescending(x => x.Value).Take(6))
                lines.Add($"• {kv.Key}: {kv.Value} Zeile(n)");
            lines.Add("");
            lines.Add("Bitte zuerst diese Verweise auflösen (z. B. Umbuchen oder löschen) und dann erneut versuchen.");

            System.Windows.MessageBox.Show(string.Join(Environment.NewLine, lines),
                "Löschen nicht möglich", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return false;
        }

        // FK-/Delete-Fehler nutzerfreundlich anzeigen UND als Fehlschlag signalisieren
        private bool HandleSqlDeleteException(Exception ex, string objektBezeichnung)
        {
            if (ex is SqlException sqlEx && sqlEx.Number == 547) // FK-Constraint
            {
                System.Windows.MessageBox.Show(
                    $"{objektBezeichnung} kann nicht gelöscht werden, weil noch abhängige Daten existieren.\n\n" +
                    "Bitte abhängige Datensätze zuerst entfernen oder umhängen.",
                    "Löschen nicht möglich",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return true; // handled → keine Exception weiterwerfen
            }
            return false;    // nicht behandelt → Aufrufer entscheidet

        }
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

        public System.Collections.Generic.Dictionary<string, bool?> LadeArtFlagProLabel(string labelColumn)
        {
            var col = (labelColumn ?? "").Trim();
            if (col != "Art" && col != "Gruppe" && col != "Untergruppe")
                throw new ArgumentException("labelColumn muss 'Art', 'Gruppe' oder 'Untergruppe' sein.", nameof(labelColumn));

            EnsureNumberRangeRulesTable();

            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();

            // Regel zuerst, sonst Text-Heuristik (keine fixen 3xxx/7xxx/4xxx-6xxx mehr!)
            var sql = $@"
SELECT 
  COALESCE(NULLIF(kp.{col}, ''), '(ohne Zuordnung)') AS Label,
  SUM(CASE 
        WHEN nr.Richtung = N'Einnahme' OR (
             nr.Richtung IS NULL AND (
               UPPER(ISNULL(kp.Art,'')) LIKE '%EINNAHM%' OR
               UPPER(ISNULL(kp.Art,'')) LIKE '%ERTR%'   OR
               UPPER(ISNULL(kp.Gruppe,'')) LIKE '%EINNAHM%' OR
               UPPER(ISNULL(kp.Gruppe,'')) LIKE '%ERTR%'   OR
               UPPER(ISNULL(kp.Untergruppe,'')) LIKE '%EINNAHM%' OR
               UPPER(ISNULL(kp.Untergruppe,'')) LIKE '%ERTR%'   OR
               UPPER(ISNULL(kp.Detail,'')) LIKE '%EINNAHM%' OR
               UPPER(ISNULL(kp.Detail,'')) LIKE '%ERTR%'
             )
        )
      THEN 1 ELSE 0 END) AS Ein,
  SUM(CASE 
        WHEN nr.Richtung = N'Ausgabe' OR (
             nr.Richtung IS NULL AND (
               UPPER(ISNULL(kp.Art,'')) LIKE '%AUSGAB%' OR
               UPPER(ISNULL(kp.Art,'')) LIKE '%AUFW%'   OR
               UPPER(ISNULL(kp.Gruppe,'')) LIKE '%AUSGAB%' OR
               UPPER(ISNULL(kp.Gruppe,'')) LIKE '%AUFW%'   OR
               UPPER(ISNULL(kp.Untergruppe,'')) LIKE '%AUSGAB%' OR
               UPPER(ISNULL(kp.Untergruppe,'')) LIKE '%AUFW%'   OR
               UPPER(ISNULL(kp.Detail,'')) LIKE '%AUSGAB%' OR
               UPPER(ISNULL(kp.Detail,'')) LIKE '%AUFW%'
             )
        )
      THEN 1 ELSE 0 END) AS Aus
FROM Kontenplan kp
OUTER APPLY (
  SELECT TOP 1 Richtung
  FROM NumberRangeRules
  WHERE kp.Kontonummer IS NOT NULL
    AND kp.Kontonummer BETWEEN RangeStart AND RangeEnd
  ORDER BY (RangeEnd - RangeStart) ASC, RangeStart ASC
) nr
GROUP BY COALESCE(NULLIF(kp.{col}, ''), '(ohne Zuordnung)')
ORDER BY Label";
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();

            var dict = new System.Collections.Generic.Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
            while (r.Read())
            {
                var label = r.GetString(0);
                var ein = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                var aus = r.IsDBNull(2) ? 0 : r.GetInt32(2);
                bool? flag = null; // null = unklar/gemischt
                if (ein > 0 && aus == 0) flag = true;      // Einnahme
                if (aus > 0 && ein == 0) flag = false;     // Ausgabe
                dict[label] = flag;
            }
            return dict;
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
                                 int? adresseId, int? geldinstitutId)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"INSERT INTO Transaktion
                         (Datum, VonKontoId, NachKontoId, Betrag, Notiz, AdresseId, GeldinstitutId)
                         VALUES (@d, @v, @n, @b, @z, @a, @g)";

            using var cmd = new SqlCommand(sql, c);

            cmd.Parameters.Add(new SqlParameter("@d", System.Data.SqlDbType.Date) { Value = datum.Date });

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

        public List<Transaktion> LadeTransaktionen(DateTime? bisDatum = null)
        {
            var list = new List<Transaktion>();
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT t.Id, t.Datum, t.VonKontoId, t.NachKontoId,
       t.Betrag, t.Notiz,
       t.AdresseId, a.Name as AdresseName,
       t.GeldinstitutId, g.Name as BankName,
       t.ImportQuelle
FROM Transaktion t
LEFT JOIN Adresse a     ON t.AdresseId = a.Id
LEFT JOIN Geldinstitut g ON t.GeldinstitutId = g.Id
WHERE (@bis IS NULL OR t.Datum <= @bis)
ORDER BY t.Datum DESC";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@bis", (object?)bisDatum ?? DBNull.Value);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Transaktion
                {
                    Id = r.GetInt32(0),
                    Datum = r.GetDateTime(1),
                    VonKontoId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                    NachKontoId = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    Betrag = r.GetDecimal(4),
                    Notiz = r.IsDBNull(5) ? null : r.GetString(5),
                    AdresseId = r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
                    AdresseName = r.IsDBNull(7) ? null : r.GetString(7),
                    GeldinstitutId = r.IsDBNull(8) ? (int?)null : r.GetInt32(8),
                    BankName = r.IsDBNull(9) ? null : r.GetString(9),
                    ImportQuelle = r.IsDBNull(10) ? null : r.GetString(10)
                });
            }

            return list;
        }

        public MyCoinFlow.Models.Transaktion? HoleTransaktion(int id)
        {
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT t.Id, t.Datum, t.VonKontoId, t.NachKontoId,
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
                VonKontoId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                NachKontoId = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                Betrag = r.GetDecimal(4),
                Notiz = r.IsDBNull(5) ? null : r.GetString(5),
                AdresseId = r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
                AdresseName = r.IsDBNull(7) ? null : r.GetString(7),
                GeldinstitutId = r.IsDBNull(8) ? (int?)null : r.GetInt32(8),
                BankName = r.IsDBNull(9) ? null : r.GetString(9),
                ImportQuelle = r.IsDBNull(10) ? null : r.GetString(10)
            };
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
SELECT t.Id, t.Datum, t.VonKontoId, t.NachKontoId, t.Betrag, t.Notiz,
       t.AdresseId, a.Name as AdresseName,
       t.GeldinstitutId, g.Name as BankName,
       t.ImportQuelle
FROM Transaktion t
LEFT JOIN Adresse a ON t.AdresseId = a.Id
LEFT JOIN Geldinstitut g ON t.GeldinstitutId = g.Id
WHERE t.GeldinstitutId = @gi
  AND (@von IS NULL OR t.Datum >= @von)
  AND (@bis IS NULL OR t.Datum <= @bis)
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
                    VonKontoId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                    NachKontoId = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    Betrag = r.GetDecimal(4),
                    Notiz = r.IsDBNull(5) ? null : r.GetString(5),
                    AdresseId = r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
                    AdresseName = r.IsDBNull(7) ? null : r.GetString(7),
                    GeldinstitutId = r.IsDBNull(8) ? (int?)null : r.GetInt32(8),
                    BankName = r.IsDBNull(9) ? null : r.GetString(9),
                    ImportQuelle = r.IsDBNull(10) ? null : r.GetString(10)
                });
            }

            return list;
        }



        // ---------- TRANSAKTIONEN UPDATE/DELETE ----------

        public void AktualisiereTransaktion(int id, DateTime datum, int? vonKontoId, int? nachKontoId,
                                            decimal betrag, string? notiz,
                                            int? adresseId, int? geldinstitutId)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"UPDATE Transaktion SET
                           Datum=@d,
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
            const string sql = "DELETE FROM Transaktion WHERE Id=@id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // --- Defaults für Konto-Vorschläge (Adresse.DefaultKontoId) -----------------

        private static string NormalizeIban(string? iban)
            => string.IsNullOrWhiteSpace(iban) ? "" : iban.Replace(" ", "").ToUpperInvariant();

        /// <summary>
        /// Liefert die DefaultKontoId direkt aus der Tabelle Adresse.
        /// </summary>
        public int? HoleDefaultKontoIdByAdresse(int adresseId)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = "SELECT DefaultKontoId FROM Adresse WHERE Id = @adr;";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@adr", adresseId);
            var v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? (int?)null : Convert.ToInt32(v);
        }

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

        /// <summary>
        /// Setzt die DefaultKontoId für eine Adresse.
        /// </summary>
        public void SetDefaultKontoFuerAdresse(int adresseId, int kontoId)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = "UPDATE Adresse SET DefaultKontoId = @k WHERE Id = @a;";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@a", adresseId);
            cmd.Parameters.AddWithValue("@k", kontoId);
            cmd.ExecuteNonQuery();
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



        // ---- Kategorie → Konto (persistentes Mapping) ----
        public List<KategorieKontoMapping> LadeKategorieKontoMappings()
        {
            var list = new List<KategorieKontoMapping>();
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"SELECT Id, Kategorie, KontoId FROM KategorieKontoMapping ORDER BY Kategorie";
            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new KategorieKontoMapping
                {
                    Id = r.GetInt32(0),
                    Kategorie = r.GetString(1),
                    KontoId = r.GetInt32(2)
                });
            }
            return list;
        }

        public int? HoleKontoIdFuerKategorie(string? kategorie)
        {
            if (string.IsNullOrWhiteSpace(kategorie)) return null;

            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT TOP 1 KontoId
FROM KategorieKontoMapping
WHERE UPPER(LTRIM(RTRIM(Kategorie))) = UPPER(LTRIM(RTRIM(@kat)))";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@kat", kategorie);

            var v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? (int?)null : Convert.ToInt32(v);
        }

        public void UpsertKategorieKonto(string kategorie, int kontoId)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string check = @"SELECT Id FROM KategorieKontoMapping
                           WHERE UPPER(LTRIM(RTRIM(Kategorie))) = UPPER(LTRIM(RTRIM(@kat)))";
            using var ck = new SqlCommand(check, c);
            ck.Parameters.AddWithValue("@kat", kategorie);
            var idObj = ck.ExecuteScalar();

            if (idObj != null && idObj != DBNull.Value)
            {
                const string upd = @"UPDATE KategorieKontoMapping SET KontoId=@k WHERE Id=@id";
                using var u = new SqlCommand(upd, c);
                u.Parameters.AddWithValue("@k", kontoId);
                u.Parameters.AddWithValue("@id", (int)idObj);
                u.ExecuteNonQuery();
            }
            else
            {
                const string ins = @"INSERT INTO KategorieKontoMapping (Kategorie, KontoId) VALUES (@kat, @k)";
                using var i = new SqlCommand(ins, c);
                i.Parameters.AddWithValue("@kat", kategorie.Trim());
                i.Parameters.AddWithValue("@k", kontoId);
                i.ExecuteNonQuery();
            }
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

        /// Stabiler Hash: Datum|Betrag(2)|Beschreibung|Händler|Kartennummer (Upper/Trim)
        public string BaueImportHash(DateTime datum, decimal betragPositiv, string beschreibung, string? haendler, string? kartennummer)
        {
            var key = string.Join("|",
                datum.ToString("yyyy-MM-dd"),
                betragPositiv.ToString("F2", CultureInfo.InvariantCulture),
                (beschreibung ?? "").Trim().ToUpperInvariant(),
                (haendler ?? "").Trim().ToUpperInvariant(),
                (kartennummer ?? "").Replace(" ", "").ToUpperInvariant()
            );
            return ComputeSha256Hex(key);
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

            // === A) CSV (TOP-Card) ============================================
            if (ext == ".csv")
            {
                // CSV einlesen: 1. Zeile = Header, Trennzeichen ';'
                t = new DataTable();
                // TOP-Card ist oft Latin1/Windows-1252; UTF8 klappt meist auch.
                using (var sr = new StreamReader(filePath, Encoding.Latin1, detectEncodingFromByteOrderMarks: true))
                {
                    string? headerLine = sr.ReadLine();
                    if (string.IsNullOrWhiteSpace(headerLine))
                        return new();

                    var headers = headerLine.Split(';');
                    foreach (var h in headers)
                        t.Columns.Add((h ?? string.Empty).Trim());

                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        var parts = line.Split(';');
                        var row = t.NewRow();
                        for (int i = 0; i < t.Columns.Count && i < parts.Length; i++)
                            row[i] = parts[i];
                        t.Rows.Add(row);
                    }
                }

                // Mapping (inkl. Ableitung Debit/Kredit aus Belastung/Gutschrift)
                t = new CreditCardImportMappingService(this).ApplyMappingIfNeeded(t);
            }
            // === B) Excel (SwissCard Master) ==================================
            else
            {
                // Excel via ExcelDataReader
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(fs);
                var ds = reader.AsDataSet(new ExcelDataReader.ExcelDataSetConfiguration
                {
                    ConfigureDataTable = _ => new ExcelDataReader.ExcelDataTableConfiguration { UseHeaderRow = true }
                });

                if (ds.Tables.Count == 0) return new();
                t = ds.Tables[0];

                // Mapping (Top-Card-Sonderfälle sind hier egal; für xlsx bleibt praktisch alles Master)
                t = new CreditCardImportMappingService(this).ApplyMappingIfNeeded(t);
            }

            // === C) Deine bestehende Logik (unverändert) ======================
            const string COL_DATUM = "Transaktionsdatum";
            const string COL_BESCH = "Beschreibung";
            const string COL_HAEND = "Händler";
            const string COL_KAT = "Händlerkategorie";
            const string COL_BETR = "Betrag";
            const string COL_DK = "Debit/Kredit";
            const string COL_CARD = "Kartennummer"; // optional

            foreach (var col in new[] { COL_DATUM, COL_BESCH, COL_HAEND, COL_KAT, COL_BETR, COL_DK })
                if (!t.Columns.Contains(col))
                    throw new Exception($"Spalte „{col}“ fehlt in der Excel-Datei.");

            var list = new List<CreditCardImportRow>();
            var ciCH = new CultureInfo("de-CH");

            foreach (DataRow r in t.Rows)
            {
                if (!TryGetDate(r[COL_DATUM], out var datum)) continue;
                if (!TryGetDecimal(r[COL_BETR], ciCH, out var betrag)) continue;

                var besch = r[COL_BESCH]?.ToString()?.Trim() ?? "";
                var haend = r[COL_HAEND]?.ToString()?.Trim();
                var kat = r[COL_KAT]?.ToString()?.Trim();
                var dk = r[COL_DK]?.ToString()?.Trim() ?? "";
                var card = t.Columns.Contains(COL_CARD) ? r[COL_CARD]?.ToString()?.Trim() : null;

                var betragPos = Math.Abs(betrag);

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

            static bool TryGetDate(object? v, out DateTime? d)
            {
                d = null;
                if (v == null) return false;
                if (v is DateTime dt) { d = dt; return true; }
                if (DateTime.TryParse(v.ToString(), out var dt2)) { d = dt2; return true; }
                return false;
            }

            static bool TryGetDecimal(object? v, CultureInfo ci, out decimal dec)
            {
                dec = 0m;
                if (v == null) return false;
                if (v is double d) { dec = (decimal)d; return true; }
                if (v is float f) { dec = (decimal)f; return true; }
                if (decimal.TryParse(v.ToString(), NumberStyles.Any, ci, out var dd)) { dec = dd; return true; }
                if (decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out dd)) { dec = dd; return true; }
                return false;
            }
        }

        // ---- Alles-in-einem: Verbuchen (liefert Kennzahlen) ----
        public (int inserted, int skipped, int duplicates) VerbuchenCcStaging(int batchId, int kreditkartenKontoId, int? geldinstitutId)
        {
            int ins = 0, skip = 0, dup = 0;

            using var c = new SqlConnection(_connectionString);
            c.Open();

            // Nur gemappte Zeilen des Batches
            const string sel = @"
            SELECT Id, Datum, Betrag, DebitKredit, Beschreibung, Haendler, Kategorie, Kartennummer, KontoId, ImportHash
            FROM CreditCardImportStaging
            WHERE BatchId=@b AND KontoId IS NOT NULL
            ORDER BY Datum, Id";

            using var cmd = new SqlCommand(sel, c);
            cmd.Parameters.AddWithValue("@b", batchId);

            var rows = new List<(int Id, DateTime Datum, decimal Betrag, string DK, string? Bez, string? H, string? K, string? Card, int KontoId, string? Hash)>();
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
                        r.IsDBNull(9) ? null : r.GetString(9)
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

                var adrId = FindeOderErzeugeAdresseByName(r.H);

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

        public (int inserted, int skipped, int duplicates) VerbucheCreditCardRowsMitKreditkartenkonto(
            IEnumerable<CreditCardImportRow> rows,
            int kreditkartenKontoId,
            int? geldinstitutId = null)
        {
            int inserted = 0, skipped = 0, duplicates = 0;

            foreach (var r in rows)
            {
                // a) nur Zeilen mit Ziel-Konto
                if (!r.KontoId.HasValue) { skipped++; continue; }

                // b) Debit ODER Kredit zulassen (Synonyme)
                var dk = (r.DebitKredit ?? "").Trim();

                bool IstBelastung(string? s)
                {
                    var x = (s ?? "").Trim().ToUpperInvariant();
                    return x is "BELASTUNG" or "DEBIT" or "SOLL" or "CHARGE" or "AUSGABE" or "DEBITO";
                }

                bool IstGutschrift(string? s)
                {
                    var x = (s ?? "").Trim().ToUpperInvariant();
                    return x is "KREDIT" or "GUTSCHRIFT" or "CREDIT" or "CRDT" or "HABEN";
                }

                int? von = null, nach = null;

                if (IstBelastung(dk))
                {
                    // Belastung: Ausgleich vom KK-Konto -> Zielkonto
                    von = kreditkartenKontoId;
                    nach = r.KontoId.Value;
                }
                else if (IstGutschrift(dk))
                {
                    // Gutschrift (Rückzahlung): Zielkonto -> KK-Konto
                    von = r.KontoId.Value;
                    nach = kreditkartenKontoId;
                }
                else
                {
                    skipped++;
                    continue;
                }

                // c) Adresse (nur Name) anlegen, falls Händler vorhanden
                int? adresseId = FindeOderErzeugeAdresseByName(r.Haendler);

                // d) Dedupe per Hash
                var hash = BaueImportHashV2(r.Datum, r.Betrag, r.Beschreibung, r.Haendler, r.Kartennummer, r.DebitKredit);

                if (SucheTransaktionIdByHash(hash).HasValue) { duplicates++; continue; }

                // e) Buchung mit positivem Betrag (r.Betrag ist bereits Math.Abs in deinem Reader)
                InsertTransaktionMitImport(
                    r.Datum,
                    vonKontoId: von,
                    nachKontoId: nach,
                    betragPositiv: r.Betrag,
                    notiz: r.Beschreibung,
                    adresseId: adresseId,
                    geldinstitutId: geldinstitutId,
                    importQuelle: "KreditkartenExcel",
                    importHash: hash);

                inserted++;
            }

            return (inserted, skipped, duplicates);
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
            int ins = 0, skip = 0, dup = 0;

            using var c = new SqlConnection(_connectionString);
            c.Open();

            foreach (var r in rows)
            {
                // Nur Zeilen mit bekanntem Debit/Kredit – beides zulassen
                if (!IstBelastung(r.DebitKredit) && !IstGutschrift(r.DebitKredit)) { skip++; continue; }


                var key = BaueMappingSchluessel(r.Beschreibung, r.Haendler, r.Kategorie);
                var mapKonto = HoleKontoIdFuerMapping(key);

                var hash = BaueImportHashV2(r.Datum, Math.Abs(r.Betrag), r.Beschreibung, r.Haendler, r.Kartennummer, r.DebitKredit);
                // ... diesen hash in die Staging-Zeile schreiben


                // Dedupe über Staging/Archiv/Transaktion
                if (HashExistsAnywhere(c, hash)) { dup++; continue; }

                const string insSql = @"
INSERT INTO CreditCardImportStaging
(BatchId, Datum, Betrag, DebitKredit, Beschreibung, Haendler, Kategorie, Kartennummer, MappingKey, KontoId, ImportHash)
VALUES (@b, @d, @w, @dk, @bez, @h, @kat, @card, @key, @konto, @hash)";
                using var cmd = new SqlCommand(insSql, c);
                cmd.Parameters.AddWithValue("@b", batchId);
                cmd.Parameters.AddWithValue("@d", r.Datum.Date);
                cmd.Parameters.AddWithValue("@w", Math.Abs(r.Betrag));
                cmd.Parameters.AddWithValue("@dk", (object?)r.DebitKredit ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@bez", (object?)r.Beschreibung ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@h", (object?)r.Haendler ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@kat", (object?)r.Kategorie ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@card", (object?)r.Kartennummer ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@key", (object?)key ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@konto", (object?)mapKonto ?? DBNull.Value);
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

        public List<CreditCardImportRow> LadeCcStaging(int? batchId = null)
        {
            var list = new List<CreditCardImportRow>();
            using var c = new SqlConnection(_connectionString);
            c.Open();

            const string sql = @"
SELECT s.Id, s.BatchId, s.Datum, s.Betrag, s.DebitKredit, s.Beschreibung, s.Haendler, s.Kategorie,
       s.Kartennummer, s.KontoId
FROM CreditCardImportStaging s
WHERE (@b IS NULL OR s.BatchId=@b)
ORDER BY s.Datum, s.Id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@b", (object?)batchId ?? DBNull.Value);

            // Für Anzeige: Konto-Labels vorab laden
            var labels = LadeKontoLookup().ToDictionary(x => x.Id, x => x.Anzeige);

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
                    KontoId = r.IsDBNull(9) ? (int?)null : r.GetInt32(9)
                };
                if (row.KontoId.HasValue && labels.TryGetValue(row.KontoId.Value, out var lbl))
                    row.Konto = lbl;

                list.Add(row);
            }
            return list;
        }

        public void UpdateCcStagingZuordnung(int rowId, string mappingKey, int kontoId, bool applyToSameKey)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();
            using var tx = c.BeginTransaction();

            // 1) Row direkt setzen
            using (var u = new SqlCommand("UPDATE CreditCardImportStaging SET KontoId=@k WHERE Id=@id", c, tx))
            {
                u.Parameters.AddWithValue("@k", kontoId);
                u.Parameters.AddWithValue("@id", rowId);
                u.ExecuteNonQuery();
            }

            // 2) Optional alle offenen mit gleichem Key mitziehen
            if (applyToSameKey && !string.IsNullOrWhiteSpace(mappingKey))
            {
                using var u2 = new SqlCommand(
                    "UPDATE CreditCardImportStaging SET KontoId=@k WHERE MappingKey=@key AND KontoId IS NULL", c, tx);
                u2.Parameters.AddWithValue("@k", kontoId);
                u2.Parameters.AddWithValue("@key", mappingKey);
                u2.ExecuteNonQuery();
            }

            tx.Commit();
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

        public int ClearCcStaging(int batchId)
        {
            using var c = new SqlConnection(_connectionString);
            c.Open();
            using var cmd = new SqlCommand("DELETE FROM CreditCardImportStaging WHERE BatchId=@b", c);
            cmd.Parameters.AddWithValue("@b", batchId);
            return cmd.ExecuteNonQuery();
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
SELECT t.Id, t.Datum, t.VonKontoId, t.NachKontoId, t.Betrag, t.Notiz,
       t.AdresseId, a.Name as AdresseName,
       t.GeldinstitutId, g.Name as BankName,
       t.ImportQuelle
FROM Transaktion t
LEFT JOIN Adresse a      ON a.Id = t.AdresseId
LEFT JOIN Geldinstitut g ON g.Id = t.GeldinstitutId
WHERE (t.VonKontoId = @kto OR t.NachKontoId = @kto)
  AND (@von IS NULL OR t.Datum >= @von)
  AND (@bis IS NULL OR t.Datum <= @bis)
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
                    VonKontoId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                    NachKontoId = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    Betrag = r.GetDecimal(4),
                    Notiz = r.IsDBNull(5) ? null : r.GetString(5),
                    AdresseId = r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
                    AdresseName = r.IsDBNull(7) ? null : r.GetString(7),
                    GeldinstitutId = r.IsDBNull(8) ? (int?)null : r.GetInt32(8),
                    BankName = r.IsDBNull(9) ? null : r.GetString(9),
                    ImportQuelle = r.IsDBNull(10) ? null : r.GetString(10)
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



        /// <summary>
        /// Liefert true, wenn diese Transaktion für das angegebene Konto als "Ausgabe" angezeigt werden soll.
        /// Standard: Von=Konto -> Ausgabe, Nach=Konto -> Einnahme.
        /// Spezialfall: KreditkartenExcel-Import -> auf Nicht-Einnahmenkonten stets Ausgabe (auch wenn Nach=Konto).
        /// </summary>
        public bool IstAusgabeFuerKonto(int kontoId, Transaktion t, string? importQuelle)
        {
            // 1) Kreditkarten-Detailbuchungen (Konto->Konto-Umbuchung der KK-Verteilung)
            if (string.Equals(importQuelle, "KreditkartenExcel", StringComparison.OrdinalIgnoreCase))
            {
                // Wenn das angefragte Konto kein Einnahmenkonto ist, als Ausgabe darstellen.
                // (Dein Budget-/Kostenkonto bleibt damit visuell "Ausgabe", auch wenn es Nach=Konto ist.)
                if (!IstEinnahmenKonto(kontoId))
                    return true;
                // Einnahmenkonten (z. B. Lohn) dürfen bei KK-Gutschriften als Einnahme erscheinen:
                if (t.NachKontoId == kontoId) return false;
                if (t.VonKontoId == kontoId) return true;
                // Fallback
                return false;
            }

            // 2) Standardfall (ohne Import-Speziallogik)
            if (t.VonKontoId == kontoId) return true;   // Ausgabe
            if (t.NachKontoId == kontoId) return false; // Einnahme

            // Fallback: wenn das Konto grundsätzlich ein Ausgabenkonto ist, als Ausgabe darstellen
            return !IstEinnahmenKonto(kontoId);
        }






        public bool IstAusgabenKonto(int kontoId) => !IstEinnahmenKonto(kontoId);

        public void EnsureNumberRangeRulesTable()
        {
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();

            // 1) Tabelle anlegen (falls nicht vorhanden) – unverändert bis auf expliziten Constraint-Namen
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
        CONSTRAINT CK_NumberRangeRules_Range CHECK ([RangeStart] <= [RangeEnd])
    );
END";
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(createSql, c))
                cmd.ExecuteNonQuery();

            // 2) Sicherstellen: Spalte Bezeichnung existiert
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

            // 3) **WICHTIG**: CHECK-Constraint für Richtung auf (Ausgabe, Einnahme, Neutral) bringen
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
            const string sql = @"SELECT Id, RangeStart, RangeEnd, Richtung, Bezeichnung, IstBudgetkonto
                         FROM dbo.NumberRangeRules
                         ORDER BY RangeStart, RangeEnd";
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
                    IstBudgetkonto = r.GetBoolean(5)
                });
            }
            return list;
        }

        public int SpeichereNummernRegel(NumberRangeRule rule)
        {
            EnsureNumberRangeRulesTable();
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();
            const string sql = @"INSERT INTO dbo.NumberRangeRules (RangeStart, RangeEnd, Richtung, Bezeichnung, IstBudgetkonto)
                         OUTPUT INSERTED.Id
                         VALUES (@s, @e, @r, @b, @flag)";
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@s", rule.RangeStart);
            cmd.Parameters.AddWithValue("@e", rule.RangeEnd);
            cmd.Parameters.AddWithValue("@r", rule.Richtung);
            cmd.Parameters.AddWithValue("@b", (object?)rule.Bezeichnung ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@flag", rule.IstBudgetkonto);
            return (int)cmd.ExecuteScalar();
        }

        public void AktualisiereNummernRegel(NumberRangeRule rule)
        {
            EnsureNumberRangeRulesTable();
            using var c = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            c.Open();
            const string sql = @"UPDATE dbo.NumberRangeRules
                         SET RangeStart=@s, RangeEnd=@e, Richtung=@r, Bezeichnung=@b, IstBudgetkonto=@flag
                         WHERE Id=@id";
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@s", rule.RangeStart);
            cmd.Parameters.AddWithValue("@e", rule.RangeEnd);
            cmd.Parameters.AddWithValue("@r", rule.Richtung);
            cmd.Parameters.AddWithValue("@b", (object?)rule.Bezeichnung ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@flag", rule.IstBudgetkonto);
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

        public void AssertNumberRangeRulesSchema()
        {
            // 1) Erstellen/Erweitern versuchen
            EnsureNumberRangeRulesTable();
            // 2) Hart prüfen
            if (!NumberRangeRulesHasBezeichnung())
                throw new Exception("Spalte 'Bezeichnung' fehlt weiterhin in dbo.NumberRangeRules. " +
                                    "Prüfe Datenbank/Connection und Rechte.");
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

            if (adresseId <= 0) return result;

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            const string sql = @"
SELECT 
    t.Id, 
    t.Datum, 
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
  AND (@von IS NULL OR t.Datum >= @von)
  AND (@bis IS NULL OR t.Datum <= @bis)
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
                    VonKontoId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                    NachKontoId = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    Betrag = r.GetDecimal(4),
                    Notiz = r.IsDBNull(5) ? null : r.GetString(5),
                    AdresseId = r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
                    AdresseName = r.IsDBNull(7) ? null : r.GetString(7),
                    GeldinstitutId = r.IsDBNull(8) ? (int?)null : r.GetInt32(8),
                    BankName = r.IsDBNull(9) ? null : r.GetString(9),
                    ImportQuelle = r.IsDBNull(10) ? null : r.GetString(10)
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

        // ---------------------------------------------
        // SCHRITT A – Schema-Erweiterung OCR/Textindex
        // ---------------------------------------------

        /// <summary>
        /// Führt alle idempotenten Checks aus, um die für Anhänge/OCR benötigten DB-Objekte bereitzustellen.
        /// </summary>
        public void EnsureAttachmentsSchema()
        {
            EnsureAppSettingsTable();
            EnsureAttachmentsTable();
            EnsureAttachmentTextTable(); // NEU: Textindex pro Attachment
        }

        /// <summary>
        /// Legt die AttachmentText-Tabelle an, falls nicht vorhanden:
        /// AttachmentText (AttachmentId PK/FK, Text NVARCHAR(MAX), Lang NVARCHAR(16), ExtractedAtUtc DATETIME2)
        /// </summary>
        private void EnsureAttachmentTextTable()
        {
            using var conn = CreateConnection();
            conn.Open();

            var sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AttachmentText' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.AttachmentText
    (
        AttachmentId   INT NOT NULL CONSTRAINT PK_AttachmentText PRIMARY KEY,
        [Text]         NVARCHAR(MAX) NULL,
        [Lang]         NVARCHAR(16) NULL,
        ExtractedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_AttachmentText_Extracted DEFAULT SYSUTCDATETIME()
    );

    -- optionaler FK
    -- ALTER TABLE dbo.AttachmentText
    -- ADD CONSTRAINT FK_AttachmentText_Attachment
    -- FOREIGN KEY (AttachmentId) REFERENCES dbo.Attachment(Id) ON DELETE CASCADE;
END;
";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Liefert Tesseract-Pfad (exe) und Languages (Default: deu+eng), aus AppSettings.
        /// </summary>
        public (string TesseractExe, string Languages) GetOcrSettings()
        {
            string exe = GetAppSetting("TesseractExePath") ?? "";
            string langs = GetAppSetting("TesseractLanguages") ?? "deu+eng";
            return (exe, langs);
        }

        /// <summary>
        /// Legt oder aktualisiert den Textindex für ein Attachment.
        /// </summary>
        public void UpsertAttachmentText(int attachmentId, string? text, string? lang)
        {
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
MERGE dbo.AttachmentText AS target
USING (SELECT @id AS AttachmentId) AS src
ON (target.AttachmentId = src.AttachmentId)
WHEN MATCHED THEN
    UPDATE SET [Text] = @t, [Lang] = @l, ExtractedAtUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (AttachmentId, [Text], [Lang]) VALUES (@id, @t, @l);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.Parameters.AddWithValue("@t", (object?)text ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l", (object?)lang ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }


        /// <summary>
        /// Legt eine sehr einfache Key/Value-Tabelle an, falls nicht vorhanden:
        /// AppSetting (Key NVARCHAR(64) PK, Value NVARCHAR(512) NULL)
        /// </summary>
        private void EnsureAppSettingsTable()
        {
            using (var conn = CreateConnection())
            {
                conn.Open();

                // Tabelle anlegen, falls nicht vorhanden
                var sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AppSetting' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.AppSetting
    (
        [Key]   NVARCHAR(64)  NOT NULL CONSTRAINT PK_AppSetting PRIMARY KEY,
        [Value] NVARCHAR(512) NULL
    );
END;
";
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Setzt (insert/update) ein AppSetting. Wenn value == null, wird der Eintrag gelöscht.
        /// </summary>
        public void SetAppSetting(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key darf nicht leer sein.", nameof(key));

            using (var conn = CreateConnection())
            {
                conn.Open();

                if (value == null)
                {
                    var del = "DELETE FROM dbo.AppSetting WHERE [Key] = @k;";
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = del;
                        var p = cmd.CreateParameter();
                        p.ParameterName = "@k";
                        p.Value = key;
                        cmd.Parameters.Add(p);
                        cmd.ExecuteNonQuery();
                    }
                    return;
                }

                var upsert = @"
MERGE dbo.AppSetting AS target
USING (SELECT @k AS [Key], @v AS [Value]) AS src
ON target.[Key] = src.[Key]
WHEN MATCHED THEN UPDATE SET [Value] = src.[Value]
WHEN NOT MATCHED THEN INSERT ([Key],[Value]) VALUES (src.[Key], src.[Value]);
";
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = upsert;

                    var p1 = cmd.CreateParameter();
                    p1.ParameterName = "@k";
                    p1.Value = key;
                    cmd.Parameters.Add(p1);

                    var p2 = cmd.CreateParameter();
                    p2.ParameterName = "@v";
                    p2.Value = (object)value ?? DBNull.Value;
                    cmd.Parameters.Add(p2);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Liest ein AppSetting. Gibt null zurück, wenn Key nicht existiert.
        /// </summary>
        public string? GetAppSetting(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key darf nicht leer sein.", nameof(key));

            using (var conn = CreateConnection())
            {
                conn.Open();
                var sel = "SELECT [Value] FROM dbo.AppSetting WHERE [Key] = @k;";
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sel;
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@k";
                    p.Value = key;
                    cmd.Parameters.Add(p);

                    var obj = cmd.ExecuteScalar();
                    if (obj == null || obj == DBNull.Value) return null;
                    return Convert.ToString(obj);
                }
            }
        }

        /// <summary>
        /// Legt die Attachment-Tabelle an, falls nicht vorhanden:
        /// Attachment (Id, TransaktionId, FileName, OriginalName, FolderRel, SizeBytes, ImportedAtUtc, OcrStatus)
        /// </summary>
        private void EnsureAttachmentsTable()
        {
            using (var conn = CreateConnection())
            {
                conn.Open();

                // 1) Tabelle anlegen (idempotent)
                var createSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Attachment' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.Attachment
    (
        Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Attachment PRIMARY KEY,
        TransaktionId  INT NOT NULL,
        FileName       NVARCHAR(260) NOT NULL, -- gespeicherter Dateiname im Zielordner
        OriginalName   NVARCHAR(260) NULL,     -- Quellname beim Import
        FolderRel      NVARCHAR(128) NOT NULL, -- z. B. '2025\10'
        SizeBytes      BIGINT NULL,
        ImportedAtUtc  DATETIME2 NOT NULL CONSTRAINT DF_Attachment_ImportedAt DEFAULT SYSUTCDATETIME(),
        OcrStatus      NVARCHAR(16) NULL       -- 'Text' | 'Image' | 'OCR' | 'Error'
    );

    -- optionaler FK (ohne CASCADE, damit Löschen von Transaktionen nicht blockiert,
    -- sondern app-seitig gesteuert werden kann):
    -- ALTER TABLE dbo.Attachment
    -- ADD CONSTRAINT FK_Attachment_Transaktion
    -- FOREIGN KEY (TransaktionId) REFERENCES dbo.Transaktion(Id);
END;
";

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = createSql;
                    cmd.ExecuteNonQuery();
                }

                // 2) Sinnvolle Indizes (idempotent)
                var indexSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Attachment_TransaktionId' AND object_id = OBJECT_ID('dbo.Attachment'))
BEGIN
    CREATE INDEX IX_Attachment_TransaktionId ON dbo.Attachment(TransaktionId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Attachment_FolderRel' AND object_id = OBJECT_ID('dbo.Attachment'))
BEGIN
    CREATE INDEX IX_Attachment_FolderRel ON dbo.Attachment(FolderRel);
END;
";
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = indexSql;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ================== ATTACHMENTS: SETTINGS & QUERIES ==================

        /// <summary>
        /// Liefert Root-Pfad und Max-MB aus AppSetting.
        /// Fallbacks: Root = %USERPROFILE%\Dokumente\MyCoinFlow\Attachments; MaxMb = 20.
        /// </summary>
        public (string Root, int MaxMb) GetAttachmentSettings()
        {
            string? root = GetAppSetting("AttachmentRoot");
            string? max = GetAppSetting("AttachmentMaxMB");

            if (string.IsNullOrWhiteSpace(root))
            {
                var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                root = Path.Combine(doc, "MyCoinFlow", "Attachments");
            }

            int maxMb = 20;
            if (!string.IsNullOrWhiteSpace(max) && int.TryParse(max, out var parsed) && parsed >= 1 && parsed <= 1024)
                maxMb = parsed;

            return (root, maxMb);
        }

        /// <summary>
        /// Liest den ImportHash aus Transaktion, falls vorhanden; sonst null.
        /// </summary>
        public string? GetImportHashForTransaktion(int transaktionId)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"SELECT ImportHash FROM dbo.Transaktion WHERE Id=@id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", transaktionId);
            var obj = cmd.ExecuteScalar();
            if (obj == null || obj == DBNull.Value) return null;
            return Convert.ToString(obj);
        }

        /// <summary>
        /// True, wenn zu einer Transaktion mindestens ein Attachment existiert.
        /// </summary>
        public bool HasAttachments(int transaktionId)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"SELECT TOP(1) 1 FROM dbo.Attachment WHERE TransaktionId=@id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", transaktionId);
            var v = cmd.ExecuteScalar();
            return v != null && v != DBNull.Value;
        }

        /// <summary>
        /// Legt einen Attachment-Datensatz an. Gibt die neue Id zurück.
        /// </summary>
        public int SaveAttachment(int transaktionId, string fileName, string? originalName, string folderRel, long? sizeBytes, string? ocrStatus)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"
INSERT INTO dbo.Attachment (TransaktionId, FileName, OriginalName, FolderRel, SizeBytes, OcrStatus)
VALUES (@t, @f, @o, @folder, @sz, @ocr);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@t", transaktionId);
            cmd.Parameters.AddWithValue("@f", fileName);
            cmd.Parameters.AddWithValue("@o", (object?)originalName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@folder", folderRel);
            cmd.Parameters.AddWithValue("@sz", (object?)sizeBytes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ocr", (object?)ocrStatus ?? DBNull.Value);

            var idObj = cmd.ExecuteScalar();
            return (idObj is int i) ? i : Convert.ToInt32(idObj);
        }

        /// <summary>
        /// Liefert alle Attachments zu einer Transaktion (Id, FileName, FolderRel).
        /// </summary>
        public List<(int Id, string FileName, string FolderRel)> LoadAttachmentsByTransaktionId(int transaktionId)
        {
            var list = new List<(int Id, string FileName, string FolderRel)>();
            using var c = CreateConnection();
            c.Open();
            const string sql = @"SELECT Id, FileName, FolderRel FROM dbo.Attachment WHERE TransaktionId=@id ORDER BY Id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", transaktionId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((r.GetInt32(0), r.GetString(1), r.GetString(2)));
            }
            return list;
        }

        /// <summary>
        /// Liefert detaillierte Attachmentdaten je Transaktion (für Tooltip/Dialog).
        /// </summary>
        public List<(int Id, string FileName, string FolderRel, string? OcrStatus, long? SizeBytes, string? OriginalName)>
            LoadAttachmentDetailsByTransaktionId(int transaktionId)
        {
            var list = new List<(int, string, string, string?, long?, string?)>();
            using var c = CreateConnection();
            c.Open();
            const string sql = @"
SELECT Id, FileName, FolderRel, OcrStatus, SizeBytes, OriginalName
FROM dbo.Attachment
WHERE TransaktionId=@id
ORDER BY Id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", transaktionId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((
                    r.GetInt32(0),
                    r.GetString(1),
                    r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? (long?)null : r.GetInt64(4),
                    r.IsDBNull(5) ? null : r.GetString(5)
                ));
            }
            return list;
        }

        /// <summary>
        /// Load by Id – minimal für Delete / Open.
        /// </summary>
        public (int Id, int TransaktionId, string FileName, string FolderRel)? GetAttachmentById(int attachmentId)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"SELECT Id, TransaktionId, FileName, FolderRel FROM dbo.Attachment WHERE Id=@id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                return (r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3));
            }
            return null;
        }

        /// <summary>
        /// Löscht einen Attachment-Datensatz inkl. zugehörigem Textindex (AttachmentText) in einer Transaktion.
        /// </summary>
        public void DeleteAttachment(int attachmentId)
        {
            using var c = CreateConnection();
            c.Open();
            using var tx = c.BeginTransaction();

            try
            {
                // Textindex zuerst entfernen (falls vorhanden)
                using (var cmdTxt = new SqlCommand("DELETE FROM dbo.AttachmentText WHERE AttachmentId = @id;", c, tx))
                {
                    cmdTxt.Parameters.AddWithValue("@id", attachmentId);
                    cmdTxt.ExecuteNonQuery();
                }

                // Attachment löschen
                using (var cmdAtt = new SqlCommand("DELETE FROM dbo.Attachment WHERE Id = @id;", c, tx))
                {
                    cmdAtt.Parameters.AddWithValue("@id", attachmentId);
                    cmdAtt.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { /* ignore */ }
                throw;
            }
        }


        /// <summary>
        /// Setzt den OCR-Status eines Attachments (z. B. "Text", "Image", "OCR", "Error").
        /// </summary>
        public void UpdateAttachmentOcrStatus(int attachmentId, string? status)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"UPDATE dbo.Attachment SET OcrStatus = @s WHERE Id = @id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.Parameters.AddWithValue("@s", (object?)status ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        /// <summary>
        /// Liefert Kennzahlen zur aktuellen DB: Name, Daten-Dateigröße (MB, ohne Log),
        /// Express-Max (MB), sowie Counts/Bytes für Attachments/Index.
        /// </summary>
        public (string DatabaseName, double DataSizeMB, double DataMaxMB, int AttachmentCount, int AttachmentTextCount, long AttachmentTextBytes)
            GetDatabaseStats()
        {
            using var c = CreateConnection();
            c.Open();

            string dbName = "Unbekannt";
            double dataMb = 0;
            double maxMb = 10240; // SQL Express/LocalDB: 10 GB Daten-Limit
            int attachCount = 0;
            int attachTextCount = 0;
            long attachTextBytes = 0;

            // DB-Name
            using (var cmd = new SqlCommand("SELECT DB_NAME();", c))
            {
                var obj = cmd.ExecuteScalar();
                if (obj != null && obj != DBNull.Value) dbName = Convert.ToString(obj) ?? dbName;
            }

            // Daten-Dateigröße (nur ROWS, ohne Log)
            const string sizeSql = @"
SELECT CAST(SUM(size) * 8.0 / 1024.0 AS FLOAT) AS DataSizeMB
FROM sys.database_files
WHERE type_desc = 'ROWS';";
            using (var cmd = new SqlCommand(sizeSql, c))
            {
                var obj = cmd.ExecuteScalar();
                if (obj != null && obj != DBNull.Value) dataMb = Convert.ToDouble(obj);
            }

            // Sicherstellen, dass Tabellen existieren (idempotent, billig)
            try { EnsureAttachmentsSchema(); } catch { /* still */ }

            // Counts
            try
            {
                using var cmd1 = new SqlCommand("SELECT COUNT(*) FROM dbo.Attachment;", c);
                var o1 = cmd1.ExecuteScalar();
                attachCount = (o1 == null || o1 == DBNull.Value) ? 0 : Convert.ToInt32(o1);
            }
            catch { /* Tabelle evtl. nicht vorhanden */ }

            try
            {
                using var cmd2 = new SqlCommand("SELECT COUNT(*), SUM(DATALENGTH([Text])) FROM dbo.AttachmentText;", c);
                using var r = cmd2.ExecuteReader();
                if (r.Read())
                {
                    attachTextCount = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                    attachTextBytes = r.IsDBNull(1) ? 0L : Convert.ToInt64(r.GetValue(1));
                }
            }
            catch { /* Tabelle evtl. nicht vorhanden */ }

            return (dbName, dataMb, maxMb, attachCount, attachTextCount, attachTextBytes);
        }
        /// <summary>
        /// Liefert Attachments, die (noch) keinen Textindex haben:
        /// AttachmentText fehlt oder ist leer. Rückgabe: (Id, FileName, FolderRel, OcrStatus)
        /// </summary>
        public List<(int Id, string FileName, string FolderRel, string? OcrStatus)> LoadAttachmentsNeedingIndex()
        {
            var list = new List<(int, string, string, string?)>();
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT a.Id, a.FileName, a.FolderRel, a.OcrStatus
FROM dbo.Attachment a
LEFT JOIN dbo.AttachmentText t ON t.AttachmentId = a.Id
WHERE t.AttachmentId IS NULL OR t.[Text] IS NULL OR LTRIM(RTRIM(t.[Text])) = '' 
ORDER BY a.Id";

            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((r.GetInt32(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));
            }
            return list;
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
       t.Id, t.Datum, t.VonKontoId, t.NachKontoId,
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

            if (vonDatum.HasValue) sb.AppendLine("  AND t.Datum >= @von");
            if (bisDatum.HasValue) sb.AppendLine("  AND t.Datum <= @bis");
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
                    VonKontoId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                    NachKontoId = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    Betrag = r.GetDecimal(4),
                    Notiz = r.IsDBNull(5) ? null : r.GetString(5),
                    AdresseId = r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
                    AdresseName = r.IsDBNull(7) ? null : r.GetString(7),
                    GeldinstitutId = r.IsDBNull(8) ? (int?)null : r.GetInt32(8),
                    BankName = r.IsDBNull(9) ? null : r.GetString(9),
                    ImportQuelle = r.IsDBNull(10) ? null : r.GetString(10)
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
WHERE (@von IS NULL OR t.Datum >= @von)
  AND (@bis IS NULL OR t.Datum <= @bis)
  AND (kv.Kontonummer = @knr OR kn.Kontonummer = @knr);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@knr", kontonummer);
            cmd.Parameters.AddWithValue("@von", (object?)von ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bis", (object?)bis ?? DBNull.Value);
            var o = cmd.ExecuteScalar();
            return o != null && o != DBNull.Value;
        }

        public bool KontoHatBuchungenImZeitraumByKontoId(int kontoId, DateTime? von, DateTime? bis)
        {
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT TOP(1) 1
FROM dbo.Transaktion t
WHERE (@von IS NULL OR t.Datum >= @von)
  AND (@bis IS NULL OR t.Datum <= @bis)
  AND (t.VonKontoId = @id OR t.NachKontoId = @id);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", kontoId);
            cmd.Parameters.AddWithValue("@von", (object?)von ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bis", (object?)bis ?? DBNull.Value);
            var o = cmd.ExecuteScalar();
            return o != null && o != DBNull.Value;
        }

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
        Notiz          NVARCHAR(200) NULL,
        CreatedAtUtc   DATETIME2 NOT NULL CONSTRAINT DF_StweZaehler_Created DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_StweZaehler_Liegenschaft
            FOREIGN KEY (LiegenschaftId) REFERENCES dbo.StweLiegenschaft(Id),

        CONSTRAINT FK_StweZaehler_Einheit
            FOREIGN KEY (EinheitId) REFERENCES dbo.StweEinheit(Id)
    );

    CREATE INDEX IX_StweZaehler_LiegenschaftId ON dbo.StweZaehler(LiegenschaftId);
    CREATE INDEX IX_StweZaehler_EinheitId      ON dbo.StweZaehler(EinheitId);
END;

-- ------------------------------------------------------------
-- ENERGIE: Zählerstände je Set (Alt/Neu)
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
        Notiz            NVARCHAR(200) NULL,
        UpdatedAtUtc     DATETIME2 NOT NULL CONSTRAINT DF_StweZaehlerdatenSet_Updated DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_StweZaehlerdatenSet_Liegenschaft
            FOREIGN KEY (LiegenschaftId) REFERENCES dbo.StweLiegenschaft(Id)
    );

    CREATE INDEX IX_StweZaehlerdatenSet_Lid_Am ON dbo.StweZaehlerdatenSet(LiegenschaftId, ErfasstAm DESC, Id DESC);
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
            // Wir nutzen die bestehende Tabelle Transaktion.
            // Minimal: letzte N Transaktionen für Auswahl im Dialog.
            var list = new List<MyCoinFlow.Models.Transaktion>();

            using var c = CreateConnection();
            c.Open();

            var sql = $@"
SELECT TOP ({top})
    t.Id, t.Datum, t.VonKontoId, t.NachKontoId,
    t.Betrag, t.Notiz,
    t.AdresseId, a.Name as AdresseName,
    t.GeldinstitutId, g.Name as BankName,
    t.ImportQuelle
FROM dbo.Transaktion t
LEFT JOIN dbo.Adresse a      ON a.Id = t.AdresseId
LEFT JOIN dbo.Geldinstitut g ON g.Id = t.GeldinstitutId
ORDER BY t.Datum DESC, t.Id DESC;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.Transaktion
                {
                    Id = r.GetInt32(0),
                    Datum = r.GetDateTime(1),
                    VonKontoId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                    NachKontoId = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    Betrag = r.GetDecimal(4),
                    Notiz = r.IsDBNull(5) ? null : r.GetString(5),
                    AdresseId = r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
                    AdresseName = r.IsDBNull(7) ? null : r.GetString(7),
                    GeldinstitutId = r.IsDBNull(8) ? (int?)null : r.GetInt32(8),
                    BankName = r.IsDBNull(9) ? null : r.GetString(9),
                    ImportQuelle = r.IsDBNull(10) ? null : r.GetString(10)
                });
            }

            return list;
        }

        public bool StweAutoDetectIsCreditForTransaktion(int transaktionId)
        {
            using var c = CreateConnection();
            c.Open();

            // Regel:
            // 1) VonKontoId != NULL => Gutschrift (Rückvergütung etc.)
            // 2) sonst wenn Adresse.IstBudgetiert = 1 => Einzahlung Eigentümer
            // 3) sonst => Belastung (Rechnung)
            const string sql = @"
SELECT TOP(1)
    CASE 
        WHEN t.VonKontoId IS NOT NULL THEN 1
        WHEN ISNULL(a.IstBudgetiert, 0) = 1 THEN 1
        ELSE 0
    END AS IsCredit
FROM dbo.Transaktion t
LEFT JOIN dbo.Adresse a ON a.Id = t.AdresseId
WHERE t.Id = @id;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", transaktionId);

            var v = cmd.ExecuteScalar();
            return v != null && v != DBNull.Value && Convert.ToInt32(v) == 1;
        }

        public int StweSetInsert(int liegenschaftId, int transaktionId, string? titel)
        {
            EnsureStweSchema();

            bool isCredit = false;
            try
            {
                isCredit = StweAutoDetectIsCreditForTransaktion(transaktionId);
            }
            catch
            {
                // defensiv: Default bleibt Belastung
                isCredit = false;
            }

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
INSERT INTO dbo.StweSet (LiegenschaftId, TransaktionId, Titel, IsCredit)
OUTPUT INSERTED.Id
VALUES (@lid, @tid, @t, @ic);";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;

            cmd.Parameters.AddWithValue("@lid", liegenschaftId);
            cmd.Parameters.AddWithValue("@tid", transaktionId);
            cmd.Parameters.AddWithValue("@t", (object?)titel ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ic", isCredit ? 1 : 0);

            return Convert.ToInt32(cmd.ExecuteScalar());
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
SELECT l.Id, l.SchluesselId, l.EigentuemerId, e.Name, l.AnteilProzent
FROM dbo.StweSchluesselLine l
JOIN dbo.StweEigentuemer e ON e.Id = l.EigentuemerId
WHERE l.SchluesselId = @sid
ORDER BY e.Name;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            var p = cmd.CreateParameter(); p.ParameterName = "@sid"; p.Value = schluesselId; cmd.Parameters.Add(p);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new MyCoinFlow.Models.StweSchluesselLine
                {
                    Id = r.GetInt32(0),
                    SchluesselId = r.GetInt32(1),
                    EigentuemerId = r.GetInt32(2),
                    EigentuemerName = r.GetString(3),
                    AnteilProzent = r.GetDecimal(4)
                });
            }

            return list;
        }

        public void StweSchluesselLinesReplace(int schluesselId, List<(int EigentuemerId, decimal AnteilProzent)> lines)
        {
            EnsureStweSchema();

            if (schluesselId <= 0) throw new ArgumentOutOfRangeException(nameof(schluesselId));
            lines ??= new();

            using var c = CreateConnection();
            c.Open();
            using var tx = c.BeginTransaction();

            try
            {
                // delete old
                using (var del = c.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM dbo.StweSchluesselLine WHERE SchluesselId = @sid;";
                    var p = del.CreateParameter(); p.ParameterName = "@sid"; p.Value = schluesselId; del.Parameters.Add(p);
                    del.ExecuteNonQuery();
                }

                // insert new
                foreach (var (oid, pct) in lines)
                {
                    using var ins = c.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = @"
INSERT INTO dbo.StweSchluesselLine (SchluesselId, EigentuemerId, AnteilProzent)
VALUES (@sid, @oid, @pct);";

                    var p1 = ins.CreateParameter(); p1.ParameterName = "@sid"; p1.Value = schluesselId; ins.Parameters.Add(p1);
                    var p2 = ins.CreateParameter(); p2.ParameterName = "@oid"; p2.Value = oid; ins.Parameters.Add(p2);
                    var p3 = ins.CreateParameter(); p3.ParameterName = "@pct"; p3.Value = pct; ins.Parameters.Add(p3);

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
SELECT Id, LiegenschaftId, Name, Typ, EinheitId, Notiz
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
                    Notiz = r.IsDBNull(5) ? null : r.GetString(5)
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
INSERT INTO dbo.StweZaehler (LiegenschaftId, Name, Typ, EinheitId, Notiz)
OUTPUT INSERTED.Id
VALUES (@lid, @n, @t, @eid, @no);";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@lid", z.LiegenschaftId);
            cmd.Parameters.AddWithValue("@n", z.Name.Trim());
            cmd.Parameters.AddWithValue("@t", z.Typ);
            cmd.Parameters.AddWithValue("@eid", (object?)z.EinheitId ?? DBNull.Value);
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
    Notiz          = @no
WHERE Id = @id;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", z.Id);
            cmd.Parameters.AddWithValue("@lid", z.LiegenschaftId);
            cmd.Parameters.AddWithValue("@n", z.Name.Trim());
            cmd.Parameters.AddWithValue("@t", z.Typ);
            cmd.Parameters.AddWithValue("@eid", (object?)z.EinheitId ?? DBNull.Value);
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

        public void StweZaehlerDelete(int id)
        {
            EnsureStweSchema();

            if (id <= 0) return;

            // Harte Regel: nicht löschen, wenn bereits in Energie-Sets verwendet
            if (StweZaehlerUsedInEnergieSets(id))
            {
                System.Windows.MessageBox.Show(
                    "Dieser Zähler kann nicht gelöscht werden,\n" +
                    "weil er bereits in Energie-Sets (Zählerständen) verwendet wird.",
                    "Löschen nicht möglich",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            using var c = CreateConnection();
            c.Open();

            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM dbo.StweZaehler WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
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
    SUM(l.Betrag) AS Summe
FROM dbo.StweSetLine l
JOIN dbo.StweSet s           ON s.Id = l.SetId
JOIN dbo.Transaktion t       ON t.Id = s.TransaktionId
JOIN dbo.StweEigentuemer o   ON o.Id = l.EigentuemerId
WHERE s.LiegenschaftId = @lid
  AND (@von IS NULL OR t.Datum >= @von)
  AND (@bis IS NULL OR t.Datum <= @bis)
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
                    Summe = r.GetDecimal(2)
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
    s.TransaktionId,
    COALESCE(NULLIF(s.Titel,''), COALESCE(NULLIF(t.Notiz,''),'(ohne Text)')) AS Titel,
    l.Schluessel,
    l.Notiz,
    l.Betrag
FROM dbo.StweSetLine l
JOIN dbo.StweSet s      ON s.Id = l.SetId
JOIN dbo.Transaktion t  ON t.Id = s.TransaktionId
WHERE s.LiegenschaftId = @lid
  AND l.EigentuemerId = @oid
  AND (@von IS NULL OR t.Datum >= @von)
  AND (@bis IS NULL OR t.Datum <= @bis)
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
                    TransaktionId = r.GetInt32(2),
                    Titel = r.GetString(3),
                    Schluessel = r.IsDBNull(4) ? null : r.GetString(4),
                    Notiz = r.IsDBNull(5) ? null : r.GetString(5),
                    Betrag = r.GetDecimal(6)
                });
            }

            return list;
        }

        public List<StweOriginalTransaktionRow> StweReportOriginalTransaktionen(int liegenschaftId, DateTime? von, DateTime? bis)
        {
            EnsureStweSchema();

            var list = new List<StweOriginalTransaktionRow>();

            // SIGNED: Wenn Set.IsCredit=1 => Betrag negativ
            const string sql = @"
SELECT DISTINCT
    t.Id            AS TransaktionsId,
    t.Datum         AS Datum,
    CASE WHEN ISNULL(s.IsCredit, 0) = 1 THEN -t.Betrag ELSE t.Betrag END AS BetragSigned,
    t.Notiz         AS Notiz
FROM dbo.StweSet s
INNER JOIN dbo.Transaktion t ON t.Id = s.TransaktionId
WHERE s.LiegenschaftId = @LiegenschaftId
  AND (@Von IS NULL OR t.Datum >= @Von)
  AND (@Bis IS NULL OR t.Datum <= @Bis)
ORDER BY t.Datum DESC, t.Id DESC;";

            using var con = new SqlConnection(_connectionString);
            con.Open();

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@LiegenschaftId", liegenschaftId);
            cmd.Parameters.AddWithValue("@Von", (object?)von ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Bis", (object?)bis ?? DBNull.Value);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new StweOriginalTransaktionRow
                {
                    TransaktionsId = r.GetInt32(0),
                    Datum = r.GetDateTime(1),
                    Betrag = r.GetDecimal(2),          // jetzt SIGNED
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
  AND (@von IS NULL OR t.Datum >= @von)
  AND (@bis IS NULL OR t.Datum <= @bis)
ORDER BY t.Datum DESC, s.Id DESC;";

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
SELECT Id, LiegenschaftId, ErfasstAm, RechnungKwhTotal, GutschriftChf, Notiz
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
                    Notiz = r.IsDBNull(5) ? null : r.GetString(5)
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
INSERT INTO dbo.StweZaehlerdatenSet (LiegenschaftId, ErfasstAm, RechnungKwhTotal, GutschriftChf, Notiz)
OUTPUT INSERTED.Id
VALUES (@lid, @am, @rk, @gc, @n);";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@lid", m.LiegenschaftId);
            cmd.Parameters.AddWithValue("@am", m.ErfasstAm);
            cmd.Parameters.AddWithValue("@rk", (object?)m.RechnungKwhTotal ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gc", (object?)m.GutschriftChf ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@n", (object?)m.Notiz ?? DBNull.Value);

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
    Notiz            = @n,
    UpdatedAtUtc     = SYSUTCDATETIME()
WHERE Id = @id;";

            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", m.Id);
            cmd.Parameters.AddWithValue("@am", m.ErfasstAm);
            cmd.Parameters.AddWithValue("@rk", (object?)m.RechnungKwhTotal ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gc", (object?)m.GutschriftChf ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@n", (object?)m.Notiz ?? DBNull.Value);

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

        /// <summary>
        /// Liefert das direkt vorherige Zählerdaten-Set (nach ErfasstAm) innerhalb derselben Liegenschaft.
        /// </summary>
        public StweZaehlerdatenSet? StweZaehlerdatenGetPreviousSet(int liegenschaftId, DateTime currentErfasstAm, int currentId)
        {
            EnsureStweSchema();

            using var c = CreateConnection();
            c.Open();

            const string sql = @"
SELECT TOP(1) Id, LiegenschaftId, ErfasstAm, RechnungKwhTotal, GutschriftChf, Notiz
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
                Notiz = r.IsDBNull(5) ? null : r.GetString(5)
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
