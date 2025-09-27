using System.Windows.Controls;
using MyCoinFlow.ViewModels;
using MyCoinFlow.Services.Dashboard;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();

            // Provider: echte DB
            var provider = new SqlDashboardProvider(() => new DatabaseService().CreateConnection());
            var vm = new DashboardViewModel(provider);

            if (DataContext == null)
                DataContext = vm;

            Loaded += async (_, __) => await vm.InitializeAsync();
        }
    }
}
