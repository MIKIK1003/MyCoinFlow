using System.Windows.Controls;
using MyCoinFlow.ViewModels;

namespace MyCoinFlow.Views
{
    public partial class KontenUnterGruppeView : UserControl
    {
        public KontenUnterGruppeView()
        {
            InitializeComponent();
            DataContext = new KontenUnterGruppeViewModel();
        }
    }
}
