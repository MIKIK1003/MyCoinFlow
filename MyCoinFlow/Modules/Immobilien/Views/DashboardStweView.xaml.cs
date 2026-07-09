using System.Windows.Controls;
using MyCoinFlow.ViewModels;

namespace MyCoinFlow.Views
{
    public partial class DashboardStweView : UserControl
    {
        public DashboardStweView()
        {
            InitializeComponent();

            // Bombensicher: STWE-View verwaltet ihr eigenes VM.
            // Damit sind wir unabhängig vom Host/Container.
            if (DataContext == null)
                DataContext = new DashboardStweViewModel();
        }
    }
}
