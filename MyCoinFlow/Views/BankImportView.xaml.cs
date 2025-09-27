using System.Windows.Controls;
using MyCoinFlow.ViewModels;

namespace MyCoinFlow.Views
{
    public partial class BankImportView : UserControl
    {
        public BankImportView()
        {
            InitializeComponent();
            DataContext = new BankImportViewModel();
        }
    }
}
