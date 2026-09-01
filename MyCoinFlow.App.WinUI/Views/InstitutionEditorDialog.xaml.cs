using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Globalization;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InstitutionEditorDialog : ContentDialog
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private readonly DatabaseService _database = new();
    private readonly Geldinstitut? _institution;

    public InstitutionEditorDialog(Geldinstitut? institution = null)
    {
        InitializeComponent();
        _institution = institution;
        DialogHeading.Text = institution is null ? "Neues Geldinstitut" : $"{institution.Name} bearbeiten";

        if (institution is null)
        {
            InitialBalanceBox.Text = 0m.ToString("F2", SwissCulture);
            return;
        }

        NameBox.Text = institution.Name;
        BicBox.Text = institution.BIC ?? string.Empty;
        IbanBox.Text = institution.IBAN ?? string.Empty;
        AccountNumberBox.Text = institution.KontoNummer ?? string.Empty;
        NoteBox.Text = institution.Notiz ?? string.Empty;
        InitialBalanceBox.Text = institution.Anfangsbestand.ToString("F2", SwissCulture);
        InitialDatePicker.SelectedDate = institution.Anfangsdatum.HasValue
            ? new DateTimeOffset(institution.Anfangsdatum.Value)
            : null;
    }

    public bool Saved { get; private set; }
    public int? SavedId { get; private set; }

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

            var initialBalance = 0m;
            var balanceText = InitialBalanceBox.Text?.Trim();
            if (!string.IsNullOrEmpty(balanceText) &&
                !decimal.TryParse(
                    balanceText,
                    NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                    SwissCulture,
                    out initialBalance))
            {
                args.Cancel = true;
                ShowError("Anfangsbestand ist keine gültige Zahl (z. B. 1'500.00).");
                return;
            }

            var result = new Geldinstitut
            {
                Id = _institution?.Id ?? 0,
                Name = NameBox.Text.Trim(),
                BIC = Normalize(BicBox.Text),
                IBAN = Normalize(IbanBox.Text),
                KontoNummer = Normalize(AccountNumberBox.Text),
                Notiz = Normalize(NoteBox.Text),
                Anfangsbestand = initialBalance,
                Anfangsdatum = InitialDatePicker.SelectedDate?.Date
            };

            SavedId = await Task.Run(() =>
            {
                if (_institution is null)
                    return _database.SpeichereGeldinstitut(result);

                _database.AktualisiereGeldinstitut(result);
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
}
