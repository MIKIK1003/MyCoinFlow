using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Globalization;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class HaushaltObjektDialog
    {
        public HaushaltObjekt Ergebnis { get; private set; }

        public HaushaltObjektDialog(HaushaltObjekt? objekt = null)
        {
            InitializeComponent();

            Ergebnis = objekt == null
                ? new HaushaltObjekt
                {
                    IconKey = "PackageVariantClosed",
                    IstAktiv = true
                }
                : new HaushaltObjekt
                {
                    Id = objekt.Id,
                    RaumId = objekt.RaumId,
                    RaumBezeichnung = objekt.RaumBezeichnung,

                    KategorieId = objekt.KategorieId,
                    KategorieBezeichnung = objekt.KategorieBezeichnung,
                    KategorieIconKey = objekt.KategorieIconKey,

                    Bezeichnung = objekt.Bezeichnung,
                    Kategorie = objekt.Kategorie,
                    IconKey = objekt.IconKey,

                    Hersteller = objekt.Hersteller,
                    Modell = objekt.Modell,
                    Seriennummer = objekt.Seriennummer,
                    Kaufdatum = objekt.Kaufdatum,
                    Kaufpreis = objekt.Kaufpreis,
                    Bemerkung = objekt.Bemerkung,
                    IstAktiv = objekt.IstAktiv,
                    ErstelltAm = objekt.ErstelltAm,
                    GeaendertAm = objekt.GeaendertAm
                };

            var db = new DatabaseService();
            KategorieBox.ItemsSource = db.HaushaltObjektKategorienGetAll();

            if (Ergebnis.KategorieId.HasValue && Ergebnis.KategorieId.Value > 0)
                KategorieBox.SelectedValue = Ergebnis.KategorieId.Value;

            HerstellerBox.Text = Ergebnis.Hersteller;
            ModellBox.Text = Ergebnis.Modell;
            SeriennummerBox.Text = Ergebnis.Seriennummer;
            KaufdatumPicker.SelectedDate = Ergebnis.Kaufdatum;

            KaufpreisBox.Text = Ergebnis.Kaufpreis.HasValue
                ? Ergebnis.Kaufpreis.Value.ToString("0.00", CultureInfo.CurrentCulture)
                : "";

            BemerkungBox.Text = Ergebnis.Bemerkung;
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            if (KategorieBox.SelectedItem is not HaushaltObjektKategorie kategorie)
            {
                MessageBox.Show(
                    this,
                    "Bitte eine Objekt-Kategorie auswählen.",
                    "Kategorie fehlt",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                KategorieBox.Focus();
                return;
            }

            decimal? kaufpreis = null;
            var kaufpreisText = KaufpreisBox.Text?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(kaufpreisText))
            {
                if (!decimal.TryParse(kaufpreisText, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed))
                {
                    MessageBox.Show(
                        this,
                        "Bitte beim Kaufpreis eine gültige Zahl erfassen.",
                        "Ungültiger Kaufpreis",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    KaufpreisBox.Focus();
                    return;
                }

                kaufpreis = parsed;
            }

            Ergebnis.KategorieId = kategorie.Id;
            Ergebnis.KategorieBezeichnung = kategorie.Bezeichnung;
            Ergebnis.Kategorie = kategorie.Bezeichnung;
            Ergebnis.KategorieIconKey = kategorie.IconKey;
            Ergebnis.IconKey = kategorie.IconKey;

            Ergebnis.Hersteller = HerstellerBox.Text?.Trim() ?? "";
            Ergebnis.Modell = ModellBox.Text?.Trim() ?? "";
            Ergebnis.Seriennummer = SeriennummerBox.Text?.Trim() ?? "";
            Ergebnis.Kaufdatum = KaufdatumPicker.SelectedDate;
            Ergebnis.Kaufpreis = kaufpreis;
            Ergebnis.Bemerkung = BemerkungBox.Text?.Trim() ?? "";
            Ergebnis.IstAktiv = true;

            DialogResult = true;
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}