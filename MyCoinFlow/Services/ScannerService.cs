using System;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Spricht einen angeschlossenen Scanner über Windows Image Acquisition (WIA) an – rein per
    /// COM-Spätbindung (ProgID "WIA.CommonDialog"), damit keine zusätzliche Interop-Assembly
    /// nötig ist. Zeigt den nativen Windows-Scan-Dialog (Gerät/Auflösung wählen) und legt das
    /// Ergebnis als einseitiges PDF im Zielordner ab (Attachment-Whitelist/DMS erwarten
    /// üblicherweise PDFs; WIA liefert nur Rasterbilder, daher wird das gescannte Bild in ein
    /// minimales Einzelseiten-PDF verpackt – kein zusätzliches NuGet-Paket nötig).
    /// Keine UI (ausser dem vom Betriebssystem bereitgestellten Scan-Dialog), keine DB-Zugriffe.
    /// </summary>
    public static class ScannerService
    {
        // WiaDeviceType
        private const int ScannerDeviceType = 1;

        /// <summary>
        /// Öffnet den Windows-Scan-Dialog und speichert das gescannte Bild als PDF im
        /// angegebenen Ordner. Gibt den vollen Zielpfad zurück, oder null, wenn der Benutzer
        /// abgebrochen hat. Wirft eine InvalidOperationException, wenn WIA/kein Scanner
        /// verfügbar ist.
        /// </summary>
        public static string? ScanToFolder(string targetDirectory)
        {
            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new ArgumentException("Zielordner fehlt.", nameof(targetDirectory));

            var dialogType = Type.GetTypeFromProgID("WIA.CommonDialog")
                ?? throw new InvalidOperationException(
                    "Windows Image Acquisition (WIA) ist auf diesem System nicht verfügbar.");

            dynamic dialog = Activator.CreateInstance(dialogType)!;

            dynamic? imageFile;
            try
            {
                // ShowAcquireImage(DeviceType, Intent, Bias, FormatID, AlwaysSelectDevice, UseCommonUI, CancelError)
                // FormatID wird von WIA intern als Klassenzeichenfolge (GUID) geparst – ein leerer
                // String ist dafür KEIN gültiger Platzhalter (führt zu CO_E_CLASSSTRING / "Ungültige
                // Klassenzeichenfolge"). Type.Missing sorgt dafür, dass WIA seinen echten
                // Default anwendet, statt dass wir ihn erraten.
                imageFile = dialog.ShowAcquireImage(
                    ScannerDeviceType,
                    Type.Missing,   // Intent
                    Type.Missing,   // Bias
                    Type.Missing,   // FormatID
                    Type.Missing,   // AlwaysSelectDevice
                    true,           // UseCommonUI: zeigt den Windows-Scan-Dialog
                    Type.Missing);  // CancelError
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Scannen fehlgeschlagen: " + ex.Message, ex);
            }

            if (imageFile == null) return null; // Benutzer hat den Dialog abgebrochen

            Directory.CreateDirectory(targetDirectory);

            // WIA-Rohausgabe bewusst AUSSERHALB des überwachten Arbeitsordners zwischenspeichern:
            // der Watcher soll erst die fertige PDF sehen, kein Zwischenformat, das er sonst
            // (erfolglos) selbst aufzugreifen versucht.
            string tempRaw = Path.Combine(Path.GetTempPath(), $"mcf_scan_{Guid.NewGuid():N}.tmp");
            try
            {
                imageFile.SaveFile(tempRaw);

                byte[] jpegBytes = ToJpegBytes(tempRaw);
                byte[] pdfBytes = WrapJpegAsSinglePagePdf(jpegBytes);

                string pdfPath = Path.Combine(targetDirectory, $"Scan-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
                File.WriteAllBytes(pdfPath, pdfBytes);
                return pdfPath;
            }
            finally
            {
                try { File.Delete(tempRaw); } catch { /* still */ }
            }
        }

        /// <summary>
        /// Dekodiert das WIA-Rohbild (Format je nach Treiber – BMP/TIFF/JPEG…) und kodiert es
        /// einheitlich als JPEG, damit die PDF-Einbettung (DCTDecode) unabhängig vom
        /// Treiberformat funktioniert.
        /// </summary>
        private static byte[] ToJpegBytes(string sourcePath)
        {
            var decoder = BitmapDecoder.Create(
                new Uri(sourcePath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
            encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Verpackt ein JPEG als minimales, gültiges Einzelseiten-PDF (Bild als XObject mit
        /// DCTDecode-Filter, unverändert eingebettet). Seitengrösse wird aus den DPI-Metadaten
        /// des JPEGs berechnet, damit die PDF-Seite den echten Papiermassen entspricht statt der
        /// Pixelgrösse.
        /// </summary>
        private static byte[] WrapJpegAsSinglePagePdf(byte[] jpegBytes)
        {
            var jpegDecoder = new JpegBitmapDecoder(
                new MemoryStream(jpegBytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = jpegDecoder.Frames[0];

            double dpiX = frame.DpiX > 0 ? frame.DpiX : 96;
            double dpiY = frame.DpiY > 0 ? frame.DpiY : 96;
            double widthPt = frame.PixelWidth / dpiX * 72.0;
            double heightPt = frame.PixelHeight / dpiY * 72.0;

            string colorSpace = (frame.Format == PixelFormats.Gray8 || frame.Format == PixelFormats.BlackWhite)
                ? "/DeviceGray" : "/DeviceRGB";

            using var ms = new MemoryStream();
            var offsets = new long[6];

            void WriteAscii(string s)
            {
                var bytes = Encoding.ASCII.GetBytes(s);
                ms.Write(bytes, 0, bytes.Length);
            }

            WriteAscii("%PDF-1.4\n");

            offsets[1] = ms.Position;
            WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            offsets[2] = ms.Position;
            WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

            offsets[3] = ms.Position;
            WriteAscii(
                $"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPt:F2} {heightPt:F2}] " +
                "/Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");

            offsets[4] = ms.Position;
            WriteAscii(
                $"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {frame.PixelWidth} " +
                $"/Height {frame.PixelHeight} /ColorSpace {colorSpace} /BitsPerComponent 8 " +
                $"/Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n");
            ms.Write(jpegBytes, 0, jpegBytes.Length);
            WriteAscii("\nendstream\nendobj\n");

            string content = $"q {widthPt:F2} 0 0 {heightPt:F2} 0 0 cm /Im0 Do Q";
            offsets[5] = ms.Position;
            WriteAscii($"5 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");

            long xrefStart = ms.Position;
            var sb = new StringBuilder();
            sb.Append("xref\n0 6\n0000000000 65535 f \n");
            for (int i = 1; i <= 5; i++)
                sb.Append($"{offsets[i]:D10} 00000 n \n");
            sb.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
            sb.Append(xrefStart);
            sb.Append("\n%%EOF");
            WriteAscii(sb.ToString());

            return ms.ToArray();
        }
    }
}
