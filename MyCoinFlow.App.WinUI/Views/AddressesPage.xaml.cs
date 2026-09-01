using Microsoft.Data.SqlClient;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using Windows.System;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class AddressesPage : Page
{
    private const string AliasTable = "dbo.AdresseAlias";
    private readonly DatabaseService _database = new();
    private readonly AddressRepository _repository = new();
    private readonly HashSet<AddressTransactionsWindow> _transactionWindows = new();
    private List<AddressDisplayRow> _allRows = new();
    private AddressDisplayRow? _selectedAddress;
    private bool _initialized;
    private bool _loading;
    private bool _isUnloading;

    public AddressesPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isUnloading = false;
        if (_initialized) return;
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
        selectedId ??= _selectedAddress?.Id;

        try
        {
            var addresses = await Task.Run(() => _database.LadeAdressen());
            _allRows = addresses.Select(address => new AddressDisplayRow(address)).ToList();
            ApplySearch(selectedId);
        }
        catch (Exception exception)
        {
            ShowStatus("Adressen konnten nicht geladen werden: " + exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            BusyRing.IsActive = false;
            _loading = false;
        }
    }

    private void ApplySearch(int? selectedId = null)
    {
        selectedId ??= _selectedAddress?.Id;
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var tokens = query.Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var filtered = tokens.Length == 0
            ? _allRows
            : _allRows.Where(row => tokens.All(token => Matches(row, token))).ToList();

        AddressesList.ItemsSource = filtered;
        AddressesList.SelectedItem = selectedId.HasValue
            ? filtered.FirstOrDefault(row => row.Id == selectedId.Value)
            : null;
        ResultText.Text = tokens.Length == 0
            ? $"{filtered.Count:N0} Adressen"
            : $"{filtered.Count:N0} von {_allRows.Count:N0} Adressen";
    }

    private static bool Matches(AddressDisplayRow row, string token)
    {
        var values = new[]
        {
            row.Name,
            row.Street,
            row.PostalCode,
            row.City,
            row.Country,
            row.Type,
            row.Iban,
            row.Note
        };
        return values.Any(value => value.Contains(token, StringComparison.CurrentCultureIgnoreCase));
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void OnSearchClick(object sender, RoutedEventArgs e) => ApplySearch();

    private void OnClearSearchClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        ApplySearch();
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        ApplySearch();
        e.Handled = true;
    }

    private async void OnNewClick(object sender, RoutedEventArgs e) => await ShowEditorAsync(null);

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAddress is not null)
            await ShowEditorAsync(_selectedAddress.Address);
    }

    private async Task ShowEditorAsync(Adresse? address)
    {
        try
        {
            var source = address is null
                ? null
                : await Task.Run(() => _database.HoleAdresse(address.Id));
            var dialog = new AddressEditorDialog(source) { XamlRoot = XamlRoot };
            await dialog.InitializeAsync();
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || !dialog.Saved) return;

            await ReloadAsync(dialog.SavedId);
            ShowStatus(
                address is null ? "Adresse wurde angelegt." : "Adresse wurde aktualisiert.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus("Adresse konnte nicht gespeichert werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAddress is null) return;
        var address = _selectedAddress;

        try
        {
            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Adresse löschen?",
                Content = $"„{address.Name}“ wirklich löschen?",
                PrimaryButtonText = "Löschen",
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Close
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

            var references = await _repository.AnalyzeDeletionAsync(address.Id);
            var aliasCount = references
                .Where(reference => reference.TableName.Equals(AliasTable, StringComparison.OrdinalIgnoreCase))
                .Sum(reference => reference.Count);
            var hasOtherReferences = references.Any(reference =>
                !reference.TableName.Equals(AliasTable, StringComparison.OrdinalIgnoreCase));

            if (aliasCount > 0)
            {
                var additionalNote = hasOtherReferences
                    ? "\n\nHinweis: Es existieren weitere Verweise (z. B. Transaktionen). Diese werden nicht automatisch gelöscht."
                    : string.Empty;
                var aliasQuestion = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "Adresse & Aliase löschen",
                    Content = aliasCount == 1
                        ? $"Zu dieser Adresse existiert 1 Alias.\n\nAdresse zusammen mit diesem Alias löschen?{additionalNote}"
                        : $"Zu dieser Adresse existieren {aliasCount} Aliase.\n\nAdresse zusammen mit diesen Aliasen löschen?{additionalNote}",
                    PrimaryButtonText = "Zusammen löschen",
                    CloseButtonText = "Abbrechen",
                    DefaultButton = ContentDialogButton.Close
                };
                if (await aliasQuestion.ShowAsync() != ContentDialogResult.Primary) return;

                await _repository.DeleteAliasesAsync(address.Id);
                references = await _repository.AnalyzeDeletionAsync(address.Id);
            }

            if (references.Count > 0)
            {
                var lines = references
                    .GroupBy(reference => reference.TableName, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new { Table = group.Key, Count = group.Sum(reference => reference.Count) })
                    .OrderByDescending(reference => reference.Count)
                    .Take(8)
                    .Select(reference => $"• {reference.Table}: {reference.Count} Zeile(n)");
                var blocked = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "Löschen nicht möglich",
                    Content = "Adresse kann nicht gelöscht werden, weil noch Verweise existieren:\n\n" +
                              string.Join("\n", lines) +
                              "\n\nBitte zuerst diese Verweise auflösen und dann erneut versuchen.",
                    CloseButtonText = "Schließen"
                };
                await blocked.ShowAsync();
                return;
            }

            await _repository.DeleteAsync(address.Id);
            _selectedAddress = null;
            await ReloadAsync();
            ShowStatus("Adresse wurde gelöscht.", InfoBarSeverity.Success);
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            ShowStatus("Die Adresse kann nicht gelöscht werden, weil noch abhängige Daten existieren.", InfoBarSeverity.Warning);
        }
        catch (Exception exception)
        {
            ShowStatus("Adresse konnte nicht gelöscht werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnAddressSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedAddress = AddressesList.SelectedItem as AddressDisplayRow;
        var hasSelection = _selectedAddress is not null;
        EditButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
    }

    private void OnAddressDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_selectedAddress is not null)
            OpenTransactions(_selectedAddress);
    }

    private void OnOpenTransactionsClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AddressDisplayRow row)
            OpenTransactions(row);
    }

    private void OpenTransactions(AddressDisplayRow row)
    {
        try
        {
            var window = new AddressTransactionsWindow(row.Address);
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
