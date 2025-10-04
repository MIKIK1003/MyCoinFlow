using System;
using System.IO;
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
        private readonly UpdateService _update;
        private AppVersionInfo? _latest;

        public AdminUpdatesControl()
        {
            InitializeComponent();
            _update = new UpdateService();

            var current = _update.GetCurrentVersion();
            CurrentVersionText.Text = current.ToString();

            AvailableVersionText.Text = "—";
            ReleaseNotesText.Text = "Klicke auf „Nach Updates suchen“, um die neueste Version zu prüfen.";
        }

        private async void CheckButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CheckButton.IsEnabled = false;
                AvailableVersionText.Text = "…";
                ReleaseNotesText.Text = "Prüfe Version…";

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

                var isNewer = UpdateService.IsNewer(_update.GetCurrentVersion(), _latest.Version);
                UpdateButton.IsEnabled = isNewer && !string.IsNullOrWhiteSpace(_latest.FileUrl);

                if (!isNewer)
                {
                    MessageBox.Show("Sie verwenden bereits die aktuelle Version.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
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

                // Zielpfad wählen
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"{dbName}_{DateTime.Now:yyyyMMdd_HHmm}.bak",
                    Filter = "Backup (*.bak)|*.bak",
                    OverwritePrompt = true
                };
                if (dlg.ShowDialog() != true) return;

                var backup = new DbBackupService();
                await backup.BackupAsync(dbName, dlg.FileName);

                MessageBox.Show("Backup erfolgreich erstellt.", "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Backupfehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_latest == null)
            {
                MessageBox.Show("Bitte zuerst „Nach Updates suchen“.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(_latest.FileUrl))
            {
                MessageBox.Show("Kein Download-Link in der version.json.", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                "Vor dem Update wird empfohlen, ein Datenbank-Backup zu erstellen.\n\nMöchten Sie das Update jetzt installieren?",
                "Update bestätigen",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

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

                var cts = new CancellationTokenSource();
                var setupPath = await _update.DownloadSetupAsync(_latest.FileUrl, progress, cts.Token);
                if (setupPath == null || !File.Exists(setupPath))
                    throw new InvalidOperationException("Setup konnte nicht heruntergeladen werden.");

                // Starten & App schließen
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
    }
}
