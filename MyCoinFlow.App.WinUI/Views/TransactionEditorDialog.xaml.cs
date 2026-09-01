using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using System.Globalization;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class TransactionEditorDialog : ContentDialog
{
    private readonly TransactionRepository _repository;
    private readonly TransactionRecord? _record;
    private BudgetPeriod? _activePeriod;
    private bool _initializing;

    public TransactionEditorDialog(TransactionRepository repository, TransactionRecord? record = null)
    {
        InitializeComponent();
        _repository = repository;
        _record = record;
        DialogHeading.Text = record is null ? "Neue Buchung" : $"Buchung #{record.Id} bearbeiten";
        PrimaryButtonText = record is null ? "Buchen" : "Änderungen speichern";
    }

    public async Task InitializeAsync()
    {
        _initializing = true;
        try
        {
            var accountsTask = _repository.GetAccountsAsync();
            var addressesTask = _repository.GetAddressesAsync();
            var institutionsTask = _repository.GetInstitutionsAsync();
            var periodTask = _repository.GetActiveBudgetPeriodAsync();
            await Task.WhenAll(accountsTask, addressesTask, institutionsTask, periodTask);

            SourceAccountBox.ItemsSource = accountsTask.Result;
            TargetAccountBox.ItemsSource = accountsTask.Result;
            AddressBox.ItemsSource = addressesTask.Result;
            InstitutionBox.ItemsSource = institutionsTask.Result;
            _activePeriod = periodTask.Result;

            var date = _record?.Datum ?? DateTime.Today;
            DatePicker.Date = new DateTimeOffset(date.Date);
            BudgetDatePicker.SelectedDate = _record?.BudgetDatum is DateTime budgetDate
                ? new DateTimeOffset(budgetDate.Date)
                : null;
            AmountBox.Text = _record?.Betrag.ToString("N2", CultureInfo.CurrentCulture) ?? string.Empty;
            NoteBox.Text = _record?.Notiz ?? string.Empty;
            SourceAccountBox.SelectedValue = _record?.VonKontoId;
            TargetAccountBox.SelectedValue = _record?.NachKontoId;
            AddressBox.SelectedValue = _record?.AdresseId;
            InstitutionBox.SelectedValue = _record?.GeldinstitutId;

            TypeOptions.SelectedIndex = await DetectTypeIndexAsync(_record);
            BudgetIncomeSwitch.IsOn = await DetectBudgetIncomeAsync(_record);
            UpdateFieldsForType();
            UpdateBudgetWarning();
        }
        finally
        {
            _initializing = false;
        }
    }

    private async Task<int> DetectTypeIndexAsync(TransactionRecord? record)
    {
        if (record is null) return 0;
        if (record.AdresseId.HasValue && !record.GeldinstitutId.HasValue &&
            !record.VonKontoId.HasValue && record.NachKontoId.HasValue) return 1;
        if (record.AdresseId.HasValue && record.GeldinstitutId.HasValue && record.NachKontoId.HasValue &&
            await _repository.IsIncomeAccountAsync(record.NachKontoId.Value)) return 3;
        if (record.VonKontoId.HasValue && record.NachKontoId.HasValue) return 2;
        if (record.VonKontoId.HasValue && !record.NachKontoId.HasValue) return 4;
        if (record.AdresseId.HasValue && record.GeldinstitutId.HasValue) return 3;
        return 0;
    }

    private async Task<bool> DetectBudgetIncomeAsync(TransactionRecord? record)
    {
        if (record is null) return false;
        if (string.Equals(record.Notiz?.Trim(), "Budgetierte Einnahme", StringComparison.OrdinalIgnoreCase))
            return true;
        return record.AdresseId.HasValue && record.GeldinstitutId.HasValue && record.NachKontoId.HasValue &&
               await _repository.IsIncomeAccountAsync(record.NachKontoId.Value);
    }

    private void OnTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) UpdateFieldsForType();
    }

    private void OnBudgetIncomeToggled(object sender, RoutedEventArgs e)
    {
        if (!_initializing) UpdateFieldsForType();
    }

    private void UpdateFieldsForType()
    {
        var type = SelectedType();
        SourceAccountBox.IsEnabled = type is TransactionType.AccountToAccount or TransactionType.AccountToBank;
        TargetAccountBox.IsEnabled = type is TransactionType.BankToAccount
            or TransactionType.AccountToAccount
            or TransactionType.AddressToAccount
            or TransactionType.AddressToBank;
        InstitutionBox.IsEnabled = type is TransactionType.BankToAccount
            or TransactionType.AccountToBank
            or TransactionType.AddressToBank;
        AddressBox.IsEnabled = true;
        BudgetIncomeSwitch.IsEnabled = type == TransactionType.AddressToBank;
        if (type != TransactionType.AddressToBank)
            BudgetIncomeSwitch.IsOn = false;
    }

    private void OnDateChanged(object? sender, DatePickerValueChangedEventArgs args)
    {
        if (!_initializing) UpdateBudgetWarning();
    }

    private void UpdateBudgetWarning()
    {
        if (_activePeriod is null)
        {
            BudgetWarning.IsOpen = false;
            return;
        }
        var date = DatePicker.Date.Date;
        var outside = date < _activePeriod.Start.Date || date > _activePeriod.End.Date;
        BudgetWarning.IsOpen = outside;
        BudgetWarning.Message = outside
            ? $"Das Bankdatum liegt außerhalb des aktiven Budgetzeitraums {_activePeriod.Start:dd.MM.yyyy} – {_activePeriod.End:dd.MM.yyyy}."
            : string.Empty;
    }

    private void OnClearBudgetDateClick(object sender, RoutedEventArgs e) =>
        BudgetDatePicker.SelectedDate = null;

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            EditorError.IsOpen = false;
            var draft = await CreateDraftAsync();
            await _repository.SaveAsync(draft);
        }
        catch (Exception exception)
        {
            args.Cancel = true;
            EditorError.Message = exception.Message;
            EditorError.IsOpen = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<TransactionDraft> CreateDraftAsync()
    {
        if (!TryParseAmount(AmountBox.Text, out var amount) || amount <= 0m)
            throw new InvalidOperationException("Bitte geben Sie einen Betrag größer als 0 ein.");

        var type = SelectedType();
        var sourceAccountId = SelectedId(SourceAccountBox);
        var targetAccountId = SelectedId(TargetAccountBox);
        var institutionId = SelectedId(InstitutionBox);
        var addressId = SelectedId(AddressBox);
        var note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

        int? source = null;
        int? target = null;
        int? institution = null;
        switch (type)
        {
            case TransactionType.AccountToAccount:
                Require(sourceAccountId, "Bitte wählen Sie das Von-Konto.");
                Require(targetAccountId, "Bitte wählen Sie das Nach-Konto.");
                source = sourceAccountId;
                target = targetAccountId;
                break;
            case TransactionType.AccountToBank:
                Require(sourceAccountId, "Bitte wählen Sie das Von-Konto.");
                Require(institutionId, "Bitte wählen Sie das Geldinstitut.");
                source = sourceAccountId;
                institution = institutionId;
                break;
            case TransactionType.AddressToAccount:
                Require(addressId, "Bitte wählen Sie die Adresse.");
                Require(targetAccountId, "Bitte wählen Sie das Nach-Konto.");
                target = targetAccountId;
                break;
            case TransactionType.AddressToBank:
                Require(addressId, "Bitte wählen Sie die Adresse.");
                Require(institutionId, "Bitte wählen Sie das Geldinstitut.");
                Require(targetAccountId, BudgetIncomeSwitch.IsOn
                    ? "Bitte wählen Sie das Einnahmenkonto."
                    : "Bitte wählen Sie das Rückzahlungskonto.");
                institution = institutionId;
                if (BudgetIncomeSwitch.IsOn)
                {
                    target = targetAccountId;
                    note ??= "Budgetierte Einnahme";
                }
                else
                {
                    source = targetAccountId;
                }
                break;
            default:
                Require(targetAccountId, "Bitte wählen Sie das Nach-Konto.");
                Require(institutionId, "Bitte wählen Sie das Geldinstitut.");
                if (await _repository.IsIncomeAccountAsync(targetAccountId!.Value))
                    throw new InvalidOperationException(
                        "Das gewählte Konto ist als Einnahmenkonto klassifiziert. Verwenden Sie dafür den Buchungsweg Adresse → Bank.");
                target = targetAccountId;
                institution = institutionId;
                break;
        }

        var date = DatePicker.Date.Date;
        var budgetDate = BudgetDatePicker.SelectedDate?.Date;
        if (budgetDate == date) budgetDate = null;
        return new TransactionDraft
        {
            Id = _record?.Id,
            Datum = date,
            BudgetDatum = budgetDate,
            VonKontoId = source,
            NachKontoId = target,
            Betrag = amount,
            Notiz = note,
            AdresseId = addressId,
            GeldinstitutId = institution
        };
    }

    private static bool TryParseAmount(string? text, out decimal amount)
    {
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount)
               || decimal.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("de-CH"), out amount)
               || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private static int? SelectedId(ComboBox comboBox) => comboBox.SelectedValue switch
    {
        int value => value,
        _ => null
    };

    private TransactionType SelectedType() => TypeOptions.SelectedIndex switch
    {
        1 => TransactionType.AddressToAccount,
        2 => TransactionType.AccountToAccount,
        3 => TransactionType.AddressToBank,
        4 => TransactionType.AccountToBank,
        _ => TransactionType.BankToAccount
    };

    private static void Require(int? value, string message)
    {
        if (!value.HasValue) throw new InvalidOperationException(message);
    }
}
