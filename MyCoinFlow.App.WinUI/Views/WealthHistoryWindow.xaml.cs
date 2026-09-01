using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Services;
using System.Globalization;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class WealthHistoryWindow : PersistentWindow
{
    private static readonly CultureInfo Swiss = CultureInfo.GetCultureInfo("de-CH");
    private static readonly string[] Periods = ["1 Monat", "3 Monate", "6 Monate", "1 Jahr", "Alles"];
    private readonly DatabaseService _database = new();
    private readonly VermoegenPosition _position;
    private IReadOnlyList<VermoegenKursHistorie> _chartValues = [];
    private string _selectedPeriod = "3 Monate";
    private bool _loadingPeriod;
    private bool _isForeignCurrency;

    public WealthHistoryWindow(VermoegenPosition position)
    {
        InitializeComponent();
        _position = position;
        HeadingText.Text = $"Kursverlauf – {position.Titel}";
        SubheadingText.Text = BuildSubtitle(position);
        DatePicker.Date = DateTime.Today;
        AppWindow.Resize(new SizeInt32(1100, 760));

        _loadingPeriod = true;
        PeriodBox.ItemsSource = Periods;
        PeriodBox.SelectedItem = _selectedPeriod;
        _loadingPeriod = false;
        Load();
    }

    private void Load(int? id = null)
    {
        var startDate = PeriodStartDate(_selectedPeriod);
        var values = _database.VermoegenKursHistorieGetByPosition(_position.Id)
            .Where(value => !startDate.HasValue || value.KursDatum.Date >= startDate.Value)
            .OrderBy(value => value.KursDatum)
            .ToList();
        _chartValues = values;

        _isForeignCurrency = !string.Equals(
            string.IsNullOrWhiteSpace(_position.Waehrung) ? "CHF" : _position.Waehrung.Trim(),
            "CHF",
            StringComparison.OrdinalIgnoreCase);
        var fxHistory = _isForeignCurrency
            ? _database.VermoegenFxHistorieGetNachChf(_position.Waehrung)
            : [];

        var rows = values
            .OrderByDescending(value => value.KursDatum)
            .Select(value =>
            {
                decimal? fx = _isForeignCurrency
                    ? fxHistory.LastOrDefault(item => item.KursDatum.Date <= value.KursDatum.Date)?.Kurs
                    : 1m;
                var priceChf = fx.HasValue ? value.Kurs * fx.Value : (decimal?)null;
                return new WealthHistoryRow
                {
                    Model = value,
                    KursDatumText = value.KursDatum.ToString("dd.MM.yyyy"),
                    KursText = value.Kurs.ToString("N2", Swiss),
                    FxKursText = _isForeignCurrency ? fx?.ToString("N6", Swiss) ?? "-" : string.Empty,
                    KursChfText = _isForeignCurrency ? priceChf?.ToString("N2", Swiss) ?? "-" : string.Empty,
                    Quelle = value.Quelle,
                    ErfasstAmText = value.ErfasstAm.ToString("dd.MM.yyyy HH:mm"),
                    FxColumnWidth = _isForeignCurrency ? new GridLength(120) : new GridLength(0)
                };
            })
            .ToList();

        FxHeaderColumn.Width = _isForeignCurrency ? new GridLength(120) : new GridLength(0);
        ChfHeaderColumn.Width = _isForeignCurrency ? new GridLength(120) : new GridLength(0);
        HistoryList.ItemsSource = rows;
        HistoryList.SelectedItem = id.HasValue
            ? rows.FirstOrDefault(value => value.Model.Id == id.Value)
            : null;
        RenderChart();
    }

    private void RenderChart()
    {
        var currency = string.IsNullOrWhiteSpace(_position.Waehrung) ? "CHF" : _position.Waehrung.Trim();
        var points = _chartValues
            .Select(value => new WealthChartPoint(
                value.KursDatum,
                (double)value.Kurs,
                $"{value.KursDatum:dd.MM.yyyy} · {currency} {value.Kurs.ToString("N2", Swiss)}"))
            .ToArray();
        WealthChartRenderer.RenderLine(PriceHistoryCanvas, points, value => value.ToString("N2", Swiss));
    }

    private void OnPeriodSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPeriod || PeriodBox.SelectedItem is not string period)
            return;
        _selectedPeriod = period;
        Load();
    }

    private void OnPriceHistorySizeChanged(object sender, SizeChangedEventArgs e) => RenderChart();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var value = HistoryList.SelectedItem as WealthHistoryRow;
        UpdateButton.IsEnabled = DeleteButton.IsEnabled = value is not null;
        if (value is null)
            return;
        DatePicker.Date = value.Model.KursDatum;
        PriceBox.Text = value.Model.Kurs.ToString("N6", Swiss).TrimEnd('0').TrimEnd('.');
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryInput(out var date, out var price))
            return;
        _database.VermoegenKursHistorieInsertIfMissing(_position.Id, date, price, "Manuell");
        _database.VermoegenPositionKursUpdate(_position.Id, price, date);
        Load();
        await Task.CompletedTask;
    }

    private void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not WealthHistoryRow value || !TryInput(out var date, out var price))
            return;
        _database.VermoegenKursHistorieUpdate(value.Model.Id, date, price, "Manuell");
        _database.VermoegenPositionKursUpdate(_position.Id, price, date);
        Load(value.Model.Id);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not WealthHistoryRow value)
            return;
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Kurs löschen?",
            Content = $"{value.Model.KursDatum:dd.MM.yyyy} · {value.Model.Kurs}",
            PrimaryButtonText = "Löschen",
            CloseButtonText = "Abbrechen"
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        _database.VermoegenKursHistorieDelete(value.Model.Id);
        Load();
    }

    private bool TryInput(out DateTime date, out decimal price)
    {
        date = DatePicker.Date?.Date ?? default;
        price = 0m;
        if (date == default ||
            !decimal.TryParse(PriceBox.Text?.Trim().Replace("'", string.Empty), NumberStyles.Number, Swiss, out price) ||
            price < 0m)
        {
            StatusBar.Message = "Bitte Kursdatum und einen gültigen Kurs erfassen.";
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.IsOpen = true;
            return false;
        }
        return true;
    }

    private static DateTime? PeriodStartDate(string period) => period switch
    {
        "1 Monat" => DateTime.Today.AddMonths(-1),
        "3 Monate" => DateTime.Today.AddMonths(-3),
        "6 Monate" => DateTime.Today.AddMonths(-6),
        "1 Jahr" => DateTime.Today.AddYears(-1),
        _ => null
    };

    private static string BuildSubtitle(VermoegenPosition position)
    {
        var parts = new[]
        {
            string.IsNullOrWhiteSpace(position.ISIN) ? null : $"ISIN: {position.ISIN}",
            string.IsNullOrWhiteSpace(position.Valor) ? null : $"Valor: {position.Valor}",
            string.IsNullOrWhiteSpace(position.Symbol) ? null : $"Symbol: {position.Symbol}",
            string.IsNullOrWhiteSpace(position.Boerse) ? null : $"Börse: {position.Boerse}",
            string.IsNullOrWhiteSpace(position.Waehrung) ? null : $"Währung: {position.Waehrung}"
        };
        return string.Join(" · ", parts.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private sealed class WealthHistoryRow
    {
        public required VermoegenKursHistorie Model { get; init; }
        public string KursDatumText { get; init; } = string.Empty;
        public string KursText { get; init; } = string.Empty;
        public string FxKursText { get; init; } = string.Empty;
        public string KursChfText { get; init; } = string.Empty;
        public string Quelle { get; init; } = string.Empty;
        public string ErfasstAmText { get; init; } = string.Empty;
        public GridLength FxColumnWidth { get; init; }
    }
}
