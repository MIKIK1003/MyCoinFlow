using System.Windows.Controls;
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
    }
}
