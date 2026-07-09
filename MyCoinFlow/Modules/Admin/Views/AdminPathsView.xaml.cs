using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class AdminPathsView : UserControl
    {
        private readonly DatabaseService _db = new();

        private const string KeyRoot = "AttachmentRoot";
        private const string KeyMax = "AttachmentMaxMB";
        private const string KeyTessExe = "TesseractExePath";
        private const string KeyTessLang = "TesseractLanguages";

        public AdminPathsView()
        {
            InitializeComponent();

            try
            {
                _db.EnsureAttachmentsSchema(); // AppSetting + Attachment + AttachmentText
            }
            catch { /* still */ }

            LoadSettings();
            LoadDbStats();
        }

        private void LoadSettings()
        {
            try
            {
                var root = _db.GetAppSetting(KeyRoot);
                if (string.IsNullOrWhiteSpace(root))
                {
                    var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    root = Path.Combine(doc, "MyCoinFlow", "Attachments");
                }
                AttachRootBox.Text = root;

                var max = _db.GetAppSetting(KeyMax);
                MaxMbBox.Text = string.IsNullOrWhiteSpace(max) ? "20" : max.Trim();

                var appBase = AppDomain.CurrentDomain.BaseDirectory?.TrimEnd(Path.DirectorySeparatorChar) ?? "";
                var defaultTess = string.IsNullOrWhiteSpace(appBase)
                    ? @"C:\Programme\MyCoinFlow\OCR\tesseract.exe"
                    : Path.Combine(appBase, "OCR", "tesseract.exe");

                var tess = _db.GetAppSetting(KeyTessExe);
                TessPathBox.Text = string.IsNullOrWhiteSpace(tess) ? defaultTess : tess.Trim();

                var langs = _db.GetAppSetting(KeyTessLang);
                LangsBox.Text = string.IsNullOrWhiteSpace(langs) ? "deu+eng" : langs.Trim();

                Status("Einstellungen geladen.");
            }
            catch (Exception ex)
            {
                Status("Fehler beim Laden: " + ex.Message);
            }
        }

        public void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var root = (AttachRootBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(root))
                {
                    Status("Bitte einen gültigen Zielordner angeben.");
                    return;
                }

                try { Directory.CreateDirectory(root); } catch { /* später erneut prüfen */ }
                if (!Directory.Exists(root))
                {
                    Status("Ordner konnte nicht angelegt werden. Bitte Berechtigungen prüfen.");
                    return;
                }

                var mbTxt = (MaxMbBox.Text ?? "").Trim();
                if (!int.TryParse(mbTxt, out var mb) || mb < 1 || mb > 1024)
                {
                    Status("Ungültige Maximalgröße. Erlaubt: 1–1024 MB.");
                    return;
                }

                _db.SetAppSetting(KeyRoot, root);
                _db.SetAppSetting(KeyMax, mb.ToString(CultureInfo.InvariantCulture));

                Status("Verzeichnis-Einstellungen gespeichert.");
            }
            catch (Exception ex)
            {
                Status("Speichern fehlgeschlagen: " + ex.Message);
            }
        }

        public void SaveOcr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var exe = (TessPathBox.Text ?? "").Trim();
                var langs = (LangsBox.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(exe))
                {
                    Status("Bitte Pfad zu tesseract.exe angeben.");
                    return;
                }
                if (!exe.EndsWith("tesseract.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe))
                {
                    Status("tesseract.exe wurde am angegebenen Pfad nicht gefunden.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(langs))
                {
                    langs = "deu+eng";
                }

                _db.SetAppSetting(KeyTessExe, exe);
                _db.SetAppSetting(KeyTessLang, langs);

                Status("OCR-Einstellungen gespeichert.");
            }
            catch (Exception ex)
            {
                Status("Speichern fehlgeschlagen: " + ex.Message);
            }
        }

        public void BrowseTesseract_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "tesseract.exe|tesseract.exe|Alle Dateien|*.*",
                    Title = "tesseract.exe auswählen",
                    CheckFileExists = true
                };
                if (dlg.ShowDialog() == true)
                {
                    TessPathBox.Text = dlg.FileName;
                }
            }
            catch (Exception ex)
            {
                Status("Auswahl fehlgeschlagen: " + ex.Message);
            }
        }

        public void SuggestOneDriveFolder_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var oneDriveRoot = MyCoinFlow.Services.Update.OneDriveLocalResolver.GetOneDriveRoot();
                if (string.IsNullOrWhiteSpace(oneDriveRoot))
                {
                    Status("Kein OneDrive-Ordner auf diesem PC gefunden.");
                    return;
                }

                AttachRootBox.Text = Path.Combine(oneDriveRoot, "MyCoinFlow", "Dokumente");
                Status("OneDrive-Ordner eingetragen. Bitte \"Ordner erstellen\" und danach \"Speichern\" klicken.");
            }
            catch (Exception ex)
            {
                Status("OneDrive-Ordner konnte nicht ermittelt werden: " + ex.Message);
            }
        }

        public void OpenInExplorer_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var root = (AttachRootBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    Status("Ordner nicht gefunden.");
                    return;
                }
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{root}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Status("Explorer konnte nicht geöffnet werden: " + ex.Message);
            }
        }

        public void CreateFolder_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var root = (AttachRootBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(root))
                {
                    Status("Bitte zuerst einen Ordnerpfad eingeben.");
                    return;
                }
                Directory.CreateDirectory(root);
                Status(Directory.Exists(root) ? "Ordner ist vorhanden." : "Ordner konnte nicht angelegt werden.");
            }
            catch (Exception ex)
            {
                Status("Fehler: " + ex.Message);
            }
        }

        public void RefreshDbStats_Click(object sender, RoutedEventArgs e) => LoadDbStats();

        private void LoadDbStats()
        {
            try
            {
                var s = _db.GetDatabaseStats(); // holt Name, Größe, Limits, Counts

                DbNameText.Text = s.DatabaseName;
                DbSizeText.Text = $"{s.DataSizeMB:N2} MB";
                DbLimitText.Text = $"{s.DataMaxMB:N0} MB";

                double pct = s.DataMaxMB > 0 ? (s.DataSizeMB / s.DataMaxMB) * 100.0 : 0.0;
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;

                DbUsageBar.Value = pct;
                DbUsageText.Text = $"{pct:N1}% genutzt";

                DbAttachCountText.Text = s.AttachmentCount.ToString("N0", CultureInfo.CurrentCulture);
                DbAttachTextCountText.Text = s.AttachmentTextCount.ToString("N0", CultureInfo.CurrentCulture);
                DbTextBytesText.Text = $"{(s.AttachmentTextBytes / (1024.0 * 1024.0)):N2} MB";
            }
            catch (Exception ex)
            {
                Status("DB-Status konnte nicht geladen werden: " + ex.Message);
            }
        }

        public async void Reindex_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var btn = sender as Button;
                if (btn != null) btn.IsEnabled = false;
                Status("Index: starte…");

                var svc = new OcrService();

                // Fortschritt live in die Statuszeile
                var progress = new Progress<(int done, int total, int withText, int images, int errors, int skipped)>(p =>
                {
                    Status($"Index: {p.done}/{p.total} – Text:{p.withText}, Bild-OCR:{p.images}, Fehler:{p.errors}, übersprungen:{p.skipped}");
                });

                var result = await svc.IndexMissingAsync(progress);

                Status($"Index fertig: {result.done}/{result.total} – Text:{result.withText}, Bild-OCR:{result.images}, Fehler:{result.errors}, übersprungen:{result.skipped}");
                LoadDbStats(); // Counts aktualisieren
            }
            catch (Exception ex)
            {
                Status("Index fehlgeschlagen: " + ex.Message);
            }
            finally
            {
                //
            }
        }

        public void ShowSection(int index)
        {
            PanelAttach.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            PanelOcr.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            PanelDb.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        }


        private void Status(string text) => StatusText.Text = text ?? "";
    }
}
