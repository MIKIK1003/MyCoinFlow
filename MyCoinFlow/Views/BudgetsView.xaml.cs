using System.Windows.Controls;

namespace MyCoinFlow.Views
{
    public partial class BudgetsView : UserControl
    {
        public BudgetsView()
        {
            InitializeComponent();
            // DataContext kommt von außen (MainViewModel via DataTemplate)
        }
    }
}
