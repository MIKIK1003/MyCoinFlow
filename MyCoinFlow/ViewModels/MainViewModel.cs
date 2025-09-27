using MyCoinFlow.Helpers;
using System.Windows.Input;
using MyCoinFlow.Views;
using System.Windows.Controls;
using MyCoinFlow.ViewModels; // <-- wichtig, damit AddressesViewModel gefunden wird

namespace MyCoinFlow.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private UserControl? _currentView;
        public UserControl? CurrentView
        {
            get => _currentView;
            set { _currentView = value; OnPropertyChanged(); }
        }

        public ICommand ShowDashboardCommand { get; }
        public ICommand ShowTransactionsCommand { get; }
        public ICommand ShowAccountsCommand { get; }
        public ICommand ShowInstitutionsCommand { get; }
        public ICommand ShowAddressesCommand { get; }
        public ICommand ShowAdminCommand { get; }
        public ICommand ShowBudgetsCommand { get; }

        public MainViewModel()
        {
            // Startview (optional auch mit VM setzen, je nach Bedarf)
            CurrentView = new DashboardView();

            ShowDashboardCommand = new RelayCommand(_ =>
            {
                CurrentView = new DashboardView();
            });

            ShowTransactionsCommand = new RelayCommand(_ =>
            {
                var v = new TransactionsView();
                v.DataContext = new TransactionsViewModel(); // <-- wichtig
                CurrentView = v;
            });


            ShowAccountsCommand = new RelayCommand(_ =>
            {
                CurrentView = new AccountsView();
            });

            ShowInstitutionsCommand = new RelayCommand(_ =>
            {
                var v = new InstitutionsView();
                v.DataContext = new InstitutionsViewModel(); // <-- wichtig
                CurrentView = v;
            });


            // *** HIER WICHTIG: View + ViewModel verbinden ***
            ShowAddressesCommand = new RelayCommand(_ =>
            {
                var v = new AddressesView();
                v.DataContext = new AddressesViewModel();   // <- ohne das geht "Neu" nicht
                CurrentView = v;
            });

            ShowAdminCommand = new RelayCommand(_ =>
            {
                CurrentView = new AdminView();
            });

            ShowBudgetsCommand = new RelayCommand(_ =>
            {
                var v = new BudgetsView();
                v.DataContext = new BudgetsViewModel();     // (optional) gleich mit VM
                CurrentView = v;
            });

        }
    }
}
