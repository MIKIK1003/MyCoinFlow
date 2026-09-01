using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.WinUI.Views;
public sealed partial class WealthDepotEditorDialog : ContentDialog
{
    private readonly VermoegenDepot? _source;
    public WealthDepotEditorDialog(VermoegenDepot? source = null)
    {
        InitializeComponent(); _source = source;
        var db = new DatabaseService(); InstitutionBox.ItemsSource = db.VermoegenGeldinstituteGetForAuswahl(); CurrencyBox.ItemsSource = new[] { "CHF", "EUR", "USD", "GBP" };
        NameBox.Text = source?.Name ?? string.Empty; LegacyInstitutionBox.Text = source?.Institut ?? string.Empty; CurrencyBox.SelectedItem = string.IsNullOrWhiteSpace(source?.Waehrung) ? "CHF" : source.Waehrung;
        if (source?.GeldinstitutId is int id) InstitutionBox.SelectedItem = ((IEnumerable<VermoegenGeldinstitutAuswahl>)InstitutionBox.ItemsSource).FirstOrDefault(value => value.Id == id);
    }
    public VermoegenDepot? Result { get; private set; }
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { args.Cancel = true; ErrorBar.Message = "Bitte einen Depotnamen erfassen."; ErrorBar.IsOpen = true; return; }
        var institution = InstitutionBox.SelectedItem as VermoegenGeldinstitutAuswahl;
        Result = new VermoegenDepot { Id = _source?.Id ?? 0, GeldinstitutId = institution?.Id, GeldinstitutName = institution?.Name ?? string.Empty, Name = NameBox.Text.Trim(), Institut = LegacyInstitutionBox.Text.Trim(), Waehrung = (CurrencyBox.SelectedItem as string ?? "CHF").Trim().ToUpperInvariant(), IstAktiv = _source?.IstAktiv ?? true, IstStandard = _source?.IstStandard ?? false };
    }
}
