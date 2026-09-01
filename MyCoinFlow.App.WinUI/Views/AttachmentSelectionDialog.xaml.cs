using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using System.Collections.ObjectModel;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class AttachmentSelectionDialog : ContentDialog
{
    private readonly TransactionToolsRepository _repository;
    public ObservableCollection<AttachmentRecord> Files { get; } = new();

    public AttachmentSelectionDialog(IEnumerable<AttachmentRecord> files, TransactionToolsRepository repository)
    {
        InitializeComponent();
        _repository = repository;
        foreach (var file in files) Files.Add(file);
        FilesList.ItemsSource = Files;
    }

    private async void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AttachmentRecord file) return;
        try
        {
            await _repository.OpenAttachmentAsync(file);
            Hide();
        }
        catch (Exception exception)
        {
            MessageBar.Message = exception.Message;
            MessageBar.Severity = InfoBarSeverity.Error;
            MessageBar.IsOpen = true;
        }
    }
}
