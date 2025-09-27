using System.Windows.Controls;
using MyCoinFlow.ViewModels;

namespace MyCoinFlow.Views
{
    public partial class KontenGruppeView : UserControl
    {
        public KontenGruppeView()
        {
            InitializeComponent();
            this.DataContext = new KontenGruppeViewModel();
        }
    }
}
