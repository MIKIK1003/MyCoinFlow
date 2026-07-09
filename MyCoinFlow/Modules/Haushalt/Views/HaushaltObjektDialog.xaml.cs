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
                    IstAktiv = true,
                    VorlaufTage = 0
                }
                : new HaushaltObjekt
                {
                    Id = objekt.Id,
                    RaumId = objekt.RaumId,
                    RaumBezeichnung = objekt.RaumBezeichnung,

                    KategorieId = objekt.KategorieId,
                    KategorieBezeichnung = objekt.KategorieBezeichnung,
                    KategorieIconKey = objekt.KategorieIconKey,

                    ArbeitsanweisungId = objekt.ArbeitsanweisungId,
                    ArbeitsanweisungBezeichnung = objekt.ArbeitsanweisungBezeichnung,
                    ArbeitsanweisungBeschreibung = objekt.ArbeitsanweisungBeschreibung,

                    ZeitintervallId = objekt.ZeitintervallId,
                    ZeitintervallBezeichnung = objekt.ZeitintervallBezeichnung,
                    ZeitintervallTage = objekt.ZeitintervallTage,

                    VorlaufTage = objekt.VorlaufTage,
                    LetzteAusfuehrungAm = objekt.LetzteAusfuehrungAm,

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
            TaetigkeitBox.ItemsSource = db.HaushaltArbeitsanweisungenGetAll();
            ZeitintervallBox.ItemsSource = db.HaushaltZeitintervalleGetAll();

            if (Ergebnis.KategorieId.HasValue && Ergebnis.KategorieId.Value > 0)
                KategorieBox.SelectedValue = Ergebnis.KategorieId.Value;

            if (Ergebnis.ArbeitsanweisungId.HasValue && Ergebnis.ArbeitsanweisungId.Value > 0)
                TaetigkeitBox.SelectedValue = Ergebnis.ArbeitsanweisungId.Value;

            if (Ergebnis.ZeitintervallId.HasValue && Ergebnis.ZeitintervallId.Value > 0)
                ZeitintervallBox.SelectedValue = Ergebnis.ZeitintervallId.Value;

            VorlaufTageBox.Text = Ergebnis.VorlaufTage > 0
                ? Ergebnis.VorlaufTage.ToString()
                : "0";

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
                MessageBox.Show(this, "Bitte eine Objekt-Kategorie auswählen.", "Kategorie fehlt",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                KategorieBox.Focus();
                return;
            }

            if (TaetigkeitBox.SelectedItem is not HaushaltArbeitsanweisung taetigkeit)
            {
                MessageBox.Show(this, "Bitte eine Tätigkeit auswählen.", "Tätigkeit fehlt",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                TaetigkeitBox.Focus();
                return;
            }

            if (ZeitintervallBox.SelectedItem is not HaushaltZeitintervall intervall)
            {
                MessageBox.Show(this, "Bitte ein Zeitintervall auswählen.", "Zeitintervall fehlt",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                ZeitintervallBox.Focus();
                return;
            }

            if (!int.TryParse(VorlaufTageBox.Text?.Trim(), out var vorlaufTage) || vorlaufTage < 0)
            {
                MessageBox.Show(this, "Bitte einen gültigen Vorlauf in Tagen erfassen. Erlaubt sind 0 oder mehr Tage.",
                    "Ungültiger Vorlauf",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                VorlaufTageBox.Focus();
                return;
            }

            decimal? kaufpreis = null;
            var kaufpreisText = KaufpreisBox.Text?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(kaufpreisText))
            {
                if (!decimal.TryParse(kaufpreisText, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed))
                {
                    MessageBox.Show(this, "Bitte beim Kaufpreis eine gültige Zahl erfassen.",
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

            Ergebnis.ArbeitsanweisungId = taetigkeit.Id;
            Ergebnis.ArbeitsanweisungBezeichnung = taetigkeit.Bezeichnung;
            Ergebnis.ArbeitsanweisungBeschreibung = taetigkeit.Beschreibung;

            Ergebnis.ZeitintervallId = intervall.Id;
            Ergebnis.ZeitintervallBezeichnung = intervall.Bezeichnung;
            Ergebnis.ZeitintervallTage = intervall.Tage;

            Ergebnis.VorlaufTage = vorlaufTage;

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