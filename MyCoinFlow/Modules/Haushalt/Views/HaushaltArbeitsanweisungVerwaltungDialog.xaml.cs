using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class HaushaltArbeitsanweisungVerwaltungDialog
    {
        private readonly DatabaseService _db = new();

        public HaushaltArbeitsanweisungVerwaltungDialog()
        {
            InitializeComponent();
            LadeDaten();
        }

        private HaushaltArbeitsanweisung? SelectedAnweisung =>
            AnweisungGrid.SelectedItem as HaushaltArbeitsanweisung;

        private void LadeDaten()
        {
            AnweisungGrid.ItemsSource = null;
            AnweisungGrid.ItemsSource = _db.HaushaltArbeitsanweisungenGetAll();
        }

        private void Neu_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new HaushaltArbeitsanweisungDialog
            {
                Owner = this
            };

            if (dlg.ShowDialog() != true)
                return;

            _db.HaushaltArbeitsanweisungInsert(dlg.Ergebnis);
            LadeDaten();
        }

        private void Bearbeiten_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedAnweisung == null)
            {
                MessageBox.Show(this, "Bitte zuerst eine Tätigkeit auswählen.", "Tätigkeit bearbeiten",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new HaushaltArbeitsanweisungDialog(SelectedAnweisung)
            {
                Owner = this
            };

            if (dlg.ShowDialog() != true)
                return;

            _db.HaushaltArbeitsanweisungUpdate(dlg.Ergebnis);
            LadeDaten();
        }

        private void Loeschen_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedAnweisung == null)
            {
                MessageBox.Show(this, "Bitte zuerst eine Tätigkeit auswählen.", "Tätigkeit löschen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var antwort = MessageBox.Show(
                this,
                $"Soll die Tätigkeit \"{SelectedAnweisung.Bezeichnung}\" wirklich gelöscht werden?",
                "Tätigkeit löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (antwort != MessageBoxResult.Yes)
                return;

            _db.HaushaltArbeitsanweisungDelete(SelectedAnweisung.Id);
            LadeDaten();
        }

        private void Schliessen_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}