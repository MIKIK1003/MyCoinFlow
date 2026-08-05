using System.Windows;
using System.Windows.Controls;

namespace MyCoinFlow.Views
{
    public partial class TransactionsView : UserControl
    {
        public TransactionsView()
        {
            InitializeComponent();
        }

        // Gewählte Zeile sichtbar machen – wichtig beim Sprung aus einem anderen Modul,
        // wo die Auswahl vom ViewModel gesetzt wird und sonst ausserhalb des Sichtbereichs läge.
        private void TransaktionenGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem != null)
                grid.ScrollIntoView(grid.SelectedItem);
        }

        private void OpenCreditCardImport_Click(object sender, RoutedEventArgs e)
        {
            var win = new CreditCardImportWindow
            {
                Owner = Window.GetWindow(this)
            };
            win.ShowDialog();
        }
    }
}
