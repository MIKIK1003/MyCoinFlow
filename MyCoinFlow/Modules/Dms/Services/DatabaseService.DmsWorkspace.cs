using Microsoft.Data.SqlClient;
using MyCoinFlow.Models;

namespace MyCoinFlow.Services
{
    public partial class DatabaseService
    {
        private void EnsureDmsWorkspaceSchema()
        {
            using var connection = CreateConnection();
            connection.Open();

            const string columnsSql = @"
IF COL_LENGTH('dbo.Attachment', 'Belegart') IS NULL
    ALTER TABLE dbo.Attachment ADD Belegart NVARCHAR(40) NULL;
IF COL_LENGTH('dbo.Attachment', 'Schlagwoerter') IS NULL
    ALTER TABLE dbo.Attachment ADD Schlagwoerter NVARCHAR(1000) NULL;
IF COL_LENGTH('dbo.Attachment', 'Notiz') IS NULL
    ALTER TABLE dbo.Attachment ADD Notiz NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.Attachment', 'Bearbeitungsstatus') IS NULL
    ALTER TABLE dbo.Attachment ADD Bearbeitungsstatus NVARCHAR(32) NOT NULL CONSTRAINT DF_Attachment_Bearbeitungsstatus DEFAULT N'Neu';
IF COL_LENGTH('dbo.Attachment', 'Verantwortlich') IS NULL
    ALTER TABLE dbo.Attachment ADD Verantwortlich NVARCHAR(128) NULL;
IF COL_LENGTH('dbo.Attachment', 'FaelligAm') IS NULL
    ALTER TABLE dbo.Attachment ADD FaelligAm DATE NULL;
IF COL_LENGTH('dbo.Attachment', 'AufbewahrenBis') IS NULL
    ALTER TABLE dbo.Attachment ADD AufbewahrenBis DATE NULL;
IF COL_LENGTH('dbo.Attachment', 'IstFavorit') IS NULL
    ALTER TABLE dbo.Attachment ADD IstFavorit BIT NOT NULL CONSTRAINT DF_Attachment_IstFavorit DEFAULT 0;
IF COL_LENGTH('dbo.Attachment', 'IstSteuerunterlage') IS NULL
    ALTER TABLE dbo.Attachment ADD IstSteuerunterlage BIT NOT NULL CONSTRAINT DF_Attachment_IstSteuerunterlage DEFAULT 0;
IF COL_LENGTH('dbo.Attachment', 'AktuelleVersion') IS NULL
    ALTER TABLE dbo.Attachment ADD AktuelleVersion INT NOT NULL CONSTRAINT DF_Attachment_AktuelleVersion DEFAULT 1;
IF COL_LENGTH('dbo.Attachment', 'InhaltHash') IS NULL
    ALTER TABLE dbo.Attachment ADD InhaltHash NVARCHAR(64) NULL;
IF COL_LENGTH('dbo.Attachment', 'LetzteAenderungAmUtc') IS NULL
    ALTER TABLE dbo.Attachment ADD LetzteAenderungAmUtc DATETIME2 NOT NULL CONSTRAINT DF_Attachment_LetzteAenderungAmUtc DEFAULT SYSUTCDATETIME();";

            using (var command = new SqlCommand(columnsSql, connection))
                command.ExecuteNonQuery();

            const string tablesSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AttachmentVersion' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.AttachmentVersion
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttachmentVersion PRIMARY KEY,
        AttachmentId INT NOT NULL,
        VersionNr INT NOT NULL,
        FileName NVARCHAR(260) NOT NULL,
        FolderRel NVARCHAR(260) NOT NULL,
        SizeBytes BIGINT NOT NULL,
        InhaltHash NVARCHAR(64) NULL,
        ErstelltAmUtc DATETIME2 NOT NULL,
        ErstelltVon NVARCHAR(128) NULL,
        Kommentar NVARCHAR(1000) NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'DmsAktivitaet' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.DmsAktivitaet
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DmsAktivitaet PRIMARY KEY,
        AttachmentId INT NULL,
        DokumentTitelSnapshot NVARCHAR(260) NOT NULL,
        Art NVARCHAR(64) NOT NULL,
        ZeitpunktUtc DATETIME2 NOT NULL,
        Benutzername NVARCHAR(128) NULL,
        Beschreibung NVARCHAR(1000) NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AttachmentVersion_AttachmentId_VersionNr' AND object_id = OBJECT_ID('dbo.AttachmentVersion'))
    CREATE UNIQUE INDEX IX_AttachmentVersion_AttachmentId_VersionNr ON dbo.AttachmentVersion(AttachmentId, VersionNr);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DmsAktivitaet_AttachmentId_ZeitpunktUtc' AND object_id = OBJECT_ID('dbo.DmsAktivitaet'))
    CREATE INDEX IX_DmsAktivitaet_AttachmentId_ZeitpunktUtc ON dbo.DmsAktivitaet(AttachmentId, ZeitpunktUtc DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Attachment_InhaltHash' AND object_id = OBJECT_ID('dbo.Attachment'))
    CREATE INDEX IX_Attachment_InhaltHash ON dbo.Attachment(InhaltHash) WHERE InhaltHash IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Attachment_IstSteuerunterlage' AND object_id = OBJECT_ID('dbo.Attachment'))
    CREATE INDEX IX_Attachment_IstSteuerunterlage ON dbo.Attachment(IstSteuerunterlage) WHERE IstSteuerunterlage = 1;";

            using (var command = new SqlCommand(tablesSql, connection))
                command.ExecuteNonQuery();

            const string dataSql = @"
UPDATE dbo.Attachment
SET Bearbeitungsstatus = N'Freigegeben'
WHERE EntityType = N'Transaktion' AND Bearbeitungsstatus = N'Neu';

INSERT INTO dbo.DmsAktivitaet
    (AttachmentId, DokumentTitelSnapshot, Art, ZeitpunktUtc, Benutzername, Beschreibung)
SELECT a.Id, COALESCE(NULLIF(a.Titel, N''), a.FileName), N'Importiert', a.ImportedAtUtc, NULL,
       N'Bestehendes Dokument in die erweiterte DMS-Akte übernommen'
FROM dbo.Attachment a
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.DmsAktivitaet d
    WHERE d.AttachmentId = a.Id AND d.Art = N'Importiert'
);";

            using var dataCommand = new SqlCommand(dataSql, connection);
            dataCommand.ExecuteNonQuery();
        }

