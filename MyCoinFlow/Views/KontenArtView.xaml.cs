using System.Windows.Controls;
using MyCoinFlow.ViewModels;

namespace MyCoinFlow.Views
{
    public partial class KontenArtView : UserControl
    {
        public KontenArtView()
        {
            InitializeComponent();

            // Admin-SubView: hostet ihr eigenes ViewModel (kein Navigation-Target via DataTemplates)
            DataContext = new KontenArtViewModel();
        }
    }
}
