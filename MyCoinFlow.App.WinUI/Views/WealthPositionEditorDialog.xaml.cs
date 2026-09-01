using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using System.Globalization;

namespace MyCoinFlow.WinUI.Views;
public sealed partial class WealthPositionEditorDialog : ContentDialog
{
    private static readonly CultureInfo Swiss = CultureInfo.GetCultureInfo("de-CH");
    private readonly VermoegenPosition? _source;
    public WealthPositionEditorDialog(IReadOnlyList<VermoegenDepot> depots, VermoegenPosition? source = null)
    {
        InitializeComponent(); _source = source; DepotBox.ItemsSource = depots.Where(value => value.IstAktiv).ToList();
        ExchangeBox.ItemsSource = new[] { "SIX", "NYSE", "NASDAQ", "XETRA", "EURONEXT", "LSE", "KRYPTO", "SONSTIGE" }; AssetClassBox.ItemsSource = new[] { "Aktie", "Fonds", "ETF", "Obligation", "Kryptowährung", "Edelmetall", "Immobilie", "Sonstiges" }; CurrencyBox.ItemsSource = new[] { "CHF", "EUR", "USD", "GBP" }; CostCurrencyBox.ItemsSource = new[] { "Wie Handelswährung", "CHF", "EUR", "USD", "GBP" };
        DepotBox.SelectedItem = depots.FirstOrDefault(value => value.Id == source?.DepotId) ?? depots.FirstOrDefault(); TitleBox.Text = source?.Titel ?? string.Empty; IsinBox.Text = source?.ISIN ?? string.Empty; ValorBox.Text = source?.Valor ?? string.Empty; SymbolBox.Text = source?.Symbol ?? string.Empty; ExchangeBox.SelectedItem = string.IsNullOrWhiteSpace(source?.Boerse) ? "SIX" : source.Boerse; AssetClassBox.SelectedItem = string.IsNullOrWhiteSpace(source?.Anlageklasse) ? "Aktie" : source.Anlageklasse; CurrencyBox.SelectedItem = string.IsNullOrWhiteSpace(source?.Waehrung) ? "CHF" : source.Waehrung; CostCurrencyBox.SelectedItem = string.IsNullOrWhiteSpace(source?.EinstandWaehrung) ? "Wie Handelswährung" : source.EinstandWaehrung; QuantityBox.Text = Number(source?.Anzahl); CostPriceBox.Text = Number(source?.Einstandspreis); CurrentPriceBox.Text = source?.AktuellerKurs is decimal price ? Number(price) : string.Empty; CostDatePicker.Date = source?.EinstandDatum; PriceDatePicker.Date = source?.KursDatum ?? DateTime.Today; NoteBox.Text = source?.Notiz ?? string.Empty;
    }
    public VermoegenPosition? Result { get; private set; }
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (DepotBox.SelectedItem is not VermoegenDepot depot) { Error(args, "Bitte ein Depot auswählen."); return; }
        if (string.IsNullOrWhiteSpace(TitleBox.Text)) { Error(args, "Bitte einen Titel erfassen."); return; }
        if (!Parse(QuantityBox.Text, out var quantity) || quantity <= 0m) { Error(args, "Bitte eine gültige Anzahl größer 0 erfassen."); return; }
        if (!Parse(CostPriceBox.Text, out var cost) || cost < 0m) { Error(args, "Bitte einen gültigen Einstandspreis erfassen."); return; }
        decimal? price = null; if (!string.IsNullOrWhiteSpace(CurrentPriceBox.Text)) { if (!Parse(CurrentPriceBox.Text, out var parsed) || parsed < 0m) { Error(args, "Bitte einen gültigen aktuellen Kurs erfassen."); return; } price = parsed; }
        var currency = (CurrencyBox.SelectedItem as string ?? "CHF").Trim().ToUpperInvariant(); var costCurrency = CostCurrencyBox.SelectedItem as string; if (string.IsNullOrWhiteSpace(costCurrency) || costCurrency == "Wie Handelswährung" || costCurrency.Equals(currency, StringComparison.OrdinalIgnoreCase)) costCurrency = string.Empty;
        Result = new VermoegenPosition { Id = _source?.Id ?? 0, DepotId = depot.Id, DepotName = depot.Name, Titel = TitleBox.Text.Trim(), ISIN = IsinBox.Text.Trim().ToUpperInvariant(), Valor = ValorBox.Text.Trim().ToUpperInvariant(), Symbol = SymbolBox.Text.Trim().ToUpperInvariant(), Boerse = (ExchangeBox.SelectedItem as string ?? string.Empty).Trim().ToUpperInvariant(), Waehrung = currency, EinstandWaehrung = costCurrency?.Trim().ToUpperInvariant() ?? string.Empty, Anlageklasse = AssetClassBox.SelectedItem as string ?? "Aktie", Anzahl = quantity, Einstandspreis = cost, EinstandDatum = CostDatePicker.Date?.Date, AktuellerKurs = price, KursDatum = PriceDatePicker.Date?.Date, Notiz = NoteBox.Text.Trim(), IstAktiv = _source?.IstAktiv ?? true };
    }
    private void Error(ContentDialogButtonClickEventArgs args, string message) { args.Cancel = true; ErrorBar.Message = message; ErrorBar.IsOpen = true; }
    private static bool Parse(string? text, out decimal value) => decimal.TryParse((text ?? string.Empty).Trim().Replace("'", string.Empty).Replace(" ", string.Empty), NumberStyles.Number, Swiss, out value);
    private static string Number(decimal? value) => value is > 0m ? value.Value.ToString("N8", Swiss).TrimEnd('0').TrimEnd('.') : string.Empty;
}
