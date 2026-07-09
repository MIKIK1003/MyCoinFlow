using MyCoinFlow.Helpers;
using System.Windows.Input;

namespace MyCoinFlow.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private object? _currentViewModel;

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
        public ICommand ShowBudgetsCommand { get; }
        public ICommand ShowAdminCommand { get; }
        public ICommand ShowLiegenschaftenCommand { get; }
        public ICommand ShowVermoegenCommand { get; }
        public ICommand ShowTransaktionSetsCommand { get; }
        public ICommand ShowHaushaltCommand { get; }
        public ICommand ShowDmsCommand { get; }

        public MainViewModel()
        {
            CurrentViewModel = new DashboardViewModel();

            ShowDashboardCommand = new RelayCommand(_ => CurrentViewModel = new DashboardViewModel());
            ShowTransactionsCommand = new RelayCommand(_ => CurrentViewModel = new TransactionsViewModel());
            ShowAccountsCommand = new RelayCommand(_ => CurrentViewModel = new AccountsViewModel());
            ShowInstitutionsCommand = new RelayCommand(_ => CurrentViewModel = new InstitutionsViewModel());
            ShowAddressesCommand = new RelayCommand(_ => CurrentViewModel = new AddressesViewModel());
            ShowBudgetsCommand = new RelayCommand(_ => CurrentViewModel = new BudgetsViewModel());
            ShowAdminCommand = new RelayCommand(_ => CurrentViewModel = new AdminViewModel());
            ShowLiegenschaftenCommand = new RelayCommand(_ => CurrentViewModel = new LiegenschaftenViewModel());
            ShowVermoegenCommand = new RelayCommand(_ => CurrentViewModel = new VermoegenViewModel());
            ShowTransaktionSetsCommand = new RelayCommand(_ => CurrentViewModel = new TransaktionSetsViewModel());
            ShowHaushaltCommand = new RelayCommand(_ => CurrentViewModel = new HaushaltViewModel());
            ShowDmsCommand = new RelayCommand(_ => CurrentViewModel = new DmsViewModel());
        }
    }
}