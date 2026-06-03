using MyCoinFlow.Models;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using MyCoinFlow.Services;
using System.Linq;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class VermoegenPositionDialog
    {
        private static readonly CultureInfo ChCulture = CultureInfo.GetCultureInfo("de-CH");
        private readonly DatabaseService _db = new();

        public VermoegenPosition Model { get; }

        public ObservableCollection<VermoegenDepot> Depots { get; } = new();

        public ObservableCollection<string> Boersen { get; } = new()
        {
            "SIX",
            "NYSE",
            "NASDAQ",
            "XETRA",
            "EURONEXT",
            "LSE",
            "KRYPTO",
            "SONSTIGE"
        };

        public ObservableCollection<string> Anlageklassen { get; } = new()
        {
            "Aktie",
            "ETF",
            "Obligation",
            "Kryptowährung",
            "Edelmetall",
            "Immobilie",
            "Sonstiges"
        };

        private VermoegenDepot? _selectedDepot;
        public VermoegenDepot? SelectedDepot
        {
            get => _selectedDepot;
            set
            {
                _selectedDepot = value;
                if (value != null)
                {
                    Model.DepotId = value.Id;
                    Model.DepotName = value.Name;
                }
            }
        }

        public string AnzahlText { get; set; } = "";
        public string EinstandspreisText { get; set; } = "";
        public string AktuellerKursText { get; set; } = "";

        public VermoegenPositionDialog(
            ObservableCollection<VermoegenDepot> depots,
            VermoegenPosition? model = null)
        {
            InitializeComponent();

            foreach (var d in depots.Where(d => d.IstAktiv))
                Depots.Add(d);

            Model = model == null
                ? new VermoegenPosition
                {
                    Anlageklasse = "Aktie",
                    Boerse = "SIX",
                    IstAktiv = true,
                    KursDatum = DateTime.Today
                }
                : new VermoegenPosition
                {
                    Id = model.Id,
                    DepotId = model.DepotId,
                    DepotName = model.DepotName,
                    Titel = model.Titel,
                    ISIN = model.ISIN,
                    Valor = model.Valor,
                    Symbol = model.Symbol,
                    Boerse = string.IsNullOrWhiteSpace(model.Boerse) ? "SIX" : model.Boerse,
                    Anlageklasse = string.IsNullOrWhiteSpace(model.Anlageklasse) ? "Aktie" : model.Anlageklasse,
                    Anzahl = model.Anzahl,
                    Einstandspreis = model.Einstandspreis,
                    EinstandDatum = model.EinstandDatum,
                    AktuellerKurs = model.AktuellerKurs,
                    KursDatum = model.KursDatum,
                    Notiz = model.Notiz,
                    IstAktiv = model.IstAktiv
                };

            SelectedDepot = Depots.FirstOrDefault(d => d.Id == Model.DepotId) ?? Depots.FirstOrDefault();

            AnzahlText = Model.Anzahl == 0 ? "" : Model.Anzahl.ToString("N8", ChCulture).TrimEnd('0').TrimEnd('.');
            EinstandspreisText = Model.Einstandspreis == 0 ? "" : Model.Einstandspreis.ToString("N6", ChCulture).TrimEnd('0').TrimEnd('.');
            AktuellerKursText = Model.AktuellerKurs.HasValue
                ? Model.AktuellerKurs.Value.ToString("N6", ChCulture).TrimEnd('0').TrimEnd('.')
                : "";

            DataContext = this;

            Loaded += (_, _) =>
            {
                TitelBox.Focus();
                TitelBox.SelectAll();
            };
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedDepot == null || Model.DepotId <= 0)
            {
                MessageBox.Show(
                    "Bitte ein Depot auswählen.",
                    "Vermögensposition",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(Model.Titel))
            {
                MessageBox.Show(
                    "Bitte einen Titel erfassen.",
                    "Vermögensposition",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                TitelBox.Focus();
                return;
            }

            if (!TryParseDecimal(AnzahlText, out var anzahl) || anzahl <= 0)
            {
                MessageBox.Show(
                    "Bitte eine gültige Anzahl größer 0 erfassen.",
                    "Vermögensposition",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!TryParseDecimal(EinstandspreisText, out var einstandspreis) || einstandspreis < 0)
            {
                MessageBox.Show(
                    "Bitte einen gültigen Einstandspreis erfassen.",
                    "Vermögensposition",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            decimal? aktuellerKurs = null;
            if (!string.IsNullOrWhiteSpace(AktuellerKursText))
            {
                if (!TryParseDecimal(AktuellerKursText, out var kurs) || kurs < 0)
                {
                    MessageBox.Show(
                        "Bitte einen gültigen aktuellen Kurs erfassen.",
                        "Vermögensposition",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                aktuellerKurs = kurs;
            }

            Model.Titel = Model.Titel.Trim();
            Model.ISIN = (Model.ISIN ?? "").Trim().ToUpperInvariant();
            Model.Valor = (Model.Valor ?? "").Trim().ToUpperInvariant();
            Model.Symbol = (Model.Symbol ?? "").Trim().ToUpperInvariant();
            Model.Boerse = string.IsNullOrWhiteSpace(Model.Boerse)
                ? ""
                : Model.Boerse.Trim().ToUpperInvariant();

            Model.Anlageklasse = string.IsNullOrWhiteSpace(Model.Anlageklasse)
                ? "Aktie"
                : Model.Anlageklasse.Trim();

            Model.Anzahl = anzahl;
            Model.Einstandspreis = einstandspreis;
            Model.AktuellerKurs = aktuellerKurs;
            Model.Notiz = (Model.Notiz ?? "").Trim();

            DialogResult = true;
        }

        private static bool TryParseDecimal(string? text, out decimal value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var cleaned = text.Trim()
                .Replace("'", "")
                .Replace(" ", "");

            return decimal.TryParse(
                cleaned,
                NumberStyles.Number,
                ChCulture,
                out value);
        }

        private void SymbolSuchen_Click(object sender, RoutedEventArgs e)
        {
            var einstellung = _db.VermoegenApiEinstellungGet();

            if (!einstellung.Aktiv || string.IsNullOrWhiteSpace(einstellung.ApiKey))
            {
                MessageBox.Show(
                    "Bitte zuerst im Vermögensmodul den EODHD API-Key erfassen.",
                    "Symbol suchen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var suchtext =
                !string.IsNullOrWhiteSpace(Model.ISIN)
                    ? Model.ISIN
                    : Model.Titel;

            var dlg = new VermoegenSymbolSucheWindow(suchtext, einstellung.ApiKey);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() != true || dlg.Ergebnis == null)
                return;

            var result = dlg.Ergebnis;

            if (!string.IsNullOrWhiteSpace(result.Titel))
                Model.Titel = result.Titel.Trim();

            if (!string.IsNullOrWhiteSpace(result.ISIN))
                Model.ISIN = result.ISIN.Trim().ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(result.Symbol))
                Model.Symbol = result.Symbol.Trim().ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(result.Boerse))
                Model.Boerse = result.Boerse.Trim().ToUpperInvariant();

            // Binding-Refresh, weil Model selbst kein INotifyPropertyChanged implementiert.
            DataContext = null;
            DataContext = this;
        }

        private static void TrySetOwner(Window dlg)
        {
            try
            {
                if (Application.Current?.MainWindow != null)
                    dlg.Owner = Application.Current.MainWindow;
            }
            catch
            {
                // keine UI-Blockade
            }
        }
    }
}