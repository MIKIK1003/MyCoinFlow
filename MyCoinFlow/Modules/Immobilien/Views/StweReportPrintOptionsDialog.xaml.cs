using MyCoinFlow.Models;
using MyCoinFlow.UI.Base;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class StweReportPrintOptionsDialog : BaseWindow
    {
        public StweReportPrintOptions Options { get; }

        public StweReportPrintOptionsDialog(StweReportPrintOptions? initial = null)
        {
            InitializeComponent();

            Options = initial ?? new StweReportPrintOptions
            {
                MitDeckblatt = true,
                NeueSeiteProEigentuemer = true,
                MitOriginalTransaktionen = true
            };

            DataContext = this;
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
