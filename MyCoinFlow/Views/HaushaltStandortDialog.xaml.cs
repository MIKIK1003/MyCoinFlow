using MyCoinFlow.Models;
using System.Collections.Generic;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class HaushaltStandortDialog
    {
        public HaushaltStandort Ergebnis { get; private set; }

        public HaushaltStandortDialog(HaushaltStandort? standort = null)
        {
            InitializeComponent();

            Ergebnis = standort == null
                ? new HaushaltStandort
                {
                    IconKey = "HomeCityOutline",
                    FarbeKey = "DeepPurple",
                    IstAktiv = true
                }
                : new HaushaltStandort
                {
                    Id = standort.Id,
                    Bezeichnung = standort.Bezeichnung,
                    IconKey = standort.IconKey,
                    FarbeKey = standort.FarbeKey,
                    Bemerkung = standort.Bemerkung,
                    IstAktiv = standort.IstAktiv,
                    ErstelltAm = standort.ErstelltAm,
                    GeaendertAm = standort.GeaendertAm
                };

            IconBox.ItemsSource = BuildIcons();
            FarbeBox.ItemsSource = BuildFarben();

            BezeichnungBox.Text = Ergebnis.Bezeichnung;
            IconBox.SelectedValue = string.IsNullOrWhiteSpace(Ergebnis.IconKey) ? "HomeCityOutline" : Ergebnis.IconKey;
            FarbeBox.SelectedValue = string.IsNullOrWhiteSpace(Ergebnis.FarbeKey) ? "DeepPurple" : Ergebnis.FarbeKey;
            BemerkungBox.Text = Ergebnis.Bemerkung;
        }

        private static List<AuswahlItem> BuildIcons()
        {
            return new List<AuswahlItem>
            {
                new("HomeCityOutline", "Wohnhaus / Standort"),
                new("HomeOutline", "Haus"),
                new("OfficeBuildingOutline", "Firma / Büro"),
                new("Warehouse", "Lager / Werkstatt"),
                new("Car", "Fahrzeug"),
                new("Tools", "Werkstatt"),
                new("Factory", "Betrieb"),
                new("CabinAFrame", "Ferienhaus"),
                new("Garage", "Garage"),
                new("CogOutline", "Technik")
            };
        }

        private static List<AuswahlItem> BuildFarben()
        {
            return new List<AuswahlItem>
            {
                new("DeepPurple", "Violett"),
                new("Blue", "Blau"),
                new("Teal", "Türkis"),
                new("Green", "Grün"),
                new("Amber", "Gelb / Amber"),
                new("Orange", "Orange"),
                new("Red", "Rot"),
                new("BlueGrey", "Blaugrau")
            };
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            var bezeichnung = BezeichnungBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(bezeichnung))
            {
                MessageBox.Show(
                    this,
                    "Bitte eine Bezeichnung erfassen.",
                    "Pflichtfeld",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                BezeichnungBox.Focus();
                return;
            }

            Ergebnis.Bezeichnung = bezeichnung;
            Ergebnis.IconKey = IconBox.SelectedValue?.ToString() ?? "HomeCityOutline";
            Ergebnis.FarbeKey = FarbeBox.SelectedValue?.ToString() ?? "DeepPurple";
            Ergebnis.Bemerkung = BemerkungBox.Text?.Trim() ?? "";
            Ergebnis.IstAktiv = true;

            DialogResult = true;
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private sealed class AuswahlItem
        {
            public string Key { get; }
            public string Text { get; }

            public AuswahlItem(string key, string text)
            {
                Key = key;
                Text = text;
            }
        }
    }
}