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
        /// Zuweisen). Ein gefundenes Dokument ist im DMS-Kontext immer eine Rechnung, daher wird
        /// die Kategorie gleich auf "Rechnungen" gesetzt. Der Dateiname bleibt unverändert
        /// (einheitliches DOK-{Id}-Schema) – Adresse/Betrag der Transaktion sind über die
        /// Grid-Spalten sichtbar, nicht über den Dateinamen.
        /// </summary>
        public void LinkToTransaktion(int attachmentId, int transaktionId)
        {
            var info = _db.GetAttachmentById(attachmentId);
            if (info != null)
                _db.UpdateAttachmentFileNameAndKategorie(attachmentId, info.Value.FileName, "Rechnungen");

            _db.LinkAttachmentToTransaktion(attachmentId, transaktionId);
            _db.LogDmsActivity(attachmentId, "Verknuepft", $"Mit Transaktion #{transaktionId} verknüpft");
        }

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
