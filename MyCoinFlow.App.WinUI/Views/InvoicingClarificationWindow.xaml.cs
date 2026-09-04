using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InvoicingClarificationWindow : PersistentWindow
{
    private readonly InvoicingPaymentRepository _repository = new();
    private bool _loaded;
    private bool _busy;

    public InvoicingClarificationWindow()
    {
        InitializeComponent();
        ConfigureDpiAwareSizing(RootGrid, 1040, 720, 720, 540);
        Activated += OnActivated;
    }

    public bool Changed { get; private set; }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        await ReloadAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var rows = await _repository.LoadOpenClarificationsAsync();
            CasesList.ItemsSource = rows;
            CasesList.SelectedItem = rows.FirstOrDefault();
            CasesList.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusBar.Message = rows.Count == 0
                ? "Alle Zahlungsfälle sind geklärt."
                : $"{rows.Count} offene(r) Zahlungsfall/-fälle. Eine Klärung verändert keine Rechnung oder Buchung.";
            StatusBar.Severity = rows.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        }
        catch (Exception exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ResolveButton.IsEnabled = !_busy && CasesList.SelectedItem is InvoicingClarificationRecord;

    private async void OnResolveClick(object sender, RoutedEventArgs e)
    {
        if (_busy || CasesList.SelectedItem is not InvoicingClarificationRecord row) return;
        SetBusy(true);
        try
        {
            await _repository.ResolveClarificationAsync(row.Id);
            Changed = true;
            SetBusy(false);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CasesList.IsEnabled = !busy;
        ResolveButton.IsEnabled = !busy && CasesList.SelectedItem is InvoicingClarificationRecord;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
