using System.Windows;
using MyCoinFlow.ViewModels;

namespace MyCoinFlow.Views
{
    public partial class CreditCardImportWindow : Window
    {
        public CreditCardImportWindow()
        {
            InitializeComponent();
            DataContext = new CreditCardImportViewModel(); // reines Mock-VM (siehe unten)
        }
    }
}
