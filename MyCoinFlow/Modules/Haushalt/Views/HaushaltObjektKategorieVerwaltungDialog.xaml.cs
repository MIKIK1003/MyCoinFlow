using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Linq;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class HaushaltObjektKategorieVerwaltungDialog
    {
        private readonly DatabaseService _db = new();

        public HaushaltObjektKategorieVerwaltungDialog()
        {
            InitializeComponent();
            LadeDaten();
        }

        private HaushaltObjektKategorie? SelectedKategorie =>
            KategorieGrid.SelectedItem as HaushaltObjektKategorie;

        private void LadeDaten()
        {
            KategorieGrid.ItemsSource = null;
            KategorieGrid.ItemsSource = _db.HaushaltObjektKategorienGetAll();
        }

        private void Neu_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new HaushaltObjektKategorieDialog
            {
                Owner = this
            };

            if (dlg.ShowDialog() != true)
                return;

            _db.HaushaltObjektKategorieInsert(dlg.Ergebnis);
            LadeDaten();
        }

        private void Bearbeiten_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedKategorie == null)
            {
                MessageBox.Show(
                    this,
                    "Bitte zuerst eine Kategorie auswählen.",
                    "Kategorie bearbeiten",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dlg = new HaushaltObjektKategorieDialog(SelectedKategorie)
            {
                Owner = this
            };

            if (dlg.ShowDialog() != true)
                return;

            _db.HaushaltObjektKategorieUpdate(dlg.Ergebnis);
            LadeDaten();
        }

        private void Loeschen_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedKategorie == null)
            {
                MessageBox.Show(
                    this,
                    "Bitte zuerst eine Kategorie auswählen.",
                    "Kategorie löschen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var verwendung = _db.HaushaltObjekteGetAll()
                .Count(x => x.KategorieId == SelectedKategorie.Id);

            if (verwendung > 0)
            {
                MessageBox.Show(
                    this,
                    $"Die Kategorie \"{SelectedKategorie.Bezeichnung}\" wird bereits von {verwendung} Objekt(en) verwendet und kann deshalb nicht gelöscht werden.",
                    "Kategorie löschen nicht möglich",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var antwort = MessageBox.Show(
                this,
                $"Soll die Kategorie \"{SelectedKategorie.Bezeichnung}\" wirklich gelöscht werden?",
                "Kategorie löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (antwort != MessageBoxResult.Yes)
                return;

            _db.HaushaltObjektKategorieDelete(SelectedKategorie.Id);
            LadeDaten();
        }

        private void Schliessen_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}