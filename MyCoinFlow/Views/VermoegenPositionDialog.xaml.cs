using MyCoinFlow.Models;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class VermoegenPositionDialog
    {
        private static readonly CultureInfo ChCulture = CultureInfo.GetCultureInfo("de-CH");

        public VermoegenPosition Model { get; }

        public ObservableCollection<VermoegenDepot> Depots { get; } = new();

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
    }
}