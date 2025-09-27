using System.Windows.Controls;
using MyCoinFlow.Services;
using MyCoinFlow.ViewModels;

namespace MyCoinFlow.Views
{
    /// <summary>
    /// Interaktionslogik für CreditCardImportMappingView.xaml
    /// </summary>
    public partial class CreditCardImportMappingView : UserControl
    {
        public CreditCardImportMappingView()
        {
            InitializeComponent();

            // Robustes, selbstgenügsames Wiring:
            // Falls noch kein DataContext gesetzt ist, erstellen wir Service + ViewModel hier.
            if (DataContext == null)
            {
                var repo = new DatabaseService();                         // DatabaseService implementiert ICreditCardImportRepository
                var svc = new CreditCardImportMappingService(repo);      // Mapping-Service
                DataContext = new CreditCardImportMappingViewModel(svc);  // ViewModel
            }
        }
    }
}
