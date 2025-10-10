using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using MyCoinFlow.Services;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// OCR-/Index-Funktionen: hier nur PDF-Text (ohne OCR).
    /// </summary>
    public sealed class OcrService
    {
        private readonly DatabaseService _db = new();

        /// <summary>
        /// Prüft, ob in einem PDF extrahierbarer Text vorhanden ist (kein OCR).
        /// true = Text gefunden, false = leer/Scan, null = Datei fehlt.
        /// </summary>
        public bool? IsPdfTextBased(string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath)) return null;

            using var doc = PdfDocument.Open(pdfPath);
            for (int i = 1; i <= doc.NumberOfPages; i++)
            {
                var page = doc.GetPage(i);
                var text = ContentOrderTextExtractor.GetText(page);
                if (!string.IsNullOrWhiteSpace(text)) return true;
            }
            return false;
        }

        /// <summary>
        /// Extrahiert reinen Text aus einem PDF (ohne OCR).
        /// Gibt leere Zeichenfolge zurück, wenn nichts extrahierbar ist.
        /// </summary>
        public string ExtractTextFromPdf_NoOcr(string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath)) return string.Empty;

            var sb = new StringBuilder();
            using var doc = PdfDocument.Open(pdfPath);
            for (int i = 1; i <= doc.NumberOfPages; i++)
            {
                var page = doc.GetPage(i);
                var text = ContentOrderTextExtractor.GetText(page);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text.Trim());
                }
            }
            return sb.ToString();
        }

        // (Bleibt vorbereitet für später:)
        public string ExtractTextWithTesseract(string filePath, string? langsOverride = null)
        {
            // Platzhalter: Wir nutzen das in einem späteren Schritt,
            // wenn Bilder/Scan-PDFs per Tesseract indexiert werden.
            throw new NotImplementedException("Tesseract-Aufruf folgt in Schritt C.");
        }
    }
}
