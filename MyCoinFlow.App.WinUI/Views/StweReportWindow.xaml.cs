using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.WinUI.Services;
using System.Printing;
using Windows.Graphics;
using WpfControls = System.Windows.Controls;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class StweReportWindow : PersistentWindow
{
    private readonly DatabaseService _database = new();
    private readonly StweLiegenschaft _property;
    private List<StweOwnerSummaryRow> _owners = new();
    private DateTime? From => FromPicker.Date?.Date;
    private DateTime? To => ToPicker.Date?.Date;

    public StweReportWindow(StweLiegenschaft property)
    {
        InitializeComponent();
        _property = property;
        HeadingText.Text = $"Auswertung – {_property.Name}";
        AppWindow.Resize(new SizeInt32(1260, 760));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 940;
            presenter.PreferredMinimumHeight = 580;
        }
        LoadDefaultPeriod();
        _ = LoadAsync();
    }

    private void LoadDefaultPeriod()
    {
        try
        {
            var id = _database.HoleAktivenBudgetzeitraumId();
            var period = id.HasValue ? _database.HoleBudgetzeitraum(id.Value) : null;
            if (period is not null)
            {
                FromPicker.Date = period.Startdatum;
                ToPicker.Date = period.Enddatum;
            }
        }
        catch { }
    }

    private async Task LoadAsync()
    {
        try
        {
            var from = From;
            var to = To;
            _owners = await Task.Run(() => _database.StweReportOwnerSummary(_property.Id, from, to));
            var rows = _owners.Select(value => new StweOwnerSummaryDisplayRow(value)).ToList();
            OwnersList.ItemsSource = rows;
            OwnersList.SelectedItem = rows.FirstOrDefault();
            PeriodText.Text = GetPeriodText();
        }
        catch (Exception exception)
        {
            ShowStatus("Auswertung konnte nicht geladen werden: " + GetExceptionMessage(exception), InfoBarSeverity.Error);
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void OnOwnerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OwnersList.SelectedItem is not StweOwnerSummaryDisplayRow selected)
        {
            DetailsList.ItemsSource = null;
            DetailsHeadingText.Text = "Details";
            return;
        }

        try
        {
            var from = From;
            var to = To;
            var values = await Task.Run(() => _database.StweReportOwnerDetails(
                _property.Id, selected.Value.EigentuemerId, from, to));
            DetailsList.ItemsSource = values.Select(value => new StweOwnerDetailDisplayRow(value)).ToList();
            DetailsHeadingText.Text = $"Details – {selected.Name}";
        }
        catch (Exception exception)
        {
            DetailsList.ItemsSource = null;
            ShowStatus("Details konnten nicht geladen werden: " + GetExceptionMessage(exception), InfoBarSeverity.Error);
        }
    }

    private async void OnPrintClick(object sender, RoutedEventArgs e)
    {
        var optionsDialog = new StweReportPrintOptionsDialog { XamlRoot = RootGrid.XamlRoot };
        if (await optionsDialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var printDialog = new WpfControls.PrintDialog();
            if (printDialog.ShowDialog() != true) return;
            if (printDialog.PrintTicket is not null)
            {
                printDialog.PrintTicket.PageOrientation = PageOrientation.Portrait;
                printDialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
            }

            var paginator = StweReportDocumentBuilder.Build(
                _database,
                _property,
                From,
                To,
                printDialog.PrintableAreaWidth,
                printDialog.PrintableAreaHeight,
                optionsDialog.Options);
            printDialog.PrintDocument(paginator, $"STWE-Auswertung – {_property.Name}");
        }
        catch (Exception exception)
        {
            ShowStatus("Druck fehlgeschlagen: " + GetExceptionMessage(exception), InfoBarSeverity.Error);
        }
    }

    private string GetPeriodText() => From is null && To is null
        ? "Zeitraum: —"
        : From is not null && To is null
            ? $"Zeitraum: ab {From:dd.MM.yyyy}"
            : From is null
                ? $"Zeitraum: bis {To:dd.MM.yyyy}"
                : $"Zeitraum: {From:dd.MM.yyyy} – {To:dd.MM.yyyy}";

    private static string GetExceptionMessage(Exception exception)
        => string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
