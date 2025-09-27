using System.Windows;
using MyCoinFlow.ViewModels;

namespace MyCoinFlow
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            if (DataContext is null)
                DataContext = new MyCoinFlow.ViewModels.MainViewModel();

            // Wichtig: DataContext setzen, falls er nicht schon via DI gesetzt wird.
            if (DataContext is null)
                DataContext = new MainViewModel();
        }
    }
}
