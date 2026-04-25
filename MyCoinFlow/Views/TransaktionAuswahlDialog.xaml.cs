using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.UI.Base;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class TransaktionAuswahlDialog : BaseWindow
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

        private void Grid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SelectedRow != null)
                DialogResult = true;
        }

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
