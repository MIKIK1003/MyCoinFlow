using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class StweSetsPage : Page
{
    private readonly DatabaseService _database = new();
    private readonly HashSet<Window> _windows = new();
    private StweSetDisplayRow? _selected;
    private bool _ready;

    public StweSetsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => { foreach (var window in _windows.ToList()) window.Close(); _windows.Clear(); };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_ready) return;
        _ready = true;
        try
        {
            await Task.Run(() => _database.EnsureStweSchema());
            await LoadDefaultPeriodAsync();
            await LoadPropertiesAsync();
        }
        catch (Exception exception) { ShowStatus("STWE konnte nicht initialisiert werden: " + exception.Message, InfoBarSeverity.Error); }
    }

    private async Task LoadDefaultPeriodAsync()
    {
        try
        {
            var period = await Task.Run(() => { var id = _database.HoleAktivenBudgetzeitraumId(); return id.HasValue ? _database.HoleBudgetzeitraum(id.Value) : null; });
            if (period is not null) { FromPicker.Date = period.Startdatum; ToPicker.Date = period.Enddatum; }
        }
        catch { }
    }

    private async Task LoadPropertiesAsync()
    {
        var selectedId = (PropertyBox.SelectedItem as StweLiegenschaft)?.Id;
        var values = await Task.Run(() => _database.StweLiegenschaftenGetAll());
        PropertyBox.ItemsSource = values;
        PropertyBox.SelectedItem = selectedId.HasValue ? values.FirstOrDefault(value => value.Id == selectedId.Value) : values.FirstOrDefault();
        await LoadSetsAsync();
    }

    private async Task LoadSetsAsync(int? selectedId = null)
    {
        if (PropertyBox.SelectedItem is not StweLiegenschaft property) { SetsList.ItemsSource = null; UpdateActions(); return; }
        selectedId ??= _selected?.Id;
        var from = FromPicker.Date?.Date;
        var to = ToPicker.Date?.Date;
        var values = await Task.Run(() => _database.StweSetsGetByLiegenschaft(property.Id, from, to));
        foreach (var value in values)
        {
            var signed = value.IsCredit ? -Math.Abs(value.Betrag) : Math.Abs(value.Betrag);
            value.Betrag = signed;
            value.Rest = signed - value.Verteilt;
        }
        var rows = values.Select(value => new StweSetDisplayRow(value)).ToList();
        SetsList.ItemsSource = rows;
        SetsList.SelectedItem = selectedId.HasValue ? rows.FirstOrDefault(row => row.Id == selectedId.Value) : rows.FirstOrDefault();
        ShowStatus(rows.Count == 0 ? "Keine Sets im gewählten Zeitraum." : $"{rows.Count} Set(s) gefunden.", InfoBarSeverity.Informational);
        UpdateActions();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadPropertiesAsync();
    private async void OnPropertyChanged(object sender, SelectionChangedEventArgs e) { if (_ready) await LoadSetsAsync(); }
    private async void OnFilterChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) { if (_ready) await LoadSetsAsync(); }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) { _selected = SetsList.SelectedItem as StweSetDisplayRow; UpdateActions(); }
    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) { if (_selected is not null) OpenDistribution(); }

    private async void OnNewSetClick(object sender, RoutedEventArgs e)
    {
        if (PropertyBox.SelectedItem is not StweLiegenschaft property) return;
        var dialog = new StweTransactionSelectionDialog { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();
        if ((result != ContentDialogResult.Primary && dialog.Result is null) || dialog.Result is not Transaktion transaction) return;
        var title = string.IsNullOrWhiteSpace(transaction.Notiz) ? transaction.AdresseName ?? "(ohne Text)" : transaction.Notiz.Trim();
        try { await Task.Run(() => _database.StweSetInsert(property.Id, transaction.Id, title)); await LoadSetsAsync(); }
        catch (InvalidOperationException exception) { await ShowMessageAsync("Set erstellen", exception.Message); }
        catch (Exception exception) { ShowStatus("Set konnte nicht erstellt werden: " + exception.Message, InfoBarSeverity.Error); }
    }

    private void OnDistributeClick(object sender, RoutedEventArgs e) => OpenDistribution();
    private void OpenDistribution()
    {
        if (_selected is null) return;
        var window = new StweDistributionWindow(_selected.Value);
        TrackWindow(window, async () => await LoadSetsAsync(_selected?.Id));
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.Value.IsClosed) return;
        var dialog = new TextValueDialog("Titel ändern", "Bezeichnung", _selected.Value.Titel) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(dialog.Value)) return;
        await Task.Run(() => _database.StweSetUpdateTitel(_selected.Id, dialog.Value));
        await LoadSetsAsync(_selected.Id);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.Value.IsClosed || !await ConfirmAsync("Set löschen?", "Das ausgewählte Set wird gelöscht.")) return;
        await Task.Run(() => _database.StweSetDelete(_selected.Id));
        await LoadSetsAsync();
    }

    private async void OnCloseSetClick(object sender, RoutedEventArgs e) { if (_selected is null) return; await Task.Run(() => _database.StweSetSetClosed(_selected.Id, true)); await LoadSetsAsync(_selected.Id); }
    private async void OnReopenClick(object sender, RoutedEventArgs e) { if (_selected is null) return; await Task.Run(() => _database.StweSetSetClosed(_selected.Id, false)); await LoadSetsAsync(_selected.Id); }
    private async void OnCreditClick(object sender, RoutedEventArgs e) => await ChangeTypeAsync(true);
    private async void OnDebitClick(object sender, RoutedEventArgs e) => await ChangeTypeAsync(false);
    private async Task ChangeTypeAsync(bool credit)
    {
        if (_selected is null || _selected.Value.IsClosed || !await ConfirmAsync("Set-Typ ändern", "Set-Typ ändern?\n\nVorhandene Verteilzeilen werden automatisch gespiegelt.")) return;
        await Task.Run(() => _database.StweSetFlipCreditAndLines(_selected.Id, credit));
        await LoadSetsAsync(_selected.Id);
    }

    private void OnMeterDataClick(object sender, RoutedEventArgs e)
    {
        if (PropertyBox.SelectedItem is not StweLiegenschaft property) return;
        TrackWindow(new StweMeterDataWindow(property.Id, property.Name));
    }
    private void OnReportClick(object sender, RoutedEventArgs e)
    {
        if (PropertyBox.SelectedItem is not StweLiegenschaft property) return;
        TrackWindow(new StweReportWindow(property));
    }
    private void TrackWindow(Window window, Func<Task>? onClosed = null)
    {
        _windows.Add(window);
        window.Closed += async (_, _) => { _windows.Remove(window); if (onClosed is not null) await onClosed(); };
        window.Activate();
    }
    private void UpdateActions()
    {
        var value = _selected?.Value;
        DistributeButton.IsEnabled = value is not null;
        RenameButton.IsEnabled = value is { IsClosed: false };
        DeleteButton.IsEnabled = value is { IsClosed: false };
        CloseSetButton.IsEnabled = value is { IsClosed: false } && Math.Abs(value.Rest) < 0.0001m;
        ReopenButton.IsEnabled = value is { IsClosed: true };
        CreditButton.IsEnabled = value is { IsClosed: false, IsCredit: false };
        DebitButton.IsEnabled = value is { IsClosed: false, IsCredit: true };
    }
    private async Task<bool> ConfirmAsync(string title, string content)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, PrimaryButtonText = "Ja", CloseButtonText = "Nein", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private async Task ShowMessageAsync(string title, string message) => await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = message, CloseButtonText = "Schließen" }.ShowAsync();
    private void ShowStatus(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
}
