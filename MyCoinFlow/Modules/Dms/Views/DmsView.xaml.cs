using System.Windows.Controls;
using System.Windows.Input;
using MyCoinFlow.Models;
using MyCoinFlow.ViewModels;

namespace MyCoinFlow.Views
{
    public partial class DmsView : UserControl
    {
        public DmsView()
        {
            InitializeComponent();
            // DataContext kommt von außen (MainViewModel via DataTemplate)
            Unloaded += (s, e) => (DataContext as DmsViewModel)?.Dispose();
        }

        // WPF wählt eine Zeile bei Rechtsklick nicht automatisch aus – das Kontextmenü
        // würde sonst auf die zuvor angeklickte Zeile wirken.
        private void DataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row)
            {
                row.IsSelected = true;
                row.Focus();
            }
        }

        private void DocumentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox list || list.SelectedItem is not DmsDocument document) return;
            if (DataContext is DmsViewModel viewModel)
                viewModel.AusgewaehltesDokument = document;
        }
    }
}
