using System.Windows;
using MyCoinFlow.Models;

namespace MyCoinFlow.Views
{
    public partial class StweReportPrintOptionsDialog : Window
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
