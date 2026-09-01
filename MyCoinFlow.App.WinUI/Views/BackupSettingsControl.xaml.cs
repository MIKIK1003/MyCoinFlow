using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class BackupSettingsControl : UserControl
{
    private readonly DbBackupService _backup = new();
    private readonly DbRestoreService _restore = new();
    public BackupSettingsControl() { InitializeComponent(); ActiveDatabaseText.Text = $"Aktive DB: {ConnectionStrings.ActiveDatabaseName}"; var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MyCoinFlow", "Backups"); BackupPathBox.Text = Path.Combine(folder, $"{ConnectionStrings.ActiveDatabaseName}_{DateTime.Now:yyyyMMdd_HHmm}.bak"); }
    private async void OnSelectBackupPathClick(object sender, RoutedEventArgs e) { var path = await FilePickerService.PickSaveAsync($"{ConnectionStrings.ActiveDatabaseName}_{DateTime.Now:yyyyMMdd_HHmm}", "SQL Server Backup", ".bak"); if (path is not null) BackupPathBox.Text = path; }
    private async void OnSelectRestorePathClick(object sender, RoutedEventArgs e) { RestorePathBox.Text = await FilePickerService.PickOpenAsync(".bak") ?? RestorePathBox.Text; }
    private async void OnBackupClick(object sender, RoutedEventArgs e)
    {
        var path = BackupPathBox.Text.Trim(); if (string.IsNullOrWhiteSpace(path)) { Show("Bitte eine Zieldatei auswählen.", InfoBarSeverity.Warning); return; } if (IsOneDrivePath(path)) { Show("Backups in synchronisierte OneDrive-Ordner sind nicht erlaubt. Bitte einen lokalen Ordner wählen.", InfoBarSeverity.Warning); return; }
        try { Show($"Backup läuft… DB „{ConnectionStrings.ActiveDatabaseName}“.", InfoBarSeverity.Informational); await _backup.BackupAsync(ConnectionStrings.ActiveDatabaseName, path, true); Show("Backup erstellt: " + path, InfoBarSeverity.Success); } catch (Exception ex) { Show("Backup-Fehler: " + ex.Message, InfoBarSeverity.Error); }
    }
    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        var path = RestorePathBox.Text.Trim(); if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { Show("Bitte eine vorhandene .bak-Datei wählen.", InfoBarSeverity.Warning); return; }
        var confirm = new ContentDialog { XamlRoot = XamlRoot, Title = "Aktive Datenbank überschreiben?", Content = $"Das Backup wird in „{ConnectionStrings.ActiveDatabaseName}“ zurückgespielt. Die aktuellen Daten dieser Datenbank werden überschrieben.", PrimaryButtonText = "Wiederherstellen", CloseButtonText = "Abbrechen", DefaultButton = ContentDialogButton.Close }; if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try { Show($"Restore läuft… Ziel: „{ConnectionStrings.ActiveDatabaseName}“.", InfoBarSeverity.Informational); await _restore.RestoreActiveAsync(path); Show($"Restore erfolgreich. Aktive DB „{ConnectionStrings.ActiveDatabaseName}“ wurde überschrieben.", InfoBarSeverity.Success); } catch (Exception ex) { Show("Restore-Fehler: " + ex.Message, InfoBarSeverity.Error); }
    }
    private static bool IsOneDrivePath(string path) { var full = Path.GetFullPath(path); if (full.Contains("\\OneDrive\\", StringComparison.OrdinalIgnoreCase) || full.Contains("\\OneDrive - ", StringComparison.OrdinalIgnoreCase)) return true; foreach (var name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }) { var root = Environment.GetEnvironmentVariable(name); if (!string.IsNullOrWhiteSpace(root) && full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) return true; } return false; }
    private void Show(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
}
