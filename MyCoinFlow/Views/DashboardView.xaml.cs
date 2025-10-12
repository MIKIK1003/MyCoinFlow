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
    }
}
