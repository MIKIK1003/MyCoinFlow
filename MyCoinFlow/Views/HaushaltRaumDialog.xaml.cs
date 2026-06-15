using MyCoinFlow.Models;
using System.Collections.Generic;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class HaushaltRaumDialog
    {
        public HaushaltRaum Ergebnis { get; private set; }

        public HaushaltRaumDialog(HaushaltRaum? raum = null)
        {
            InitializeComponent();

            Ergebnis = raum == null
                ? new HaushaltRaum { IconKey = "HomeOutline", IstAktiv = true }
                : new HaushaltRaum
                {
                    Id = raum.Id,
                    Bezeichnung = raum.Bezeichnung,
                    IconKey = raum.IconKey,
                    Bemerkung = raum.Bemerkung,
                    IstAktiv = raum.IstAktiv,
                    ErstelltAm = raum.ErstelltAm,
                    GeaendertAm = raum.GeaendertAm
                };

            IconBox.ItemsSource = BuildIconAuswahl();
            BezeichnungBox.Text = Ergebnis.Bezeichnung;
            IconBox.SelectedValue = string.IsNullOrWhiteSpace(Ergebnis.IconKey)
                ? "HomeOutline"
                : Ergebnis.IconKey;
            BemerkungBox.Text = Ergebnis.Bemerkung;
        }

        private static List<IconAuswahlItem> BuildIconAuswahl()
        {
            return new List<IconAuswahlItem>
            {
                new("HomeOutline", "Allgemein / Raum"),
                new("FloorPlan", "Raum / Fläche"),
                new("SilverwareForkKnife", "Küche"),
                new("Shower", "Bad / Dusche"),
                new("SofaOutline", "Wohnzimmer"),
                new("BedOutline", "Schlafzimmer"),
                new("WashingMachine", "Waschen"),
                new("Car", "Garage / Fahrzeug"),
                new("Tools", "Werkstatt"),
                new("CogOutline", "Technik"),
                new("Warehouse", "Lager"),
                new("OfficeBuildingOutline", "Büro / Geschäft"),
                new("LightbulbOutline", "Elektro / Licht"),
                new("Water", "Wasser"),
                new("Radiator", "Heizung")
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
            Ergebnis.IconKey = IconBox.SelectedValue?.ToString() ?? "HomeOutline";
            Ergebnis.Bemerkung = BemerkungBox.Text?.Trim() ?? "";
            Ergebnis.IstAktiv = true;

            DialogResult = true;
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private sealed class IconAuswahlItem
        {
            public string Key { get; }
            public string Text { get; }

            public IconAuswahlItem(string key, string text)
            {
                Key = key;
                Text = text;
            }
        }
    }
}