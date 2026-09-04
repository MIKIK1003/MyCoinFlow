using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.WinUI.Services;
using WinUiConnectionStrings = MyCoinFlow.WinUI.Data.ConnectionStrings;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class FinanceSettingsControl : UserControl
{
    private readonly FinanceSettingsRepository _repository = new();
    private readonly IInvoicingSmtpCredentialStore _smtpCredentialStore =
        new InvoicingSmtpCredentialStore();
    private FinanceSettingsDraft? _draft;
    private string? _loadedDatabase;
    private bool _busy;

    public FinanceSettingsControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!CurrentUserContext.IsAdmin)
        {
            _draft = null;
            EditorScrollViewer.Visibility = Visibility.Collapsed;
            SetBusy(false);
            SaveButton.IsEnabled = false;
            EditorScrollViewer.IsEnabled = false;
            Show(
                "Finanzstammdaten sind administrativ geschützt.",
                InfoBarSeverity.Error,
                "Zugriff verweigert");
            return;
        }

        if (_draft is not null &&
            string.Equals(_loadedDatabase, WinUiConnectionStrings.ActiveDatabaseName, StringComparison.OrdinalIgnoreCase))
            return;
        await ReloadAsync();
    }

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _draft is null) return;
        CaptureHeaderAndAccounts();
        SetBusy(true);
        StatusInfoBar.IsOpen = false;
        try
        {
            var result = await _repository.SaveAsync(_draft);
            var warnings = result.Warnings.ToList();
            try
            {
                if (RemoveSmtpPasswordBox.IsChecked == true)
                    _smtpCredentialStore.RemovePassword();
                else if (!string.IsNullOrEmpty(SmtpPasswordBox.Password))
                    _smtpCredentialStore.SavePassword(SmtpPasswordBox.Password);
            }
            catch (Exception exception)
            {
                warnings.Add("Das SMTP-Kennwort konnte lokal nicht gespeichert werden: " + exception.Message);
            }
            _draft = result.Draft;
            _draft.HasStoredSmtpPassword = _smtpCredentialStore.HasPassword();
            _loadedDatabase = WinUiConnectionStrings.ActiveDatabaseName;
            SmtpPasswordBox.Password = string.Empty;
            RemoveSmtpPasswordBox.IsChecked = false;
            ApplyDraft();
            if (warnings.Count == 0)
            {
                Show(
                    $"Finanzstammdaten für Mandant '{_loadedDatabase}' wurden vollständig validiert und gespeichert.",
                    InfoBarSeverity.Success,
                    "Gespeichert");
            }
            else
            {
                Show(
                    "Alle unabhängig speicherbaren Daten wurden gesichert. " +
                    "Die folgenden Eingaben sind noch unvollständig oder konnten in ihrem Bereich nicht gespeichert werden; " +
                    "sie bleiben zur Korrektur im Formular:  •  " +
                    string.Join("  •  ", warnings),
                    InfoBarSeverity.Warning,
                    "Mit Hinweisen gespeichert");
            }
        }
        catch (FinanceSettingsValidationException exception)
        {
            Show(string.Join("  •  ", exception.Errors), InfoBarSeverity.Warning, "Bitte prüfen");
        }
        catch (UnauthorizedAccessException exception)
        {
            Show(exception.Message, InfoBarSeverity.Error, "Zugriff verweigert");
        }
        catch (Exception exception)
        {
            Show(
                "Die Finanzstammdaten konnten nicht gespeichert werden: " + exception.Message,
                InfoBarSeverity.Error,
                "Speichern fehlgeschlagen");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ReloadAsync()
    {
        if (_busy) return;
        SetBusy(true);
        StatusInfoBar.IsOpen = false;
        try
        {
            _draft = await _repository.LoadAsync();
            _draft.HasStoredSmtpPassword = _smtpCredentialStore.HasPassword();
            _loadedDatabase = WinUiConnectionStrings.ActiveDatabaseName;
            ApplyDraft();
            Show(
                $"Finanzstammdaten aus Mandant '{_loadedDatabase}' geladen.",
                InfoBarSeverity.Informational,
                "Bereit");
            if (string.IsNullOrWhiteSpace(_draft.IssuerName))
                IssuerNameBox.Focus(FocusState.Programmatic);
        }
        catch (UnauthorizedAccessException exception)
        {
            _draft = null;
            Show(exception.Message, InfoBarSeverity.Error, "Zugriff verweigert");
        }
        catch (Exception exception)
        {
            _draft = null;
            Show(
                "Der Finanzen-Bereich konnte nicht geladen werden: " + exception.Message,
                InfoBarSeverity.Error,
                "Laden fehlgeschlagen");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyDraft()
    {
        if (_draft is null) return;
        EditorScrollViewer.Visibility = Visibility.Visible;

        IssuerNameBox.Text = _draft.IssuerName;
        IssuerStreetBox.Text = _draft.IssuerStreet;
        IssuerPostalCodeBox.Text = _draft.IssuerPostalCode;
        IssuerCityBox.Text = _draft.IssuerCity;
        IssuerCountryCodeBox.Text = _draft.IssuerCountryCode;
        VatNumberBox.Text = _draft.VatNumber;
        InvoiceEmailBox.Text = _draft.InvoiceEmail;
        InvoicePhoneBox.Text = _draft.InvoicePhone;
        PaymentDaysBox.Value = _draft.DefaultPaymentDays;
        SmtpHostBox.Text = _draft.SmtpHost;
        SmtpPortBox.Value = _draft.SmtpPort;
        SmtpTlsBox.IsChecked = _draft.SmtpUseTls;
        SmtpUserBox.Text = _draft.SmtpUserName;
        SmtpFromBox.Text = _draft.SmtpFromAddress;
        SmtpPasswordStatusText.Text = _draft.HasStoredSmtpPassword
            ? "Ein SMTP-Kennwort ist für diesen Mandanten lokal geschützt gespeichert."
            : "Kein SMTP-Kennwort gespeichert.";

        NumberRangesList.ItemsSource = _draft.NumberRanges;
        CurrenciesList.ItemsSource = _draft.Currencies;
        BaseCurrencyBox.ItemsSource = _draft.Currencies;
        BaseCurrencyBox.SelectedValue = _draft.BaseCurrency;
        ExchangeRatesList.ItemsSource = _draft.ExchangeRates;
        VatRatesList.ItemsSource = _draft.VatRates;
        PaymentAccountsList.ItemsSource = _draft.PaymentAccounts;

        RevenueAccountsList.ItemsSource = _draft.AccountOptions;
        RevenueAccountsList.SelectedItems.Clear();
        foreach (var option in _draft.AccountOptions.Where(value => _draft.RevenueAccountIds.Contains(value.Id)))
            RevenueAccountsList.SelectedItems.Add(option);

        ExchangeGainAccountBox.ItemsSource = _draft.AccountOptions;
        ExchangeGainAccountBox.SelectedValue = _draft.ExchangeGainAccountId;
        ExchangeLossAccountBox.ItemsSource = _draft.AccountOptions;
        ExchangeLossAccountBox.SelectedValue = _draft.ExchangeLossAccountId;
    }

    private void CaptureHeaderAndAccounts()
    {
        if (_draft is null) return;
        _draft.IssuerName = IssuerNameBox.Text;
        _draft.IssuerStreet = IssuerStreetBox.Text;
        _draft.IssuerPostalCode = IssuerPostalCodeBox.Text;
        _draft.IssuerCity = IssuerCityBox.Text;
        _draft.IssuerCountryCode = IssuerCountryCodeBox.Text;
        _draft.VatNumber = VatNumberBox.Text;
        _draft.InvoiceEmail = InvoiceEmailBox.Text;
        _draft.InvoicePhone = InvoicePhoneBox.Text;
        _draft.SmtpHost = SmtpHostBox.Text;
        _draft.SmtpPort = double.IsFinite(SmtpPortBox.Value)
            ? checked((int)SmtpPortBox.Value)
            : 0;
        _draft.SmtpUseTls = SmtpTlsBox.IsChecked == true;
        _draft.SmtpUserName = SmtpUserBox.Text;
        _draft.SmtpFromAddress = SmtpFromBox.Text;
        _draft.DefaultPaymentDays = double.IsFinite(PaymentDaysBox.Value)
            ? checked((int)PaymentDaysBox.Value)
            : -1;
        _draft.BaseCurrency = BaseCurrencyBox.SelectedValue as string ?? string.Empty;
        _draft.ExchangeGainAccountId = ExchangeGainAccountBox.SelectedValue as int?;
        _draft.ExchangeLossAccountId = ExchangeLossAccountBox.SelectedValue as int?;
        _draft.RevenueAccountIds.Clear();
        foreach (var option in RevenueAccountsList.SelectedItems.OfType<FinanceAccountOption>())
            _draft.RevenueAccountIds.Add(option.Id);
    }

    private void OnAddCurrencyClick(object sender, RoutedEventArgs e)
    {
        if (_draft is null) return;
        var code = NewCurrencyCodeBox.Text.Trim().ToUpperInvariant();
        var name = NewCurrencyNameBox.Text.Trim();
        if (!FinanceSettingsValidator.IsIsoCurrencyCode(code) || string.IsNullOrWhiteSpace(name))
        {
            Show(
                "Erfassen Sie einen dreistelligen ISO-Code und eine Bezeichnung.",
                InfoBarSeverity.Warning,
                "Währung nicht hinzugefügt");
            return;
        }

        var existing = _draft.Currencies.FirstOrDefault(value => value.Code == code);
        if (existing is not null)
        {
            existing.DisplayName = name;
            existing.IsActive = true;
        }
        else
        {
            _draft.Currencies.Add(new DocumentCurrencySetting
            {
                Code = code,
                DisplayName = name,
                IsActive = true
            });
        }

        RefreshCurrencyBindings();
        NewCurrencyCodeBox.Text = string.Empty;
        NewCurrencyNameBox.Text = string.Empty;
        NewCurrencyCodeBox.Focus(FocusState.Programmatic);
    }

    private void OnAddExchangeRateClick(object sender, RoutedEventArgs e)
    {
        if (_draft is null) return;
        var currency = _draft.Currencies.FirstOrDefault(value =>
            value.IsActive && value.Code != (BaseCurrencyBox.SelectedValue as string ?? _draft.BaseCurrency));
        if (currency is null)
        {
            Show(
                "Aktivieren oder ergänzen Sie zuerst eine Fremdwährung.",
                InfoBarSeverity.Warning,
                "Kein Wechselkurs möglich");
            return;
        }

        _draft.ExchangeRates.Add(new ExchangeRateSetting
        {
            DocumentCurrency = currency.Code,
            RateToBase = 1,
            ValidFrom = DateTimeOffset.Now.Date,
            Source = "Manuell",
            IsActive = true,
            CurrencyOptions = _draft.Currencies
        });
        RefreshItems(ExchangeRatesList, _draft.ExchangeRates);
    }

    private void OnAddVatRateClick(object sender, RoutedEventArgs e)
    {
        if (_draft is null) return;
        var makeDefault = !_draft.VatRates.Any(value => value.IsActive && value.IsDefault);
        _draft.VatRates.Add(new VatRateSetting
        {
            Code = "STANDARD",
            DisplayName = "Standardsatz",
            RatePercent = 0,
            ValidFrom = DateTimeOffset.Now.Date,
            IsDefault = makeDefault,
            IsActive = true
        });
        RefreshItems(VatRatesList, _draft.VatRates);
    }

    private void OnDefaultVatClick(object sender, RoutedEventArgs e)
    {
        if (_draft is null || sender is not CheckBox checkBox ||
            checkBox.DataContext is not VatRateSetting selected)
            return;

        selected.IsDefault = checkBox.IsChecked == true;
        if (selected.IsDefault)
        {
            foreach (var rate in _draft.VatRates.Where(value => !ReferenceEquals(value, selected)))
                rate.IsDefault = false;
        }
        RefreshItems(VatRatesList, _draft.VatRates);
    }

    private void OnAddPaymentAccountClick(object sender, RoutedEventArgs e)
    {
        if (_draft is null) return;
        var currency = _draft.Currencies.FirstOrDefault(value => value.IsActive)?.Code
                       ?? _draft.BaseCurrency;
        _draft.PaymentAccounts.Add(new PaymentAccountSetting
        {
            CurrencyCode = currency,
            IsActive = true,
            CurrencyOptions = _draft.Currencies,
            InstitutionOptions = _draft.InstitutionOptions
        });
        RefreshItems(PaymentAccountsList, _draft.PaymentAccounts);
    }

    private void OnPaymentInstitutionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox ||
            comboBox.DataContext is not PaymentAccountSetting paymentAccount)
            return;

        var institution = paymentAccount.InstitutionOptions.FirstOrDefault(
            value => value.Id == paymentAccount.InstitutionId);
        paymentAccount.DisplayName = institution?.Name ?? string.Empty;
        paymentAccount.Iban = FinanceSettingsValidator.NormalizeIban(institution?.Iban);
        paymentAccount.IsQrIban = FinanceSettingsValidator.IsSwissQrIban(paymentAccount.Iban);
    }

    private void RefreshCurrencyBindings()
    {
        if (_draft is null) return;
        var selectedBase = BaseCurrencyBox.SelectedValue as string ?? _draft.BaseCurrency;
        foreach (var rate in _draft.ExchangeRates)
            rate.CurrencyOptions = _draft.Currencies;
        foreach (var account in _draft.PaymentAccounts)
            account.CurrencyOptions = _draft.Currencies;
        RefreshItems(CurrenciesList, _draft.Currencies);
        BaseCurrencyBox.ItemsSource = null;
        BaseCurrencyBox.ItemsSource = _draft.Currencies;
        BaseCurrencyBox.SelectedValue = selectedBase;
        RefreshItems(ExchangeRatesList, _draft.ExchangeRates);
        RefreshItems(PaymentAccountsList, _draft.PaymentAccounts);
    }

    private static void RefreshItems(ItemsControl control, object items)
    {
        control.ItemsSource = null;
        control.ItemsSource = items;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyRing.IsActive = busy;
        SaveButton.IsEnabled = !busy && CurrentUserContext.IsAdmin;
        EditorScrollViewer.IsEnabled = !busy && CurrentUserContext.IsAdmin;
    }

    private void Show(string message, InfoBarSeverity severity, string title)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }
}
