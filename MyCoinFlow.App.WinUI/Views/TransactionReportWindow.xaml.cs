using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.ViewModels;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Printing;
using System.Windows.Documents;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class TransactionReportWindow : PersistentWindow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private readonly TransactionReportViewModel _viewModel = new();
    private bool _initializing = true;
    private BudgetProjectionWindow? _budgetWindow;

    public TransactionReportWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1760, 940));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1280;
            presenter.PreferredMinimumHeight = 760;
        }

        ReportTitleBox.Text = _viewModel.Berichtstitel;
        BudgetPeriodBox.ItemsSource = _viewModel.Budgetzeitraeume;
        ReportModeBox.ItemsSource = _viewModel.Berichtsarten;
        GroupingBox.ItemsSource = _viewModel.Gruppierungen;
        NumberRangesList.ItemsSource = _viewModel.Nummernkreise;
        AccountsList.ItemsSource = _viewModel.Konten;
        ResultRowsList.ItemsSource = ResultRows;

        BudgetPeriodBox.SelectedItem = _viewModel.AusgewaehlterBudgetzeitraum;
        ReportModeBox.SelectedItem = _viewModel.AusgewaehlteBerichtsart;
        GroupingBox.SelectedItem = _viewModel.AusgewaehlteGruppierung;
        ApplyDatesFromViewModel();
        _initializing = false;
        RefreshPresentation();
    }

    public ObservableCollection<ReportDisplayRow> ResultRows { get; } = new();

    private void OnBudgetPeriodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        _viewModel.AusgewaehlterBudgetzeitraum = BudgetPeriodBox.SelectedItem as Budgetzeitraum;
        ApplyDatesFromViewModel();
        RefreshPresentation();
    }

    private void OnReportOptionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        _viewModel.AusgewaehlteBerichtsart = ReportModeBox.SelectedItem as TransactionReportChoice<TransactionReportMode>;
        _viewModel.AusgewaehlteGruppierung = GroupingBox.SelectedItem as TransactionReportChoice<TransactionReportGrouping>;
        RefreshPresentation();
    }

    private void OnApplyNumberRangesClick(object sender, RoutedEventArgs e)
    {
        _viewModel.NummernkreiseAnwendenCommand.Execute(null);
        RefreshPresentation();
    }

    private void OnAllAccountsClick(object sender, RoutedEventArgs e)
    {
        _viewModel.AlleKontenCommand.Execute(null);
        RefreshPresentation();
    }

    private void OnNoAccountsClick(object sender, RoutedEventArgs e)
    {
        _viewModel.KeineKontenCommand.Execute(null);
        RefreshPresentation();
    }

    private void OnBudgetAccountsClick(object sender, RoutedEventArgs e)
    {
        _viewModel.NurBudgetkontenCommand.Execute(null);
        RefreshPresentation();
    }

    private void OnAccountSelectionClick(object sender, RoutedEventArgs e) => RefreshPresentation();

    private void OnEvaluateClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Berichtstitel = ReportTitleBox.Text;
        _viewModel.AuswertungVon = FromPicker.SelectedDate?.Date;
        _viewModel.AuswertungBis = ToPicker.SelectedDate?.Date;
        _viewModel.AusgewaehlteBerichtsart = ReportModeBox.SelectedItem as TransactionReportChoice<TransactionReportMode>;
        _viewModel.AusgewaehlteGruppierung = GroupingBox.SelectedItem as TransactionReportChoice<TransactionReportGrouping>;
        _viewModel.AuswertenCommand.Execute(null);
        RefreshPresentation();
    }

    private void OnAdjustBudgetClick(object sender, RoutedEventArgs e)
    {
        if (_budgetWindow is not null)
        {
            _budgetWindow.Activate();
            return;
        }

        try
        {
            var preview = _viewModel.ErstelleBudgetanpassungsVorschau();
            var window = new BudgetProjectionWindow(preview);
            _budgetWindow = window;
            window.ApplyRequested += (_, _) =>
            {
                try
                {
                    var count = _viewModel.BudgetanpassungenUebernehmen(preview.Zeilen);
                    RefreshPresentation();
                    ShowStatus($"{count} Budgetwerte wurden erfolgreich übernommen.", InfoBarSeverity.Success);
                }
                catch (Exception exception)
                {
                    ShowStatus("Budgetwerte konnten nicht angepasst werden: " + exception.Message, InfoBarSeverity.Error);
                }
            };
            window.Closed += (_, _) => _budgetWindow = null;
            window.Activate();
        }
        catch (Exception exception)
        {
            ShowStatus("Budgetwerte konnten nicht vorbereitet werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentResult == null)
        {
            ShowStatus("Bitte zuerst eine Auswertung erstellen.", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var dialog = new System.Windows.Controls.PrintDialog();
            if (dialog.ShowDialog() != true) return;

            dialog.PrintTicket ??= new PrintTicket();
            dialog.PrintTicket.PageOrientation = PageOrientation.Landscape;
            dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);

            var document = TransactionReportDocumentBuilder.Build(
                _viewModel.CurrentResult,
                dialog.PrintableAreaWidth,
                dialog.PrintableAreaHeight);
            dialog.PrintDocument(
                ((IDocumentPaginatorSource)document).DocumentPaginator,
                _viewModel.CurrentResult.Optionen.Titel);
        }
        catch (Exception exception)
        {
            ShowStatus("Der Bericht konnte nicht gedruckt werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ApplyDatesFromViewModel()
    {
        FromPicker.SelectedDate = _viewModel.AuswertungVon;
        ToPicker.SelectedDate = _viewModel.AuswertungBis;
    }

    private void RefreshPresentation()
    {
        ResultRows.Clear();
        foreach (var row in _viewModel.Ergebniszeilen)
            ResultRows.Add(new ReportDisplayRow(row));

        TopExpensesList.ItemsSource = _viewModel.GroessteAusgaben.Select(SpotlightText).ToList();
        TopIncomeList.ItemsSource = _viewModel.GroessteEinnahmen.Select(SpotlightText).ToList();
        CriticalList.ItemsSource = _viewModel.GroessteAbweichungen
            .Select(row => $"{row.Konto} · {row.Bezeichnung} · {row.Richtung} · Δ Jahr {Format(row.DeltaJahr)}")
            .ToList();

        SelectedAccountCountText.Text = $"{_viewModel.AusgewaehlteKontenAnzahl} gewählt";
        ExpenseSummaryText.Text = _viewModel.AusgabenZusammenfassung;
        IncomeSummaryText.Text = _viewModel.EinnahmenZusammenfassung;
        NetSummaryText.Text = _viewModel.NettoZusammenfassung;
        AccountColumnTitle.Text = _viewModel.KontoSpaltenTitel.ToUpperInvariant();
        SpotlightBasisText.Text = _viewModel.SpotlightBasis;
        SpotlightSummaryText.Text = _viewModel.SpotlightZusammenfassung;
        AdjustBudgetButton.IsEnabled = _viewModel.KannBudgetAnpassen;
        PrintButton.IsEnabled = _viewModel.HatErgebnis;
        StatusBar.Message = _viewModel.StatusText;
        StatusBar.Severity = _viewModel.StatusText.StartsWith("Auswertung nicht", StringComparison.OrdinalIgnoreCase) ||
                             _viewModel.StatusText.Contains("konnten nicht", StringComparison.OrdinalIgnoreCase)
            ? InfoBarSeverity.Error
            : InfoBarSeverity.Informational;
        StatusBar.IsOpen = true;
    }

    private static string SpotlightText(TransactionReportSpotlightRow row) =>
        $"{row.Rang}. {row.Konto} · {row.Bezeichnung} · Ist/Budget {row.Betrag.ToString("N2", SwissCulture)} · " +
        $"Anteil {row.AnteilProzent.ToString("N1", SwissCulture)} % · Hochrechnung {Format(row.HochrechnungJahr)}";

    private static string Format(decimal? value, string format = "N2") =>
        value?.ToString(format, SwissCulture) ?? "–";

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    public sealed class ReportDisplayRow
    {
        public ReportDisplayRow(TransactionReportRow row)
        {
            Account = row.Konto;
            Name = row.Bezeichnung;
            Direction = row.Richtung;
            Budget = Format(row.BudgetJahr);
            Target = Format(row.SollZeitraum);
            Actual = Format(row.IstZeitraum);
            Projection = Format(row.HochrechnungJahr, "N0");
            PeriodDelta = Format(row.DeltaZeitraum);
            YearDelta = Format(row.DeltaJahr);
            Fulfillment = row.ErfuellungProzent.HasValue
                ? row.ErfuellungProzent.Value.ToString("N1", SwissCulture) + " %"
                : "–";
        }

        public string Account { get; }
        public string Name { get; }
        public string Direction { get; }
        public string Budget { get; }
        public string Target { get; }
        public string Actual { get; }
        public string Projection { get; }
        public string PeriodDelta { get; }
        public string YearDelta { get; }
        public string Fulfillment { get; }
    }
}
