using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.ViewModels;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using System.Globalization;
using System.Printing;
using System.Windows.Documents;
using System.Windows.Media;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class AccountTransactionsWindow : PersistentWindow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private readonly DatabaseService _database = new();
    private readonly TransactionRepository _transactionRepository = new();
    private readonly KontoTransaktionenViewModel _viewModel;

    public AccountTransactionsWindow(KontoplanEintrag account)
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1460, 860));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1120;
            presenter.PreferredMinimumHeight = 720;
        }

        _viewModel = new KontoTransaktionenViewModel(
            account.Id,
            string.IsNullOrWhiteSpace(account.Detail) ? $"Konto {account.Kontonummer:D4}" : account.Detail);
        Title = _viewModel.Titel;
        TitleText.Text = _viewModel.Titel;
        AddressBox.ItemsSource = _viewModel.Adressen;
        InstitutionBox.ItemsSource = _viewModel.Geldinstitute;
        ApplyFilterControlsFromViewModel();
        RefreshPresentation();
    }

    public bool Changed { get; private set; }

    private void ApplyFilterControlsFromViewModel()
    {
        MinimumBox.Text = _viewModel.FilterMinBetrag?.ToString("N2", SwissCulture) ?? string.Empty;
        MaximumBox.Text = _viewModel.FilterMaxBetrag?.ToString("N2", SwissCulture) ?? string.Empty;
        FromPicker.SelectedDate = _viewModel.FilterVon.HasValue ? new DateTimeOffset(_viewModel.FilterVon.Value) : null;
        ToPicker.SelectedDate = _viewModel.FilterBis.HasValue ? new DateTimeOffset(_viewModel.FilterBis.Value) : null;
        AddressBox.SelectedValue = _viewModel.FilterAdresseId;
        InstitutionBox.SelectedValue = _viewModel.FilterGeldinstitutId;
    }

    private void ApplyControlsToViewModel()
    {
        _viewModel.FilterMinBetrag = ParseNullableAmount(MinimumBox.Text);
        _viewModel.FilterMaxBetrag = ParseNullableAmount(MaximumBox.Text);
        _viewModel.FilterVon = FromPicker.SelectedDate?.Date;
        _viewModel.FilterBis = ToPicker.SelectedDate?.Date;
        _viewModel.FilterAdresseId = AddressBox.SelectedValue is int addressId ? addressId : null;
        _viewModel.FilterGeldinstitutId = InstitutionBox.SelectedValue is int institutionId ? institutionId : null;
    }

    private static decimal? ParseNullableAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (decimal.TryParse(text, NumberStyles.Number, SwissCulture, out var swiss)) return swiss;
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var current)) return current;
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant)) return invariant;
        throw new InvalidOperationException($"Ungültiger Betrag: {text}");
    }

    private void RefreshPresentation(int? selectedId = null)
    {
        var rows = _viewModel.Rows.Select(row => new AccountTransactionDisplayRow(row)).ToList();
        TransactionsList.ItemsSource = rows;
        TransactionsList.SelectedItem = selectedId.HasValue
            ? rows.FirstOrDefault(row => row.Id == selectedId.Value)
            : null;
        IncomeText.Text = _viewModel.SumEinnahmen.ToString("C2", SwissCulture);
        ExpenseText.Text = _viewModel.SumAusgaben.ToString("C2", SwissCulture);
        BalanceText.Text = _viewModel.Saldo.ToString("C2", SwissCulture);
        BudgetText.Text = _viewModel.Budget.ToString("C2", SwissCulture);
        DeltaText.Text = _viewModel.Delta.ToString("C2", SwissCulture);
        StatusBar.Message = $"{rows.Count:N0} Buchungen im gewählten Zeitraum";
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.IsOpen = true;
    }

    private void OnApplyFilterClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyControlsToViewModel();
            _viewModel.ApplyFilterCommand.Execute(null);
            RefreshPresentation();
        }
        catch (Exception exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnResetFilterClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.ResetFilterCommand.Execute(null);
            ApplyFilterControlsFromViewModel();
            RefreshPresentation();
        }
        catch (Exception exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        EditButton.IsEnabled = TransactionsList.SelectedItem is AccountTransactionDisplayRow;

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (TransactionsList.SelectedItem is not AccountTransactionDisplayRow selected) return;
        try
        {
            var transaction = await Task.Run(() => _database.HoleTransaktion(selected.Id));
            if (transaction is null)
                throw new InvalidOperationException("Die ausgewählte Transaktion konnte nicht geladen werden.");

            var record = new TransactionRecord
            {
                Id = transaction.Id,
                Datum = transaction.Datum,
                BudgetDatum = transaction.BudgetDatum,
                VonKontoId = transaction.VonKontoId,
                NachKontoId = transaction.NachKontoId,
                Betrag = transaction.Betrag,
                Notiz = transaction.Notiz,
                AdresseId = transaction.AdresseId,
                AdresseName = transaction.AdresseName,
                GeldinstitutId = transaction.GeldinstitutId,
                BankName = transaction.BankName,
                ImportQuelle = transaction.ImportQuelle
            };
            var dialog = new TransactionEditorDialog(_transactionRepository, record) { XamlRoot = RootGrid.XamlRoot };
            await dialog.InitializeAsync();
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            Changed = true;
            _viewModel.ApplyFilterCommand.Execute(null);
            RefreshPresentation(selected.Id);
            ShowStatus($"Transaktion #{selected.Id} wurde aktualisiert.", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus("Bearbeiten fehlgeschlagen: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new System.Windows.Controls.PrintDialog();
            if (dialog.ShowDialog() != true) return;
            dialog.PrintTicket ??= new PrintTicket();
            dialog.PrintTicket.PageOrientation = PageOrientation.Landscape;
            var document = BuildPrintDocument(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "Konto-Transaktionen");
        }
        catch (Exception exception)
        {
            ShowStatus("Druck fehlgeschlagen: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private FlowDocument BuildPrintDocument(double printableWidth, double printableHeight)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 9.5,
            PagePadding = new System.Windows.Thickness(24),
            ColumnWidth = double.PositiveInfinity,
            PageWidth = printableWidth,
            PageHeight = printableHeight
        };
        document.Blocks.Add(new Paragraph(new Bold(new Run(_viewModel.Titel)))
        {
            Margin = new System.Windows.Thickness(0, 0, 0, 4)
        });
        document.Blocks.Add(new Paragraph(new Run(BuildFilterLine()))
        {
            Margin = new System.Windows.Thickness(0, 0, 0, 8)
        });

        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = Brushes.Gray,
            BorderThickness = new System.Windows.Thickness(0.5)
        };
        document.Blocks.Add(table);
        foreach (var width in new[] { 190d, 70d, 100d, 100d, 230d, 380d })
            table.Columns.Add(new TableColumn { Width = new System.Windows.GridLength(width) });

        var header = new TableRowGroup();
        var headerRow = new TableRow();
        header.Rows.Add(headerRow);
        foreach (var text in new[] { "Geldinstitut", "Datum", "Einnahmen", "Ausgaben", "Adresse", "Notiz" })
            AddCell(headerRow, text, bold: true, background: Brushes.LightGray);
        table.RowGroups.Add(header);

        var data = new TableRowGroup();
        foreach (var row in _viewModel.Rows.OrderByDescending(row => row.Datum).ThenByDescending(row => row.Id))
        {
            var tableRow = new TableRow();
            data.Rows.Add(tableRow);
            AddCell(tableRow, row.GeldinstitutName ?? string.Empty);
            AddCell(tableRow, row.Datum.ToString("yy-MM-dd", SwissCulture));
            AddCell(tableRow, row.Einnahmen.ToString("N2", SwissCulture), right: true);
            AddCell(tableRow, row.Ausgaben.ToString("N2", SwissCulture), right: true);
            AddCell(tableRow, row.AdresseName ?? string.Empty);
            AddCell(tableRow, row.Notiz ?? string.Empty);
        }
        table.RowGroups.Add(data);

        var totals = new TableRowGroup();
        var totalsRow = new TableRow();
        totals.Rows.Add(totalsRow);
        AddCell(totalsRow, "Summen", bold: true);
        AddCell(totalsRow, string.Empty);
        AddCell(totalsRow, _viewModel.SumEinnahmen.ToString("N2", SwissCulture), bold: true, right: true);
        AddCell(totalsRow, _viewModel.SumAusgaben.ToString("N2", SwissCulture), bold: true, right: true);
        AddCell(totalsRow, string.Empty);
        AddCell(totalsRow,
            $"Saldo: {_viewModel.Saldo.ToString("N2", SwissCulture)}   |   " +
            $"Budget: {_viewModel.Budget.ToString("N2", SwissCulture)}   |   " +
            $"Δ: {_viewModel.Delta.ToString("N2", SwissCulture)}",
            bold: true,
            right: true);
        table.RowGroups.Add(totals);
        return document;
    }

    private static void AddCell(TableRow row, string text, bool bold = false, bool right = false, Brush? background = null)
    {
        var run = new Run(text);
        var paragraph = new Paragraph(bold ? new Bold(run) : run)
        {
            Margin = new System.Windows.Thickness(0),
            TextAlignment = right ? System.Windows.TextAlignment.Right : System.Windows.TextAlignment.Left
        };
        row.Cells.Add(new TableCell(paragraph)
        {
            Padding = new System.Windows.Thickness(2),
            BorderBrush = Brushes.Gray,
            BorderThickness = new System.Windows.Thickness(0.5),
            Background = background
        });
    }

    private string BuildFilterLine()
    {
        var period = (_viewModel.FilterVon, _viewModel.FilterBis) switch
        {
            (null, null) => "alle Daten",
            (DateTime from, null) => $"ab {from:yyyy-MM-dd}",
            (null, DateTime to) => $"bis {to:yyyy-MM-dd}",
            (DateTime from, DateTime to) => $"{from:yyyy-MM-dd} bis {to:yyyy-MM-dd}"
        };
        return $"Zeitraum: {period}   |   Einnahmen {_viewModel.SumEinnahmen.ToString("N2", SwissCulture)}   |   " +
               $"Ausgaben {_viewModel.SumAusgaben.ToString("N2", SwissCulture)}   |   " +
               $"Saldo {_viewModel.Saldo.ToString("N2", SwissCulture)}   |   " +
               $"Budget {_viewModel.Budget.ToString("N2", SwissCulture)}   |   " +
               $"Δ {_viewModel.Delta.ToString("N2", SwissCulture)}";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
