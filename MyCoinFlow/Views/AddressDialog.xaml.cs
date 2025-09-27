using System.Windows;
using System.Windows.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class AddressDialog : Window
    {
        private readonly DatabaseService _db = new();
        private readonly int? _adresseId;   // merken, welche Adresse wir bearbeiten

        public Adresse? Ergebnis { get; private set; }

        public AddressDialog(Adresse? vorlage = null)
        {
            InitializeComponent();

            // Id merken (falls Bearbeitung)
            _adresseId = vorlage?.Id;

            // UI-Events
            IstBudgetiertCheck.Checked += (_, __) => RefreshBudgetUi();
            IstBudgetiertCheck.Unchecked += (_, __) => RefreshBudgetUi();

            // Alles erst befüllen, wenn das Fenster geladen ist (dann existieren alle Controls)
            Loaded += AddressDialog_Loaded;
        }

        private void AddressDialog_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1) Kontoliste laden (muss VOR dem Setzen von SelectedValue passieren)
                StandardEinnahmenKontoBox.ItemsSource = _db.LadeKontoLookup();

                // 2) Falls Bearbeitung: frische Daten aus DB holen (nicht die alte Vorlage verwenden!)
                Adresse? src = null;
                if (_adresseId.HasValue)
                    src = _db.HoleAdresse(_adresseId.Value);

                // 3) UI-Felder befüllen
                if (src != null)
                {
                    NameBox.Text = src.Name;
                    StrasseBox.Text = src.Strasse;
                    PLZBox.Text = src.PLZ;
                    OrtBox.Text = src.Ort;
                    LandBox.Text = src.Land;
                    TypBox.Text = src.Typ;
                    IbanBox.Text = src.IBAN;
                    NotizBox.Text = src.Notiz;

                    IstBudgetiertCheck.IsChecked = src.IstBudgetiert;

                    if (src.StandardEinnahmenKontoId.HasValue)
                        StandardEinnahmenKontoBox.SelectedValue = src.StandardEinnahmenKontoId.Value;

                    // Ergebnis-Objekt mit Id anlegen (Rest wird bei OK neu gesetzt)
                    Ergebnis = new Adresse { Id = src.Id };
                }

                RefreshBudgetUi();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Adresse-Dialog konnte nicht initialisiert werden:\n" + ex.Message,
                    "Adresse", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Aktiviert/deaktiviert die Konto-Combo je nach Checkbox.
        /// </summary>
        private void RefreshBudgetUi()
        {
            bool aktiv = IstBudgetiertCheck.IsChecked == true;
            StandardEinnahmenKontoBox.IsEnabled = aktiv;
            StandardEinnahmenKontoBox.Opacity = aktiv ? 1.0 : 0.6;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Name ist Pflicht.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Ergebnis ??= new Adresse();
            if (_adresseId.HasValue) Ergebnis.Id = _adresseId.Value;

            Ergebnis.Name = NameBox.Text.Trim();
            Ergebnis.Strasse = TrimOrNull(StrasseBox.Text);
            Ergebnis.PLZ = TrimOrNull(PLZBox.Text);
            Ergebnis.Ort = TrimOrNull(OrtBox.Text);
            Ergebnis.Land = TrimOrNull(LandBox.Text);
            Ergebnis.Typ = TrimOrNull(TypBox.Text);

            // IBAN: Leerzeichen entfernen + Uppercase, leer => null
            var iban = TrimOrNull(IbanBox.Text);
            Ergebnis.IBAN = string.IsNullOrEmpty(iban) ? null : iban.Replace(" ", "").ToUpperInvariant();

            Ergebnis.Notiz = TrimOrNull(NotizBox.Text);

            // NEU: Budget-Flag + optionales Standardkonto
            Ergebnis.IstBudgetiert = IstBudgetiertCheck.IsChecked == true;

            int? stdKontoId = null;
            if (StandardEinnahmenKontoBox.SelectedValue is int id)
                stdKontoId = id;

            // Nur setzen, wenn budgetiert; sonst bewusst null (kein Budgetfluss)
            Ergebnis.StandardEinnahmenKontoId = Ergebnis.IstBudgetiert ? stdKontoId : null;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static string? TrimOrNull(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
