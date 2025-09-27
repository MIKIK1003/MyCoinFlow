using System.Windows;
using MyCoinFlow.ViewModels;


namespace MyCoinFlow.Views
{
    public partial class BudgetDetailWindow : Window
    {
        private readonly BudgetDetailViewModel _vm;

        public BudgetDetailWindow(int? zeitraumId)
        {
            InitializeComponent();

            // EIN gemeinsames ViewModel für das ganze Fenster
            _vm = new BudgetDetailViewModel(zeitraumId);

            // WICHTIG: Den DataContext direkt auf die View setzen,
            // nicht (nur) aufs Window.
            RootView.DataContext = _vm;

            // Optionaler Fallback: Beim Schließen alles speichern
            this.Closing += (s, e) =>
            {
                _vm.SaveAll(); // jetzt public, kein Reflection mehr nötig
            };
        }
    }
}
