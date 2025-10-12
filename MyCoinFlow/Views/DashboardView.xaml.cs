using System.Windows;
using System.Windows.Controls;

namespace MyCoinFlow.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
            if (DataContext == null)
                DataContext = new MyCoinFlow.ViewModels.DashboardViewModel();
        }

        private void PrintDashboard_Click(object sender, RoutedEventArgs e)
        {
            // Druckt den linken Dashboard‑Bereich (PrintScope)
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() == true)
            {
                // Hinweis: skaliert NICHT automatisch auf Seite – bei Bedarf später Feintuning.
                dlg.PrintVisual(PrintScope, "MyCoinFlow Dashboard");
            }
        }
    }
}
