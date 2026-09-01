using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class StweMeterDataWindow : PersistentWindow
{
    private readonly DatabaseService _database = new();
    private readonly int _propertyId;
    private StweMeterDataSetDisplayRow? _selected;
    private readonly HashSet<StweMeterDataEditorWindow> _editors = new();
    public StweMeterDataWindow(int propertyId, string propertyName)
    {
        InitializeComponent(); _propertyId = propertyId; HeadingText.Text = $"Zählerdaten – {propertyName}";
        AppWindow.Resize(new SizeInt32(1180, 720));
        if (AppWindow.Presenter is OverlappedPresenter presenter) { presenter.PreferredMinimumWidth = 900; presenter.PreferredMinimumHeight = 560; }
        Closed += (_, _) => { foreach (var editor in _editors.ToList()) editor.Close(); };
        _ = LoadAsync();
    }
    private async Task LoadAsync(int? selectedId = null)
    {
        selectedId ??= _selected?.Value.Id;
        var values = await Task.Run(() => _database.StweZaehlerdatenSetsGetByLiegenschaft(_propertyId));
        var rows = values.Select(value => new StweMeterDataSetDisplayRow(value)).ToList();
        SetsList.ItemsSource = rows; SetsList.SelectedItem = selectedId.HasValue ? rows.FirstOrDefault(value => value.Value.Id == selectedId) : rows.FirstOrDefault();
        ShowStatus(rows.Count == 0 ? "Keine Zählerdaten vorhanden." : $"{rows.Count} Set(s) vorhanden.", InfoBarSeverity.Informational);
    }
    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadAsync();
    private void OnNewClick(object sender, RoutedEventArgs e) => OpenEditor(null);
    private void OnEditClick(object sender, RoutedEventArgs e) { if (_selected is not null) OpenEditor(_selected); }
    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) { if (_selected is not null) OpenEditor(_selected); }
    private void OpenEditor(StweMeterDataSetDisplayRow? selected)
    {
        var window = new StweMeterDataEditorWindow(_propertyId, selected?.Value);
        _editors.Add(window);
        window.Saved += async (_, id) => await LoadAsync(id);
        window.Closed += (_, _) => _editors.Remove(window);
        window.Activate();
    }
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = "Löschen bestätigen", Content = $"Zählerdaten wirklich löschen?\n\n{_selected.Value.ErfasstAm:dd.MM.yyyy}\n{_selected.Value.Notiz}", PrimaryButtonText = "Löschen", CloseButtonText = "Abbrechen", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await Task.Run(() => _database.StweZaehlerdatenSetDelete(_selected.Value.Id)); await LoadAsync(); ShowStatus("Zählerdaten gelöscht.", InfoBarSeverity.Success);
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) { _selected = SetsList.SelectedItem as StweMeterDataSetDisplayRow; EditButton.IsEnabled = DeleteButton.IsEnabled = _selected is not null; }
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    private void ShowStatus(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
}
