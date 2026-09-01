using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class AddressEditorDialog : ContentDialog
{
    private readonly DatabaseService _database = new();
    private readonly Adresse? _address;
    private bool _initialized;

    public AddressEditorDialog(Adresse? address = null)
    {
        InitializeComponent();
        _address = address;
        DialogHeading.Text = address is null ? "Neue Adresse" : $"{address.Name} bearbeiten";

        if (address is not null)
        {
            NameBox.Text = address.Name;
            StreetBox.Text = address.Strasse ?? string.Empty;
            PostalCodeBox.Text = address.PLZ ?? string.Empty;
            CityBox.Text = address.Ort ?? string.Empty;
            CountryBox.Text = address.Land ?? string.Empty;
            TypeBox.Text = address.Typ ?? string.Empty;
            IbanBox.Text = address.IBAN ?? string.Empty;
            NoteBox.Text = address.Notiz ?? string.Empty;
            BudgetedCheckBox.IsChecked = address.IstBudgetiert;
        }

        RefreshBudgetUi();
    }

    public bool Saved { get; private set; }
    public int? SavedId { get; private set; }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var accounts = await Task.Run(() => _database.LadeKontoLookup());
            StandardIncomeAccountBox.ItemsSource = accounts;
            if (_address?.StandardEinnahmenKontoId is int accountId)
                StandardIncomeAccountBox.SelectedValue = accountId;
        }
        catch (Exception exception)
        {
            ShowError("Kontoliste konnte nicht geladen werden: " + exception.Message);
        }
    }

    private void OnBudgetedChanged(object sender, RoutedEventArgs e) => RefreshBudgetUi();

    private void RefreshBudgetUi()
    {
        var active = BudgetedCheckBox.IsChecked == true;
        StandardIncomeAccountBox.IsEnabled = active;
        StandardIncomeAccountBox.Opacity = active ? 1d : 0.6d;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            EditorError.IsOpen = false;
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                args.Cancel = true;
                ShowError("Name ist Pflicht.");
                return;
            }

            var isBudgeted = BudgetedCheckBox.IsChecked == true;
            var result = new Adresse
            {
                Id = _address?.Id ?? 0,
                Name = NameBox.Text.Trim(),
                Strasse = Normalize(StreetBox.Text),
                PLZ = Normalize(PostalCodeBox.Text),
                Ort = Normalize(CityBox.Text),
                Land = Normalize(CountryBox.Text),
                Typ = Normalize(TypeBox.Text),
                IBAN = NormalizeIban(IbanBox.Text),
                Notiz = Normalize(NoteBox.Text),
                IstBudgetiert = isBudgeted,
                StandardEinnahmenKontoId = isBudgeted && StandardIncomeAccountBox.SelectedValue is int accountId
                    ? accountId
                    : null,
                DefaultKontoId = _address?.DefaultKontoId
            };

            SavedId = await Task.Run(() =>
            {
                if (_address is null)
                    return _database.SpeichereAdresse(result);

                _database.AktualisiereAdresse(result);
                return result.Id;
            });
            Saved = true;
        }
        catch (Exception exception)
        {
            args.Cancel = true;
            ShowError(exception.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ShowError(string message)
    {
        EditorError.Message = message;
        EditorError.IsOpen = true;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeIban(string? value)
    {
        var normalized = Normalize(value);
        return normalized?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }
}