        /// <summary>
        /// Speichert sämtliche Werte aus „Dokument organisieren“ atomar. Damit können die
        /// klassischen MyCoinFlow-Felder und die erweiterten DMS-Metadaten nie auseinanderlaufen.
        /// </summary>
        public void UpdateDmsDocument(int attachmentId, DmsDocumentChanges changes, bool? isTaxDocument = null)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            string? oldStatus;
            bool oldIsTaxDocument;
            string snapshot;
            using (var read = new SqlCommand("SELECT Bearbeitungsstatus, COALESCE(NULLIF(Titel, ''), FileName), IstSteuerunterlage FROM dbo.Attachment WHERE Id=@id", connection, transaction))
            {
                read.Parameters.AddWithValue("@id", attachmentId);
                using var reader = read.ExecuteReader();
                if (!reader.Read()) throw new InvalidOperationException("Das Dokument wurde nicht gefunden.");
                oldStatus = reader.IsDBNull(0) ? null : reader.GetString(0);
                snapshot = reader.GetString(1);
                oldIsTaxDocument = reader.GetBoolean(2);
            }

            const string sql = @"
UPDATE dbo.Attachment SET
    Titel=@title,
    Kategorie=@category,
    Belegart=@documentType,
    Beschreibung=@description,
    Schlagwoerter=@keywords,
    Notiz=@note,
    Bearbeitungsstatus=@workflowStatus,
    Verantwortlich=@responsible,
    DokumentDatum=@documentDate,
    ErkannterBetrag=@recognizedAmount,
    AdresseId=@addressId,
    IstGarantieschein=@isWarrantyCertificate,
    GarantieAblaufDatum=@warrantyExpiresAt,
    FaelligAm=@dueAt,
    AufbewahrenBis=@retainUntil,
    IstSteuerunterlage=COALESCE(@isTaxDocument, IstSteuerunterlage),
    LetzteAenderungAmUtc=SYSUTCDATETIME()
WHERE Id=@id;";
            using (var command = new SqlCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@id", attachmentId);
                command.Parameters.AddWithValue("@title", DbValue(changes.Title));
                command.Parameters.AddWithValue("@category", DbValue(changes.Category));
                command.Parameters.AddWithValue("@documentType", changes.DocumentType.HasValue ? changes.DocumentType.Value.ToString() : DBNull.Value);
                command.Parameters.AddWithValue("@description", DbValue(changes.Description));
                command.Parameters.AddWithValue("@keywords", DbValue(NormalizeKeywords(changes.Keywords)));
                command.Parameters.AddWithValue("@note", DbValue(changes.Note));
                command.Parameters.AddWithValue("@workflowStatus", changes.WorkflowStatus.ToString());
                command.Parameters.AddWithValue("@responsible", DbValue(changes.Responsible));
                command.Parameters.AddWithValue("@documentDate", DateValue(changes.DocumentDate));
                command.Parameters.AddWithValue("@recognizedAmount", (object?)changes.RecognizedAmount ?? DBNull.Value);
                command.Parameters.AddWithValue("@addressId", (object?)changes.AddressId ?? DBNull.Value);
                command.Parameters.AddWithValue("@isWarrantyCertificate", changes.IsWarrantyCertificate);
                command.Parameters.AddWithValue("@warrantyExpiresAt",
                    changes.IsWarrantyCertificate ? DateValue(changes.WarrantyExpiresAt) : DBNull.Value);
                command.Parameters.AddWithValue("@dueAt", DateValue(changes.DueAt));
                command.Parameters.AddWithValue("@retainUntil", DateValue(changes.RetainUntil));
                command.Parameters.Add(new SqlParameter("@isTaxDocument", System.Data.SqlDbType.Bit)
                {
                    Value = isTaxDocument.HasValue ? isTaxDocument.Value : DBNull.Value
                });
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Das Dokument wurde beim Speichern nicht gefunden.");
            }

