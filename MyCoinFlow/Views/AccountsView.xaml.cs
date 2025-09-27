using MyCoinFlow.Models;
using MyCoinFlow.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MyCoinFlow.Views
{
    public partial class AccountsView : UserControl
    {
        public AccountsView()
        {
            InitializeComponent();
            this.DataContext = new AccountsViewModel();
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (this.DataContext is AccountsViewModel vm && e.NewValue is KontoplanKnoten knoten)
            {
                vm.AusgewaehlterKnoten = knoten;
            }
        }

        // Icon-Klick in Tabellenansicht -> Konto-Transaktionen-Fenster öffnen
        private void OpenKontoTransaktionen_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is KontoplanEintrag row)
            {
                int kontoId = row.Id;
                // Anzeigename fürs Fenster: Detail (Fallback auf Nummer)
                string name = !string.IsNullOrWhiteSpace(row.Detail)
                              ? row.Detail
                              : (row.Kontonummer > 0 ? $"Konto {row.Kontonummer}" : $"Konto #{kontoId}");

                var wnd = new KontoTransaktionenWindow(kontoId, name)
                {
                    Owner = Application.Current.MainWindow
                };
                wnd.ShowDialog();
            }
        }
    }
}
