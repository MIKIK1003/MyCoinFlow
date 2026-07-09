using System.Linq;
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

        // Sicheren Owner für Dialoge ermitteln (Host-Fenster dieser View, sonst aktives Window)
        private Window? GetSafeOwner(Window dialog)
        {
            var owner = Window.GetWindow(this)
                        ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

            // Niemals sich selbst als Owner setzen
            return owner != null && !ReferenceEquals(owner, dialog) ? owner : null;
        }

        // Klick auf das Uhren-Icon in der ersten Spalte
        private void OpenTransaktionen_Click(object sender, RoutedEventArgs e)
        {
            if (e != null) e.Handled = true;

            // Datensatz der Zeile holen
            if (sender is not Button btn || btn.DataContext is not object row) return;

            // Wir erwarten Id (int) und Name (string) auf dem Zeilenobjekt (z.B. Geldinstitut/GeldinstitutSaldo)
            dynamic dyn = row;
            int giId = (int)dyn.Id;
            string giName = (string)dyn.Name;

            var dlg = new GeldinstitutTransaktionenWindow(giId, giName);

            var owner = GetSafeOwner(dlg);
            if (owner != null) dlg.Owner = owner;

            dlg.ShowDialog();
        }

        // Doppelklick auf eine Zeile öffnet ebenfalls das Fenster
        private void InstituteGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (InstituteGrid?.SelectedItem is not object row) return;

            dynamic dyn = row;
            int giId = (int)dyn.Id;
            string giName = (string)dyn.Name;

            var dlg = new GeldinstitutTransaktionenWindow(giId, giName);

            var owner = GetSafeOwner(dlg);
            if (owner != null) dlg.Owner = owner;

            dlg.ShowDialog();
        }
    }
}
