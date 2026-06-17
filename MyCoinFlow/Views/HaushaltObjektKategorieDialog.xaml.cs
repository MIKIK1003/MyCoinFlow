using MyCoinFlow.Models;
using System.Collections.Generic;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class HaushaltObjektKategorieDialog
    {
        public HaushaltObjektKategorie Ergebnis { get; private set; }

        public HaushaltObjektKategorieDialog(HaushaltObjektKategorie? kategorie = null)
        {
            InitializeComponent();

            Ergebnis = kategorie == null
                ? new HaushaltObjektKategorie
                {
                    IconKey = "PackageVariantClosed",
                    IstAktiv = true
                }
                : new HaushaltObjektKategorie
                {
                    Id = kategorie.Id,
                    Bezeichnung = kategorie.Bezeichnung,
                    IconKey = kategorie.IconKey,
                    Bemerkung = kategorie.Bemerkung,
                    IstAktiv = kategorie.IstAktiv,
                    ErstelltAm = kategorie.ErstelltAm,
                    GeaendertAm = kategorie.GeaendertAm
                };

            IconBox.ItemsSource = BuildIconAuswahl();

            BezeichnungBox.Text = Ergebnis.Bezeichnung;
            IconBox.SelectedValue = string.IsNullOrWhiteSpace(Ergebnis.IconKey)
                ? "PackageVariantClosed"
                : Ergebnis.IconKey;
            BemerkungBox.Text = Ergebnis.Bemerkung;
        }

        private static List<AuswahlItem> BuildIconAuswahl()
        {
            return new List<AuswahlItem>
            {
                new("PackageVariantClosed", "Allgemein / Objekt"),
                new("FloorPlan", "Boden / Fläche"),
                new("LightbulbOutline", "Lampe / Licht"),
                new("Radiator", "Radiator / Heizung"),
                new("Water", "Wasser / Sanitär"),
                new("Toilet", "WC"),
                new("Shower", "Dusche / Bad"),
                new("WashingMachine", "Waschmaschine"),
                new("Stove", "Backofen / Herd"),
                new("FridgeOutline", "Kühlschrank"),
                new("Television", "Fernseher"),
                new("SofaOutline", "Möbel"),
                new("Car", "Fahrzeug"),
                new("Tools", "Werkzeug"),
                new("CogOutline", "Technik"),
                new("Door", "Tür"),
                new("WindowClosedVariant", "Fenster")
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
            Ergebnis.IconKey = IconBox.SelectedValue?.ToString() ?? "PackageVariantClosed";
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