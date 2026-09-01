using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.WinUI.Services;
using MyCoinFlow.Import;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class BookingAssignmentWindow : PersistentWindow
{
    private readonly BankImportItem _item;
    private readonly BookingAssignmentWorkflow _workflow = new();
    private readonly IReadOnlyList<KontoLookup> _accounts;
    private int? _selectedAccountId;
    private bool _initializing = true;

    public BookingAssignmentWindow(BankImportItem item)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        InitializeComponent();

        AppWindow.Resize(new SizeInt32(900, 850));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        var state = _workflow.Load(item);
        _accounts = state.Accounts;

        BookingInfoText.Text =
            $"{item.BookingDate:yyyy-MM-dd}  |  {item.Amount:N2} {item.Currency}  |  " +
            (item.Direction == KreditDebit.Debit ? "Ausgabe" : "Einnahme");
        BookingText.Text = string.IsNullOrWhiteSpace(item.Text) ? "(kein Buchungstext)" : item.Text;
        InstitutionInfoText.Text = !string.IsNullOrWhiteSpace(item.AccountIban)
            ? $"Konto-IBAN: {item.AccountIban}"
            : "unbekannt";

        AddressBox.ItemsSource = state.Addresses;
        AddressBox.SelectedItem = state.SelectedAddressId.HasValue
            ? state.Addresses.FirstOrDefault(address => address.Id == state.SelectedAddressId.Value)
            : null;
        NewAddressCheck.IsChecked = state.CreateNewAddress;
        NewAddressNameBox.Text = state.NewAddressName;
        NewAddressIbanBox.Text = state.NewAddressIban;
        BudgetIncomeCheck.IsChecked = state.IsBudgetedIncome;

        _selectedAccountId = state.SelectedAccountId;
        AccountBox.ItemsSource = _accounts;
        AccountBox.Text = state.SelectedAccountId.HasValue
            ? _accounts.FirstOrDefault(account => account.Id == state.SelectedAccountId.Value)?.Anzeige ?? ""
            : "";

        var quickAccounts = state.QuickAccountIds
            .Select(accountId => _accounts.FirstOrDefault(candidate => candidate.Id == accountId))
            .Where(account => account != null)
            .ToList();
        QuickAccountList.ItemsSource = quickAccounts;
        QuickChoiceCard.Visibility = quickAccounts.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(state.BudgetPeriodHint))
        {
            BudgetHintBar.Message = state.BudgetPeriodHint;
            BudgetHintBar.IsOpen = true;
        }

        _initializing = false;
        UpdateAddressPanels();
        UpdateRulePreview();
    }

    public event EventHandler<BookingAssignmentResult>? AssignmentCompleted;

    private void OnInputChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        UpdateAddressPanels();
        UpdateRulePreview();
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing) UpdateRulePreview();
    }

    private void OnAddressChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;

        if (AddressBox.SelectedItem is Adresse address &&
            _item.Direction == KreditDebit.Credit && address.IstBudgetiert)
        {
            BudgetIncomeCheck.IsChecked = true;
        }
        UpdateRulePreview();
    }

    private void UpdateAddressPanels()
    {
        var createNew = NewAddressCheck.IsChecked == true;
        ExistingAddressPanel.Visibility = createNew ? Visibility.Collapsed : Visibility.Visible;
        NewAddressPanel.Visibility = createNew ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAccountTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_initializing || args.Reason == AutoSuggestionBoxTextChangeReason.SuggestionChosen) return;

        var query = sender.Text.Trim();
        sender.ItemsSource = string.IsNullOrWhiteSpace(query)
            ? _accounts
            : _accounts.Where(account => account.Anzeige.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        var exact = _accounts.FirstOrDefault(account =>
            string.Equals(account.Anzeige, sender.Text, StringComparison.CurrentCultureIgnoreCase));
        _selectedAccountId = exact?.Id;
        UpdateRulePreview();
    }

    private void OnAccountSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not KontoLookup account) return;
        _selectedAccountId = account.Id;
        sender.Text = account.Anzeige;
        UpdateRulePreview();
    }

    private void OnQuickAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int accountId }) return;
        var account = _accounts.FirstOrDefault(candidate => candidate.Id == accountId);
        if (account == null) return;

        _selectedAccountId = account.Id;
        AccountBox.ItemsSource = _accounts;
        AccountBox.Text = account.Anzeige;
        UpdateRulePreview();
    }

    private void UpdateRulePreview()
    {
        var addressName = NewAddressCheck.IsChecked == true
            ? NewAddressNameBox.Text
            : (AddressBox.SelectedItem as Adresse)?.Name;
        var isTransfer = !string.IsNullOrWhiteSpace(addressName) &&
                         addressName.Trim().StartsWith("Interne Umbuchung", StringComparison.CurrentCultureIgnoreCase);
        var isIncome = _item.Direction == KreditDebit.Credit;
        var accountLabel = _selectedAccountId.HasValue
            ? _accounts.FirstOrDefault(account => account.Id == _selectedAccountId.Value)?.Anzeige ?? "(Konto auswählen)"
            : "(Konto auswählen)";

        if (!isIncome)
        {
            if (isTransfer)
            {
                BookingTypeIcon.Glyph = "\uE8AB";
                BookingTypeText.Text = "Umbuchung (Bank ↔ Bank)";
                BookingTypeHint.Text = "Durchlaufkonto (DefaultKonto der Umbuchungs-Adresse) wird verwendet.";
            }
            else
            {
                BookingTypeIcon.Glyph = "\uE8C8";
                BookingTypeText.Text = "Bank → Konto (Ausgabe)";
                BookingTypeHint.Text = $"Ziel (Nach-Konto): {accountLabel}";
            }
        }
        else if (BudgetIncomeCheck.IsChecked == true)
        {
            BookingTypeIcon.Glyph = "\uE8D4";
            BookingTypeText.Text = "Adresse → Bank (Einnahme)";
            BookingTypeHint.Text = $"Nach-Konto (Einnahmenkonto): {accountLabel}";
        }
        else
        {
            BookingTypeIcon.Glyph = "\uE825";
            BookingTypeText.Text = "Konto → Bank (Refund)";
            BookingTypeHint.Text = $"Von-Konto (Rückzahlungskonto): {accountLabel}";
        }
    }

    private void OnWebClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!WebRechercheService.OpenSearch(_item.Text, _item.CounterpartyName))
                ShowStatus("Kein verwertbarer Buchungstext für die Recherche vorhanden.", InfoBarSeverity.Informational);
        }
        catch (Exception exception)
        {
            ShowStatus("Recherche konnte nicht geöffnet werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = _workflow.Save(_item, new BookingAssignmentInput(
                NewAddressCheck.IsChecked == true,
                (AddressBox.SelectedItem as Adresse)?.Id,
                NewAddressNameBox.Text,
                NewAddressIbanBox.Text,
                BudgetIncomeCheck.IsChecked == true,
                _selectedAccountId));

            AssignmentCompleted?.Invoke(this, result);
            Close();
        }
        catch (Exception exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
