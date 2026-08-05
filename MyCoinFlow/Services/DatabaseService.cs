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
    public partial class DatabaseService : ICreditCardImportRepository
    {
        // Verbindung zur Datenbank
        private string _connectionString => MyCoinFlow.Services.ConnectionStrings.Current;


        public Microsoft.Data.SqlClient.SqlConnection CreateConnection()
        {
            // Liefert eine GESCHLOSSENE Verbindung. Öffnen/Schließen übernimmt der Provider.
            return new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
        }



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
        // ---------------------------------------------
        // SCHRITT A – Schema-Erweiterung OCR/Textindex
        // ---------------------------------------------

        /// <summary>
        /// Führt alle idempotenten Checks aus, um die für Anhänge/OCR benötigten DB-Objekte bereitzustellen.
        /// </summary>
        /// 
        // Neu:Budgetdatum
        public void EnsureAttachmentsSchema()
        {
            EnsureAppSettingsTable();
            EnsureAttachmentsTable();
            EnsureAttachmentTextTable(); // NEU: Textindex pro Attachment

            // =========================================================
            // NEU: BudgetDatum-Spalte in dbo.Transaktion sicherstellen
            // =========================================================
            using var conn = CreateConnection();
            conn.Open();

            var sql = @"
IF COL_LENGTH('dbo.Transaktion', 'BudgetDatum') IS NULL
BEGIN
    ALTER TABLE dbo.Transaktion
    ADD BudgetDatum DATE NULL;
END;
";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();

            // =========================================================
            // NEU (DMS): dbo.Attachment generischer machen
            // - EntityType/EntityId: künftige Verknüpfung mit Adressen/
            //   Liegenschaften/STWE/Wealth statt nur Transaktionen
            // - Titel/Kategorie: für freistehende DMS-Dokumente
            // - TransaktionId nullable: freistehende Dokumente haben keine
            // =========================================================
            // Schritt 1: Spalten anlegen / TransaktionId nullable machen.
            // Muss als EIGENER Batch laufen, sonst prüft SQL Server die
            // untenstehenden Statements (die die neuen Spalten referenzieren)
            // bereits beim Parsen des Batches, bevor die Spalten existieren
            // ("Invalid column name").
            var dmsSchemaSql = @"
IF COL_LENGTH('dbo.Attachment', 'EntityType') IS NULL
BEGIN
    ALTER TABLE dbo.Attachment ADD EntityType NVARCHAR(32) NULL;
END;

IF COL_LENGTH('dbo.Attachment', 'EntityId') IS NULL
BEGIN
    ALTER TABLE dbo.Attachment ADD EntityId INT NULL;
END;

IF COL_LENGTH('dbo.Attachment', 'Titel') IS NULL
BEGIN
    ALTER TABLE dbo.Attachment ADD Titel NVARCHAR(200) NULL;
END;

IF COL_LENGTH('dbo.Attachment', 'Kategorie') IS NULL
BEGIN
    ALTER TABLE dbo.Attachment ADD Kategorie NVARCHAR(100) NULL;
END;

-- NEU: erkanntes/angenommenes Dokumentdatum (fürs Fälligkeits-Tracking: Datum + 30 Tage,
-- solange keine Transaktion zugeordnet ist).
IF COL_LENGTH('dbo.Attachment', 'DokumentDatum') IS NULL
BEGIN
    ALTER TABLE dbo.Attachment ADD DokumentDatum DATE NULL;
END;

-- NEU: Garantieschein-Kennzeichnung mit Ablaufdatum.
IF COL_LENGTH('dbo.Attachment', 'IstGarantieschein') IS NULL
BEGIN
    ALTER TABLE dbo.Attachment ADD IstGarantieschein BIT NOT NULL CONSTRAINT DF_Attachment_IstGarantieschein DEFAULT 0;
END;

IF COL_LENGTH('dbo.Attachment', 'GarantieAblaufDatum') IS NULL
BEGIN
    ALTER TABLE dbo.Attachment ADD GarantieAblaufDatum DATE NULL;
END;

-- NEU: eigene Adresse am Dokument (z.B. Bankdokument/Vertrag ohne zugehoerige Zahlung).
-- Bewusst ohne Fremdschluessel, damit das Loeschen einer Adresse nicht blockiert wird;
-- die Anzeige laeuft ueber LEFT JOIN und bleibt bei geloeschter Adresse einfach leer.
IF COL_LENGTH('dbo.Attachment', 'AdresseId') IS NULL
BEGIN
    ALTER TABLE dbo.Attachment ADD AdresseId INT NULL;
END;

-- NEU: frei erfassbarer Beschreibungstext (ersetzt den frueheren Titel in der Bearbeitung,
-- da die Dateinamen einheitlich vergeben werden und nicht mehr sprechend sind).
IF COL_LENGTH('dbo.Attachment', 'Beschreibung') IS NULL
BEGIN
    ALTER TABLE dbo.Attachment ADD Beschreibung NVARCHAR(1000) NULL;
END;

-- NEU: aus dem Dokument erkannter Rechnungsbetrag (bester Betragskandidat der
-- Texterkennung). Anzeige im DMS-Grid, solange keine Transaktion verknuepft ist.
IF COL_LENGTH('dbo.Attachment', 'ErkannterBetrag') IS NULL
BEGIN
    ALTER TABLE dbo.Attachment ADD ErkannterBetrag DECIMAL(19,2) NULL;
END;

-- NEU: Gesehen-Markierung fuer die Neu-Anzeige im DMS-Grid.
-- NULL = frisch importiert, noch nicht angeklickt. Beim Anlegen der Spalte werden
-- Bestandsdokumente als gesehen markiert, damit nicht das ganze Archiv aufleuchtet.
IF COL_LENGTH('dbo.Attachment', 'GesehenAm') IS NULL
BEGIN
    ALTER TABLE dbo.Attachment ADD GesehenAm DATETIME2 NULL;
    EXEC('UPDATE dbo.Attachment SET GesehenAm = SYSUTCDATETIME()');
END;

IF EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables t ON t.object_id = c.object_id
    WHERE t.name = 'Attachment' AND c.name = 'TransaktionId' AND c.is_nullable = 0
)
BEGIN
    ALTER TABLE dbo.Attachment ALTER COLUMN TransaktionId INT NULL;
END;
";
            using (var dmsSchemaCmd = conn.CreateCommand())
            {
                dmsSchemaCmd.CommandText = dmsSchemaSql;
                dmsSchemaCmd.ExecuteNonQuery();
            }

            // Schritt 2: jetzt existieren die Spalten wirklich -> Backfill + Index
            // dürfen (in einem eigenen, zweiten Batch) darauf zugreifen.
            var dmsDataSql = @"
UPDATE dbo.Attachment
SET EntityType = 'Transaktion', EntityId = TransaktionId
WHERE EntityType IS NULL AND TransaktionId IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Attachment_EntityType_EntityId' AND object_id = OBJECT_ID('dbo.Attachment'))
BEGIN
    CREATE INDEX IX_Attachment_EntityType_EntityId ON dbo.Attachment(EntityType, EntityId);
END;
";
            using (var dmsDataCmd = conn.CreateCommand())
            {
                dmsDataCmd.CommandText = dmsDataSql;
                dmsDataCmd.ExecuteNonQuery();
            }

            // NEU: Verarbeitungs-Historie für den DMS-Watcher (Task 24).
            var dmsLogSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'DmsProcessingLog' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.DmsProcessingLog
    (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        OccurredAtUtc DATETIME2 NOT NULL,
        FileName NVARCHAR(260) NULL,
        Ergebnis NVARCHAR(500) NULL
    );
END;
";
            using (var dmsLogCmd = conn.CreateCommand())
            {
                dmsLogCmd.CommandText = dmsLogSql;
                dmsLogCmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Schreibt einen Eintrag in die DMS-Verarbeitungs-Historie (Task 24).
        /// </summary>
        public void LogDmsProcessing(string? fileName, string? ergebnis)
        {
            using var conn = CreateConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO dbo.DmsProcessingLog (OccurredAtUtc, FileName, Ergebnis)
VALUES (@occurred, @fileName, @ergebnis);";
            cmd.Parameters.AddWithValue("@occurred", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@fileName", (object?)fileName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ergebnis", (object?)ergebnis ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Lädt die DMS-Verarbeitungs-Historie, neueste zuerst (Task 24).
        /// </summary>
        public List<DmsProcessingLogEntry> LoadDmsProcessingLog(int maxRows = 500)
        {
            var result = new List<DmsProcessingLogEntry>();

            using var conn = CreateConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT TOP (@max) Id, OccurredAtUtc, FileName, Ergebnis
FROM dbo.DmsProcessingLog
ORDER BY OccurredAtUtc DESC;";
            cmd.Parameters.AddWithValue("@max", maxRows);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new DmsProcessingLogEntry
                {
                    Id = reader.GetInt32(0),
                    OccurredAtUtc = reader.GetDateTime(1),
                    FileName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Ergebnis = reader.IsDBNull(3) ? null : reader.GetString(3)
                });
            }

            return result;
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
        public int SaveAttachment(int? transaktionId, string fileName, string? originalName, string folderRel, long? sizeBytes, string? ocrStatus,
            string? entityType = null, int? entityId = null, string? titel = null, string? kategorie = null, DateTime? dokumentDatum = null)
        {
            // Rückwärtskompatibel: bisherige Aufrufer übergeben nur transaktionId,
            // EntityType/EntityId werden dann automatisch daraus abgeleitet.
            if (entityType == null && entityId == null && transaktionId.HasValue)
            {
                entityType = "Transaktion";
                entityId = transaktionId;
            }

            using var c = CreateConnection();
            c.Open();
            const string sql = @"
INSERT INTO dbo.Attachment (TransaktionId, FileName, OriginalName, FolderRel, SizeBytes, OcrStatus, EntityType, EntityId, Titel, Kategorie, DokumentDatum)
VALUES (@t, @f, @o, @folder, @sz, @ocr, @et, @eid, @titel, @kat, @dokDatum);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@t", (object?)transaktionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@f", fileName);
            cmd.Parameters.AddWithValue("@o", (object?)originalName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@folder", folderRel);
            cmd.Parameters.AddWithValue("@sz", (object?)sizeBytes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ocr", (object?)ocrStatus ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@et", (object?)entityType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@eid", (object?)entityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@titel", (object?)titel ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@kat", (object?)kategorie ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dokDatum", (object?)dokumentDatum?.Date ?? DBNull.Value);

            var idObj = cmd.ExecuteScalar();
            return (idObj is int i) ? i : Convert.ToInt32(idObj);
        }

        /// <summary>
        /// DMS: markiert ein Dokument als Garantieschein (mit Ablaufdatum) oder hebt die
        /// Kennzeichnung wieder auf (istGarantieschein = false, ablaufDatum wird dann ignoriert).
        /// </summary>
        public void UpdateAttachmentGarantie(int attachmentId, bool istGarantieschein, DateTime? ablaufDatum)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"UPDATE dbo.Attachment SET IstGarantieschein = @ist, GarantieAblaufDatum = @ablauf WHERE Id = @id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.Parameters.AddWithValue("@ist", istGarantieschein);
            cmd.Parameters.AddWithValue("@ablauf", istGarantieschein && ablaufDatum.HasValue ? (object)ablaufDatum.Value.Date : DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// DMS: liefert alle Dokumente (transaktionsgebunden + freistehend), optional gefiltert
        /// nach Volltext (Dateiname/Titel/Kategorie/OCR-Text) und/oder Kategorie.
        /// </summary>
        public List<DmsDocument> LoadAllDocuments(string? searchText, string? kategorie)
        {
            var list = new List<DmsDocument>();
            using var c = CreateConnection();
            c.Open();

            var sql = @"
SELECT a.Id, a.TransaktionId, a.EntityType, a.EntityId, a.Titel, a.Kategorie,
       a.FileName, a.OriginalName, a.FolderRel, a.SizeBytes, a.ImportedAtUtc, a.OcrStatus,
       a.DokumentDatum, a.IstGarantieschein, a.GarantieAblaufDatum,
       tr.Betrag AS TransBetrag, adr.Name AS TransAdresseName,
       a.GesehenAm, a.ErkannterBetrag, a.Beschreibung,
       a.AdresseId, dadr.Name AS EigeneAdresseName
FROM dbo.Attachment a
LEFT JOIN dbo.AttachmentText txt ON txt.AttachmentId = a.Id
LEFT JOIN dbo.Transaktion tr ON a.EntityType = 'Transaktion' AND tr.Id = a.EntityId
LEFT JOIN dbo.Adresse adr ON adr.Id = tr.AdresseId
LEFT JOIN dbo.Adresse dadr ON dadr.Id = a.AdresseId
WHERE (@q IS NULL OR
       a.FileName LIKE '%' + @q + '%' OR
       a.Titel LIKE '%' + @q + '%' OR
       a.Beschreibung LIKE '%' + @q + '%' OR
       a.Kategorie LIKE '%' + @q + '%' OR
       adr.Name LIKE '%' + @q + '%' OR
       dadr.Name LIKE '%' + @q + '%' OR
       txt.[Text] LIKE '%' + @q + '%')
  AND (@kat IS NULL OR a.Kategorie = @kat)
ORDER BY a.ImportedAtUtc DESC;";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@q", string.IsNullOrWhiteSpace(searchText) ? DBNull.Value : (object)searchText.Trim());
            cmd.Parameters.AddWithValue("@kat", string.IsNullOrWhiteSpace(kategorie) ? DBNull.Value : (object)kategorie.Trim());

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var transaktionId = r.IsDBNull(1) ? (int?)null : r.GetInt32(1);
                var entityType = r.IsDBNull(2) ? null : r.GetString(2);
                var entityId = r.IsDBNull(3) ? (int?)null : r.GetInt32(3);

                list.Add(new DmsDocument
                {
                    Id = r.GetInt32(0),
                    TransaktionId = transaktionId,
                    EntityType = entityType,
                    EntityId = entityId,
                    Titel = r.IsDBNull(4) ? null : r.GetString(4),
                    Kategorie = r.IsDBNull(5) ? null : r.GetString(5),
                    FileName = r.GetString(6),
                    OriginalName = r.IsDBNull(7) ? null : r.GetString(7),
                    FolderRel = r.GetString(8),
                    SizeBytes = r.IsDBNull(9) ? (long?)null : r.GetInt64(9),
                    ImportedAtUtc = r.GetDateTime(10),
                    OcrStatus = r.IsDBNull(11) ? null : r.GetString(11),
                    DokumentDatum = r.IsDBNull(12) ? (DateTime?)null : r.GetDateTime(12),
                    IstGarantieschein = r.GetBoolean(13),
                    GarantieAblaufDatum = r.IsDBNull(14) ? (DateTime?)null : r.GetDateTime(14),
                    TransBetrag = r.IsDBNull(15) ? (decimal?)null : r.GetDecimal(15),
                    TransAdresseName = r.IsDBNull(16) ? null : r.GetString(16),
                    IstNeu = r.IsDBNull(17),
                    ErkannterBetrag = r.IsDBNull(18) ? (decimal?)null : r.GetDecimal(18),
                    Beschreibung = r.IsDBNull(19) ? null : r.GetString(19),
                    AdresseId = r.IsDBNull(20) ? (int?)null : r.GetInt32(20),
                    EigeneAdresseName = r.IsDBNull(21) ? null : r.GetString(21)
                });
            }
            return list;
        }

        /// <summary>
        /// DMS: Anzahl Dokumente, die eine bestimmte Kategorie verwenden
        /// (für die Rückfrage vor dem Löschen der Kategorie).
        /// </summary>
        public int ZaehleDokumenteMitKategorie(string kategorie)
        {
            if (string.IsNullOrWhiteSpace(kategorie)) return 0;

            using var c = CreateConnection();
            c.Open();
            const string sql = @"SELECT COUNT(1) FROM dbo.Attachment WHERE Kategorie = @kat";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@kat", kategorie.Trim());

            var v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? 0 : Convert.ToInt32(v);
        }

        /// <summary>
        /// DMS: entfernt eine Kategorie, indem sie bei allen Dokumenten geleert wird.
        /// Die Dokumente selbst bleiben unverändert erhalten. Da die Kategorienliste aus
        /// den verwendeten Werten gebildet wird, verschwindet der Eintrag damit aus der Auswahl.
        /// </summary>
        public int LoescheKategorie(string kategorie)
        {
            if (string.IsNullOrWhiteSpace(kategorie)) return 0;

            using var c = CreateConnection();
            c.Open();
            const string sql = @"UPDATE dbo.Attachment SET Kategorie = NULL WHERE Kategorie = @kat";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@kat", kategorie.Trim());

            return cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// DMS: speichert alle im Bearbeiten-Dialog erfassbaren Felder eines Dokuments
        /// in einem Rutsch (Kategorie, Beschreibung, Dokumentdatum, Betrag, Garantie).
        /// Dateiname, Größe, Status und Transaktions-Verknüpfung bleiben unberührt.
        /// </summary>
        public void UpdateAttachmentDetails(
            int attachmentId,
            string? kategorie,
            string? beschreibung,
            DateTime? dokumentDatum,
            decimal? betrag,
            bool istGarantieschein,
            DateTime? garantieAblaufDatum,
            int? adresseId = null)
        {
            using var c = CreateConnection();
            c.Open();

            const string sql = @"
UPDATE dbo.Attachment SET
    Kategorie = @kat,
    Beschreibung = @besch,
    DokumentDatum = @dok,
    ErkannterBetrag = @betrag,
    IstGarantieschein = @garantie,
    GarantieAblaufDatum = @ablauf,
    AdresseId = @adr
WHERE Id = @id";

            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.Parameters.AddWithValue("@adr", (object?)adresseId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@kat", string.IsNullOrWhiteSpace(kategorie) ? DBNull.Value : (object)kategorie.Trim());
            cmd.Parameters.AddWithValue("@besch", string.IsNullOrWhiteSpace(beschreibung) ? DBNull.Value : (object)beschreibung.Trim());
            cmd.Parameters.AddWithValue("@dok", dokumentDatum.HasValue ? (object)dokumentDatum.Value.Date : DBNull.Value);
            cmd.Parameters.AddWithValue("@betrag", (object?)betrag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@garantie", istGarantieschein);
            cmd.Parameters.AddWithValue("@ablauf", istGarantieschein && garantieAblaufDatum.HasValue ? (object)garantieAblaufDatum.Value.Date : DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// DMS: speichert den aus dem Dokumenttext erkannten Rechnungsbetrag
        /// (bester Betragskandidat; Anzeige im Grid für freie Dokumente).
        /// </summary>
        public void UpdateAttachmentErkannterBetrag(int attachmentId, decimal? betrag)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"UPDATE dbo.Attachment SET ErkannterBetrag = @betrag WHERE Id = @id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.Parameters.AddWithValue("@betrag", (object?)betrag ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// DMS: markiert ein Dokument als gesehen (Neu-Icon im Grid verschwindet).
        /// </summary>
        public void MarkDocumentSeen(int attachmentId)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"UPDATE dbo.Attachment SET GesehenAm = SYSUTCDATETIME()
                                 WHERE Id = @id AND GesehenAm IS NULL";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// DMS: aktualisiert Titel/Kategorie eines Dokuments (Datei bleibt unverändert).
        /// </summary>
        public void UpdateAttachmentMeta(int attachmentId, string? titel, string? kategorie)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"UPDATE dbo.Attachment SET Titel = @titel, Kategorie = @kat WHERE Id = @id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.Parameters.AddWithValue("@titel", (object?)titel ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@kat", (object?)kategorie ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// DMS: liefert alle bisher verwendeten Kategorien (für Vorschlagsliste im Upload-Dialog).
        /// </summary>
        public List<string> GetDistinctKategorien()
        {
            var list = new List<string>();
            using var c = CreateConnection();
            c.Open();
            const string sql = @"SELECT DISTINCT Kategorie FROM dbo.Attachment WHERE Kategorie IS NOT NULL AND Kategorie <> '' ORDER BY Kategorie";
            using var cmd = new SqlCommand(sql, c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(r.GetString(0));
            return list;
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
        /// Load by Id – minimal für Delete / Open / Re-Matching.
        /// </summary>
        public (int Id, int? TransaktionId, string FileName, string FolderRel, DateTime ImportedAtUtc)? GetAttachmentById(int attachmentId)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"SELECT Id, TransaktionId, FileName, FolderRel, ImportedAtUtc FROM dbo.Attachment WHERE Id=@id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                return (r.GetInt32(0), r.IsDBNull(1) ? (int?)null : r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetDateTime(4));
            }
            return null;
        }

        /// <summary>
        /// DMS: liest den gespeicherten OCR-/Textlayer-Inhalt eines Attachments (für erneutes
        /// Transaktions-Matching, ohne die Datei nochmals per OCR verarbeiten zu müssen).
        /// </summary>
        public string? GetAttachmentText(int attachmentId)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"SELECT [Text] FROM dbo.AttachmentText WHERE AttachmentId=@id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            var obj = cmd.ExecuteScalar();
            return (obj == null || obj == DBNull.Value) ? null : Convert.ToString(obj);
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
        /// DMS: verknüpft ein bestehendes (freistehendes) Dokument mit einer Transaktion
        /// (automatisches Matching oder manuelles Zuweisen). Datei bleibt am aktuellen Ort.
        /// </summary>
        public void LinkAttachmentToTransaktion(int attachmentId, int transaktionId)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"
UPDATE dbo.Attachment
SET TransaktionId = @tid, EntityType = 'Transaktion', EntityId = @tid
WHERE Id = @id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.Parameters.AddWithValue("@tid", transaktionId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// DMS: aktualisiert Dateiname und Kategorie eines Dokuments (z. B. nach dem Umbenennen
        /// bei Zuordnung zu einer Transaktion). Titel bleibt unverändert.
        /// </summary>
        public void UpdateAttachmentFileNameAndKategorie(int attachmentId, string fileName, string? kategorie)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"UPDATE dbo.Attachment SET FileName = @fn, Kategorie = @kat WHERE Id = @id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.Parameters.AddWithValue("@fn", fileName);
            cmd.Parameters.AddWithValue("@kat", (object?)kategorie ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// DMS: aktualisiert nur den Dateinamen (z. B. Umbenennen auf das einheitliche
        /// DOK-{Id}-Schema direkt nach dem Anlegen). Kategorie/Titel bleiben unverändert.
        /// </summary>
        public void UpdateAttachmentFileName(int attachmentId, string fileName)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"UPDATE dbo.Attachment SET FileName = @fn WHERE Id = @id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.Parameters.AddWithValue("@fn", fileName);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// DMS: liefert Id/Dateiname/Ordner aller Attachments – Grundlage für die
        /// Migration bestehender Dokumente auf das einheitliche DOK-{Id}-Namensschema.
        /// </summary>
        public List<(int Id, string FileName, string FolderRel)> LoadAllAttachmentFilePaths()
        {
            var result = new List<(int, string, string)>();

            using var c = CreateConnection();
            c.Open();
            const string sql = @"SELECT Id, FileName, FolderRel FROM dbo.Attachment;";
            using var cmd = new SqlCommand(sql, c);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
            }

            return result;
        }

        /// <summary>
        /// DMS: löst die Verknüpfung eines Dokuments von seiner Transaktion (Gegenteil von
        /// LinkAttachmentToTransaktion). Datei und DB-Zeile bleiben erhalten – im Unterschied
        /// zu DeleteAttachment. Das Dokument erscheint danach als freistehend im DMS.
        /// </summary>
        public void UnlinkAttachment(int attachmentId)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"
UPDATE dbo.Attachment
SET TransaktionId = NULL, EntityType = NULL, EntityId = NULL
WHERE Id = @id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Aktualisiert das erkannte Dokumentdatum eines Attachments (Basis fürs
        /// Fälligkeits-Tracking). Wird u. a. beim erneuten Matching (DmsWatcherService) genutzt,
        /// um ältere Dokumente (angelegt, bevor DokumentDatum existierte, oder ohne Treffer beim
        /// ersten Lauf) nachträglich zu befüllen.
        /// </summary>
        public void UpdateAttachmentDokumentDatum(int attachmentId, DateTime datum)
        {
            using var c = CreateConnection();
            c.Open();
            const string sql = @"UPDATE dbo.Attachment SET DokumentDatum = @d WHERE Id = @id";
            using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.Parameters.AddWithValue("@d", datum.Date);
            cmd.ExecuteNonQuery();
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

    }
}
