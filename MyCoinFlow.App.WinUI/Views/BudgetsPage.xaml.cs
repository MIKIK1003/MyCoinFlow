using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class BudgetsPage : Page
{
    private readonly DatabaseService _database = new();
    private readonly HashSet<BudgetDetailWindow> _detailWindows = new();
    private BudgetPeriodDisplayRow? _selected;
    private bool _initialized;
    private bool _loading;

    public BudgetsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        await ReloadAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        foreach (var window in _detailWindows.ToList())
            window.Close();
        _detailWindows.Clear();
    }

    private async Task ReloadAsync(int? selectedId = null)
    {
        if (_loading) return;
        _loading = true;
        BusyRing.IsActive = true;
        selectedId ??= _selected?.Id;
        try
        {
            var periods = await Task.Run(() => _database.LadeBudgetzeitraeume());
            var rows = periods.Select(period => new BudgetPeriodDisplayRow(period)).ToList();
            PeriodsList.ItemsSource = rows;
            PeriodsList.SelectedItem = selectedId.HasValue
                ? rows.FirstOrDefault(row => row.Id == selectedId.Value)
                : null;
            ResultText.Text = $"{rows.Count:N0} Budgetzeiträume · {rows.Count(row => row.Period.IstAktiv):N0} aktiv";
        }
        catch (Exception exception)
        {
            ShowStatus("Budgetzeiträume konnten nicht geladen werden: " + exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            BusyRing.IsActive = false;
            _loading = false;
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync();
    private async void OnNewClick(object sender, RoutedEventArgs e) => await ShowEditorAsync(null);

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not null)
            await ShowEditorAsync(_selected);
    }

    private async Task ShowEditorAsync(BudgetPeriodDisplayRow? selected)
    {
        try
        {
            var dialog = new BudgetPeriodEditorDialog(selected?.Period) { XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || !dialog.Accepted) return;

            await Task.Run(() =>
            {
                if (selected is null)
                {
                    _database.BudgetzeitraumSpeichern(
                        dialog.PeriodName, dialog.StartDate, dialog.EndDate, dialog.IsActive);
                }
                else
                {
                    _database.BudgetzeitraumAktualisieren(
                        selected.Id, dialog.PeriodName, dialog.StartDate, dialog.EndDate, dialog.IsActive);
                }
            });
            await ReloadAsync(selected?.Id);
            ShowStatus(selected is null ? "Budgetzeitraum wurde angelegt." : "Budgetzeitraum wurde aktualisiert.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus("Budgetzeitraum konnte nicht gespeichert werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        if (_selected.Period.IstAktiv)
        {
            var blocked = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Löschen nicht möglich",
                Content = "Der aktive Budgetzeitraum kann nicht gelöscht werden.\n\nBitte zuerst einen anderen Zeitraum aktivieren oder diesen deaktivieren.",
                CloseButtonText = "Schließen"
            };
            await blocked.ShowAsync();
            return;
        }

        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Löschen bestätigen",
            Content = $"Möchten Sie den Budgetzeitraum „{_selected.Name}  ({_selected.DurationText})“ wirklich löschen?",
            PrimaryButtonText = "Löschen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await Task.Run(() => _database.BudgetzeitraumLoeschen(_selected.Id));
            _selected = null;
            await ReloadAsync();
            ShowStatus("Budgetzeitraum wurde gelöscht.", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus("Budgetzeitraum konnte nicht gelöscht werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnValuesClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not null)
            OpenBudgetValues(_selected);
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_selected is not null)
            OpenBudgetValues(_selected);
    }

    private void OpenBudgetValues(BudgetPeriodDisplayRow period)
    {
        try
        {
            var window = new BudgetDetailWindow(period.Id, period.Name);
            _detailWindows.Add(window);
            window.Closed += (_, _) => _detailWindows.Remove(window);
            window.Activate();
        }
        catch (Exception exception)
        {
            ShowStatus("Budgetwerte konnten nicht geöffnet werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = PeriodsList.SelectedItem as BudgetPeriodDisplayRow;
        var hasSelection = _selected is not null;
        EditButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
        ValuesButton.IsEnabled = hasSelection;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
