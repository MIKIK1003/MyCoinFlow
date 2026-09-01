using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class AccountEditorDialog : ContentDialog
{
    private readonly DatabaseService _database = new();
    private readonly KontoplanEintrag? _account;

    public AccountEditorDialog(KontoplanEintrag? account = null)
    {
        InitializeComponent();
        _account = account;
        DialogHeading.Text = account is null ? "Neues Konto" : $"Konto {account.Kontonummer:D4} bearbeiten";
    }

    public bool Saved { get; private set; }

    public async Task InitializeAsync()
    {
        var data = await Task.Run(() => new
        {
            Arten = _database.LadeKontenArten(),
            Gruppen = _database.LadeKontenGruppen(),
            Untergruppen = _database.LadeKontenUnterGruppen()
        });

        ArtBox.ItemsSource = data.Arten;
        GroupBox.ItemsSource = data.Gruppen;
        SubgroupBox.ItemsSource = data.Untergruppen;

        if (_account is null)
        {
            ArtBox.SelectedIndex = data.Arten.Count > 0 ? 0 : -1;
            GroupBox.SelectedIndex = data.Gruppen.Count > 0 ? 0 : -1;
            SubgroupBox.SelectedIndex = data.Untergruppen.Count > 0 ? 0 : -1;
            return;
        }

        AccountNumberBox.Text = _account.Kontonummer.ToString();
        ArtBox.SelectedItem = data.Arten.FirstOrDefault(item => item.Bezeichnung == _account.Art);
        GroupBox.SelectedItem = data.Gruppen.FirstOrDefault(item => item.Bezeichnung == _account.Gruppe);
        SubgroupBox.SelectedItem = data.Untergruppen.FirstOrDefault(item => item.Bezeichnung == _account.Untergruppe);
        DetailBox.Text = _account.Detail ?? string.Empty;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            EditorError.IsOpen = false;
            var accountNumber = int.TryParse(AccountNumberBox.Text, out var parsed) ? parsed : 0;
            var art = (ArtBox.SelectedItem as KontenArt)?.Bezeichnung ?? string.Empty;
            var group = (GroupBox.SelectedItem as KontenGruppe)?.Bezeichnung ?? string.Empty;
            var subgroup = (SubgroupBox.SelectedItem as KontenUnterGruppe)?.Bezeichnung ?? string.Empty;
            var detail = DetailBox.Text;

            await Task.Run(() =>
            {
                if (_account is null)
                    _database.NeuenKontoplanEintragSpeichern(accountNumber, art, group, subgroup, detail);
                else
                    _database.KontenplanEintragAktualisieren(_account.Id, accountNumber, art, group, subgroup, detail);
            });
            Saved = true;
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
}