            InsertActivity(connection, transaction, attachmentId, snapshot, "MetadatenGeaendert", "Metadaten aktualisiert");
            if (!string.Equals(oldStatus, changes.WorkflowStatus.ToString(), StringComparison.Ordinal))
                InsertActivity(connection, transaction, attachmentId, snapshot, "StatusGeaendert",
                    $"Status: {oldStatus ?? "Neu"} → {changes.WorkflowStatus}");
            if (isTaxDocument.HasValue && oldIsTaxDocument != isTaxDocument.Value)
                InsertActivity(connection, transaction, attachmentId, snapshot, "SteuerkennzeichnungGeaendert",
                    isTaxDocument.Value ? "Als Steuerunterlage markiert" : "Steuerkennzeichnung entfernt");

            transaction.Commit();
        }

        /// <summary>
        /// Verknüpft ein DMS-Dokument atomar mit einer Transaktion und übernimmt gleichzeitig
        /// die daraus abgeleiteten Metadaten. Dadurch verhalten sich manuelle Zuweisung,
        /// automatisches Matching und die Verknüpfung von der Transaktionsseite identisch.
        /// </summary>
        public void LinkAttachmentToTransaktionAndUpdateMetadata(
            int attachmentId,
            int transaktionId,
            DmsDocumentChanges changes,
            bool requireUnlinked,
            string linkDescription)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var snapshot = LoadSnapshot(connection, transaction, attachmentId);

