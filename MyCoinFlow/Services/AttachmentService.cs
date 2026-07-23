using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

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

            // Dateiname TX-{Id}-H{Hash12}{ext}
            var hash = _db.GetImportHashForTransaktion(transaktionId);
            string hash12 = !string.IsNullOrWhiteSpace(hash)
                ? (hash.Length <= 12 ? hash : hash.Substring(hash.Length - 12))
                : "D" + now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

            string baseNameNoExt = $"TX-{transaktionId}-H{hash12}";
            string baseName = baseNameNoExt + ext;
            string targetPath = Path.Combine(targetDir, baseName);

            if (File.Exists(targetPath))
            {
                int n = 2;
                while (true)
                {
                    string candidate = Path.Combine(targetDir, $"{baseNameNoExt}-{n}{ext}");
                    if (!File.Exists(candidate)) { targetPath = candidate; break; }
                    n++;
                }
            }

            // Verschieben
            File.Move(sourceFilePath, targetPath);

            // DB: Attachment anlegen -> Id zurück
            int attachmentId = _db.SaveAttachment(
                transaktionId: transaktionId,
                fileName: Path.GetFileName(targetPath),
                originalName: Path.GetFileName(sourceFilePath),
                folderRel: folderRel.Replace('/', '\\'),
                sizeBytes: fi.Length,
                ocrStatus: null, // setzen wir gleich passend
                dokumentDatum: now
            );

            RunOcrIndexing(attachmentId, targetPath, ext);

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

            string slug = Path.GetFileNameWithoutExtension(sourceFilePath);
            if (slug.Length > 40) slug = slug.Substring(0, 40);
            string baseNameNoExt = $"DOC-{Guid.NewGuid().ToString("N").Substring(0, 8)}-{slug}";
            string baseName = baseNameNoExt + ext;
            string targetPath = Path.Combine(targetDir, baseName);

            if (File.Exists(targetPath))
            {
                int n = 2;
                while (true)
                {
                    string candidate = Path.Combine(targetDir, $"{baseNameNoExt}-{n}{ext}");
                    if (!File.Exists(candidate)) { targetPath = candidate; break; }
                    n++;
                }
            }

            File.Move(sourceFilePath, targetPath);

            int attachmentId = _db.SaveAttachment(
                transaktionId: null,
                fileName: Path.GetFileName(targetPath),
                originalName: Path.GetFileName(sourceFilePath),
                folderRel: folderRel.Replace('/', '\\'),
                sizeBytes: fi.Length,
                ocrStatus: null,
                titel: titel,
                kategorie: kategorie,
                dokumentDatum: now);

            RunOcrIndexing(attachmentId, targetPath, ext);

            return (targetPath, attachmentId);
        }

        /// <summary>
        /// DMS-Arbeitsordner-Überwachung: legt ein Dokument ab, dessen Zielunterordner und
        /// Dateiname aus dem erkannten Dokumentdatum bzw. Titel (DmsDocumentAnalyzer) stammen,
        /// statt aus dem Zeitpunkt des Uploads/Originalnamen. Läuft ansonsten wie
        /// AttachFreestanding (gleiche Ablage-Root, gleiche Whitelist/Grössenprüfung).
        /// Text/OcrStatus wurden von DmsWatcherService bereits für die Datums-/Titel-/Betrags-
        /// Erkennung extrahiert und werden hier nur noch gespeichert (kein zweiter OCR-Lauf).
        /// Gibt Zielpfad und neue Attachment-Id zurück.
        /// </summary>
        public (string TargetPath, int AttachmentId) AttachFromWatcher(string sourceFilePath, DateTime dokumentDatum, string titelSlug,
            string? extractedText, string? textLang, string ocrStatus)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath)) throw new ArgumentException("Dateipfad fehlt.", nameof(sourceFilePath));
            if (!File.Exists(sourceFilePath)) throw new FileNotFoundException("Quelldatei nicht gefunden.", sourceFilePath);
            if (string.IsNullOrWhiteSpace(titelSlug)) titelSlug = "Dokument";

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

            string baseNameNoExt = $"{dokumentDatum:yyyy-MM-dd}_{titelSlug}";
            string baseName = baseNameNoExt + ext;
            string targetPath = Path.Combine(targetDir, baseName);

            if (File.Exists(targetPath))
            {
                int n = 2;
                while (true)
                {
                    string candidate = Path.Combine(targetDir, $"{baseNameNoExt}-{n}{ext}");
                    if (!File.Exists(candidate)) { targetPath = candidate; break; }
                    n++;
                }
            }

            File.Move(sourceFilePath, targetPath);

            int attachmentId = _db.SaveAttachment(
                transaktionId: null,
                fileName: Path.GetFileName(targetPath),
                originalName: Path.GetFileName(sourceFilePath),
                folderRel: folderRel.Replace('/', '\\'),
                sizeBytes: fi.Length,
                ocrStatus: ocrStatus,
                dokumentDatum: dokumentDatum);

            if (!string.IsNullOrWhiteSpace(extractedText))
                _db.UpsertAttachmentText(attachmentId, extractedText, textLang);

            return (targetPath, attachmentId);
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
        /// Zuweisen) und benennt es passend um: {TransaktionsDatum}_Rechnung_{Adresse}. Ein
        /// gefundenes Dokument ist im DMS-Kontext immer eine Rechnung, daher wird die Kategorie
        /// gleich auf "Rechnungen" gesetzt. Datei bleibt im selben Ordner, nur der Dateiname
        /// ändert sich. Schlägt das Umbenennen fehl (Datei fehlt/gesperrt), bleibt der
        /// bestehende Dateiname erhalten – die Verknüpfung wird trotzdem gesetzt.
        /// </summary>
        public void LinkToTransaktion(int attachmentId, int transaktionId)
        {
            var info = _db.GetAttachmentById(attachmentId);
            var transaktion = _db.HoleTransaktion(transaktionId);

            if (info != null && transaktion != null)
            {
                var (root, _) = _db.GetAttachmentSettings();
                if (string.IsNullOrWhiteSpace(root))
                {
                    var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    root = Path.Combine(doc, "MyCoinFlow", "Attachments");
                }

                var currentDir = Path.Combine(root, info.Value.FolderRel);
                var currentPath = Path.Combine(currentDir, info.Value.FileName);
                var ext = Path.GetExtension(info.Value.FileName);

                if (File.Exists(currentPath))
                {
                    var gegenpartei = SanitizeKeepingSpaces(
                        transaktion.AdresseName ?? transaktion.BankName ?? "Unbekannt");
                    var baseNameNoExt = $"{transaktion.Datum:yyyy-MM-dd}_Rechnung_{gegenpartei}";
                    var targetPath = Path.Combine(currentDir, baseNameNoExt + ext);

                    if (!string.Equals(targetPath, currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        int n = 2;
                        while (File.Exists(targetPath))
                        {
                            targetPath = Path.Combine(currentDir, $"{baseNameNoExt}-{n}{ext}");
                            n++;
                        }

                        try
                        {
                            File.Move(currentPath, targetPath);
                            _db.UpdateAttachmentFileNameAndKategorie(attachmentId, Path.GetFileName(targetPath), "Rechnungen");
                        }
                        catch (IOException) { /* Datei gesperrt – Verknüpfung trotzdem setzen */ }
                        catch (UnauthorizedAccessException) { /* Berechtigungen – Verknüpfung trotzdem setzen */ }
                    }
                    else
                    {
                        _db.UpdateAttachmentFileNameAndKategorie(attachmentId, info.Value.FileName, "Rechnungen");
                    }
                }
            }

            _db.LinkAttachmentToTransaktion(attachmentId, transaktionId);
        }

        private static string SanitizeKeepingSpaces(string raw)
        {
            var sb = new StringBuilder(raw.Trim());
            foreach (var c in Path.GetInvalidFileNameChars())
                sb.Replace(c, '-');

            var result = sb.ToString().Trim();
            if (result.Length == 0) result = "Unbekannt";
            if (result.Length > DmsDocumentAnalyzer.TitleMaxLength)
                result = result.Substring(0, DmsDocumentAnalyzer.TitleMaxLength).TrimEnd();
            return result;
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
