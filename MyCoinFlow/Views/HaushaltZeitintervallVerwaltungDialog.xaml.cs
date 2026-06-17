using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class HaushaltZeitintervallVerwaltungDialog
    {
        private readonly DatabaseService _db = new();

        public HaushaltZeitintervallVerwaltungDialog()
        {
            InitializeComponent();
            LadeDaten();
        }

        private HaushaltZeitintervall? SelectedIntervall =>
            IntervallGrid.SelectedItem as HaushaltZeitintervall;

        private void LadeDaten()
        {
            IntervallGrid.ItemsSource = null;
            IntervallGrid.ItemsSource = _db.HaushaltZeitintervalleGetAll();
        }

        private void Neu_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new HaushaltZeitintervallDialog
            {
                Owner = this
            };

            if (dlg.ShowDialog() != true)
                return;

            _db.HaushaltZeitintervallInsert(dlg.Ergebnis);
            LadeDaten();
        }

        private void Bearbeiten_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedIntervall == null)
            {
                MessageBox.Show(this, "Bitte zuerst ein Zeitintervall auswählen.", "Zeitintervall bearbeiten",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new HaushaltZeitintervallDialog(SelectedIntervall)
            {
                Owner = this
            };

            if (dlg.ShowDialog() != true)
                return;

            _db.HaushaltZeitintervallUpdate(dlg.Ergebnis);
            LadeDaten();
        }

        private void Loeschen_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedIntervall == null)
            {
                MessageBox.Show(this, "Bitte zuerst ein Zeitintervall auswählen.", "Zeitintervall löschen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var antwort = MessageBox.Show(
                this,
                $"Soll das Zeitintervall \"{SelectedIntervall.Bezeichnung}\" wirklich gelöscht werden?",
                "Zeitintervall löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (antwort != MessageBoxResult.Yes)
                return;

            _db.HaushaltZeitintervallDelete(SelectedIntervall.Id);
            LadeDaten();
        }

        private void Schliessen_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}