using System.Windows;
using System.Windows.Controls;

namespace MyCoinFlow.Views
{
    public partial class InstitutionsView : UserControl
    {
        public InstitutionsView()
        {
            InitializeComponent();
        }

        // Klick auf das Uhren-Icon in der ersten Spalte
        private void OpenTransaktionen_Click(object sender, RoutedEventArgs e)
        {
            // Datensatz der Zeile holen
            if (sender is Button btn && btn.DataContext is object row)
            {
                // Wir erwarten ein Objekt mit mindestens Id (int) und Name (string).
                // Deine Grid-Items sind i.d.R. 'Geldinstitut' oder 'GeldinstitutSaldo' – beide haben Id/Name.
                dynamic dyn = row;
                int giId = (int)dyn.Id;
                string giName = (string)dyn.Name;

                var wnd = new GeldinstitutTransaktionenWindow(giId, giName)
                {
                    Owner = Application.Current.MainWindow
                };
                wnd.ShowDialog();
            }
        }

        // Komfort: Doppelklick auf eine Zeile öffnet ebenfalls das Fenster
        private void InstituteGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (InstituteGrid?.SelectedItem is object row)
            {
                dynamic dyn = row;
                int giId = (int)dyn.Id;
                string giName = (string)dyn.Name;

                var wnd = new GeldinstitutTransaktionenWindow(giId, giName)
                {
                    Owner = Application.Current.MainWindow
                };
                wnd.ShowDialog();
            }
        }
    }
}
