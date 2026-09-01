using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using System.Collections.ObjectModel;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class AttachmentsDialog : PersistentWindow
{
    private readonly int _transactionId;
    private readonly TransactionToolsRepository _repository;
    public ObservableCollection<AttachmentRecord> Files { get; } = new();
    public bool Changed { get; private set; }

    public AttachmentsDialog(int transactionId, TransactionToolsRepository repository)
    {
        InitializeComponent();
        _transactionId = transactionId;
        _repository = repository;
        FilesList.ItemsSource = Files;
        Title = "MyCoinFlow – Anhänge verwalten";
        SubtitleText.Text = $"Dokumente zu Transaktion #{transactionId}. Zurückstellen löst nur die Verknüpfung; Löschen verschiebt die Datei in den Archivbereich.";
        AppWindow.Resize(new SizeInt32(920, 540));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 760;
            presenter.PreferredMinimumHeight = 420;
        }
    }

    public async Task InitializeAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        Files.Clear();
        foreach (var row in await _repository.GetAttachmentsAsync(_transactionId)) Files.Add(row);
    }

    private async void OnUnlinkClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AttachmentRecord row) return;
        if (!await ConfirmAsync("Anhang zurückstellen?", $"„{row.DisplayName}“ wird von dieser Transaktion gelöst. Die Datei bleibt im DMS erhalten.")) return;
        await GuardAsync(async () => { await _repository.UnlinkAttachmentAsync(row.Id); Changed = true; await ReloadAsync(); if (Files.Count == 0) Close(); });
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AttachmentRecord row) return;
        if (!await ConfirmAsync("Anhang löschen?", $"„{row.DisplayName}“ wird aus dem aktiven DMS entfernt und in den Archivbereich verschoben.")) return;
        await GuardAsync(async () => { await _repository.DeleteAttachmentAsync(row); Changed = true; await ReloadAsync(); if (Files.Count == 0) Close(); });
    }

    private async Task<bool> ConfirmAsync(string title, string content)
    {
        var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = title, Content = content, PrimaryButtonText = "Bestätigen", CloseButtonText = "Abbrechen", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task GuardAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) { MessageBar.Message = exception.Message; MessageBar.Severity = InfoBarSeverity.Error; MessageBar.IsOpen = true; }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
