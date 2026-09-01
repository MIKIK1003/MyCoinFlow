using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Services;
using MyCoinFlow.Services.Update;
using MyCoinFlow.WinUI.Services;
using System.Text.Json;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class UpdateSettingsControl : UserControl
{
    private readonly UpdateService _update = new(); private readonly DatabaseService _database = new(); private AppVersionInfo? _latest;
    public UpdateSettingsControl() { InitializeComponent(); var installed = _database.GetAppSetting("InstalledVersion"); if (string.IsNullOrWhiteSpace(installed)) { installed = Normalize4(_update.GetCurrentVersion().ToString()); _database.SetAppSetting("InstalledVersion", installed); } CurrentVersionText.Text = Normalize4(installed); AvailableVersionText.Text = "—"; NotesText.Text = "Klicke auf „Suchen“, um die neueste Version zu prüfen."; }
    private async void OnCheckClick(object sender, RoutedEventArgs e)
    {
        try { if (LocalModeButton.IsChecked == true) { var path = AppReleaseConfig.LocalVersionJsonPath; if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { Show("Keine lokale version.json gefunden.", InfoBarSeverity.Warning); return; } _latest = JsonSerializer.Deserialize<AppVersionInfo>(File.ReadAllText(path)); } else _latest = await _update.TryFetchLatestAsync(); if (_latest is null || string.IsNullOrWhiteSpace(_latest.Version)) { Show("Konnte keine gültige Version laden.", InfoBarSeverity.Warning); return; } AvailableVersionText.Text = _latest.Version; NotesText.Text = string.IsNullOrWhiteSpace(_latest.Notes) ? "Keine Release Notes." : _latest.Notes; var newer = UpdateService.IsNewer(new Version(CurrentVersionText.Text), _latest.Version); UpdateButton.IsEnabled = newer && HasSource(_latest); Show(newer ? "Eine neue Version ist verfügbar." : "Sie verwenden bereits die aktuelle Version.", newer ? InfoBarSeverity.Success : InfoBarSeverity.Informational); }
        catch (Exception ex) { Show("Updatefehler: " + ex.Message, InfoBarSeverity.Error); }
    }
    private async void OnBackupClick(object sender, RoutedEventArgs e) { try { var path = await FilePickerService.PickSaveAsync($"{ConnectionStrings.ActiveDatabaseName}_{DateTime.Now:yyyyMMdd_HHmm}", "SQL Server Backup", ".bak"); if (path is null) return; await new DbBackupService().BackupAsync(ConnectionStrings.ActiveDatabaseName, path); Show("Backup erfolgreich erstellt.", InfoBarSeverity.Success); } catch (Exception ex) { Show("Backup fehlgeschlagen: " + ex.Message, InfoBarSeverity.Error); } }
    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_latest is null) { Show("Bitte zuerst nach Updates suchen.", InfoBarSeverity.Warning); return; }
        try { DownloadProgress.Visibility = Visibility.Visible; DownloadProgress.IsIndeterminate = true; string? setup; if (LocalModeButton.IsChecked == true) setup = UpdatePathResolver.GetSetupPath(AppReleaseConfig.DefaultSetupFileName); else { var progress = new Progress<double>(value => { DownloadProgress.IsIndeterminate = false; DownloadProgress.Value = value * 100d; }); setup = await _update.DownloadSetupAsync(_latest.FileUrl, progress); } if (string.IsNullOrWhiteSpace(setup) || !File.Exists(setup)) throw new InvalidOperationException("Setup-Datei konnte nicht bereitgestellt werden."); UpdateService.StartPassiveInstallerAndExit(setup); ((App)Microsoft.UI.Xaml.Application.Current).MainWindow.Close(); }
        catch (Exception ex) { Show("Updatefehler: " + ex.Message, InfoBarSeverity.Error); DownloadProgress.Visibility = Visibility.Collapsed; }
    }
    private static bool HasSource(AppVersionInfo value) => !string.IsNullOrWhiteSpace(value.FileUrl) || File.Exists(UpdatePathResolver.GetSetupPath(AppReleaseConfig.DefaultSetupFileName));
    private static string Normalize4(string value) { var cut = value.Split('+', '-', ' ', '(')[0].Trim(); return cut.Split('.').Length switch { 1 => cut + ".0.0.0", 2 => cut + ".0.0", 3 => cut + ".0", _ => cut }; }
    private void Show(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
}
