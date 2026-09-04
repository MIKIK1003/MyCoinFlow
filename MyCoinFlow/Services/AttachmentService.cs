using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MyCoinFlow.Models;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Kapselt Dateisystem-Operationen für PDF-Anhänge (Move & Benennung)
    /// – keine UI, keine MessageBox hier.
    /// </summary>
    public class AttachmentService
    {
        private const string TransactionMetadataActivityType = "MetadatenAusTransaktionV2";
        private readonly DatabaseService _db = new();

        /// <summary>
        /// Verschiebt eine lokale PDF-Datei in den konfigurierten Zielordner,
        /// benennt sie nach Schema um und legt einen Attachment-Datensatz an.
        /// Gibt den finalen Vollpfad zurück.
        /// </summary>
        public string AttachAndSave(int transaktionId, string sourceFilePath)
        {
            if (transaktionId <= 0) throw new ArgumentException("Ungültige TransaktionId.", nameof(transaktionId));
            if (string.IsNullOrWhiteSpace(sourceFilePath)) throw new ArgumentException("Dateipfad fehlt.", nameof(sourceFilePath));
            if (!File.Exists(sourceFilePath)) throw new FileNotFoundException("Quelldatei nicht gefunden.", sourceFilePath);

            // NEU: Schema immer sicherstellen (Attachment + AttachmentText + AppSettings)
            _db.EnsureAttachmentsSchema();

            var ext = Path.GetExtension(sourceFilePath)?.ToLowerInvariant();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png" };
            if (string.IsNullOrWhiteSpace(ext) || !allowed.Contains(ext))
                throw new InvalidOperationException("Nur PDF/JPG/PNG sind erlaubt.");

            // Settings
            var (root, maxMb) = _db.GetAttachmentSettings();
            if (string.IsNullOrWhiteSpace(root))
            {
                var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                root = Path.Combine(doc, "MyCoinFlow", "Attachments");
            }

            // Größe
            var fi = new FileInfo(sourceFilePath);
            long limitBytes = (long)Math.Max(1, maxMb) * 1024L * 1024L;
            if (fi.Length > limitBytes)
                throw new InvalidOperationException($"Datei ist größer als das Limit von {maxMb} MB.");

            // Zielordner <root>\YYYY\MM
            var now = DateTime.Now;
            var folderRel = Path.Combine(now.ToString("yyyy"), now.ToString("MM"));
            var targetDir = Path.Combine(root, folderRel);
            Directory.CreateDirectory(targetDir);

            // Einheitliches ID-basiertes Namensschema (DOK-000123.pdf), analog AttachFromWatcher/
            // AttachFreestanding: die endgültige Nummer ist die Attachment-Id, die wir erst nach
            // dem DB-Insert kennen – daher erst unter Temp-Namen verschieben, dann umbenennen.
            string tempName = $"DOK-TMP-{Guid.NewGuid():N}{ext}";
            string tempPath = Path.Combine(targetDir, tempName);
            File.Move(sourceFilePath, tempPath);

            // DB: Attachment anlegen -> Id zurück
            int attachmentId = _db.SaveAttachment(
                transaktionId: transaktionId,
                fileName: tempName,
                originalName: Path.GetFileName(sourceFilePath),
                folderRel: folderRel.Replace('/', '\\'),
                sizeBytes: fi.Length,
                ocrStatus: null, // setzen wir gleich passend
                dokumentDatum: now
            );

            string finalName = $"DOK-{attachmentId:D6}{ext}";
            string targetPath = Path.Combine(targetDir, finalName);
            try
            {
                File.Move(tempPath, targetPath);
                _db.UpdateAttachmentFileName(attachmentId, finalName);
            }
            catch (IOException)
            {
                targetPath = tempPath;
            }

            RunOcrIndexing(attachmentId, targetPath, ext);
            InitializeWorkspaceFile(attachmentId, targetPath);
            LinkToTransaktion(attachmentId, transaktionId);

            return targetPath;
        }

        /// <summary>
        /// DMS: Legt ein Dokument OHNE Transaktionsbezug an (z.B. Vertrag, Police).
        /// Läuft dieselbe Ablage-/OCR-Logik wie AttachAndSave, aber ohne TransaktionId.
        /// Gibt Zielpfad und neue Attachment-Id zurück.
        /// </summary>
        public (string TargetPath, int AttachmentId) AttachFreestanding(string sourceFilePath, string? titel, string? kategorie)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath)) throw new ArgumentException("Dateipfad fehlt.", nameof(sourceFilePath));
            if (!File.Exists(sourceFilePath)) throw new FileNotFoundException("Quelldatei nicht gefunden.", sourceFilePath);

            _db.EnsureAttachmentsSchema();

            var ext = Path.GetExtension(sourceFilePath)?.ToLowerInvariant();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png" };
            if (string.IsNullOrWhiteSpace(ext) || !allowed.Contains(ext))
                throw new InvalidOperationException("Nur PDF/JPG/PNG sind erlaubt.");

            var (root, maxMb) = _db.GetAttachmentSettings();
            if (string.IsNullOrWhiteSpace(root))
            {
                var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                root = Path.Combine(doc, "MyCoinFlow", "Attachments");
            }

            var fi = new FileInfo(sourceFilePath);
            long limitBytes = (long)Math.Max(1, maxMb) * 1024L * 1024L;
            if (fi.Length > limitBytes)
                throw new InvalidOperationException($"Datei ist größer als das Limit von {maxMb} MB.");

            var now = DateTime.Now;
            var folderRel = Path.Combine("Frei", now.ToString("yyyy"), now.ToString("MM"));
            var targetDir = Path.Combine(root, folderRel);
            Directory.CreateDirectory(targetDir);

            // Einheitliches ID-basiertes Namensschema (DOK-000123.pdf), analog AttachFromWatcher.
            string tempName = $"DOK-TMP-{Guid.NewGuid():N}{ext}";
            string tempPath = Path.Combine(targetDir, tempName);
            File.Move(sourceFilePath, tempPath);

            int attachmentId = _db.SaveAttachment(
                transaktionId: null,
                fileName: tempName,
                originalName: Path.GetFileName(sourceFilePath),
                folderRel: folderRel.Replace('/', '\\'),
                sizeBytes: fi.Length,
                ocrStatus: null,
                titel: titel,
                kategorie: kategorie,
                dokumentDatum: now);

            string finalName = $"DOK-{attachmentId:D6}{ext}";
            string targetPath = Path.Combine(targetDir, finalName);
            try
            {
                File.Move(tempPath, targetPath);
                _db.UpdateAttachmentFileName(attachmentId, finalName);
            }
            catch (IOException)
            {
                targetPath = tempPath;
            }

            RunOcrIndexing(attachmentId, targetPath, ext);
            InitializeWorkspaceFile(attachmentId, targetPath);

            return (targetPath, attachmentId);
        }

        /// <summary>
        /// Kopiert eine PDF-/Bilddatei in die vorhandene DMS-Ablage und verknüpft sie über
        /// die generische EntityType-/EntityId-Grenze. Die Quelldatei bleibt unverändert.
        /// </summary>
        public (string TargetPath, int AttachmentId) AttachEntityCopy(
            string sourceFilePath,
            string entityType,
            int entityId,
            string? titel,
            string? kategorie,
            DateTime? dokumentDatum = null)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
                throw new ArgumentException("Dateipfad fehlt.", nameof(sourceFilePath));
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException("Quelldatei nicht gefunden.", sourceFilePath);
            entityType = (entityType ?? string.Empty).Trim();
            if (entityType.Length is < 1 or > 32)
                throw new ArgumentException("Der DMS-Entitätstyp muss 1 bis 32 Zeichen enthalten.", nameof(entityType));
            if (entityId <= 0)
                throw new ArgumentException("Die DMS-Entitäts-ID ist ungültig.", nameof(entityId));

            _db.EnsureAttachmentsSchema();
            var ext = Path.GetExtension(sourceFilePath)?.ToLowerInvariant();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".pdf", ".jpg", ".jpeg", ".png" };
            if (string.IsNullOrWhiteSpace(ext) || !allowed.Contains(ext))
                throw new InvalidOperationException("Nur PDF/JPG/PNG sind als DMS-Beilage erlaubt.");

            var (root, maxMb) = _db.GetAttachmentSettings();
            if (string.IsNullOrWhiteSpace(root))
            {
                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                root = Path.Combine(documents, "MyCoinFlow", "Attachments");
            }

            var source = new FileInfo(sourceFilePath);
            if (source.Length > (long)Math.Max(1, maxMb) * 1024L * 1024L)
                throw new InvalidOperationException($"Datei ist größer als das Limit von {maxMb} MB.");

            var date = (dokumentDatum ?? DateTime.Today).Date;
            var folderRel = Path.Combine("Fakturierung", date.ToString("yyyy"), date.ToString("MM"));
            var targetDirectory = Path.Combine(root, folderRel);
            Directory.CreateDirectory(targetDirectory);
            var temporaryName = $"DOK-TMP-{Guid.NewGuid():N}{ext}";
            var temporaryPath = Path.Combine(targetDirectory, temporaryName);
            File.Copy(sourceFilePath, temporaryPath, overwrite: false);

            int attachmentId;
            try
            {
                attachmentId = _db.SaveAttachment(
                    transaktionId: null,
                    fileName: temporaryName,
                    originalName: source.Name,
                    folderRel: folderRel.Replace('/', '\\'),
                    sizeBytes: source.Length,
                    ocrStatus: null,
                    entityType: entityType,
                    entityId: entityId,
                    titel: string.IsNullOrWhiteSpace(titel) ? null : titel.Trim(),
                    kategorie: string.IsNullOrWhiteSpace(kategorie) ? null : kategorie.Trim(),
                    dokumentDatum: date);
            }
            catch
            {
                try { File.Delete(temporaryPath); } catch { }
                throw;
            }

            var finalName = $"DOK-{attachmentId:D6}{ext}";
            var targetPath = Path.Combine(targetDirectory, finalName);
            try
            {
                File.Move(temporaryPath, targetPath);
                _db.UpdateAttachmentFileName(attachmentId, finalName);
            }
            catch (IOException)
            {
                targetPath = temporaryPath;
            }

            RunOcrIndexing(attachmentId, targetPath, ext);
            InitializeWorkspaceFile(attachmentId, targetPath);
            return (targetPath, attachmentId);
        }

        /// <summary>
        /// DMS-Arbeitsordner-Überwachung: legt ein Dokument mit einheitlichem, ID-basiertem
        /// Dateinamen (DOK-000123.pdf – gleiche Länge für alle Dokumente, keine Inhalts-Infos im
        /// Namen; Adresse/Betrag/Titel sind stattdessen als Grid-Spalten sichtbar) im
        /// Ablageordner unter Frei\Jahr\Monat des erkannten Dokumentdatums ab.
        /// Text/OcrStatus wurden von DmsWatcherService bereits für die Datums-/Titel-/Betrags-
        /// Erkennung extrahiert und werden hier nur noch gespeichert (kein zweiter OCR-Lauf).
        /// Der erkannte Titel wandert ins Titel-Feld (Anzeige), nicht in den Dateinamen.
        /// Gibt Zielpfad und neue Attachment-Id zurück.
        /// </summary>
        public (string TargetPath, int AttachmentId) AttachFromWatcher(string sourceFilePath, DateTime dokumentDatum, string titelSlug,
            string? extractedText, string? textLang, string ocrStatus)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath)) throw new ArgumentException("Dateipfad fehlt.", nameof(sourceFilePath));
            if (!File.Exists(sourceFilePath)) throw new FileNotFoundException("Quelldatei nicht gefunden.", sourceFilePath);

            _db.EnsureAttachmentsSchema();

            var ext = Path.GetExtension(sourceFilePath)?.ToLowerInvariant();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png" };
            if (string.IsNullOrWhiteSpace(ext) || !allowed.Contains(ext))
                throw new InvalidOperationException("Nur PDF/JPG/PNG sind erlaubt.");

            var (root, maxMb) = _db.GetAttachmentSettings();
            if (string.IsNullOrWhiteSpace(root))
            {
                var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                root = Path.Combine(doc, "MyCoinFlow", "Attachments");
            }

            var fi = new FileInfo(sourceFilePath);
            long limitBytes = (long)Math.Max(1, maxMb) * 1024L * 1024L;
            if (fi.Length > limitBytes)
                throw new InvalidOperationException($"Datei ist größer als das Limit von {maxMb} MB.");

            var folderRel = Path.Combine("Frei", dokumentDatum.ToString("yyyy"), dokumentDatum.ToString("MM"));
            var targetDir = Path.Combine(root, folderRel);
            Directory.CreateDirectory(targetDir);

            // Die endgültige DOK-Nummer ist die Attachment-Id – die kennen wir erst nach dem
            // DB-Insert. Daher: erst unter temporärem Namen ins Ziel verschieben, dann Datensatz
            // anlegen, dann auf DOK-{Id} umbenennen und den Dateinamen in der DB nachziehen.
            string tempName = $"DOK-TMP-{Guid.NewGuid():N}{ext}";
            string tempPath = Path.Combine(targetDir, tempName);
            File.Move(sourceFilePath, tempPath);

            int attachmentId = _db.SaveAttachment(
                transaktionId: null,
                fileName: tempName,
                originalName: Path.GetFileName(sourceFilePath),
                folderRel: folderRel.Replace('/', '\\'),
                sizeBytes: fi.Length,
                ocrStatus: ocrStatus,
                titel: string.IsNullOrWhiteSpace(titelSlug) ? null : titelSlug,
                dokumentDatum: dokumentDatum);

            string finalName = $"DOK-{attachmentId:D6}{ext}";
            string targetPath = Path.Combine(targetDir, finalName);
            try
            {
                File.Move(tempPath, targetPath);
                _db.UpdateAttachmentFileName(attachmentId, finalName);
            }
            catch (IOException)
            {
                targetPath = tempPath; // Umbenennen fehlgeschlagen – Temp-Name bleibt, DB ist konsistent
            }

            if (!string.IsNullOrWhiteSpace(extractedText))
                _db.UpsertAttachmentText(attachmentId, extractedText, textLang);

            InitializeWorkspaceFile(attachmentId, targetPath);

            return (targetPath, attachmentId);
        }

        /// <summary>
        /// Benennt bestehende Attachments (inkl. der noch aus der Zeit vor dem einheitlichen
        /// Schema stammenden TX-{Id}-H{Hash}-Dateien) auf DOK-{Id:D6}{ext} um – Datei auf der
        /// Platte UND FileName in der DB. Bereits korrekt benannte Einträge werden übersprungen
        /// (idempotent, kann gefahrlos mehrfach laufen).
        /// </summary>
        public (int Renamed, int AlreadyOk, int Missing) MigrateAllToUniformNaming()
        {
            var (root, _) = _db.GetAttachmentSettings();
            if (string.IsNullOrWhiteSpace(root))
            {
                var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                root = Path.Combine(doc, "MyCoinFlow", "Attachments");
            }

            int renamed = 0, alreadyOk = 0, missing = 0;

            foreach (var (id, fileName, folderRel) in _db.LoadAllAttachmentFilePaths())
            {
                var ext = Path.GetExtension(fileName);
                var targetName = $"DOK-{id:D6}{ext}";

                if (string.Equals(fileName, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    alreadyOk++;
                    continue;
                }

                var dir = Path.Combine(root, folderRel);
                var oldPath = Path.Combine(dir, fileName);
                var newPath = Path.Combine(dir, targetName);

                if (!File.Exists(oldPath) || File.Exists(newPath))
                {
                    // Datei fehlt bereits auf der Platte, oder das Zielnamen existiert schon
                    // (sollte wegen eindeutiger Id nicht vorkommen) – zur Sicherheit überspringen,
                    // statt DB und Dateisystem auseinanderlaufen zu lassen.
                    missing++;
                    continue;
                }

                File.Move(oldPath, newPath);
                _db.UpdateAttachmentFileName(id, targetName);
                renamed++;
            }

            return (renamed, alreadyOk, missing);
        }

        /// <summary>
        /// Öffnet ein einzelnes Dokument direkt (DMS-Übersicht: ein Klick = eine Datei).
        /// </summary>
        public void OpenAttachment(int attachmentId)
        {
            var info = _db.GetAttachmentById(attachmentId);
            if (info == null) return;

            var (root, _) = _db.GetAttachmentSettings();
            if (string.IsNullOrWhiteSpace(root))
            {
                var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                root = Path.Combine(doc, "MyCoinFlow", "Attachments");
            }

            var full = Path.Combine(root, info.Value.FolderRel, info.Value.FileName);
            if (File.Exists(full))
            {
                Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
                _db.LogDmsActivity(attachmentId, "Geoeffnet", "Dokument geöffnet");
            }
            else
            {
                var dir = Path.Combine(root, info.Value.FolderRel);
                if (Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
        }

        /// <summary>
        /// Führt die Text-/OCR-Indexierung für ein frisch gespeichertes Attachment aus
        /// (gemeinsame Logik für AttachAndSave und AttachFreestanding).
        /// </summary>
        private void RunOcrIndexing(int attachmentId, string targetPath, string ext)
        {
            try
            {
                if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var text = OcrService.ExtractTextFromPdf_NoOcr(targetPath);

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // Erst Status setzen, dann Text upserten (Reihenfolge robust, falls Text sehr lang ist)
                        _db.UpdateAttachmentOcrStatus(attachmentId, "Text");
                        _db.UpsertAttachmentText(attachmentId, text, "pdf");
                    }
                    else
                    {
                        _db.UpdateAttachmentOcrStatus(attachmentId, "Image"); // vermutlich Scan-PDF
                    }
                }
                else
                {
                    // Bilder markieren wir vorerst als "Image" (OCR folgt später)
                    _db.UpdateAttachmentOcrStatus(attachmentId, "Image");
                }
            }
            catch
            {
                // Index-/Erkennungsfehler nicht durchreichen; OcrStatus bleibt wie gesetzt oder null.
                // Ein "Index aktualisieren"-Lauf kann das später nachziehen.
            }
        }


        /// <summary>
        /// Öffnet das einzige PDF direkt im Standardviewer oder – bei mehreren – den Ordner im Explorer.
        /// </summary>
        public void OpenFirstOrFolder(int transaktionId)
        {
            var list = _db.LoadAttachmentsByTransaktionId(transaktionId);
            if (list.Count == 0) return;

            var (root, _) = _db.GetAttachmentSettings();
            if (string.IsNullOrWhiteSpace(root))
            {
                var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                root = Path.Combine(doc, "MyCoinFlow", "Attachments");
            }

            if (list.Count == 1)
            {
                var file = Path.Combine(root, list[0].FolderRel, list[0].FileName);
                if (File.Exists(file))
                {
                    Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                }
                else
                {
                    var dir = Path.Combine(root, list[0].FolderRel);
                    if (Directory.Exists(dir))
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                }
            }
            else
            {
                var dir = Path.Combine(root, list[0].FolderRel);
                if (Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
        }

        /// <summary>
        /// Verknüpft ein Dokument mit einer Transaktion (automatisches Matching oder manuelles
        /// Zuweisen) und leitet daraus dieselben DMS-Metadaten ab. Der Dateiname bleibt
        /// unverändert (einheitliches DOK-{Id}-Schema).
        /// </summary>
        public void LinkToTransaktion(int attachmentId, int transaktionId, bool requireUnlinked = false)
        {
            _db.EnsureAttachmentsSchema();
            var document = _db.LoadAllDocuments(null, null).FirstOrDefault(value => value.Id == attachmentId)
                ?? throw new InvalidOperationException("Das DMS-Dokument wurde nicht gefunden.");
            var transaction = _db.HoleTransaktion(transaktionId)
                ?? throw new InvalidOperationException("Die ausgewählte Transaktion wurde nicht gefunden.");
            var documentType = DetermineDmsDocumentType(transaction);
            var changes = BuildTransactionMetadata(document, transaction, documentType);
            var relation = documentType == DmsBelegart.Gutschrift ? "Gutschrift" : "Zahlung";

            _db.LinkAttachmentToTransaktionAndUpdateMetadata(
                attachmentId,
                transaktionId,
                changes,
                requireUnlinked,
                $"Mit Transaktion #{transaktionId} verknüpft ({relation})");
        }

        /// <summary>
        /// Ergänzt einmalig bereits früher verknüpfte Dokumente, bei denen die damalige
        /// Verknüpfung noch keine Transaktions-Metadaten übernommen hat.
        /// </summary>
        public (int Updated, int Failed) RefreshLinkedTransactionMetadata()
        {
            _db.EnsureAttachmentsSchema();
            var updated = 0;
            var failed = 0;
            var documents = _db.LoadAllDocuments(null, null)
                .Where(value => value.EntityType == "Transaktion" && (value.EntityId ?? value.TransaktionId) > 0)
                .ToList();

            foreach (var document in documents)
            {
                try
                {
                    if (_db.HasDmsActivity(document.Id, TransactionMetadataActivityType))
                        continue;
                    var transactionId = document.EntityId ?? document.TransaktionId;
                    var transaction = transactionId.HasValue ? _db.HoleTransaktion(transactionId.Value) : null;
                    if (transaction is null)
                    {
                        failed++;
                        continue;
                    }

                    var documentType = DetermineDmsDocumentType(transaction);
                    var changes = BuildTransactionMetadata(document, transaction, documentType);
                    _db.UpdateDmsDocument(document.Id, changes);
                    _db.LogDmsActivity(document.Id, TransactionMetadataActivityType,
                        "Bestehende Verknüpfung: Dokumentdaten aus der Transaktion ergänzt");
                    updated++;
                }
                catch
                {
                    failed++;
                }
            }

            return (updated, failed);
        }

        private DmsDocumentChanges BuildTransactionMetadata(
            DmsDocument document,
            Transaktion transaction,
            DmsBelegart documentType)
        {
            var transactionParty = NormalizeWhitespace(transaction.AdresseName);
            var party = FirstNonEmpty(
                transactionParty,
                document.EigeneAdresseName,
                transaction.BankName);
            // Sobald die Buchung eine erkannte Gegenpartei besitzt, ist diese für den
            // DMS-Titel verbindlicher als der OCR-Titel. OCR findet auf Rechnungen häufig
            // zuerst den Rechnungsempfänger (z. B. den eigenen Namen) statt des Absenders.
            var title = !string.IsNullOrWhiteSpace(transactionParty) || IsGenericDocumentTitle(document)
                ? BuildTransactionTitle(documentType, party, transaction.Notiz, transaction.Datum)
                : document.Titel;
            var description = MergeTransactionDescription(
                document.Beschreibung,
                BuildTransactionDescription(documentType, transaction, party));
            var keywords = AddKeyword(document.Schlagwoerter, documentType.ToString());

            return new DmsDocumentChanges(
                title,
                documentType == DmsBelegart.Gutschrift ? "Gutschriften" : "Rechnungen",
                documentType,
                description,
                keywords,
                string.IsNullOrWhiteSpace(document.Notiz) ? NormalizeWhitespace(transaction.Notiz) : document.Notiz,
                document.Bearbeitungsstatus,
                document.Verantwortlich,
                document.DokumentDatum ?? transaction.Datum.Date,
                document.ErkannterBetrag ?? Math.Abs(transaction.Betrag),
                transaction.AdresseId ?? document.AdresseId,
                document.IstGarantieschein,
                document.GarantieAblaufDatum,
                document.ExplizitFaelligAm,
                document.AufbewahrenBis);
        }

        private DmsBelegart DetermineDmsDocumentType(Transaktion transaction)
        {
            var fromDirection = GetAccountDirection(transaction.VonKontoId);
            var toDirection = GetAccountDirection(transaction.NachKontoId);
            if (string.Equals(fromDirection, "Einnahme", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toDirection, "Einnahme", StringComparison.OrdinalIgnoreCase))
                return DmsBelegart.Gutschrift;

            // Die importierten Belastungen laufen Bank/Durchlauf -> Kostenkonto.
            if (string.Equals(toDirection, "Ausgabe", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fromDirection, "Ausgabe", StringComparison.OrdinalIgnoreCase))
                return DmsBelegart.Rechnung;

            // Rückzahlungen/Gutschriften laufen in Gegenrichtung: Kostenkonto ->
            // Bank/Kreditkarten-/Durchlaufseite.
            if (string.Equals(fromDirection, "Ausgabe", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(toDirection, "Ausgabe", StringComparison.OrdinalIgnoreCase))
                return DmsBelegart.Gutschrift;

            if (transaction.VonKontoId.HasValue && !transaction.NachKontoId.HasValue)
                return DmsBelegart.Gutschrift;
            if (!transaction.VonKontoId.HasValue && transaction.NachKontoId.HasValue)
                return DmsBelegart.Rechnung;

            // Nur bei strukturell nicht eindeutigen Alt-/Manuellbuchungen dient der
            // Buchungstext als zusätzliche Evidenz. So kann z. B. das Wort "Gutschrift"
            // in einer normalen Rechnungsnotiz keine klare Belastung umklassifizieren.
            var note = NormalizeWhitespace(transaction.Notiz)?.ToUpperInvariant() ?? string.Empty;
            string[] creditTerms =
            {
                "GUTSCHRIFT", "RÜCKERSTATT", "RUECKERSTATT", "RÜCKVERGÜT", "RUECKVERGUET",
                "REFUND", "STORNO", "CREDIT NOTE"
            };
            if (creditTerms.Any(term => note.Contains(term, StringComparison.Ordinal)))
                return DmsBelegart.Gutschrift;

            return DmsBelegart.Rechnung;
        }

        private string? GetAccountDirection(int? accountId)
        {
            if (!accountId.HasValue) return null;
            var accountNumber = _db.HoleKontonummerByKontoId(accountId.Value);
            return accountNumber.HasValue ? _db.FindeRegelFuerKontonummer(accountNumber.Value)?.Richtung : null;
        }

        private static string BuildTransactionTitle(
            DmsBelegart documentType,
            string? party,
            string? note,
            DateTime transactionDate)
        {
            var subject = FirstNonEmpty(party, NormalizeWhitespace(note));
            if (!string.IsNullOrWhiteSpace(subject) && subject.Length > 130)
                subject = subject[..130].TrimEnd();
            var title = string.IsNullOrWhiteSpace(subject)
                ? $"{documentType} vom {transactionDate:dd.MM.yyyy}"
                : $"{documentType} – {subject}";
            return title.Length <= 200 ? title : title[..200].TrimEnd();
        }

        private static string BuildTransactionDescription(
            DmsBelegart documentType,
            Transaktion transaction,
            string? party)
        {
            var movement = documentType == DmsBelegart.Gutschrift ? "Gutschrift von" : "Zahlung an";
            var counterpart = string.IsNullOrWhiteSpace(party) ? "unbekannte Gegenpartei" : party;
            var amount = Math.Abs(transaction.Betrag).ToString("N2", CultureInfo.GetCultureInfo("de-CH"));
            var builder = new StringBuilder($"Transaktionsbezug: {movement} {counterpart} am {transaction.Datum:dd.MM.yyyy} über CHF {amount}.");
            var note = NormalizeWhitespace(transaction.Notiz);
            if (!string.IsNullOrWhiteSpace(note))
                builder.Append(" Buchungstext: ").Append(note).Append('.');
            if (!string.IsNullOrWhiteSpace(transaction.BankName)
                && !string.Equals(transaction.BankName, party, StringComparison.CurrentCultureIgnoreCase))
                builder.Append(" Geldinstitut: ").Append(transaction.BankName.Trim()).Append('.');
            return builder.Length <= 1000 ? builder.ToString() : builder.ToString(0, 1000).TrimEnd();
        }

        private static string MergeTransactionDescription(string? current, string automaticDescription)
        {
            const string marker = "Transaktionsbezug:";
            var existing = current?.Trim() ?? string.Empty;
            var markerIndex = existing.IndexOf(marker, StringComparison.CurrentCultureIgnoreCase);
            if (markerIndex >= 0)
                existing = existing[..markerIndex].TrimEnd();
            var merged = string.IsNullOrWhiteSpace(existing)
                ? automaticDescription
                : existing + Environment.NewLine + automaticDescription;
            return merged.Length <= 1000 ? merged : merged[..1000].TrimEnd();
        }

        private static string? AddKeyword(string? current, string keyword)
        {
            var values = (current ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (!values.Contains(keyword, StringComparer.CurrentCultureIgnoreCase))
                values.Add(keyword);
            var result = string.Join(", ", values);
            return result.Length <= 1000 ? result : result[..1000].TrimEnd(' ', ',');
        }

        private static bool IsGenericDocumentTitle(DmsDocument document)
        {
            if (string.IsNullOrWhiteSpace(document.Titel)) return true;
            var titleStem = Path.GetFileNameWithoutExtension(document.Titel).Trim();
            var fileStem = Path.GetFileNameWithoutExtension(document.FileName);
            var originalStem = Path.GetFileNameWithoutExtension(document.OriginalName ?? string.Empty);
            if (string.Equals(titleStem, fileStem, StringComparison.CurrentCultureIgnoreCase)
                || (!string.IsNullOrWhiteSpace(originalStem)
                    && string.Equals(titleStem, originalStem, StringComparison.CurrentCultureIgnoreCase)))
                return true;

            var upper = titleStem.ToUpperInvariant();
            string[] scannerPrefixes = { "WLMAGE_", "WLMAGE-", "IMG_", "IMG-", "IMAGE_", "SCAN_", "SCAN-", "DOK-", "DOK_", "DOCUMENT_", "DOKUMENT_" };
            return scannerPrefixes.Any(prefix => upper.StartsWith(prefix, StringComparison.Ordinal)
                && upper[prefix.Length..].Any(char.IsDigit));
        }

        private static string? NormalizeWhitespace(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string? FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

        public void UnlinkFromTransaktion(int attachmentId)
        {
            _db.UnlinkAttachment(attachmentId);
            _db.LogDmsActivity(attachmentId, "VerknuepfungGeloest", "Verknüpfung zur Transaktion gelöst");
        }

        /// <summary>
        /// Entfernt das Dokument aus dem aktiven DMS. Dateien werden in den wiederherstellbaren
        /// Archivbereich verschoben; eine aktive Aufbewahrungsfrist sperrt die Aktion.
        /// </summary>
        public void DeleteAttachment(int attachmentId)
        {
            if (attachmentId <= 0) return;

            var info = _db.GetDmsFileInfo(attachmentId);
            if (info == null) return;
            if (info.RetainUntil is { } retainUntil && retainUntil.Date > DateTime.Today)
                throw new InvalidOperationException($"Das Dokument ist bis {retainUntil:dd.MM.yyyy} aufbewahrungsgesperrt.");

            var root = GetStorageRoot();
            var sourcePath = SafePath(root, Path.Combine(info.FolderRel, info.FileName));
            var archiveFolder = SafePath(root, Path.Combine("Archiv", info.FolderRel));
            Directory.CreateDirectory(archiveFolder);
            var archivePath = UniquePath(archiveFolder, info.FileName);

            string? versionsSource = null;
            string? versionsArchive = null;
            if (File.Exists(sourcePath)) File.Move(sourcePath, archivePath);
            try
            {
                versionsSource = SafePath(root, Path.Combine("_Versionen", attachmentId.ToString()));
                if (Directory.Exists(versionsSource))
                {
                    var versionsArchiveRoot = SafePath(root, Path.Combine("Archiv", "_Versionen"));
                    Directory.CreateDirectory(versionsArchiveRoot);
                    versionsArchive = UniqueDirectory(versionsArchiveRoot, attachmentId.ToString());
                    Directory.Move(versionsSource, versionsArchive);
                }

                _db.LogDmsActivity(attachmentId, "Geloescht", "Dokument aus dem aktiven DMS entfernt");
                _db.DeleteDmsVersions(attachmentId);
                _db.DeleteAttachment(attachmentId);
            }
            catch
            {
                if (versionsArchive != null && versionsSource != null && Directory.Exists(versionsArchive) && !Directory.Exists(versionsSource))
                    Directory.Move(versionsArchive, versionsSource);
                if (File.Exists(archivePath) && !File.Exists(sourcePath))
                    File.Move(archivePath, sourcePath);
                throw;
            }
        }

        public void ReplaceWithNewVersion(int attachmentId, string sourceFilePath, string? comment)
        {
            if (!File.Exists(sourceFilePath)) throw new FileNotFoundException("Die neue Dokumentversion wurde nicht gefunden.", sourceFilePath);
            var info = _db.GetDmsFileInfo(attachmentId) ?? throw new InvalidOperationException("Das Dokument wurde nicht gefunden.");
            var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            if (!new[] { ".pdf", ".jpg", ".jpeg", ".png" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Nur PDF/JPG/PNG sind erlaubt.");

            var (_, maxMb) = _db.GetAttachmentSettings();
            var newFile = new FileInfo(sourceFilePath);
            if (newFile.Length > (long)Math.Max(1, maxMb) * 1024L * 1024L)
                throw new InvalidOperationException($"Datei ist größer als das Limit von {maxMb} MB.");

            var root = GetStorageRoot();
            var currentPath = SafePath(root, Path.Combine(info.FolderRel, info.FileName));
            if (!File.Exists(currentPath)) throw new FileNotFoundException("Die aktuelle Dokumentdatei wurde nicht gefunden.", currentPath);

            var versionFolderRel = Path.Combine("_Versionen", attachmentId.ToString(), $"v{info.CurrentVersion:D3}");
            var versionFolder = SafePath(root, versionFolderRel);
            Directory.CreateDirectory(versionFolder);
            var archivedPath = UniquePath(versionFolder, info.FileName);
            var newFileName = $"DOK-{attachmentId:D6}{extension}";
            var newPath = SafePath(root, Path.Combine(info.FolderRel, newFileName));
            if (!string.Equals(currentPath, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(newPath))
                newPath = UniquePath(Path.GetDirectoryName(newPath)!, newFileName);

            File.Move(currentPath, archivedPath);
            try
            {
                File.Copy(sourceFilePath, newPath, overwrite: false);
                var hash = ComputeHash(newPath);
                _db.CommitDmsVersion(info, Path.GetFileName(archivedPath), versionFolderRel,
                    Path.GetFileName(newPath), Path.GetFileName(sourceFilePath), newFile.Length, hash, comment);
            }
            catch
            {
                if (File.Exists(newPath)) File.Delete(newPath);
                if (File.Exists(archivedPath) && !File.Exists(currentPath)) File.Move(archivedPath, currentPath);
                throw;
            }
        }

        public void OpenVersion(DmsVersionEntry version)
        {
            if (version.IsCurrent)
            {
                OpenAttachment(version.AttachmentId);
                return;
            }

            var fullPath = SafePath(GetStorageRoot(), Path.Combine(version.FolderRel, version.FileName));
            if (!File.Exists(fullPath)) throw new FileNotFoundException("Die archivierte Version wurde nicht gefunden.", fullPath);
            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
        }

        /// <summary>
        /// Kopiert die aktuelle Fassung aller markierten Steuerunterlagen in einen gemeinsamen
        /// Zielordner. Bereits vorhandene Dateien werden über ihren SHA-256-Inhalt erkannt;
        /// gleichnamige, aber unterschiedliche Dokumente erhalten eine laufende Nummer.
        /// </summary>
        public async Task<DmsTaxExportResult> ExportTaxDocumentsAsync(
            IReadOnlyCollection<DmsDocument> documents,
            string targetFolder,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(targetFolder))
                throw new ArgumentException("Der Zielordner fehlt.", nameof(targetFolder));

            var taxDocuments = documents.Where(document => document.IstSteuerunterlage).ToList();
            var fullTargetFolder = Path.GetFullPath(targetFolder);
            Directory.CreateDirectory(fullTargetFolder);

            var existingHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var existingFile in Directory.EnumerateFiles(fullTargetFolder, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    existingHashes.Add(await ComputeHashAsync(existingFile, cancellationToken));
                }
                catch (IOException exception)
                {
                    throw new IOException($"Die vorhandene Datei „{Path.GetFileName(existingFile)}“ konnte für die Dublettenprüfung nicht gelesen werden.", exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    throw new UnauthorizedAccessException($"Auf die vorhandene Datei „{Path.GetFileName(existingFile)}“ kann für die Dublettenprüfung nicht zugegriffen werden.", exception);
                }
            }

            var storageRoot = GetStorageRoot();
            var copied = 0;
            var duplicates = 0;
            var missing = 0;
            var renamed = 0;

            foreach (var document in taxDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourcePath = SafePath(storageRoot, Path.Combine(document.FolderRel, document.FileName));
                if (!File.Exists(sourcePath))
                {
                    missing++;
                    continue;
                }

                var sourceHash = string.IsNullOrWhiteSpace(document.InhaltHash)
                    ? await ComputeHashAsync(sourcePath, cancellationToken)
                    : document.InhaltHash;
                if (existingHashes.Contains(sourceHash))
                {
                    duplicates++;
                    continue;
                }

                var desiredFileName = BuildTaxExportFileName(document);
                var destinationPath = UniquePath(fullTargetFolder, desiredFileName);
                if (!string.Equals(Path.GetFileName(destinationPath), desiredFileName, StringComparison.OrdinalIgnoreCase))
                    renamed++;

                try
                {
                    await using var source = new FileStream(
                        sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                        81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await using var destination = new FileStream(
                        destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await source.CopyToAsync(destination, cancellationToken);
                }
                catch
                {
                    if (File.Exists(destinationPath))
                        File.Delete(destinationPath);
                    throw;
                }

                existingHashes.Add(sourceHash);
                copied++;
            }

            return new DmsTaxExportResult(taxDocuments.Count, copied, duplicates, missing, renamed);
        }

        public void InitializeExistingDocumentHashes()
        {
            var root = GetStorageRoot();
            foreach (var info in _db.LoadDmsFilesWithoutHash())
            {
                try
                {
                    var path = SafePath(root, Path.Combine(info.FolderRel, info.FileName));
                    InitializeWorkspaceFile(info.Id, path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private void InitializeWorkspaceFile(int attachmentId, string path)
        {
            if (!File.Exists(path)) return;
            var file = new FileInfo(path);
            _db.InitializeDmsFile(attachmentId, file.Length, ComputeHash(path));
        }

        private string GetStorageRoot()
        {
            var (root, _) = _db.GetAttachmentSettings();
            if (!string.IsNullOrWhiteSpace(root)) return Path.GetFullPath(root);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MyCoinFlow", "Attachments");
        }

        private static string SafePath(string root, string relativePath)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
            if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Der Dokumentpfad liegt außerhalb des konfigurierten DMS-Ordners.");
            return fullPath;
        }

        private static string ComputeHash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        }

        private static string BuildTaxExportFileName(DmsDocument document)
        {
            var extension = Path.GetExtension(document.FileName);
            var title = document.TitelAnzeige.Trim();
            if (!string.IsNullOrWhiteSpace(extension) && title.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                title = title[..^extension.Length];

            var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
            var sanitized = new string(title
                .Select(character => invalidCharacters.Contains(character) || char.IsControl(character) ? '_' : character)
                .ToArray())
                .Trim()
                .TrimEnd('.', ' ');
            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = $"Dokument-{document.Id:D6}";
            if (sanitized.Length > 180)
                sanitized = sanitized[..180].TrimEnd('.', ' ');

            var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            };
            if (reservedNames.Contains(sanitized))
                sanitized = "_" + sanitized;
            return sanitized + extension.ToLowerInvariant();
        }

        private static string UniquePath(string folder, string fileName)
        {
            var candidate = Path.Combine(folder, fileName);
            if (!File.Exists(candidate)) return candidate;
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            for (var index = 2; ; index++)
            {
                candidate = Path.Combine(folder, $"{stem}-{index}{extension}");
                if (!File.Exists(candidate)) return candidate;
            }
        }

        private static string UniqueDirectory(string folder, string name)
        {
            var candidate = Path.Combine(folder, name);
            if (!Directory.Exists(candidate)) return candidate;
            return Path.Combine(folder, $"{name}-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
        }



    }
}