            const string sql = @"
UPDATE dbo.Attachment SET
    TransaktionId=@transactionId,
    EntityType=N'Transaktion',
    EntityId=@transactionId,
    Titel=@title,
    Kategorie=@category,
    Belegart=@documentType,
    Beschreibung=@description,
    Schlagwoerter=@keywords,
    Notiz=@note,
    DokumentDatum=@documentDate,
    ErkannterBetrag=@recognizedAmount,
    AdresseId=@addressId,
    LetzteAenderungAmUtc=SYSUTCDATETIME()
WHERE Id=@id
  AND (@requireUnlinked=0 OR (TransaktionId IS NULL AND EntityType IS NULL));";
            using (var command = new SqlCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@id", attachmentId);
                command.Parameters.AddWithValue("@transactionId", transaktionId);
                command.Parameters.AddWithValue("@requireUnlinked", requireUnlinked ? 1 : 0);
                command.Parameters.AddWithValue("@title", DbValue(changes.Title));
                command.Parameters.AddWithValue("@category", DbValue(changes.Category));
                command.Parameters.AddWithValue("@documentType", changes.DocumentType.HasValue ? changes.DocumentType.Value.ToString() : DBNull.Value);
                command.Parameters.AddWithValue("@description", DbValue(changes.Description));
                command.Parameters.AddWithValue("@keywords", DbValue(NormalizeKeywords(changes.Keywords)));
                command.Parameters.AddWithValue("@note", DbValue(changes.Note));
                command.Parameters.AddWithValue("@documentDate", DateValue(changes.DocumentDate));
                command.Parameters.AddWithValue("@recognizedAmount", (object?)changes.RecognizedAmount ?? DBNull.Value);
                command.Parameters.AddWithValue("@addressId", (object?)changes.AddressId ?? DBNull.Value);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(requireUnlinked
                        ? "Das Dokument ist nicht mehr frei oder wurde zwischenzeitlich anderweitig verknüpft."
                        : "Das Dokument wurde beim Verknüpfen nicht gefunden.");
            }

