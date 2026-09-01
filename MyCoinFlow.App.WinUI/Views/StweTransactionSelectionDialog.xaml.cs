using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class StweTransactionSelectionDialog : ContentDialog
{
    public StweTransactionSelectionDialog()
    {
        InitializeComponent();
        var database = new DatabaseService();
        TransactionsList.ItemsSource = database.StweTransaktionenGetRecent(500).Select(value => new StweTransactionDisplayRow(value)).ToList();
    }

    public Transaktion? Result => (TransactionsList.SelectedItem as StweTransactionDisplayRow)?.Value;

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (Result is not null) return;
        args.Cancel = true;
        ErrorBar.Message = "Bitte eine Transaktion auswählen.";
        ErrorBar.IsOpen = true;
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Result is not null) Hide();
    }
}
