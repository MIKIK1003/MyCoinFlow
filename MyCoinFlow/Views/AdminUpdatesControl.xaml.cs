using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MyCoinFlow.Services;
using MyCoinFlow.Services.Update;

namespace MyCoinFlow.Views.Admin
{
    public partial class AdminUpdatesControl : UserControl
    {
        private readonly UpdateService _update = new UpdateService();
        private AppVersionInfo? _latest;

        public AdminUpdatesControl()
        {
            InitializeComponent();

            // 1) DB vorbereiten + InstalledVersion seed/sync
            try
            {
                EnsureInstalledVersionSeedAndDevSync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Update-Initialisierung fehlgeschlagen:\n" + ex.Message,
                    "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // 2) Anzeige: Aktuell = DB (immer), Neu = noch leer
            CurrentVersionText.Text = GetInstalledVersionOrFallback();
            AvailableVersionText.Text = "—";
            ReleaseNotesText.Text = "Klicke auf „Nach Updates suchen“, um die neueste Version zu prüfen.";
        }

        // =================== Kernlogik ===================

        /// <summary>
        /// Stellt sicher:
        /// - AppSetting-Tabelle existiert
        /// - InstalledVersion ist gesetzt (Seed)
        /// - DEBUG: wenn JSON > DB, DB anheben (Entwicklerkomfort)
        /// </summary>
        // AdminUpdatesControl.xaml.cs  — Methode ersetzen
        private void EnsureInstalledVersionSeedAndDevSync()
        {
            var db = new DatabaseService();

            // Tabelle sicherstellen (idempotent)
            try { db.SetAppSetting("___probe___", null); } catch { /* still */ }

            // Seed: wenn InstalledVersion fehlt -> auf Assembly-Version setzen
            var installed = db.GetAppSetting("InstalledVersion");
            if (string.IsNullOrWhiteSpace(installed))
            {
                var seed = _update.GetCurrentVersion().ToString(); // Assembly/InformationalVersion
                db.SetAppSetting("InstalledVersion", Normalize4(seed));
            }

            // KEIN Debug-Autoupdate aus JSON mehr – DB wird nur noch durch laufende EXE (MainWindow) angehoben
        }


        private string GetInstalledVersionOrFallback()
        {
            try
            {
                var db = new DatabaseService();
                var v = db.GetAppSetting("InstalledVersion");
                if (!string.IsNullOrWhiteSpace(v)) return Normalize4(v);
            }
            catch { /* still */ }
            return _update.GetCurrentVersion().ToString();
        }

        // Lokale OneDrive-\...\MyCoinFlowUpdate\version.json lesen
        private static string? TryReadLocalJsonVersion()
        {
            try
            {
                var path = AppReleaseConfig.LocalVersionJsonPath; // wählt Dok./Documents Varianten
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var raw = File.ReadAllText(path);
                    var info = JsonSerializer.Deserialize<AppVersionInfo>(raw);
                    if (!string.IsNullOrWhiteSpace(info?.Version))
                        return info!.Version!;
                }
            }
            catch { /* still */ }
            return null;
        }

        private static string Normalize4(string v)
        {
            var cut = v.Split('+', '-', ' ', '(')[0].Trim();
            var p = cut.Split('.');
            return p.Length switch
            {
                3 => cut + ".0",
                2 => cut + ".0.0",
                1 => cut + ".0.0.0",
                _ => cut
            };
        }

        // =================== Buttons ===================

        private async void CheckButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CheckButton.IsEnabled = false;
                AvailableVersionText.Text = "…";
                ReleaseNotesText.Text = "Prüfe Version…";

                // Holt Version aus Feed (online 1drv.ms zu version.json oder lokaler Fallback)
                _latest = await _update.TryFetchLatestAsync();
                if (_latest == null || string.IsNullOrWhiteSpace(_latest.Version))
                {
                    AvailableVersionText.Text = "—";
                    ReleaseNotesText.Text = "Konnte keine gültige Version laden.";
                    UpdateButton.IsEnabled = false;
                    return;
                }

