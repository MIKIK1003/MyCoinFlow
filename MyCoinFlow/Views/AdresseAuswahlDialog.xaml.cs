using System.Linq;
using System.Windows;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class AdresseAuswahlDialog : Window
    {
        private readonly DatabaseService _db = new();

        // <- NEU: Defaults, die beim Loaded gesetzt werden
        public string? DefaultName { get; set; }
        public string? DefaultIban { get; set; }

        public int? AusgewaehlteAdresseId { get; private set; }

        public AdresseAuswahlDialog()
        {
            InitializeComponent();
            LadeAdressen();
        }

        // Bequemer Overload: setzt nur die Defaults, reales Befüllen passiert in Window_Loaded
        public AdresseAuswahlDialog(string? defaultName, string? defaultIban) : this()
        {
            DefaultName = defaultName;
            DefaultIban = defaultIban;
        }

        private void LadeAdressen()
        {
            var list = _db.LadeAdressen()
                          .OrderBy(a => a.Name)
                          .ToList();
            AdrBox.ItemsSource = list;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(DefaultName) && string.IsNullOrWhiteSpace(NeuNameBox.Text))
                NeuNameBox.Text = DefaultName!.Trim();

            if (!string.IsNullOrWhiteSpace(DefaultIban) && string.IsNullOrWhiteSpace(NeuIbanBox.Text))
                NeuIbanBox.Text = DefaultIban!.Trim();
        }

        private void NeuAnlegen_Click(object sender, RoutedEventArgs e)
        {
            // 1) Eingaben prüfen
            var name = NeuNameBox.Text?.Trim();
            var ibanRaw = NeuIbanBox.Text?.Trim();
            string? iban = string.IsNullOrWhiteSpace(ibanRaw) ? null : ibanRaw.Replace(" ", "").ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(name) ||
                string.Equals(name, "Unbekannt", System.StringComparison.CurrentCultureIgnoreCase))
            {
                MessageBox.Show("Bitte einen aussagekräftigen Namen angeben (nicht 'Unbekannt').");
                return;
            }

            // 2) Konto wählen (Pflicht) – danach wird DefaultKontoId gesetzt
            var kontoDlg = new MyCoinFlow.Views.KontoAuswahlDialog { Owner = this };
            if (kontoDlg.ShowDialog() != true || !kontoDlg.SelectedKontoId.HasValue)
                return; // Abbruch durch Benutzer

            var defaultKontoId = kontoDlg.SelectedKontoId.Value;

            // 3) Adresse speichern inkl. DefaultKontoId
            var adr = new Adresse
            {
                Name = name!,
                IBAN = iban,
                DefaultKontoId = defaultKontoId
            };
            int newId = _db.SpeichereAdresse(adr); // schreibt DefaultKontoId in die DB

            // 4) Rückgabe & Schließen (sofort weiter im Aufrufer)
            AusgewaehlteAdresseId = newId;
            DialogResult = true;
            Close();
        }


        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            AusgewaehlteAdresseId = AdrBox.SelectedValue as int?;
            if (AusgewaehlteAdresseId == null)
            {
                MessageBox.Show("Bitte eine Adresse wählen (oder neu anlegen und dann wählen).");
                return;
            }
            DialogResult = true;
            Close();
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
