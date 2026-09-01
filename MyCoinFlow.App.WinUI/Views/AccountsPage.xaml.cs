using Microsoft.Data.SqlClient;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using System.Globalization;
using Windows.System;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class AccountsPage : Page
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private readonly DatabaseService _database = new();
    private readonly AccountRepository _repository = new();
    private readonly Dictionary<TreeViewNode, KontoplanEintrag?> _treeAccounts = new();
    private readonly HashSet<AccountTransactionsWindow> _transactionWindows = new();
    private List<KontoplanEintrag> _accounts = new();
    private List<AccountDisplayRow> _allRows = new();
    private KontoplanEintrag? _selectedAccount;
    private bool _initialized;
    private bool _loading;
    private bool _isUnloading;

    public AccountsPage()
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
        await ReloadAsync(prefillPeriod: true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isUnloading = true;
        foreach (var window in _transactionWindows.ToList())
            window.Close();
        _transactionWindows.Clear();
    }

    private async Task ReloadAsync(bool prefillPeriod = false)
    {
        if (_loading) return;
        _loading = true;
        BusyRing.IsActive = true;
        StatusBar.IsOpen = false;
        var selectedId = _selectedAccount?.Id;
        try
        {
            var result = await Task.Run(() =>
            {
                var accounts = _database.LadeKontenplan();
                Budgetzeitraum? activePeriod = null;
                try
                {
                    var activeId = _database.HoleAktivenBudgetzeitraumId();
                    if (activeId.HasValue)
                        activePeriod = _database.HoleBudgetzeitraum(activeId.Value);
                }
                catch
                {
                    // Die WPF-Seite behandelt eine fehlende Zeitraumvorbelegung ebenfalls defensiv.
                }
                return (accounts, activePeriod);
            });

            _accounts = result.accounts;
            _allRows = _accounts.Select(account => new AccountDisplayRow(account)).ToList();
            AccountFilterBox.ItemsSource = _allRows;
            if (prefillPeriod && FromPicker.SelectedDate is null && ToPicker.SelectedDate is null && result.activePeriod != null)
            {
                FromPicker.SelectedDate = new DateTimeOffset(result.activePeriod.Startdatum.Date);
                ToPicker.SelectedDate = new DateTimeOffset(result.activePeriod.Enddatum.Date);
            }

            ActivePeriodText.Text = result.activePeriod is null
                ? "Kein aktiver Budgetzeitraum"
                : $"Aktiver Zeitraum: {result.activePeriod.Startdatum:dd.MM.yyyy} – {result.activePeriod.Enddatum:dd.MM.yyyy}";
            AccountCountText.Text = $"{_accounts.Count:N0} Konten";
            BudgetTotalText.Text = $"Budget {_accounts.Sum(account => account.Budgetwert ?? 0m).ToString("C2", SwissCulture)}";
            BookedTotalText.Text = $"Gebucht {_accounts.Sum(account => account.Gebucht).ToString("C2", SwissCulture)}";
            BuildTree();
            await ApplyFilterAsync();

            if (selectedId.HasValue)
            {
                var selectedRow = (AccountsList.ItemsSource as IEnumerable<AccountDisplayRow>)?
                    .FirstOrDefault(row => row.Id == selectedId.Value);
                if (selectedRow != null)
                    AccountsList.SelectedItem = selectedRow;
            }
        }
        catch (Exception exception)
        {
            ShowStatus("Kontenplan konnte nicht geladen werden: " + exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            BusyRing.IsActive = false;
            _loading = false;
        }
    }

    private async Task ApplyFilterAsync()
    {
        var tokens = (SearchBox.Text ?? string.Empty)
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length > 0)
            .ToArray();
        var selectedAccountId = (AccountFilterBox.SelectedItem as AccountDisplayRow)?.Id;
        var from = FromPicker.SelectedDate?.Date;
        var to = ToPicker.SelectedDate?.Date;
        var source = _accounts.ToList();

        var filtered = await Task.Run(() =>
        {
            IEnumerable<KontoplanEintrag> query = source;
            if (tokens.Length > 0)
            {
                query = query.Where(account => tokens.All(token =>
                    account.Kontonummer.ToString(CultureInfo.InvariantCulture).Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    account.Art.Contains(token, StringComparison.CurrentCultureIgnoreCase) ||
                    account.Gruppe.Contains(token, StringComparison.CurrentCultureIgnoreCase) ||
                    account.Untergruppe.Contains(token, StringComparison.CurrentCultureIgnoreCase) ||
                    (account.Detail?.Contains(token, StringComparison.CurrentCultureIgnoreCase) ?? false)));
            }
            if (selectedAccountId.HasValue)
                query = query.Where(account => account.Id == selectedAccountId.Value);

            var current = query.ToList();
            if (from.HasValue || to.HasValue)
            {
                var database = new DatabaseService();
                current = current
                    .Where(account => database.KontoHatBuchungenImZeitraumByKontonummer(
                        account.Kontonummer, from, to))
                    .ToList();
            }
            return current.Select(account => new AccountDisplayRow(account)).ToList();
        });

        AccountsList.ItemsSource = filtered;
        ResultText.Text = $"{filtered.Count:N0} von {_accounts.Count:N0} Konten in der Tabellenansicht";
        if (_selectedAccount != null && filtered.All(row => row.Id != _selectedAccount.Id) && ViewToggle.IsOn)
            SetSelectedAccount(null);
    }

    private void BuildTree()
    {
        _treeAccounts.Clear();
        AccountsTree.RootNodes.Clear();
        foreach (var model in BuildTreeModels(_accounts))
            AccountsTree.RootNodes.Add(CreateTreeNode(model));
    }

    private TreeViewNode CreateTreeNode(KontoplanKnoten model)
    {
        var node = new TreeViewNode { Content = model.AnzeigeText };
        _treeAccounts[node] = model.OriginalEintrag;
        foreach (var child in model.Kinder)
            node.Children.Add(CreateTreeNode(child));
        return node;
    }

    private static IReadOnlyList<KontoplanKnoten> BuildTreeModels(IEnumerable<KontoplanEintrag> accounts)
    {
        return accounts
            .GroupBy(account => (account.Art ?? string.Empty).Trim())
            .OrderBy(group => group.Key)
            .Select(artGroup =>
            {
                var art = new KontoplanKnoten(string.IsNullOrWhiteSpace(artGroup.Key) ? "(ohne Art)" : artGroup.Key);
                foreach (var groupGroup in artGroup.GroupBy(account => (account.Gruppe ?? string.Empty).Trim()).OrderBy(group => group.Key))
                {
                    var group = new KontoplanKnoten(string.IsNullOrWhiteSpace(groupGroup.Key) ? "(ohne Gruppe)" : groupGroup.Key);
                    art.Kinder.Add(group);
                    foreach (var subgroupGroup in groupGroup.GroupBy(account => (account.Untergruppe ?? string.Empty).Trim()).OrderBy(group => group.Key))
                    {
                        var subgroup = new KontoplanKnoten(string.IsNullOrWhiteSpace(subgroupGroup.Key) ? "(ohne Untergruppe)" : subgroupGroup.Key);
                        group.Kinder.Add(subgroup);
                        foreach (var account in subgroupGroup.OrderBy(account => account.Kontonummer).ThenBy(account => account.Detail))
                            subgroup.Kinder.Add(new KontoplanKnoten(account.Detail ?? "(ohne Detail)", account));
                    }
                }
                return art;
            })
            .ToList();
    }

    private void OnViewToggled(object sender, RoutedEventArgs e)
    {
        var table = ViewToggle.IsOn;
        TableHost.Visibility = table ? Visibility.Visible : Visibility.Collapsed;
        TreeHost.Visibility = table ? Visibility.Collapsed : Visibility.Visible;
        if (table && AccountsList.SelectedItem is AccountDisplayRow row)
            SetSelectedAccount(row.Account);
        else if (!table && AccountsTree.SelectedNode != null && _treeAccounts.TryGetValue(AccountsTree.SelectedNode, out var account))
            SetSelectedAccount(account);
    }

    private void OnAccountSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SetSelectedAccount((AccountsList.SelectedItem as AccountDisplayRow)?.Account);

    private void OnTreeSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        var selectedNode = sender.SelectedNode;
        SetSelectedAccount(selectedNode != null && _treeAccounts.TryGetValue(selectedNode, out var account)
            ? account
            : null);
    }

    private void SetSelectedAccount(KontoplanEintrag? account)
    {
        _selectedAccount = account;
        EditButton.IsEnabled = account?.Id > 0;
        DeleteButton.IsEnabled = account?.Id > 0;
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void OnApplyFilterClick(object sender, RoutedEventArgs e)
    {
        try { await ApplyFilterAsync(); }
        catch (Exception exception) { ShowStatus(exception.Message, InfoBarSeverity.Error); }
    }

    private async void OnClearFilterClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        FromPicker.SelectedDate = null;
        ToPicker.SelectedDate = null;
        AccountFilterBox.SelectedItem = null;
        await ApplyFilterAsync();
    }

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        await ApplyFilterAsync();
    }

    private async void OnNewClick(object sender, RoutedEventArgs e) => await ShowEditorAsync(null);
    private async void OnEditClick(object sender, RoutedEventArgs e) => await ShowEditorAsync(_selectedAccount);

    private async Task ShowEditorAsync(KontoplanEintrag? account)
    {
        try
        {
            var dialog = new AccountEditorDialog(account) { XamlRoot = XamlRoot };
            await dialog.InitializeAsync();
            await dialog.ShowAsync();
            if (!dialog.Saved) return;

            UiEvents.RaiseReloadKontenplan();
            ShowStatus(account is null ? "Konto wurde gespeichert." : "Konto wurde aktualisiert.", InfoBarSeverity.Success);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAccount is not { } account) return;
        try
        {
            if (!await ConfirmAsync(
                    "Konto löschen?",
                    $"{account.Kontonummer:D4} — {account.Detail}\n\nDieser Vorgang kann nicht rückgängig gemacht werden.",
                    "Löschen"))
                return;

            var plan = await _repository.AnalyzeDeletionAsync(account.Id);
            if (plan.MappingCount > 0)
            {
                var suffix = plan.HasReferencesOtherThanMappings
                    ? "\n\nHinweis: Weitere Verweise werden nicht automatisch gelöscht."
                    : string.Empty;
                var question = plan.MappingCount == 1
                    ? $"Zu diesem Konto existiert 1 Mapping in KategorieKontoMapping.\n\nKonto zusammen mit diesem Mapping löschen?{suffix}"
                    : $"Zu diesem Konto existieren {plan.MappingCount} Mappings in KategorieKontoMapping.\n\nKonto zusammen mit diesen Mappings löschen?{suffix}";
                if (!await ConfirmAsync("Konto und Mappings löschen?", question, "Mappings lösen"))
                    return;
                await _repository.DeleteCategoryMappingsAsync(account.Id);
                plan = await _repository.AnalyzeDeletionAsync(account.Id);
            }

            if (plan.AddressCount > 0 && !plan.HasHardBlockersBesidesAddresses)
            {
                var examples = plan.AddressExamples.Count == 0
                    ? string.Empty
                    : "\n\nBeispiele:\n" + string.Join("\n", plan.AddressExamples.Select(example => "• " + example));
                var question = plan.AddressCount == 1
                    ? $"1 Adresse referenziert dieses Konto als Standardkonto.{examples}\n\nStandardkonto bei den Adressen leeren und das Konto löschen? Die Adressen bleiben erhalten."
                    : $"{plan.AddressCount} Adressen referenzieren dieses Konto als Standardkonto.{examples}\n\nStandardkonto bei den Adressen leeren und das Konto löschen? Die Adressen bleiben erhalten.";
                if (!await ConfirmAsync("Standardkonto lösen?", question, "Verknüpfung lösen"))
                    return;
                await _repository.ClearAddressDefaultsAsync(account.Id);
                plan = await _repository.AnalyzeDeletionAsync(account.Id);
            }

            if (plan.HasReferences)
            {
                var grouped = plan.References
                    .GroupBy(reference => reference.TableName)
                    .Select(group => $"• {group.Key}: {group.Sum(reference => reference.Count)} Zeile(n)")
                    .Take(6);
                await ShowMessageAsync(
                    "Löschen nicht möglich",
                    "Der Kontenplan-Eintrag kann nicht gelöscht werden, weil noch Verweise existieren:\n\n" +
                    string.Join("\n", grouped) +
                    "\n\nBitte zuerst diese Verweise auflösen und danach erneut versuchen.");
                return;
            }

            await _repository.DeleteAsync(account.Id);
            SetSelectedAccount(null);
            UiEvents.RaiseReloadKontenplan();
            ShowStatus("Konto wurde gelöscht.", InfoBarSeverity.Success);
            await ReloadAsync();
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            await ShowMessageAsync(
                "Löschen nicht möglich",
                "Der Kontenplan-Eintrag kann nicht gelöscht werden, weil noch abhängige Daten existieren. Bitte diese zuerst entfernen oder umhängen.");
        }
        catch (Exception exception)
        {
            ShowStatus("Konto konnte nicht gelöscht werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnOpenAccountTransactionsClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AccountDisplayRow row) return;
        var window = new AccountTransactionsWindow(row.Account);
        _transactionWindows.Add(window);
        window.Closed += async (_, _) =>
        {
            _transactionWindows.Remove(window);
            if (!_isUnloading) await ReloadAsync();
        };
        window.Activate();
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primaryText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 },
            PrimaryButtonText = primaryText,
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 },
            CloseButtonText = "Schließen"
        };
        await dialog.ShowAsync();
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
