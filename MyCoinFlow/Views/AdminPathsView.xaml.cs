using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class AdminPathsView : UserControl
    {
        private readonly DatabaseService _db = new();

        private const string KeyRoot = "AttachmentRoot";
        private const string KeyMax = "AttachmentMaxMB";

        public AdminPathsView()
        {
            InitializeComponent();

            // Sicherstellen, dass Schema vorhanden ist (idempotent, dauert ms)
            try { _db.EnsureAttachmentsSchema(); } catch { /* still */ }

            LoadSettings();
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

                Status("Einstellungen geladen.");
            }
            catch (Exception ex)
            {
                Status("Fehler beim Laden: " + ex.Message);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var root = (AttachRootBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(root))
                {
                    Status("Bitte einen gültigen Zielordner angeben.");
                    return;
                }

                // Verzeichnis bei Bedarf anlegen
                try { Directory.CreateDirectory(root); } catch { /* später erneut prüfen */ }
                if (!Directory.Exists(root))
                {
                    Status("Ordner konnte nicht angelegt werden. Bitte Berechtigungen prüfen.");
                    return;
                }

                // Max MB prüfen (Zahl, 1..1024)
                var mbTxt = (MaxMbBox.Text ?? "").Trim();
                if (!int.TryParse(mbTxt, out var mb) || mb < 1 || mb > 1024)
                {
                    Status("Ungültige Maximalgröße. Erlaubt: 1–1024 MB.");
                    return;
                }

                _db.SetAppSetting(KeyRoot, root);
                _db.SetAppSetting(KeyMax, mb.ToString());

                Status("Gespeichert.");
            }
            catch (Exception ex)
            {
                Status("Speichern fehlgeschlagen: " + ex.Message);
            }
        }

        private void CreateFolder_Click(object? sender, RoutedEventArgs e)
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

        private void OpenInExplorer_Click(object? sender, RoutedEventArgs e)
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

        private void Status(string text) => StatusText.Text = text ?? "";
    }
}
