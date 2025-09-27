using System;
using System.Globalization;
using System.Windows;
using MyCoinFlow.Models;

namespace MyCoinFlow.Views
{
    public partial class InstitutionDialog : Window
    {
        public Geldinstitut? Ergebnis { get; private set; }

        // Feste Kultur für Komma/Formatierung (Schweiz)
        private readonly CultureInfo _ci = new CultureInfo("de-CH");

        public InstitutionDialog(Geldinstitut? vorlage = null)
        {
            InitializeComponent();

            if (vorlage != null)
            {
                // Bestehende Felder
                NameBox.Text = vorlage.Name;
                BicBox.Text = vorlage.BIC;
                IbanBox.Text = vorlage.IBAN;
                KtoBox.Text = vorlage.KontoNummer;
                NotizBox.Text = vorlage.Notiz;

                // NEU: Anfangsbestand + Anfangsdatum vorbefüllen
                AnfangsbestandBox.Text = vorlage.Anfangsbestand.ToString("F2", _ci);
                if (vorlage.Anfangsdatum.HasValue)
                    AnfangsdatumPicker.SelectedDate = vorlage.Anfangsdatum.Value;

                // Id merken (für Update)
                Ergebnis = new Geldinstitut { Id = vorlage.Id };
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Name ist Pflicht.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // NEU: Anfangsbestand parsen (leer => 0.00)
            decimal anfangsbestand = 0m;
            var txt = AnfangsbestandBox.Text?.Trim();
            if (!string.IsNullOrEmpty(txt))
            {
                if (!decimal.TryParse(txt, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, _ci, out anfangsbestand))
                {
                    MessageBox.Show("Anfangsbestand ist keine gültige Zahl (z. B. 1'500.00).",
                        "Eingabefehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            Ergebnis ??= new Geldinstitut();

            // Pflicht + optionale Felder
            Ergebnis.Name = NameBox.Text.Trim();
            Ergebnis.BIC = string.IsNullOrWhiteSpace(BicBox.Text) ? null : BicBox.Text.Trim();
            Ergebnis.IBAN = string.IsNullOrWhiteSpace(IbanBox.Text) ? null : IbanBox.Text.Trim();
            Ergebnis.KontoNummer = string.IsNullOrWhiteSpace(KtoBox.Text) ? null : KtoBox.Text.Trim();
            Ergebnis.Notiz = string.IsNullOrWhiteSpace(NotizBox.Text) ? null : NotizBox.Text.Trim();

            // NEU: Anfangsbestand & Anfangsdatum
            Ergebnis.Anfangsbestand = anfangsbestand;
            Ergebnis.Anfangsdatum = AnfangsdatumPicker.SelectedDate;

            DialogResult = true;
            Close();
        }
    }
}
