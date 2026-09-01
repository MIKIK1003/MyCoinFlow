using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.WinUI.Services;
using MyCoinFlow.WinUI.ViewModels;
using System.Diagnostics;
using Windows.System;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class TransactionsPage : Page
{
    private readonly TransactionRepository _repository = new();
    private readonly TransactionToolsRepository _toolsRepository = new();
    private bool _initialized;
    private bool _isUnloading;
    private BankImportWindow? _bankImportWindow;
    private CreditCardImportWindow? _creditCardImportWindow;
    private TransactionReportWindow? _reportWindow;
    private readonly HashSet<AttachmentsDialog> _attachmentWindows = new();
    private int? _pendingFocusTransactionId;
    private int? _selectionToRestoreId;
    private ListView? _activeTransactionList;

    public TransactionsViewModel ViewModel { get; }

    public TransactionsPage()
    {
        InitializeComponent();
        ViewModel = new TransactionsViewModel(_repository);
        DataContext = ViewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is int transactionId && transactionId > 0)
            _pendingFocusTransactionId = transactionId;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isUnloading = true;
        _bankImportWindow?.Close();
        _creditCardImportWindow?.Close();
        foreach (var window in _attachmentWindows.ToList()) window.Close();
        _attachmentWindows.Clear();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isUnloading = false;
        if (!_initialized)
        {
            _initialized = true;
            await ViewModel.InitializeAsync();
        }
        if (_pendingFocusTransactionId is int transactionId)
        {
            _pendingFocusTransactionId = null;
            await FocusTransactionAsync(transactionId);
        }
    }

    public async Task FocusTransactionAsync(int transactionId)
    {
        if (!_initialized)
        {
            _pendingFocusTransactionId = transactionId;
            return;
        }
        await ViewModel.FocusTransactionAsync(transactionId);
        if (ViewModel.SelectedTransaction is not null)
        {
            _selectionToRestoreId = ViewModel.SelectedTransaction.Id;
            var group = ViewModel.TransactionGroups.FirstOrDefault(candidate =>
                candidate.Entries.Any(transaction => transaction.Id == ViewModel.SelectedTransaction.Id));
            if (group is not null)
            {
                group.IsExpanded = true;
                TransactionGroupsList.ScrollIntoView(group, ScrollIntoViewAlignment.Leading);
            }
            DispatcherQueue.TryEnqueue(RestoreTransactionSelection);
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ViewModel.RefreshAsync();
    private async void OnApplyFilterClick(object sender, RoutedEventArgs e) => await ViewModel.RefreshAsync();
    private async void OnResetFilterClick(object sender, RoutedEventArgs e) => await ViewModel.ResetFiltersAsync();

    private async void OnFilterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await ViewModel.RefreshAsync();
        }
    }

    private async void OnNewClick(object sender, RoutedEventArgs e) => await ShowEditorAsync(null);

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTransaction is not null)
            await ShowEditorAsync(ViewModel.SelectedTransaction);
    }

    private async void OnListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedTransaction is not null)
            await ShowEditorAsync(ViewModel.SelectedTransaction);
    }

    private void OnTransactionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView list || list.SelectedItem is not TransactionRecord transaction)
            return;

        if (_activeTransactionList is not null && _activeTransactionList != list)
            _activeTransactionList.SelectedItem = null;
        _activeTransactionList = list;
        _selectionToRestoreId = transaction.Id;
        ViewModel.SelectedTransaction = transaction;
    }

    private void OnTransactionListLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListView list)
            return;
        RestoreTransactionSelection(list);
    }

    private void RestoreTransactionSelection()
    {
        foreach (var list in DescendantTransactionLists(TransactionGroupsList))
        {
            if (RestoreTransactionSelection(list))
                return;
        }
    }

    private bool RestoreTransactionSelection(ListView list)
    {
        var selectedId = _selectionToRestoreId ?? ViewModel.SelectedTransaction?.Id;
        if (!selectedId.HasValue || list.ItemsSource is not IEnumerable<TransactionRecord> rows)
            return false;

        var transaction = rows.FirstOrDefault(candidate => candidate.Id == selectedId.Value);
        if (transaction is null)
            return false;

        if (_activeTransactionList is not null && _activeTransactionList != list)
            _activeTransactionList.SelectedItem = null;
        _activeTransactionList = list;
        list.SelectedItem = transaction;
        list.ScrollIntoView(transaction, ScrollIntoViewAlignment.Leading);
        list.Focus(FocusState.Programmatic);
        return true;
    }

    private static IEnumerable<ListView> DescendantTransactionLists(DependencyObject root)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ListView list)
                yield return list;
            foreach (var descendant in DescendantTransactionLists(child))
                yield return descendant;
        }
    }

    private async Task ShowEditorAsync(TransactionRecord? record)
    {
        try
        {
            var dialog = new TransactionEditorDialog(_repository, record) { XamlRoot = XamlRoot };
            await dialog.InitializeAsync();
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.ReportStatus(record is null ? "Buchung wurde erstellt." : $"Buchung #{record.Id} wurde aktualisiert.");
                await ViewModel.RefreshAsync();
            }
        }
        catch (Exception exception)
        {
            ViewModel.ReportError(exception);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.SelectedTransaction;
        if (selected is null) return;

        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Transaktion #{selected.Id} löschen?",
            Content = $"{selected.DatumAnzeige} · {selected.BetragAnzeige}\n\nDieser Vorgang kann nicht rückgängig gemacht werden.",
            PrimaryButtonText = "Löschen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await _repository.DeleteAsync(selected.Id);
            ViewModel.ReportStatus($"Transaktion #{selected.Id} wurde gelöscht.");
            await ViewModel.RefreshAsync();
        }
        catch (Exception exception)
        {
            ViewModel.ReportError(exception);
        }
    }

    private async Task ShowDocumentManagerAsync(TransactionRecord transaction)
    {
        try
        {
            var dialog = new AttachmentsDialog(transaction.Id, _toolsRepository);
            await dialog.InitializeAsync();
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _attachmentWindows.Add(dialog);
            dialog.Closed += (_, _) => { _attachmentWindows.Remove(dialog); closed.TrySetResult(); };
            dialog.Activate();
            await closed.Task;
            if (dialog.Changed) await ViewModel.RefreshAsync();
        }
        catch (Exception exception) { ViewModel.ReportError(exception); }
    }

    private async Task OpenDocumentsAsync(TransactionRecord transaction)
    {
        try
        {
            var documents = await _toolsRepository.GetAttachmentsAsync(transaction.Id);
            if (documents.Count == 0) { ViewModel.ReportStatus("Diese Transaktion hat noch kein Dokument."); return; }
            if (documents.Count == 1) { await _toolsRepository.OpenAttachmentAsync(documents[0]); return; }
            var dialog = new AttachmentSelectionDialog(documents, _toolsRepository) { XamlRoot = XamlRoot };
            await dialog.ShowAsync();
        }
        catch (Exception exception) { ViewModel.ReportError(exception); }
    }

    private async void OnAttachDocumentRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TransactionRecord transaction) return;
        try
        {
            var freeDocuments = await _toolsRepository.GetUnlinkedDocumentsAsync(null);
            if (freeDocuments.Count > 0)
            {
                var linkDialog = new DocumentLinkDialog(_toolsRepository) { XamlRoot = XamlRoot };
                await linkDialog.InitializeAsync();
                var result = await linkDialog.ShowAsync();
                var chosenDocument = linkDialog.ConfirmedDocument ?? (result == ContentDialogResult.Primary ? linkDialog.SelectedDocument : null);
                if (chosenDocument is { } document)
                {
                    await _toolsRepository.LinkExistingDocumentAsync(document.Id, transaction.Id);
                    ViewModel.ReportStatus($"„{document.DisplayName}“ wurde mit Transaktion #{transaction.Id} verknüpft.");
                    await ViewModel.RefreshAsync();
                    return;
                }
                if (result != ContentDialogResult.Secondary) return;
            }
            var path = await FilePickerService.PickOpenAsync(".pdf", ".jpg", ".jpeg", ".png");
            if (path is null) return;
            await _toolsRepository.AttachAsync(transaction.Id, path);
            ViewModel.ReportStatus($"Dokument wurde mit Transaktion #{transaction.Id} verknüpft.");
            await ViewModel.RefreshAsync();
        }
        catch (Exception exception) { ViewModel.ReportError(exception); }
    }

    private async void OnOpenDocumentRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TransactionRecord transaction) await OpenDocumentsAsync(transaction);
    }

    private async void OnManageDocumentsRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TransactionRecord transaction) await ShowDocumentManagerAsync(transaction);
    }

    private void OnWebResearchClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTransaction is not { } selected) return;
        var query = string.Join(" ", new[] { selected.Notiz, selected.AdresseName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(query)) { ViewModel.ReportStatus("Für diese Transaktion ist kein recherchierbarer Text vorhanden."); return; }
        try { Process.Start(new ProcessStartInfo($"https://www.google.com/search?q={Uri.EscapeDataString(query)}") { UseShellExecute = true }); }
        catch (Exception exception) { ViewModel.ReportError(exception); }
    }

    private async void OnDuplicatesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new DuplicatesDialog(_toolsRepository, _repository) { XamlRoot = XamlRoot };
            await dialog.InitializeAsync(); await dialog.ShowAsync(); if (dialog.Changed) await ViewModel.RefreshAsync();
        }
        catch (Exception exception) { ViewModel.ReportError(exception); }
    }

    private void OnBankImportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_bankImportWindow is not null) { _bankImportWindow.Activate(); return; }
            var window = new BankImportWindow();
            _bankImportWindow = window;
            window.Closed += async (_, _) =>
            {
                _bankImportWindow = null;
                if (window.Changed && !_isUnloading) await ViewModel.RefreshAsync();
            };
            window.Activate();
        }
        catch (Exception exception) { ViewModel.ReportError(exception); }
    }

    private void OnCreditCardImportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_creditCardImportWindow is not null) { _creditCardImportWindow.Activate(); return; }
            var window = new CreditCardImportWindow();
            _creditCardImportWindow = window;
            window.Closed += async (_, _) =>
            {
                _creditCardImportWindow = null;
                if (window.Changed && !_isUnloading) await ViewModel.RefreshAsync();
            };
            window.Activate();
        }
        catch (Exception exception) { ViewModel.ReportError(exception); }
    }

    private void OnReportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_reportWindow is not null) { _reportWindow.Activate(); return; }
            var window = new TransactionReportWindow();
            _reportWindow = window;
            window.Closed += (_, _) => _reportWindow = null;
            window.Activate();
        }
        catch (Exception exception) { ViewModel.ReportError(exception); }
    }

    private void OnErrorClosed(InfoBar sender, InfoBarClosedEventArgs args) => ViewModel.ClearError();
}
