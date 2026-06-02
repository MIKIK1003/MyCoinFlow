using MyCoinFlow.Helpers;
using System.Windows.Input;

namespace MyCoinFlow.ViewModels
{
    /// <summary>
    /// Zentrales ViewModel für das MainWindow.
    /// Navigation erfolgt über CurrentViewModel (ViewModels statt Views).
    /// Die Anzeige wird über DataTemplates in App.xaml (ViewModel -> View) gelöst.
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
        public ICommand ShowBudgetsCommand { get; }
        public ICommand ShowAdminCommand { get; }
        public ICommand ShowLiegenschaftenCommand { get; }
        public ICommand ShowVermoegenCommand { get; }

        // NEU: Tages-Workflow für Sets
        public ICommand ShowTransaktionSetsCommand { get; }

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

            // NEU
            ShowTransaktionSetsCommand = new RelayCommand(_ => CurrentViewModel = new TransaktionSetsViewModel());
        }
    }
}