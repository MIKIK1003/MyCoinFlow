using System.Windows.Controls;
using MyCoinFlow.ViewModels;

namespace MyCoinFlow.Views
{
    public partial class KontenArtView : UserControl
    {
        public KontenArtView()
        {
            InitializeComponent();
            this.DataContext = new KontenArtViewModel();
        }
    }
}
