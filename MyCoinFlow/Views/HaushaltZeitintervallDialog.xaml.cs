using MyCoinFlow.Models;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class HaushaltZeitintervallDialog
    {
        public HaushaltZeitintervall Ergebnis { get; private set; }

        public HaushaltZeitintervallDialog(HaushaltZeitintervall? intervall = null)
        {
            InitializeComponent();

            Ergebnis = intervall == null
                ? new HaushaltZeitintervall { IstAktiv = true }
                : new HaushaltZeitintervall
                {
                    Id = intervall.Id,
                    Bezeichnung = intervall.Bezeichnung,
                    Tage = intervall.Tage,
                    Bemerkung = intervall.Bemerkung,
                    IstAktiv = intervall.IstAktiv,
                    ErstelltAm = intervall.ErstelltAm,
                    GeaendertAm = intervall.GeaendertAm
                };

            BezeichnungBox.Text = Ergebnis.Bezeichnung;
            TageBox.Text = Ergebnis.Tage > 0 ? Ergebnis.Tage.ToString() : "";
            BemerkungBox.Text = Ergebnis.Bemerkung;
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            var bezeichnung = BezeichnungBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(bezeichnung))
            {
                MessageBox.Show(this, "Bitte eine Bezeichnung erfassen.", "Pflichtfeld",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                BezeichnungBox.Focus();
                return;
            }

            if (!int.TryParse(TageBox.Text?.Trim(), out var tage) || tage <= 0)
            {
                MessageBox.Show(this, "Bitte ein gültiges Intervall in Tagen erfassen.", "Pflichtfeld",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                TageBox.Focus();
                return;
            }

            Ergebnis.Bezeichnung = bezeichnung;
            Ergebnis.Tage = tage;
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