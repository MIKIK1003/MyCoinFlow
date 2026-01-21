using MyCoinFlow.Helpers;
using System.Windows.Input;

namespace MyCoinFlow.ViewModels
{
    /// <summary>
    /// Navigation: setzt CurrentViewModel. Die Views werden über DataTemplates in App.xaml gewählt.
    /// </summary>
    public class MainViewModel : BaseViewModel
    {
        private object? _currentViewModel;

        /// <summary>
        /// Aktuell angezeigtes ViewModel (ContentControl bindet daran).
        /// </summary>
        public object? CurrentViewModel
        {
            get => _currentViewModel;
            set { _currentViewModel = value; OnPropertyChanged(); }
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
            CurrentViewModel = new DashboardViewModel();

            ShowDashboardCommand = new RelayCommand(_ => CurrentViewModel = new DashboardViewModel());
            ShowTransactionsCommand = new RelayCommand(_ => CurrentViewModel = new TransactionsViewModel());
            ShowAccountsCommand = new RelayCommand(_ => CurrentViewModel = new AccountsViewModel());
            ShowInstitutionsCommand = new RelayCommand(_ => CurrentViewModel = new InstitutionsViewModel());
            ShowAddressesCommand = new RelayCommand(_ => CurrentViewModel = new AddressesViewModel());
            ShowAdminCommand = new RelayCommand(_ => CurrentViewModel = new AdminViewModel());
            ShowBudgetsCommand = new RelayCommand(_ => CurrentViewModel = new BudgetsViewModel());
        }
    }
}
