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

        private void EnsureInstalledVersionSeedAndDevSync()
        {
            var db = new DatabaseService();

            // Tabelle sicherstellen (idempotent)
            try { db.SetAppSetting("___probe___", null); } catch { }

            // Nur initial setzen, wenn leer
            var installed = db.GetAppSetting("InstalledVersion");

            if (string.IsNullOrWhiteSpace(installed))
            {
                var current = _update.GetCurrentVersion().ToString();
                db.SetAppSetting("InstalledVersion", Normalize4(current));
            }

            // WICHTIG:
            // KEIN Sync mehr mit JSON
            // KEIN Überschreiben der DB-Version
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

                // ============================
                // 🔀 MODUS ENTSCHEIDEN
                // ============================

                if (ModeLocal.IsChecked == true)
                {
                    // ✅ LOKAL (bestehender stabiler Weg)
                    var localVersion = TryReadLocalJsonVersion();

                    if (string.IsNullOrWhiteSpace(localVersion))
                    {
                        AvailableVersionText.Text = "—";
                        ReleaseNotesText.Text = "Keine lokale version.json gefunden.";
                        UpdateButton.IsEnabled = false;
                        return;
                    }

                    _latest = new AppVersionInfo
                    {
                        Version = localVersion,
                        Notes = "Lokales Update"
                    };
                }
                else
                {
                    // 🌐 ONLINE (vorerst placeholder – später Git)
                    _latest = await _update.TryFetchLatestAsync();
                }

                // ============================
                // 📊 Anzeige
                // ============================

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

            try
            {
                DownloadProgress.Visibility = Visibility.Visible;
                DownloadProgress.IsIndeterminate = true;

                string? setupPath = null;

                // ============================
                // 🔀 MODUS ENTSCHEIDEN
                // ============================

                if (ModeLocal.IsChecked == true)
                {
                    // ✅ LOKAL → direkt aus OneDrive-Ordner holen
                    var local = OneDriveLocalResolver.TryGetSetupLocalPath(AppReleaseConfig.DefaultSetupFileName);

                    if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
                    {
                        MessageBox.Show(
                            "Lokale Setup-Datei nicht gefunden.\n\nErwartet:\nOneDrive\\Dokumente\\MyCoinFlowUpdate\\MyCoinFlow-Setup.exe",
                            "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    setupPath = local;
                }
                else
                {
                    // 🌐 ONLINE → via URL laden
                    if (string.IsNullOrWhiteSpace(_latest.FileUrl))
                    {
                        MessageBox.Show("Keine Download-URL vorhanden.",
                            "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var progress = new Progress<double>(p =>
                    {
                        DownloadProgress.IsIndeterminate = false;
                        DownloadProgress.Value = p * 100.0;
                    });

                    setupPath = await _update.DownloadSetupAsync(_latest.FileUrl, progress, CancellationToken.None);
                }

                // ============================
                // 🚀 START INSTALLER
                // ============================

                if (string.IsNullOrWhiteSpace(setupPath) || !File.Exists(setupPath))
                    throw new InvalidOperationException("Setup-Datei konnte nicht bereitgestellt werden.");

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
