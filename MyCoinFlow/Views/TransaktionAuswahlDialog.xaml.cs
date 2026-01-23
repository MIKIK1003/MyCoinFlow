using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class TransaktionAuswahlDialog : Window
    {
        public ObservableCollection<Transaktion> Rows { get; } = new();
        public Transaktion? SelectedRow { get; set; }

        public Transaktion? Result => SelectedRow;

        public TransaktionAuswahlDialog()
        {
            InitializeComponent();
            DataContext = this;

            var db = new DatabaseService();
            foreach (var t in db.StweTransaktionenGetRecent(500))
                Rows.Add(t);
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void Uebernehmen_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRow == null)
            {
                MessageBox.Show("Bitte eine Transaktion auswählen.", "Set erstellen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
        }
    }
}
