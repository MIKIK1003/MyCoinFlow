using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Importing;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class AccountStructureSettingsControl : UserControl
{
    private readonly DatabaseService _database = new();
    private string Kind => (StructureTabs.SelectedItem as TabViewItem)?.Tag as string ?? "Art";
    private object? Selected => Kind switch { "Art" => ArtsList.SelectedItem, "Group" => GroupsList.SelectedItem, _ => SubgroupsList.SelectedItem };

    public AccountStructureSettingsControl() { InitializeComponent(); Reload(); }
    private void Reload() { ArtsList.ItemsSource = _database.LadeKontenArten().OrderBy(value => value.Bezeichnung).ToList(); GroupsList.ItemsSource = _database.LadeKontenGruppen().OrderBy(value => value.Bezeichnung).ToList(); SubgroupsList.ItemsSource = _database.LadeKontenUnterGruppen().OrderBy(value => value.Bezeichnung).ToList(); }
    private void OnTabChanged(object sender, SelectionChangedEventArgs e) { if (ReferenceEquals(e.OriginalSource, StructureTabs)) NameBox.Text = string.Empty; }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) { NameBox.Text = Selected switch { KontenArt value => value.Bezeichnung, KontenGruppe value => value.Bezeichnung, KontenUnterGruppe value => value.Bezeichnung, _ => string.Empty }; }
    private void OnNewClick(object sender, RoutedEventArgs e) { ArtsList.SelectedItem = GroupsList.SelectedItem = SubgroupsList.SelectedItem = null; NameBox.Text = string.Empty; NameBox.Focus(FocusState.Programmatic); }
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim(); if (string.IsNullOrWhiteSpace(name)) { Show("Bitte eine Bezeichnung erfassen.", InfoBarSeverity.Warning); return; }
        try
        {
            switch (Selected)
            {
                case KontenArt value when value.Bezeichnung != name: if (await ConfirmRenameAsync(value.Bezeichnung, name, "Art")) _database.RenameKontenArt(value.Bezeichnung, name); break;
                case KontenGruppe value when value.Bezeichnung != name: if (await ConfirmRenameAsync(value.Bezeichnung, name, "Gruppe")) _database.RenameKontenGruppe(value.Bezeichnung, name); break;
                case KontenUnterGruppe value when value.Bezeichnung != name: if (await ConfirmRenameAsync(value.Bezeichnung, name, "Untergruppe")) _database.RenameKontenUnterGruppe(value.Bezeichnung, name); break;
                case null when Kind == "Art": _database.SpeichereKontenArt(name); break;
                case null when Kind == "Group": _database.SpeichereKontenGruppe(name); break;
                case null: _database.SpeichereKontenUnterGruppe(name); break;
            }
            Reload(); Show("Kontenstruktur gespeichert.", InfoBarSeverity.Success);
        }
        catch (Exception ex) { Show(ex.Message, InfoBarSeverity.Error); }
    }
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (Selected is null || !await ConfirmAsync("Löschen bestätigen", "Konten im Kontenplan behalten ihren Text; nur der Stammdatensatz wird gelöscht.")) return;
        try { switch (Selected) { case KontenArt value: _database.LoescheKontenArt(value.Id); break; case KontenGruppe value: _database.LoescheKontenGruppe(value.Id); break; case KontenUnterGruppe value: _database.LoescheKontenUnterGruppe(value.Id); break; } Reload(); NameBox.Text = string.Empty; }
        catch (Exception ex) { Show(ex.Message, InfoBarSeverity.Error); }
    }
    private async void OnImportClick(object sender, RoutedEventArgs e) { var dialog = new AccountPlanImportDialog { XamlRoot = XamlRoot }; if (await dialog.ShowAsync() == ContentDialogResult.Primary) Reload(); }
    private async void OnExportClick(object sender, RoutedEventArgs e) { try { var path = await FilePickerService.PickSaveAsync("Kontenplan", "Excel-Datei", ".xlsx"); if (path is null) return; new KontenplanExcelExporter().Export(path); Show("Kontenplan wurde exportiert.", InfoBarSeverity.Success); } catch (Exception ex) { Show("Export fehlgeschlagen: " + ex.Message, InfoBarSeverity.Error); } }
    private async Task<bool> ConfirmRenameAsync(string oldName, string newName, string field) => await ConfirmAsync("Umbenennen bestätigen", $"„{oldName}“ in „{newName}“ umbenennen? Die Änderung wirkt auf die Stammdaten und alle betroffenen Zeilen im Kontenplan (Feld {field}).");
    private async Task<bool> ConfirmAsync(string title, string text) => await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = text, PrimaryButtonText = "Ja", CloseButtonText = "Nein", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Primary;
    private void Show(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
}
