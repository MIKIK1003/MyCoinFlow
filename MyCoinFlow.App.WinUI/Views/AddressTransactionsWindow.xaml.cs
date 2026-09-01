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

public sealed partial class AddressTransactionsWindow : PersistentWindow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private readonly DatabaseService _database = new();
    private readonly TransactionRepository _transactionRepository = new();
    private readonly AdresseTransaktionenViewModel _viewModel;

    public AddressTransactionsWindow(Adresse address)
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1460, 860));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1120;
            presenter.PreferredMinimumHeight = 720;
        }

        _viewModel = new AdresseTransaktionenViewModel(address.Id, address.Name);
        Title = _viewModel.Titel;
        TitleText.Text = _viewModel.Titel;
        AccountBox.ItemsSource = _viewModel.KontenLookup;
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
        AccountBox.SelectedValue = _viewModel.FilterKontoId;
        InstitutionBox.SelectedValue = _viewModel.FilterGeldinstitutId;
    }

    private void ApplyControlsToViewModel()
    {
        _viewModel.FilterMinBetrag = ParseNullableAmount(MinimumBox.Text);
        _viewModel.FilterMaxBetrag = ParseNullableAmount(MaximumBox.Text);
        _viewModel.FilterVon = FromPicker.SelectedDate?.Date;
        _viewModel.FilterBis = ToPicker.SelectedDate?.Date;
        _viewModel.FilterKontoId = AccountBox.SelectedValue is int accountId ? accountId : null;
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
        var rows = _viewModel.Rows.Select(row => new AddressTransactionDisplayRow(row)).ToList();
        TransactionsList.ItemsSource = rows;
        TransactionsList.SelectedItem = selectedId.HasValue
            ? rows.FirstOrDefault(row => row.Id == selectedId.Value)
            : null;
        IncomeText.Text = _viewModel.SummeEinnahmen.ToString("C2", SwissCulture);
        ExpenseText.Text = _viewModel.SummeAusgaben.ToString("C2", SwissCulture);
        BalanceText.Text = _viewModel.Saldo.ToString("C2", SwissCulture);
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
        EditButton.IsEnabled = TransactionsList.SelectedItem is AddressTransactionDisplayRow;

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (TransactionsList.SelectedItem is not AddressTransactionDisplayRow selected) return;
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
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "Adresse-Transaktionen");
        }
        catch (Exception exception)
        {
            ShowStatus("Druck fehlgeschlagen: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private FlowDocument BuildPrintDocument(double printableWidth, double printableHeight)
    {
        var widths = FitColumns(printableWidth, 190d, 70d, 110d, 110d, 220d, 360d);
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10,
            PagePadding = new System.Windows.Thickness(24),
            ColumnWidth = double.PositiveInfinity,
            PageWidth = printableWidth,
            PageHeight = printableHeight
        };
        document.Blocks.Add(new Paragraph(new Bold(new Run(_viewModel.Titel)))
        {
            Margin = new System.Windows.Thickness(0, 0, 0, 4)
        });
        document.Blocks.Add(new Paragraph(new Run(_viewModel.SummaryText))
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
        foreach (var width in widths)
            table.Columns.Add(new TableColumn { Width = new System.Windows.GridLength(width) });

        var header = new TableRowGroup();
        var headerRow = new TableRow();
        header.Rows.Add(headerRow);
        foreach (var text in new[] { "Konto", "Datum", "Einnahmen", "Ausgaben", "Geldinstitut", "Notiz" })
            AddCell(headerRow, text, bold: true, background: Brushes.LightGray);
        table.RowGroups.Add(header);

        var data = new TableRowGroup();
        foreach (var row in _viewModel.Rows.OrderByDescending(row => row.Datum).ThenByDescending(row => row.Id))
        {
            var tableRow = new TableRow();
            data.Rows.Add(tableRow);
            AddCell(tableRow, Truncate(row.Konto, 50));
            AddCell(tableRow, row.Datum.ToString("yyyy-MM-dd", SwissCulture));
            AddCell(tableRow, row.Einnahmen == 0m ? string.Empty : row.Einnahmen.ToString("N2", SwissCulture), right: true);
            AddCell(tableRow, row.Ausgaben == 0m ? string.Empty : row.Ausgaben.ToString("N2", SwissCulture), right: true);
            AddCell(tableRow, Truncate(row.GeldinstitutName ?? string.Empty, 40));
            AddCell(tableRow, Truncate(row.Notiz ?? string.Empty, 140));
        }
        table.RowGroups.Add(data);

        var totals = new TableRowGroup();
        var totalsRow = new TableRow();
        totals.Rows.Add(totalsRow);
        AddCell(totalsRow, "Summen / Saldo", bold: true);
        AddCell(totalsRow, string.Empty);
        AddCell(totalsRow, _viewModel.SummeEinnahmen.ToString("N2", SwissCulture), bold: true, right: true);
        AddCell(totalsRow, _viewModel.SummeAusgaben.ToString("N2", SwissCulture), bold: true, right: true);
        AddCell(totalsRow, string.Empty);
        AddCell(totalsRow, $"Saldo: {_viewModel.Saldo.ToString("N2", SwissCulture)}", bold: true, right: true);
        table.RowGroups.Add(totals);
        return document;
    }

    private static double[] FitColumns(double printableWidth, params double[] widths)
    {
        var available = Math.Max(300d, printableWidth - 48d);
        var total = widths.Sum();
        if (total <= available) return widths;

        var noteReduction = Math.Min(widths[^1] - 180d, total - available);
        if (noteReduction > 0)
        {
            widths[^1] -= noteReduction;
            total -= noteReduction;
        }
        if (total <= available) return widths;

        var scale = available / total;
        return widths.Select(width => width * scale).ToArray();
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
        return $"Zeitraum: {period}" +
               (_viewModel.FilterKontoId.HasValue ? $"  |  Konto-ID: {_viewModel.FilterKontoId.Value}" : string.Empty) +
               (_viewModel.FilterGeldinstitutId.HasValue ? $"  |  Bank-ID: {_viewModel.FilterGeldinstitutId.Value}" : string.Empty) +
               (_viewModel.FilterMinBetrag.HasValue ? $"  |  Betrag ≥ {_viewModel.FilterMinBetrag.Value.ToString("N2", SwissCulture)}" : string.Empty) +
               (_viewModel.FilterMaxBetrag.HasValue ? $"  |  Betrag ≤ {_viewModel.FilterMaxBetrag.Value.ToString("N2", SwissCulture)}" : string.Empty);
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

    private static string Truncate(string value, int maximum) =>
        string.IsNullOrEmpty(value) || value.Length <= maximum
            ? value
            : value[..Math.Max(0, maximum - 1)] + "…";

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
