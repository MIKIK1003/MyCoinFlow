using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class StweReportPrintOptionsDialog : ContentDialog
{
    public StweReportPrintOptionsDialog() => InitializeComponent();
    public StweReportPrintOptions Options => new() { MitDeckblatt = CoverBox.IsChecked == true, NeueSeiteProEigentuemer = OwnerPageBox.IsChecked == true, MitOriginalTransaktionen = OriginalsBox.IsChecked == true };
}
