using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
// PdfPig: Text + Bilder extrahieren (ohne OCR)
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Volltext-Indexierung für Attachments ausschließlich mit PdfPig + Tesseract:
    /// - PDFs mit Textlayer: PdfPig-Extraktion
    /// - Scan-PDFs: Bilder aus Seiten extrahieren (TryGetPng/Raw JPEG) -> Tesseract
    /// - Bilder (JPG/PNG): Tesseract
    /// Ergebnis -> dbo.AttachmentText, OcrStatus: "Text" | "OCR" | "Image" | "Error"
    /// </summary>
    public sealed class OcrService
    {
        private readonly DatabaseService _db = new();

        // -------------------------------------------
        // Admin: „Index aktualisieren“
        // -------------------------------------------
        public Task<(int done, int total, int withText, int images, int errors, int skipped)>
            IndexMissingAsync(IProgress<(int done, int total, int withText, int images, int errors, int skipped)>? progress = null,
                              CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var (root, _) = _db.GetAttachmentSettings();
                if (string.IsNullOrWhiteSpace(root))
                {
                    var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    root = Path.Combine(doc, "MyCoinFlow", "Attachments");
                }

                var todo = _db.LoadAttachmentsNeedingIndex(); // (Id, FileName, FolderRel, OcrStatus)
                int total = todo.Count;
                int done = 0;
                int withTex = 0;   // PDFs mit Textlayer
                int images = 0;   // echte OCR-Läufe (Scan-PDFs + Bilddateien)
                int errors = 0;
                int skipped = 0;

                var (tessExe, langsDb) = _db.GetOcrSettings();
                bool canTesseract = !string.IsNullOrWhiteSpace(tessExe) && File.Exists(tessExe);
                string langs = string.IsNullOrWhiteSpace(langsDb) ? "deu+eng" : langsDb;

                foreach (var a in todo)
                {
                    ct.ThrowIfCancellationRequested();

                    string full = Path.Combine(root, a.FolderRel, a.FileName);
                    string ext = Path.GetExtension(a.FileName)?.ToLowerInvariant() ?? "";

                    try
                    {
                        if (ext == ".pdf")
                        {
                            // 1) Schnell: Text ohne OCR
                            string text = ExtractTextFromPdf_NoOcr(full);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                _db.UpsertAttachmentText(a.Id, text, "pdf");
                                _db.UpdateAttachmentOcrStatus(a.Id, "Text");
                                withTex++;
                            }
                            else
                            {
                                // 2) Scan-PDF -> Bilder extrahieren und per Tesseract OCR
                                if (!canTesseract)
                                {
                                    _db.UpdateAttachmentOcrStatus(a.Id, "Image");
                                    skipped++;
                                }
                                else
                                {
                                    string ocrText = ExtractTextFromPdf_ByImages(full, langs, maxPages: 50, maxImagesPerPage: 2);
                                    if (!string.IsNullOrWhiteSpace(ocrText))
                                    {
                                        _db.UpsertAttachmentText(a.Id, ocrText, "pdf-ocr");
                                        _db.UpdateAttachmentOcrStatus(a.Id, "OCR");
                                    }
                                    else
                                    {
                                        _db.UpdateAttachmentOcrStatus(a.Id, "Image");
                                    }
                                    images++;
                                }
                            }
                        }
                        else if (ext == ".jpg" || ext == ".jpeg" || ext == ".png")
                        {
                            if (!canTesseract)
                            {
                                skipped++;
                            }
                            else
                            {
                                string t = ExtractTextWithTesseract(full, langs);
                                if (!string.IsNullOrWhiteSpace(t))
                                {
                                    _db.UpsertAttachmentText(a.Id, t, "img");
                                    _db.UpdateAttachmentOcrStatus(a.Id, "OCR");
                                }
                                else
                                {
                                    _db.UpdateAttachmentOcrStatus(a.Id, "Image");
                                }
                                images++;
                            }
                        }
                        else
                        {
                            // unbekannte Endung – ignorieren
                            skipped++;
                        }
                    }
                    catch
                    {
                        errors++;
                        try { _db.UpdateAttachmentOcrStatus(a.Id, "Error"); } catch { /* still */ }
                    }
                    finally
                    {
                        done++;
                        progress?.Report((done, total, withTex, images, errors, skipped));
                    }
                }

                return (done, total, withTex, images, errors, skipped);
            }, ct);
        }

        // -------------------------------------------
        // PDF: Text ohne OCR (Textlayer)
        // -------------------------------------------
        public static string ExtractTextFromPdf_NoOcr(string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath)) return string.Empty;

            var sb = new StringBuilder();
            using var doc = PdfDocument.Open(pdfPath);
            for (int i = 1; i <= doc.NumberOfPages; i++)
            {
                var page = doc.GetPage(i);
                var text = ContentOrderTextExtractor.GetText(page);
                if (!string.IsNullOrWhiteSpace(text))
                    sb.AppendLine(text.Trim());
            }
            return sb.ToString();
        }

        // -------------------------------------------
        // PDF: OCR über eingebettete Seitenbilder
        // – nutzt PdfPig: page.GetImages() → TryGetPng/Raw JPEG
        // -------------------------------------------
        public static string ExtractTextFromPdf_ByImages(string pdfPath, string languages, int maxPages = 50, int maxImagesPerPage = 2)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                throw new FileNotFoundException("PDF nicht gefunden.", pdfPath);

            var db = new DatabaseService();
            var (exe, langsFromDb) = db.GetOcrSettings();
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                throw new InvalidOperationException("tesseract.exe ist nicht konfiguriert.");

            if (string.IsNullOrWhiteSpace(languages))
                languages = string.IsNullOrWhiteSpace(langsFromDb) ? "deu+eng" : langsFromDb;

            var sb = new StringBuilder();

            using var doc = PdfDocument.Open(pdfPath);
            int pages = Math.Min(doc.NumberOfPages, Math.Max(1, maxPages));

            for (int i = 1; i <= pages; i++)
            {
                var page = doc.GetPage(i);
                // größte Bilder zuerst (Samples ~ Pixel)
                var imgs = page.GetImages()
                               .OrderByDescending(img =>
                               {
                                   try { return (long)img.WidthInSamples * (long)img.HeightInSamples; }
                                   catch { return 0L; }
                               })
                               .Take(maxImagesPerPage)
                               .ToList();

                foreach (var img in imgs)
                {
                    if (!TrySavePdfPigImageToTempFile(img, out var tmpPath))
                        continue;

                    try
                    {
                        string t = ExtractTextWithTesseract(tmpPath, languages);
                        if (!string.IsNullOrWhiteSpace(t))
                        {
                            if (sb.Length > 0) sb.AppendLine();
                            sb.AppendLine(t.Trim());
                        }
                    }
                    finally
                    {
                        try { File.Delete(tmpPath); } catch { /* still */ }
                    }
                }
            }

            return sb.ToString();
        }

        private static bool TrySavePdfPigImageToTempFile(UglyToad.PdfPig.Content.IPdfImage img, out string tmpPath)
        {
            // 1) Bevorzugt: direkt PNG aus PdfPig (fertig decodiert)
            if (img.TryGetPng(out var pngBytes) && pngBytes != null && pngBytes.Length > 0)
            {
                tmpPath = Path.Combine(Path.GetTempPath(), $"mcf_ocr_{Guid.NewGuid():N}.png");
                File.WriteAllBytes(tmpPath, pngBytes);
                return true;
            }

            // 2) Fallback: RawBytes – oft bei JPG direkt verwendbar
            //    -> immer in ein echtes byte[] wandeln und Länge prüfen
            var raw = img.RawBytes;                 // kann Span/Enumerable sein
            byte[] bytes = raw != null ? raw.ToArray() : Array.Empty<byte>();

            int len = bytes.Length;
            if (len >= 4)
            {
                // JPEG-Signatur prüfen (FF D8 ... FF D9)
                bool isJpeg = bytes[0] == 0xFF && bytes[1] == 0xD8 &&
                              bytes[len - 2] == 0xFF && bytes[len - 1] == 0xD9;

                if (isJpeg)
                {
                    tmpPath = Path.Combine(Path.GetTempPath(), $"mcf_ocr_{Guid.NewGuid():N}.jpg");
                    File.WriteAllBytes(tmpPath, bytes);
                    return true;
                }
            }

            tmpPath = string.Empty;
            return false;
        }


        // -------------------------------------------
        // Bild-OCR via Tesseract (CLI, stdout)
        // -------------------------------------------
        public static string ExtractTextWithTesseract(string filePath, string languages)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("Datei nicht gefunden.", filePath);

            var db = new DatabaseService();
            var (exe, langsFromDb) = db.GetOcrSettings();
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                throw new InvalidOperationException("tesseract.exe ist nicht konfiguriert.");

            if (string.IsNullOrWhiteSpace(languages))
                languages = string.IsNullOrWhiteSpace(langsFromDb) ? "deu+eng" : langsFromDb;

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"\"{filePath}\" stdout -l {languages} --psm 3",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var p = new Process { StartInfo = psi };
            p.Start();
            var output = p.StandardOutput.ReadToEnd();
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode != 0)
                throw new InvalidOperationException($"Tesseract-Fehler: {err}");

            return output ?? string.Empty;
        }
    }
}
