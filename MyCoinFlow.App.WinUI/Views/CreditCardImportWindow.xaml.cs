using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Import;
using MyCoinFlow.Models;
using MyCoinFlow.ViewModels;
using System.Collections.ObjectModel;
using System.Globalization;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class CreditCardImportWindow : PersistentWindow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private readonly CreditCardImportViewModel _viewModel = new();
    private BookingAssignmentWindow? _assignmentWindow;
    private bool _initializing = true;

    public CreditCardImportWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1600, 880));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1200;
            presenter.PreferredMinimumHeight = 680;
        }

        RowsList.ItemsSource = VisibleRows;
        BatchBox.ItemsSource = _viewModel.Batches;
        ClearingAccountBox.ItemsSource = _viewModel.KreditkartenKonten;
        InstitutionBox.ItemsSource = _viewModel.Geldinstitute;
        FilterBox.ItemsSource = new[] { "Alle", "Offen", "Zugewiesen" };
        FilterBox.SelectedItem = _viewModel.FilterModus;

        ClearingAccountBox.SelectedItem = _viewModel.AusgleichsKontoId.HasValue
            ? _viewModel.KreditkartenKonten.FirstOrDefault(account => account.Id == _viewModel.AusgleichsKontoId.Value)
            : null;
        InstitutionBox.SelectedItem = _viewModel.AusgewaehltesGeldinstitutId.HasValue
            ? _viewModel.Geldinstitute.FirstOrDefault(institution => institution.Id == _viewModel.AusgewaehltesGeldinstitutId.Value)
            : null;

        _initializing = false;
        RefreshView();
    }

    public ObservableCollection<CreditCardImportRow> VisibleRows { get; } = new();
    public bool Changed { get; private set; }

    private void OnChooseFileClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndRefresh(_viewModel.DateiWaehlenCommand);
        BatchBox.SelectedItem = _viewModel.AusgewaehlterBatch;
    }

    private void OnBookClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.BuchenCommand.CanExecute(null))
        {
            ShowStatus("Zum Buchen müssen ein Verrechnungskonto und mindestens eine zugeordnete Zeile vorhanden sein.", InfoBarSeverity.Warning);
            return;
        }

        Changed = true;
        ExecuteAndRefresh(_viewModel.BuchenCommand);
    }

    private void OnDiscardBatchClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CurrentBatchId.HasValue)
        {
            ShowStatus("Kein Batch geladen.", InfoBarSeverity.Warning);
            return;
        }

        _viewModel.BatchVerwerfenCommand.Execute(null);
        BatchBox.SelectedItem = null;
        RefreshView();
    }

    private void OnBatchSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        _viewModel.AusgewaehlterBatch = BatchBox.SelectedItem as CreditCardBatchInfo;
    }

    private void OnLoadBatchClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.AusgewaehlterBatch == null)
        {
            ShowStatus("Bitte einen Batch auswählen.", InfoBarSeverity.Warning);
            return;
        }

        _viewModel.BatchLadenCommand.Execute(null);
        RefreshView();
    }

    private void OnRefreshBatchesClick(object sender, RoutedEventArgs e)
    {
        _viewModel.BatchesNeuLadenCommand.Execute(null);
        RefreshView();
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || FilterBox.SelectedItem is not string filter) return;
        _viewModel.FilterModus = filter;
        RefreshView();
    }

    private void OnClearingAccountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        _viewModel.AusgleichsKontoId = (ClearingAccountBox.SelectedItem as KontoLookup)?.Id;
        RefreshSummary();
    }

    private void OnInstitutionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        _viewModel.AusgewaehltesGeldinstitutId = (InstitutionBox.SelectedItem as Geldinstitut)?.Id;
    }

    private void OnStatementTotalLostFocus(object sender, RoutedEventArgs e)
    {
        if (decimal.TryParse(StatementTotalBox.Text, NumberStyles.Number, SwissCulture, out var value) ||
            decimal.TryParse(StatementTotalBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
        {
            _viewModel.Abrechnungssumme = value;
        }
        RefreshSummary();
    }

    private void OnAssignRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CreditCardImportRow row) return;
        if (_assignmentWindow is not null)
        {
            _assignmentWindow.Activate();
            return;
        }

        var isIncome = !IsDebit(row.DebitKredit);
        var item = new BankImportItem
        {
            BookingDate = row.Datum,
            Amount = row.Betrag,
            Direction = isIncome ? KreditDebit.Credit : KreditDebit.Debit,
            Currency = "CHF",
            Text = row.Beschreibung,
            ServiceRef = row.Kategorie ?? "",
            CounterpartyName = row.Haendler,
            VorschlagAdresseId = row.AdresseId,
            VorschlagNachKontoId = row.KontoId
        };

        try
        {
            var window = new BookingAssignmentWindow(item);
            _assignmentWindow = window;
            window.AssignmentCompleted += (_, result) =>
            {
                try
                {
                    _viewModel.UebernehmeZuordnung(row, result.AddressId, result.AccountId);
                    RefreshView();
                    ShowStatus("Zuordnung übernommen; offene Zeilen dieses Batches wurden mit den neuen Regeln erneut geprüft.", InfoBarSeverity.Success);
                }
                catch (Exception exception)
                {
                    ShowStatus("Zuordnung fehlgeschlagen: " + exception.Message, InfoBarSeverity.Error);
                }
            };
            window.Closed += (_, _) => _assignmentWindow = null;
            window.Activate();
        }
        catch (Exception exception)
        {
            ShowStatus("Zuordnung konnte nicht geöffnet werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnDeleteRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CreditCardImportRow row) return;
        _viewModel.ZeileLoeschenCommand.Execute(row);
        RefreshView();
    }

    private void OnWebSearchRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CreditCardImportRow row)
            _viewModel.WebRechercheCommand.Execute(row);
    }

    private void ExecuteAndRefresh(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
        RefreshView();
    }

    private void RefreshView()
    {
        VisibleRows.Clear();
        foreach (var row in _viewModel.Zeilen.Where(row => _viewModel.FilterModus switch
                 {
                     "Offen" => !row.KontoId.HasValue,
                     "Zugewiesen" => row.KontoId.HasValue,
                     _ => true
                 }))
        {
            VisibleRows.Add(row);
        }

        RefreshSummary();
        StatusBar.Message = _viewModel.CurrentBatchId.HasValue
            ? $"Batch {_viewModel.CurrentBatchId}: {_viewModel.Zeilen.Count} Zeilen · {_viewModel.AnzahlZuweisbar} zugeordnet · {_viewModel.AnzahlOffen} offen"
            : "Excel-Datei wählen oder einen offenen Batch laden.";
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.IsOpen = true;
    }

    private void RefreshSummary()
    {
        TotalText.Text = _viewModel.SummeListe.ToString("N2", SwissCulture);
        AssignedText.Text = _viewModel.SummeZugeordnet.ToString("N2", SwissCulture);
        OpenText.Text = _viewModel.SummeOffen.ToString("N2", SwissCulture);
        StatementTotalBox.Text = _viewModel.Abrechnungssumme?.ToString("N2", SwissCulture) ?? "";
        DifferenceText.Text = _viewModel.DifferenzZurAbrechnung.ToString("N2", SwissCulture);
    }

    private static bool IsDebit(string? debitCredit)
    {
        var value = (debitCredit ?? "").Trim().ToUpperInvariant();
        return value is "BELASTUNG" or "DEBIT" or "SOLL" or "CHARGE" or "AUSGABE" or "DEBITO";
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
