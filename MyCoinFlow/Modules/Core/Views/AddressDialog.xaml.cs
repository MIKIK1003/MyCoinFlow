using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.UI.Base; // NEU: BaseWindow einbinden
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace MyCoinFlow.Views
{
    public partial class AddressDialog : BaseWindow // NEU: BaseWindow statt Window
    {
        private readonly DatabaseService _db = new();
        private readonly int? _adresseId;   // merkt, welche Adresse bearbeitet wird

        public Adresse? Ergebnis { get; private set; }

        public AddressDialog(Adresse? vorlage = null)
        {
            InitializeComponent();

            // Id merken (falls Bearbeitung)
            _adresseId = vorlage?.Id;

            // UI-Events für Budget-Checkbox
            IstBudgetiertCheck.Checked += (_, __) => RefreshBudgetUi();
            IstBudgetiertCheck.Unchecked += (_, __) => RefreshBudgetUi();

            // Initialisierung erst nach Laden des Fensters
            Loaded += AddressDialog_Loaded;
        }

        private void AddressDialog_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Kontoliste laden (muss vor SelectedValue erfolgen)
                StandardEinnahmenKontoBox.ItemsSource = _db.LadeKontoLookup();

                // Falls Bearbeitung → aktuelle Daten aus DB holen
                Adresse? src = null;
                if (_adresseId.HasValue)
                    src = _db.HoleAdresse(_adresseId.Value);

                // UI befüllen
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

                    // Ergebnisobjekt vorbereiten
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
        /// Aktiviert / deaktiviert Kontoauswahl je nach Budget-Checkbox
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
            if (_adresseId.HasValue)
                Ergebnis.Id = _adresseId.Value;

            Ergebnis.Name = NameBox.Text.Trim();
            Ergebnis.Strasse = TrimOrNull(StrasseBox.Text);
            Ergebnis.PLZ = TrimOrNull(PLZBox.Text);
            Ergebnis.Ort = TrimOrNull(OrtBox.Text);
            Ergebnis.Land = TrimOrNull(LandBox.Text);
            Ergebnis.Typ = TrimOrNull(TypBox.Text);

            // IBAN normalisieren
            var iban = TrimOrNull(IbanBox.Text);
            Ergebnis.IBAN = string.IsNullOrEmpty(iban)
                ? null
                : iban.Replace(" ", "").ToUpperInvariant();

            Ergebnis.Notiz = TrimOrNull(NotizBox.Text);

            // Budget-Logik
            Ergebnis.IstBudgetiert = IstBudgetiertCheck.IsChecked == true;

            int? stdKontoId = null;
            if (StandardEinnahmenKontoBox.SelectedValue is int id)
                stdKontoId = id;

            Ergebnis.StandardEinnahmenKontoId =
                Ergebnis.IstBudgetiert ? stdKontoId : null;

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