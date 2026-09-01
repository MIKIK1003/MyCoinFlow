using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class CreditCardSettingsControl : UserControl
{
    private readonly DatabaseService _database = new();
    private readonly CreditCardImportMappingService _mapping;
    private ImportSchema? _current;
    private List<FieldMapping> _rows = new();

    public CreditCardSettingsControl()
    {
        InitializeComponent();
        _mapping = new CreditCardImportMappingService(_database);
        ReloadSchemas();
        PrepareNew();
    }
    private void ReloadSchemas(int? selectedId = null) { var values = _database.ImportSchemasGetAll().Where(value => !value.IsMaster && !value.Name.Equals("Master", StringComparison.OrdinalIgnoreCase)).OrderBy(value => value.Name).ToList(); SchemaBox.ItemsSource = values; SchemaBox.SelectedItem = selectedId.HasValue ? values.FirstOrDefault(value => value.Id == selectedId) : null; }
    private void PrepareNew() { _current = null; SchemaBox.SelectedItem = null; SchemaNameBox.Text = $"Schema {DateTime.Now:yyyyMMdd_HHmm}"; _rows = _mapping.GetMasterHeaders().Select(value => new FieldMapping { MasterHeader = value }).ToList(); MappingsList.ItemsSource = _rows; }
    private void OnNewClick(object sender, RoutedEventArgs e) => PrepareNew();
    private void OnSchemaChanged(object sender, SelectionChangedEventArgs e) { if (SchemaBox.SelectedItem is not ImportSchema value) return; _current = value; SchemaNameBox.Text = value.Name; var loaded = _database.FieldMappingsGetBySchema(value.Id).ToList(); var byMaster = loaded.ToDictionary(item => item.MasterHeader, StringComparer.OrdinalIgnoreCase); _rows = _mapping.GetMasterHeaders().Select(master => byMaster.TryGetValue(master, out var item) ? item : new FieldMapping { SchemaId = value.Id, MasterHeader = master }).ToList(); MappingsList.ItemsSource = _rows; }
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = SchemaNameBox.Text.Trim(); if (name.Length < 3 || name.Equals("Master", StringComparison.OrdinalIgnoreCase)) { Show("Bitte einen gültigen Schema-Namen mit mindestens 3 Zeichen erfassen. „Master“ ist reserviert.", InfoBarSeverity.Warning); return; }
            if (_current is null) _current = _database.ImportSchemaInsert(new ImportSchema { Name = name, IsMaster = false }); else if (_current.Name != name) { _database.ImportSchemaUpdateName(_current.Id, name); _current.Name = name; }
            foreach (var row in _rows) row.SchemaId = _current.Id;
            _database.FieldMappingsReplace(_current.Id, _rows.Where(value => !string.IsNullOrWhiteSpace(value.MasterHeader) && (!string.IsNullOrWhiteSpace(value.SourceHeader) || !string.IsNullOrWhiteSpace(value.DefaultValue))).ToList());
            ReloadSchemas(_current.Id); Show("Schema wurde gespeichert.", InfoBarSeverity.Success);
        }
        catch (Exception ex) { Show("Speichern fehlgeschlagen: " + ex.Message, InfoBarSeverity.Error); }
    }
    private async void OnDeleteClick(object sender, RoutedEventArgs e) { if (_current is null || !await ConfirmAsync($"Schema „{_current.Name}“ wirklich löschen?")) return; _database.ImportSchemaDelete(_current.Id); ReloadSchemas(); PrepareNew(); Show("Schema wurde gelöscht.", InfoBarSeverity.Success); }
    private async Task<bool> ConfirmAsync(string text) => await new ContentDialog { XamlRoot = XamlRoot, Title = "Löschen bestätigen", Content = text, PrimaryButtonText = "Löschen", CloseButtonText = "Abbrechen", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Primary;
    private void Show(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
}
