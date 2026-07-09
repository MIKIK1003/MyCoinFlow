using System.Windows.Controls;
using MyCoinFlow.ViewModels;

namespace MyCoinFlow.Views
{
    public partial class BankImportView : UserControl
    {
        public BankImportView()
        {
            InitializeComponent();

            // BankImport ist ein eigenständiger Editor/View:
            // Er hostet sein ViewModel selbst, damit Commands (z.B. CAMT/OpenFile) funktionieren.
            DataContext = new BankImportViewModel();
        }
    }
}