            InsertActivity(connection, transaction, attachmentId, snapshot, "Verknuepft", linkDescription);
            InsertActivity(connection, transaction, attachmentId, snapshot, "MetadatenAusTransaktionV2",
                "Dokumentdaten aus der verknüpften Transaktion ergänzt");
            transaction.Commit();
        }

        public bool HasDmsActivity(int attachmentId, string activityType)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var command = new SqlCommand(@"
SELECT CASE WHEN EXISTS
(
    SELECT 1 FROM dbo.DmsAktivitaet
    WHERE AttachmentId=@id AND Art=@activityType
) THEN 1 ELSE 0 END", connection);
            command.Parameters.AddWithValue("@id", attachmentId);
            command.Parameters.AddWithValue("@activityType", activityType);
            return Convert.ToInt32(command.ExecuteScalar()) == 1;
        }

        public void SetDmsFavorite(int attachmentId, bool isFavorite)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var snapshot = LoadSnapshot(connection, transaction, attachmentId);
            using (var command = new SqlCommand("UPDATE dbo.Attachment SET IstFavorit=@favorite, LetzteAenderungAmUtc=SYSUTCDATETIME() WHERE Id=@id", connection, transaction))
            {
                command.Parameters.AddWithValue("@id", attachmentId);
                command.Parameters.AddWithValue("@favorite", isFavorite);
                command.ExecuteNonQuery();
            }
            InsertActivity(connection, transaction, attachmentId, snapshot, "FavoritGeaendert",
                isFavorite ? "Als Favorit markiert" : "Favoritenmarkierung entfernt");
            transaction.Commit();
        }

        public void LogDmsActivity(int attachmentId, string activityType, string description)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var snapshot = LoadSnapshot(connection, transaction, attachmentId);
            InsertActivity(connection, transaction, attachmentId, snapshot, activityType, description);
            transaction.Commit();
        }

        public List<DmsActivityEntry> LoadDmsActivities(int attachmentId)
        {
            var result = new List<DmsActivityEntry>();
            using var connection = CreateConnection();
            connection.Open();
            using var command = new SqlCommand(@"
SELECT Id, AttachmentId, Art, ZeitpunktUtc, Benutzername, Beschreibung
FROM dbo.DmsAktivitaet WHERE AttachmentId=@id ORDER BY ZeitpunktUtc DESC, Id DESC", connection);
            command.Parameters.AddWithValue("@id", attachmentId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new DmsActivityEntry
                {
                    Id = reader.GetInt32(0),
                    AttachmentId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    ActivityType = reader.GetString(2),
                    OccurredAtUtc = reader.GetDateTime(3),
                    Username = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Description = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }
            return result;
        }

        public List<DmsVersionEntry> LoadDmsVersions(int attachmentId)
        {
            var result = new List<DmsVersionEntry>();
            using var connection = CreateConnection();
            connection.Open();
            using var command = new SqlCommand(@"
SELECT Id, AttachmentId, VersionNr, FileName, FolderRel, SizeBytes, ErstelltAmUtc, ErstelltVon, Kommentar
FROM dbo.AttachmentVersion WHERE AttachmentId=@id ORDER BY VersionNr DESC", connection);
            command.Parameters.AddWithValue("@id", attachmentId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new DmsVersionEntry
                {
                    Id = reader.GetInt32(0),
                    AttachmentId = reader.GetInt32(1),
                    VersionNumber = reader.GetInt32(2),
                    FileName = reader.GetString(3),
                    FolderRel = reader.GetString(4),
                    SizeBytes = reader.GetInt64(5),
                    CreatedAtUtc = reader.GetDateTime(6),
                    CreatedBy = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Comment = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
            }
            return result;
        }

        public DmsFileInfo? GetDmsFileInfo(int attachmentId)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var command = new SqlCommand(@"
SELECT Id, FileName, COALESCE(OriginalName, FileName), FolderRel, COALESCE(SizeBytes, 0), ImportedAtUtc,
       AktuelleVersion, InhaltHash, LetzteAenderungAmUtc, AufbewahrenBis,
       COALESCE(NULLIF(Titel, ''), FileName)
FROM dbo.Attachment WHERE Id=@id", connection);
            command.Parameters.AddWithValue("@id", attachmentId);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new DmsFileInfo(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
                reader.GetDateTime(5), reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetDateTime(8), reader.IsDBNull(9) ? null : reader.GetDateTime(9), reader.GetString(10));
        }

        public List<DmsFileInfo> LoadDmsFilesWithoutHash()
        {
            var result = new List<DmsFileInfo>();
            using var connection = CreateConnection();
            connection.Open();
            using var command = new SqlCommand(@"
SELECT Id, FileName, COALESCE(OriginalName, FileName), FolderRel, COALESCE(SizeBytes, 0), ImportedAtUtc,
       AktuelleVersion, InhaltHash, LetzteAenderungAmUtc, AufbewahrenBis,
       COALESCE(NULLIF(Titel, ''), FileName)
FROM dbo.Attachment
WHERE InhaltHash IS NULL OR InhaltHash = N''", connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new DmsFileInfo(
                    reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
                    reader.GetDateTime(5), reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetDateTime(8), reader.IsDBNull(9) ? null : reader.GetDateTime(9), reader.GetString(10)));
            }
            return result;
        }

        public void InitializeDmsFile(int attachmentId, long sizeBytes, string contentHash)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var snapshot = LoadSnapshot(connection, transaction, attachmentId);
            using (var command = new SqlCommand(@"
UPDATE dbo.Attachment SET SizeBytes=@size, InhaltHash=@hash, LetzteAenderungAmUtc=SYSUTCDATETIME() WHERE Id=@id", connection, transaction))
            {
                command.Parameters.AddWithValue("@id", attachmentId);
                command.Parameters.AddWithValue("@size", sizeBytes);
                command.Parameters.AddWithValue("@hash", contentHash);
                command.ExecuteNonQuery();
            }
            using (var exists = new SqlCommand("SELECT COUNT(1) FROM dbo.DmsAktivitaet WHERE AttachmentId=@id AND Art=N'Importiert'", connection, transaction))
            {
                exists.Parameters.AddWithValue("@id", attachmentId);
                if (Convert.ToInt32(exists.ExecuteScalar()) == 0)
                    InsertActivity(connection, transaction, attachmentId, snapshot, "Importiert", "Dokument importiert");
            }
            transaction.Commit();
        }

        public void CommitDmsVersion(DmsFileInfo oldFile, string archivedFileName, string archivedFolderRel,
            string newFileName, string newOriginalName, long newSizeBytes, string newHash, string? comment)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using (var insert = new SqlCommand(@"
INSERT INTO dbo.AttachmentVersion
    (AttachmentId, VersionNr, FileName, FolderRel, SizeBytes, InhaltHash, ErstelltAmUtc, ErstelltVon, Kommentar)
VALUES (@attachmentId, @version, @fileName, @folderRel, @size, @hash, @created, @user, @comment)", connection, transaction))
            {
                insert.Parameters.AddWithValue("@attachmentId", oldFile.Id);
                insert.Parameters.AddWithValue("@version", oldFile.CurrentVersion);
                insert.Parameters.AddWithValue("@fileName", archivedFileName);
                insert.Parameters.AddWithValue("@folderRel", archivedFolderRel);
                insert.Parameters.AddWithValue("@size", oldFile.SizeBytes);
                insert.Parameters.AddWithValue("@hash", (object?)oldFile.ContentHash ?? DBNull.Value);
                insert.Parameters.AddWithValue("@created", oldFile.LastChangedAtUtc);
                insert.Parameters.AddWithValue("@user", DbValue(CurrentUserContext.Username));
                insert.Parameters.AddWithValue("@comment", DbValue(comment));
                insert.ExecuteNonQuery();
            }
            using (var update = new SqlCommand(@"
UPDATE dbo.Attachment SET AktuelleVersion=AktuelleVersion+1, FileName=@fileName, OriginalName=@originalName,
    SizeBytes=@size, InhaltHash=@hash, LetzteAenderungAmUtc=SYSUTCDATETIME()
WHERE Id=@id", connection, transaction))
            {
                update.Parameters.AddWithValue("@id", oldFile.Id);
                update.Parameters.AddWithValue("@fileName", newFileName);
                update.Parameters.AddWithValue("@originalName", newOriginalName);
                update.Parameters.AddWithValue("@size", newSizeBytes);
                update.Parameters.AddWithValue("@hash", newHash);
                update.ExecuteNonQuery();
            }
            InsertActivity(connection, transaction, oldFile.Id, oldFile.DisplayTitle, "VersionHinzugefuegt",
                $"Version {oldFile.CurrentVersion + 1} hinzugefügt{(string.IsNullOrWhiteSpace(comment) ? "" : $": {comment.Trim()}")}");
            transaction.Commit();
        }

        public void DeleteDmsVersions(int attachmentId)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var command = new SqlCommand("DELETE FROM dbo.AttachmentVersion WHERE AttachmentId=@id", connection);
            command.Parameters.AddWithValue("@id", attachmentId);
            command.ExecuteNonQuery();
        }

        private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

        private static object DateValue(DateTime? value) => value.HasValue ? value.Value.Date : DBNull.Value;

        private static string? NormalizeKeywords(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return string.Join(", ", value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.CurrentCultureIgnoreCase));
        }

        private static string LoadSnapshot(SqlConnection connection, SqlTransaction transaction, int attachmentId)
        {
            using var command = new SqlCommand("SELECT COALESCE(NULLIF(Titel, ''), FileName) FROM dbo.Attachment WHERE Id=@id", connection, transaction);
            command.Parameters.AddWithValue("@id", attachmentId);
            return Convert.ToString(command.ExecuteScalar()) ?? $"Dokument #{attachmentId}";
        }

        private static void InsertActivity(SqlConnection connection, SqlTransaction transaction, int? attachmentId,
            string snapshot, string activityType, string description)
        {
            using var command = new SqlCommand(@"
INSERT INTO dbo.DmsAktivitaet
    (AttachmentId, DokumentTitelSnapshot, Art, ZeitpunktUtc, Benutzername, Beschreibung)
VALUES (@attachmentId, @snapshot, @activityType, SYSUTCDATETIME(), @username, @description)", connection, transaction);
            command.Parameters.AddWithValue("@attachmentId", (object?)attachmentId ?? DBNull.Value);
            command.Parameters.AddWithValue("@snapshot", snapshot);
            command.Parameters.AddWithValue("@activityType", activityType);
            command.Parameters.AddWithValue("@username", DbValue(CurrentUserContext.Username));
            command.Parameters.AddWithValue("@description", description);
            command.ExecuteNonQuery();
        }
    }
}
