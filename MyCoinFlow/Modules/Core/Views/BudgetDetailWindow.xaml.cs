using System.Windows;
using MyCoinFlow.ViewModels;
using MyCoinFlow.UI.Base; // NEU

namespace MyCoinFlow.Views
{
    public partial class BudgetDetailWindow : BaseWindow // NEU
    {
        private readonly BudgetDetailViewModel _vm;

        public BudgetDetailWindow(int? zeitraumId)
        {
            InitializeComponent();

            // EIN gemeinsames ViewModel für das ganze Fenster
            _vm = new BudgetDetailViewModel(zeitraumId);

            // DataContext direkt auf View setzen (gewollt)
            RootView.DataContext = _vm;

            // Beim Schließen automatisch speichern
            this.Closing += (s, e) =>
            {
                _vm.SaveAll();
            };
        }
    }
}