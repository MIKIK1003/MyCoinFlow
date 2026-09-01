using Microsoft.Data.SqlClient;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using System.Globalization;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InstitutionsPage : Page
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private readonly DatabaseService _database = new();
    private readonly InstitutionRepository _repository = new();
    private readonly HashSet<InstitutionTransactionsWindow> _transactionWindows = new();
    private List<InstitutionDisplayRow> _rows = new();
    private InstitutionDisplayRow? _selectedInstitution;
    private bool _initialized;
    private bool _loading;
    private bool _isUnloading;

    public InstitutionsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isUnloading = false;
        if (_initialized) return;
        CutoffPicker.SelectedDate = new DateTimeOffset(DateTime.Today);
        _initialized = true;
        await ReloadAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isUnloading = true;
        foreach (var window in _transactionWindows.ToList())
            window.Close();
        _transactionWindows.Clear();
    }

    private async Task ReloadAsync(int? selectedId = null)
    {
        if (_loading) return;
        _loading = true;
        BusyRing.IsActive = true;
        StatusBar.IsOpen = false;
        selectedId ??= _selectedInstitution?.Id;

        try
        {
            var cutoff = CutoffPicker.SelectedDate?.Date ?? DateTime.Today;
            var institutions = await Task.Run(() => _database.LadeGeldinstituteMitSaldo(cutoff));
            _rows = institutions.Select(institution => new InstitutionDisplayRow(institution)).ToList();
            InstitutionsList.ItemsSource = _rows;

            InstitutionCountText.Text = $"{_rows.Count:N0} Geldinstitute";
            InitialTotalText.Text = $"Anfang {_rows.Sum(row => row.Institution.Anfangsbestand).ToString("C2", SwissCulture)}";
            BookedTotalText.Text = $"Gebucht {_rows.Sum(row => row.Institution.Gebucht).ToString("C2", SwissCulture)}";
            ClosingTotalText.Text = $"Saldo {_rows.Sum(row => row.Institution.Schlussaldo).ToString("C2", SwissCulture)}";
            ResultText.Text = $"Salden per {cutoff:dd.MM.yyyy} · Buchungen werden ab dem jeweiligen Anfangsdatum berücksichtigt";

            InstitutionsList.SelectedItem = selectedId.HasValue
                ? _rows.FirstOrDefault(row => row.Id == selectedId.Value)
                : null;
        }
        catch (Exception exception)
        {
            ShowStatus("Geldinstitute konnten nicht geladen werden: " + exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            BusyRing.IsActive = false;
            _loading = false;
        }
    }

    private async void OnCutoffDateChanged(object sender, DatePickerValueChangedEventArgs args)
    {
        if (_initialized && !_loading)
            await ReloadAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void OnNewClick(object sender, RoutedEventArgs e) => await ShowEditorAsync(null);

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_selectedInstitution is null) return;
        var source = _selectedInstitution.Institution;
        var copy = new Geldinstitut
        {
            Id = source.Id,
            Name = source.Name,
            BIC = source.BIC,
            IBAN = source.IBAN,
            KontoNummer = source.KontoNummer,
            Notiz = source.Notiz,
            Anfangsbestand = source.Anfangsbestand,
            Anfangsdatum = source.Anfangsdatum
        };
        await ShowEditorAsync(copy);
    }

    private async Task ShowEditorAsync(Geldinstitut? institution)
    {
        try
        {
            var dialog = new InstitutionEditorDialog(institution) { XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || !dialog.Saved) return;
            await ReloadAsync(dialog.SavedId);
            ShowStatus(
                institution is null ? "Geldinstitut wurde angelegt." : "Geldinstitut wurde aktualisiert.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus("Geldinstitut konnte nicht gespeichert werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selectedInstitution is null) return;
        var institution = _selectedInstitution;
        try
        {
            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Geldinstitut löschen?",
                Content = $"„{institution.Name}“ wirklich löschen?",
                PrimaryButtonText = "Löschen",
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Close
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

            var references = await _repository.AnalyzeDeletionAsync(institution.Id);
            if (references.Count > 0)
            {
                var lines = references
                    .GroupBy(reference => reference.TableName, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new { Table = group.Key, Count = group.Sum(reference => reference.Count) })
                    .OrderByDescending(reference => reference.Count)
                    .Take(6)
                    .Select(reference => $"• {reference.Table}: {reference.Count} Zeile(n)");
                var blocked = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "Löschen nicht möglich",
                    Content = "Geldinstitut kann nicht gelöscht werden, weil noch Verweise existieren:\n\n" +
                              string.Join("\n", lines) +
                              "\n\nBitte zuerst diese Verweise auflösen (z. B. umbuchen oder löschen) und dann erneut versuchen.",
                    CloseButtonText = "Schließen"
                };
                await blocked.ShowAsync();
                return;
            }

            await _repository.DeleteAsync(institution.Id);
            _selectedInstitution = null;
            await ReloadAsync();
            ShowStatus("Geldinstitut wurde gelöscht.", InfoBarSeverity.Success);
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            ShowStatus("Das Geldinstitut kann nicht gelöscht werden, weil noch abhängige Daten existieren.", InfoBarSeverity.Warning);
        }
        catch (Exception exception)
        {
            ShowStatus("Geldinstitut konnte nicht gelöscht werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnInstitutionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedInstitution = InstitutionsList.SelectedItem as InstitutionDisplayRow;
        var hasSelection = _selectedInstitution is not null;
        EditButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
    }

    private void OnInstitutionDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_selectedInstitution is not null)
            OpenTransactions(_selectedInstitution);
    }

    private void OnOpenTransactionsClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is InstitutionDisplayRow row)
            OpenTransactions(row);
    }

    private void OpenTransactions(InstitutionDisplayRow row)
    {
        try
        {
            var window = new InstitutionTransactionsWindow(row.Institution);
            _transactionWindows.Add(window);
            window.Closed += async (_, _) =>
            {
                _transactionWindows.Remove(window);
                if (window.Changed && !_isUnloading)
                    await ReloadAsync(row.Id);
            };
            window.Activate();
        }
        catch (Exception exception)
        {
            ShowStatus("Transaktionen konnten nicht geöffnet werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
