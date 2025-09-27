using System.Collections.ObjectModel;
using System.Windows;
using MyCoinFlow.Models;

namespace MyCoinFlow.Views
{
    public partial class KategorieZuweisenDialog : Window
    {
        public int? AusgewaehlterKontoId { get; private set; }
        public bool MappingDauerhaftSpeichern => ChkSpeichern.IsChecked == true;

        // NEU: erwartet KontoLookup-Liste statt IEnumerable<string>
        public KategorieZuweisenDialog(string kategorie, ObservableCollection<KontoLookup> kontoListe)
        {
            InitializeComponent();
            KategorieBox.Text = kategorie;
            KontoCombo.ItemsSource = kontoListe;   // Items sind KontoLookup-Objekte
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (KontoCombo.SelectedValue is int id)
            {
                AusgewaehlterKontoId = id;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Bitte ein Konto auswählen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
