using System.Windows.Controls;

namespace MyCoinFlow.Views
{
    public partial class LiegenschaftenView : UserControl
    {
        public LiegenschaftenView()
        {
            InitializeComponent();
            // DataContext kommt von außen (DataTemplate -> LiegenschaftenViewModel)
        }
    }
}