                AvailableVersionText.Text = _latest.Version;
                ReleaseNotesText.Text = string.IsNullOrWhiteSpace(_latest.Notes)
                    ? "Keine Release Notes."
                    : _latest.Notes;

                var current = new Version(CurrentVersionText.Text);
                var isNewer = UpdateService.IsNewer(current, _latest.Version);
                UpdateButton.IsEnabled = isNewer && HasDownloadSource(_latest);

                if (!isNewer)
                {
                    MessageBox.Show("Sie verwenden bereits die aktuelle Version.",
                        "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AvailableVersionText.Text = "—";
                ReleaseNotesText.Text = "Fehler beim Laden der Version.";
                MessageBox.Show(ex.Message, "Updatefehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                CheckButton.IsEnabled = true;
            }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_latest == null)
            {
                MessageBox.Show("Bitte zuerst „Nach Updates suchen“.",
                    "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool hasUrl = !string.IsNullOrWhiteSpace(_latest.FileUrl);
            string? localSetup = OneDriveLocalResolver.TryGetSetupLocalPath(AppReleaseConfig.DefaultSetupFileName);
            bool hasLocal = !string.IsNullOrWhiteSpace(localSetup) && File.Exists(localSetup);

            if (!hasUrl && !hasLocal)
            {
                MessageBox.Show(
                    "Keine Setup-Quelle gefunden.\n\nLegen Sie „MyCoinFlow-Setup.exe“ nach OneDrive\\(Documents|Dokumente)\\MyCoinFlowUpdate\noder tragen Sie einen fileUrl in der version.json ein.",
                    "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var srcText = hasLocal ? $"lokal: {localSetup}" : "Online (fileUrl)";
            var confirm = MessageBox.Show(
                $"Vor dem Update wird empfohlen, ein Datenbank-Backup zu erstellen.\n\nQuelle: {srcText}\n\nUpdate jetzt installieren?",
                "Update bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                DownloadProgress.Visibility = Visibility.Visible;
                DownloadProgress.IsIndeterminate = true;

                var progress = new Progress<double>(p =>
                {
                    DownloadProgress.IsIndeterminate = false;
                    DownloadProgress.Value = p * 100.0;
                });

                var setupPath = await _update.DownloadSetupAsync(hasUrl ? _latest.FileUrl : string.Empty, progress, CancellationToken.None);
                if (string.IsNullOrWhiteSpace(setupPath) || !File.Exists(setupPath))
                    throw new InvalidOperationException("Setup-Datei konnte nicht bereitgestellt werden.");

                // Installer starten & App beenden (wie gehabt)
                UpdateService.StartPassiveInstallerAndExit(setupPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Updatefehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                DownloadProgress.Visibility = Visibility.Collapsed;
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = 0;
            }
        }

        // *** NEU: Fehle Methode für XAML-Click-Handler ***
        private async void BackupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dbName = ConnectionStrings.ActiveDatabaseName;
                if (string.IsNullOrWhiteSpace(dbName))
                {
                    MessageBox.Show("Es ist keine aktive Datenbank zugeordnet.", "Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"{dbName}_{DateTime.Now:yyyyMMdd_HHmm}.bak",
                    Filter = "SQL Server Backup (*.bak)|*.bak",
                    OverwritePrompt = true
                };
                if (sfd.ShowDialog() != true) return;

                var backup = new DbBackupService();
                await backup.BackupAsync(dbName, sfd.FileName);

                MessageBox.Show("Backup erfolgreich erstellt.", "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Backup fehlgeschlagen:\n" + ex.Message, "Backup", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool HasDownloadSource(AppVersionInfo v)
        {
            if (!string.IsNullOrWhiteSpace(v.FileUrl)) return true;
            var local = OneDriveLocalResolver.TryGetSetupLocalPath(AppReleaseConfig.DefaultSetupFileName);
            return local != null && File.Exists(local);
        }
    }
}
