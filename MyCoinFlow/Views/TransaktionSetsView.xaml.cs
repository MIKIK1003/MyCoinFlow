using System.Windows.Controls;

namespace MyCoinFlow.Views
{
    public partial class TransaktionSetsView : UserControl
    {
        public TransaktionSetsView()
        {
            InitializeComponent();
            // DataContext kommt von außen (DataTemplate -> TransaktionSetsViewModel)
        }
    }
}
