using System.Windows;
using System.Windows.Controls;

namespace MyCoinFlow.Views
{
    public partial class TransactionsView : UserControl
    {
        public TransactionsView()
        {
            InitializeComponent();
        }

        private void OpenCreditCardImport_Click(object sender, RoutedEventArgs e)
        {
            var win = new CreditCardImportWindow
            {
                Owner = Window.GetWindow(this)
            };
            win.ShowDialog();
        }
    }
}
