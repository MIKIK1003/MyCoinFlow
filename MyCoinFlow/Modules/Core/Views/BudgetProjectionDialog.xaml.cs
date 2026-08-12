using MyCoinFlow.UI.Base;
using MyCoinFlow.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MyCoinFlow.Views
{
    public partial class BudgetProjectionDialog : BaseWindow
    {
        public BudgetProjectionDialog(BudgetProjectionPreviewViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            BudgetGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            BudgetGrid.CommitEdit(DataGridEditingUnit.Row, true);

            if (HatValidierungsfehler(BudgetGrid))
            {
                MessageBox.Show(
                    this,
                    "Bitte korrigiere die markierte Eingabe. Das neue Budget muss eine Zahl grösser oder gleich null sein.",
                    "Ungültiger Budgetwert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (DataContext is not BudgetProjectionPreviewViewModel viewModel || !viewModel.HatAuswahl)
                return;

            DialogResult = true;
        }

        private static bool HatValidierungsfehler(DependencyObject element)
        {
            if (Validation.GetHasError(element))
                return true;

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            {
                if (HatValidierungsfehler(VisualTreeHelper.GetChild(element, index)))
                    return true;
            }

            return false;
        }
    }
}
