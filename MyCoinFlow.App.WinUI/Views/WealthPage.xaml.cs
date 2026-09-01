using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.Importing;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.ViewModels;
using MyCoinFlow.WinUI.Services;
using System.Globalization;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class WealthPage : Page
{
    private static readonly CultureInfo Swiss = CultureInfo.GetCultureInfo("de-CH");
    private readonly DatabaseService _database = new();
    private readonly HashSet<WealthHistoryWindow> _windows = new();
    private VermoegenViewModel? _viewModel;
    private VermoegenPositionRow? _selected;
    private string _selectedPeriod = "3 Monate";
    private bool _loadingFilters;

    public WealthPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
        Unloaded += (_, _) =>
        {
            foreach (var window in _windows.ToList())
                window.Close();
        };
    }

    private void Reload(int? positionId = null, int? depotId = null)
    {
        try
        {
            var search = SearchBox.Text;
            var selectedDepotId = depotId ?? (DepotFilterBox.SelectedItem as VermoegenDepot)?.Id;
            var assetClass = ClassFilterBox.SelectedItem as string;

            _viewModel = new VermoegenViewModel
            {
                SelectedZeitraumFilter = _selectedPeriod
            };

            _loadingFilters = true;
            SearchBox.Text = search;
            PeriodBox.ItemsSource = _viewModel.ZeitraumFilterListe;
            PeriodBox.SelectedItem = _selectedPeriod;
            DepotFilterBox.ItemsSource = _viewModel.DepotFilterListe;
            DepotFilterBox.SelectedItem = _viewModel.DepotFilterListe.FirstOrDefault(value => value.Id == selectedDepotId)
                ?? _viewModel.DepotFilterListe.FirstOrDefault();
            ClassFilterBox.ItemsSource = _viewModel.AnlageklasseFilterListe;
            ClassFilterBox.SelectedItem = assetClass is not null && _viewModel.AnlageklasseFilterListe.Contains(assetClass)
                ? assetClass
                : _viewModel.AnlageklasseFilterListe.FirstOrDefault();
            _loadingFilters = false;

            ApplyFilter(positionId);
        }
        catch (Exception exception)
        {
            _loadingFilters = false;
            ShowStatus("Wealth konnte nicht geladen werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void ApplyFilter(int? positionId = null)
    {
        if (_viewModel is null)
            return;

        _viewModel.Suchtext = SearchBox.Text ?? string.Empty;
        _viewModel.SelectedDepotFilter = DepotFilterBox.SelectedItem as VermoegenDepot;
        _viewModel.SelectedAnlageklasseFilter = ClassFilterBox.SelectedItem as string ?? "Alle Anlageklassen";
        _viewModel.SucheCommand.Execute(null);
        PositionsList.ItemsSource = _viewModel.Positionen;
        PositionsList.SelectedItem = positionId.HasValue
            ? _viewModel.Positionen.FirstOrDefault(value => value.Id == positionId)
            : _viewModel.Positionen.FirstOrDefault();
        FilterTitleText.Text = _viewModel.FilterTitelText;
        DepotValueText.Text = _viewModel.DepotwertText;
        CostValueText.Text = _viewModel.EinstandText;
        ProfitValueText.Text = _viewModel.GewinnVerlustText;
        ShowStatus(_viewModel.StatusText, InfoBarSeverity.Informational);
        UpdateDepotButtons();
        RenderCharts();
    }

    private void OnPeriodSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingFilters || _viewModel is null || PeriodBox.SelectedItem is not string period)
            return;

        _selectedPeriod = period;
        _viewModel.SelectedZeitraumFilter = period;
        RenderDepotHistory();
    }

    private void OnDepotHistorySizeChanged(object sender, SizeChangedEventArgs e) => RenderDepotHistory();

    private void RenderCharts()
    {
        RenderDepotHistory();
        RenderAllocation();
    }

    private void RenderDepotHistory()
    {
        var values = _viewModel?.DepotVerlaufDaten
            .Select(value => new WealthChartPoint(
                value.Datum,
                (double)value.DepotwertChf,
                $"{value.Datum:dd.MM.yyyy} · CHF {value.DepotwertChf.ToString("N2", Swiss)}"))
            .ToArray() ?? [];

        WealthChartRenderer.RenderLine(
            DepotHistoryCanvas,
            values,
            value => value.ToString("N0", Swiss));
    }

    private void RenderAllocation()
    {
        var values = _viewModel?.VermoegenAufteilungDaten
            .Select(value => new WealthChartSlice(value.LegendeText, (double)value.WertChf))
            .ToArray() ?? [];

        WealthChartRenderer.RenderDonut(AllocationCanvas, AllocationLegendPanel, values);
    }
    private void OnSearchClick(object sender, RoutedEventArgs e) => ApplyFilter(); private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == Windows.System.VirtualKey.Enter) ApplyFilter(); }
    private void OnClearFilterClick(object sender, RoutedEventArgs e) { SearchBox.Text = string.Empty; if (_viewModel is null) return; DepotFilterBox.SelectedItem = _viewModel.DepotFilterListe.FirstOrDefault(); ClassFilterBox.SelectedItem = _viewModel.AnlageklasseFilterListe.FirstOrDefault(); ApplyFilter(); }
    private void OnRefreshClick(object sender, RoutedEventArgs e) => Reload(_selected?.Id);
    private async void OnNewDepotClick(object sender, RoutedEventArgs e) => await EditDepotAsync(null);
    private async void OnEditDepotClick(object sender, RoutedEventArgs e) { if (DepotFilterBox.SelectedItem is VermoegenDepot { Id: > 0 } depot) await EditDepotAsync(depot); }
    private async Task EditDepotAsync(VermoegenDepot? depot) { var dialog = new WealthDepotEditorDialog(depot) { XamlRoot = XamlRoot }; if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return; var result = dialog.Result; var id = depot is null ? await Task.Run(() => _database.VermoegenDepotInsert(result)) : depot.Id; if (depot is not null) await Task.Run(() => _database.VermoegenDepotUpdate(result)); Reload(depotId: id); }
    private async void OnHideDepotClick(object sender, RoutedEventArgs e) { if (DepotFilterBox.SelectedItem is not VermoegenDepot { Id: > 0 } depot || !await ConfirmAsync("Depot ausblenden?", $"Das Depot „{depot.Name}“ wird deaktiviert.")) return; await Task.Run(() => _database.VermoegenDepotDelete(depot.Id)); Reload(); }
    private async void OnNewPositionClick(object sender, RoutedEventArgs e) => await EditPositionAsync(null);
    private async void OnEditPositionClick(object sender, RoutedEventArgs e) { if (_selected is null) return; var model = _database.VermoegenPositionenGetAll().FirstOrDefault(value => value.Id == _selected.Id); if (model is not null) await EditPositionAsync(model); }
    private async Task EditPositionAsync(VermoegenPosition? position) { var depots = _database.VermoegenDepotsGetAll(); if (!depots.Any(value => value.IstAktiv)) { await MessageAsync("Vermögensposition", "Bitte zuerst ein Depot erfassen."); return; } var dialog = new WealthPositionEditorDialog(depots, position) { XamlRoot = XamlRoot }; if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return; var model = dialog.Result; var id = position is null ? await Task.Run(() => _database.VermoegenPositionInsert(model)) : position.Id; if (position is not null) await Task.Run(() => _database.VermoegenPositionUpdate(model)); if (model.AktuellerKurs.HasValue && model.KursDatum.HasValue) await Task.Run(() => _database.VermoegenKursHistorieInsertIfMissing(id, model.KursDatum.Value, model.AktuellerKurs.Value, "Manuell")); Reload(id, model.DepotId); }
    private async void OnDeletePositionClick(object sender, RoutedEventArgs e) { if (_selected is null || !await ConfirmAsync("Position löschen?", $"„{_selected.Titel}“ wird gelöscht.")) return; await Task.Run(() => _database.VermoegenPositionDelete(_selected.Id)); Reload(); }
    private async void OnImportClick(object sender, RoutedEventArgs e) { if (DepotFilterBox.SelectedItem is not VermoegenDepot { Id: > 0 } depot) { await MessageAsync("Positionen importieren", "Bitte zuerst im Filter ein Depot auswählen."); return; } var path = await FilePickerService.PickOpenAsync(".xlsx", ".xlsm"); if (path is null) return; try { var result = await Task.Run(() => new VermoegenPositionExcelImporter().Import(path, depot.Id)); Reload(depotId: depot.Id); await MessageAsync("Positionen importieren", $"Gelesen: {result.RowsRead}\nImportiert: {result.RowsImported}\nÜbersprungen: {result.RowsSkipped}\nFehler: {result.RowsWithErrors}" + (result.Errors.Count > 0 ? "\n\n" + string.Join("\n", result.Errors.Take(10)) : string.Empty)); } catch (Exception exception) { ShowStatus("Import fehlgeschlagen: " + exception.Message, InfoBarSeverity.Error); } }
    private async void OnApiClick(object sender, RoutedEventArgs e) { var setting = _database.VermoegenApiEinstellungGet(); var keyBox = new PasswordBox { Header = "EODHD API-Key", Password = setting.ApiKey }; var activeBox = new CheckBox { Content = "Aktiv", IsChecked = setting.Aktiv }; var panel = new StackPanel { Width = 520, Spacing = 12 }; panel.Children.Add(keyBox); panel.Children.Add(activeBox); var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "API-Einstellung", Content = panel, PrimaryButtonText = "Speichern", CloseButtonText = "Abbrechen" }; if (await dialog.ShowAsync() != ContentDialogResult.Primary) return; setting.ApiProvider = "EODHD"; setting.ApiKey = keyBox.Password.Trim(); setting.Aktiv = activeBox.IsChecked == true; _database.VermoegenApiEinstellungSave(setting); }
    private async void OnUpdatePricesClick(object sender, RoutedEventArgs e) { try { ShowStatus("Kurse werden aktualisiert …", InfoBarSeverity.Informational); var result = await new VermoegenKursUpdateService().AktualisierenAsync(); Reload(_selected?.Id); ShowStatus(string.IsNullOrWhiteSpace(result.Meldung) ? "Kursaktualisierung abgeschlossen." : result.Meldung, InfoBarSeverity.Success); } catch (Exception exception) { ShowStatus("Kursaktualisierung fehlgeschlagen: " + exception.Message, InfoBarSeverity.Error); } }
    private void OnHistoryClick(object sender, RoutedEventArgs e) { if (_selected is null) return; var model = _database.VermoegenPositionenGetAll().FirstOrDefault(value => value.Id == _selected.Id); if (model is null) return; var window = new WealthHistoryWindow(model); _windows.Add(window); window.Closed += (_, _) => { _windows.Remove(window); Reload(model.Id); }; window.Activate(); }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) { _selected = PositionsList.SelectedItem as VermoegenPositionRow; EditPositionButton.IsEnabled = DeletePositionButton.IsEnabled = HistoryButton.IsEnabled = _selected is not null; }
    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => OnEditPositionClick(sender, e);
    private void UpdateDepotButtons() { var enabled = DepotFilterBox.SelectedItem is VermoegenDepot { Id: > 0 }; EditDepotButton.IsEnabled = HideDepotButton.IsEnabled = enabled; }
    private async Task<bool> ConfirmAsync(string title, string text) => await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = text, PrimaryButtonText = "Ja", CloseButtonText = "Nein", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Primary;
    private async Task MessageAsync(string title, string text) => await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = text, CloseButtonText = "Schließen" }.ShowAsync();
    private void ShowStatus(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
}
