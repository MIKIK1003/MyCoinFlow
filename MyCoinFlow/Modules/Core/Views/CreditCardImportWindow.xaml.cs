using System.Windows;
using MyCoinFlow.ViewModels;
using MyCoinFlow.UI.Base; // NEU

namespace MyCoinFlow.Views
{
    public partial class CreditCardImportWindow : BaseWindow // NEU
    {
        public CreditCardImportWindow()
        {
            InitializeComponent();
            DataContext = new CreditCardImportViewModel(); // unverändert
        }
    }
}