using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

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

            var ext = Path.GetExtension(sourceFilePath)?.ToLowerInvariant();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png" };
            if (string.IsNullOrWhiteSpace(ext) || !allowed.Contains(ext))
                throw new InvalidOperationException("Nur PDF/JPG/PNG sind erlaubt.");

            // Settings laden
            var (root, maxMb) = _db.GetAttachmentSettings();
            if (string.IsNullOrWhiteSpace(root))
            {
                var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                root = Path.Combine(doc, "MyCoinFlow", "Attachments");
            }

            // Größe prüfen
            var fi = new FileInfo(sourceFilePath);
            long limitBytes = (long)Math.Max(1, maxMb) * 1024L * 1024L;
            if (fi.Length > limitBytes)
                throw new InvalidOperationException($"Datei ist größer als das Limit von {maxMb} MB.");

            // Ordnerstruktur: <root>\YYYY\MM
            var now = DateTime.Now;
            var folderRel = Path.Combine(now.ToString("yyyy"), now.ToString("MM"));
            var targetDir = Path.Combine(root, folderRel);
            Directory.CreateDirectory(targetDir);

            // Dateiname: TX-{Id}-H{Hash12}{ext}  (Endung bleibt erhalten)
            var hash = _db.GetImportHashForTransaktion(transaktionId);
            string hash12 = !string.IsNullOrWhiteSpace(hash)
                ? (hash.Length <= 12 ? hash : hash.Substring(hash.Length - 12))
                : "D" + now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

            string baseNameNoExt = $"TX-{transaktionId}-H{hash12}";
            string baseName = baseNameNoExt + ext;              // <- Endung aus Quelle
            string targetPath = Path.Combine(targetDir, baseName);

            if (File.Exists(targetPath))
            {
                // Bei Kollision: -2, -3, ...
                int n = 2;
                while (true)
                {
                    string candidate = Path.Combine(targetDir, $"{baseNameNoExt}-{n}{ext}");
                    if (!File.Exists(candidate)) { targetPath = candidate; break; }
                    n++;
                }
            }

            // Move (nicht kopieren)
            File.Move(sourceFilePath, targetPath);

            // DB-Eintrag – Bilder als "Image" markieren, PDF vorerst ohne Status
            string? ocrStatus = (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) ? null : "Image";

            _db.SaveAttachment(
                transaktionId: transaktionId,
                fileName: Path.GetFileName(targetPath),
                originalName: Path.GetFileName(sourceFilePath),
                folderRel: folderRel.Replace('/', '\\'),
                sizeBytes: fi.Length,
                ocrStatus: ocrStatus
            );

            return targetPath;
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
        /// Löscht den Anhang (Datei + DB-Zeile). Ignoriert fehlende Datei.
        /// </summary>
        public void DeleteAttachment(int attachmentId)
        {
            if (attachmentId <= 0) return;

            var info = _db.GetAttachmentById(attachmentId);
            if (info == null) { _db.DeleteAttachment(attachmentId); return; }

            var (root, _) = _db.GetAttachmentSettings();
            if (string.IsNullOrWhiteSpace(root))
            {
                var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                root = Path.Combine(doc, "MyCoinFlow", "Attachments");
            }

            var full = Path.Combine(root, info.Value.FolderRel, info.Value.FileName);

            try
            {
                if (File.Exists(full)) File.Delete(full);
            }
            catch (IOException)
            {
                // Falls gesperrt – trotzdem DB-Eintrag löschen, damit UI konsistent bleibt.
            }
            catch (UnauthorizedAccessException)
            {
                // Berechtigungen – DB-Eintrag trotzdem löschen
            }

            _db.DeleteAttachment(attachmentId);
        }



    }
}
