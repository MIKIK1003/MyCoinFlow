using System.Windows.Controls;

namespace MyCoinFlow.Views
{
    public partial class DmsView : UserControl
    {
        public DmsView()
        {
            InitializeComponent();
            // DataContext kommt von außen (MainViewModel via DataTemplate)
        }
    }
}
