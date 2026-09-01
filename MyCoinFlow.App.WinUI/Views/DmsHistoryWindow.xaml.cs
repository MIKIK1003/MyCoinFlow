using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using MyCoinFlow.Services;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class DmsHistoryWindow : PersistentWindow
{
    private static DmsHistoryWindow? _open;
    private readonly DatabaseService _database = new();

    private DmsHistoryWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1050, 700));
        DmsWatcherService.Instance.DocumentProcessed += OnDocumentProcessed;
        Closed += (_, _) => { DmsWatcherService.Instance.DocumentProcessed -= OnDocumentProcessed; _open = null; };
        Load();
    }

    public static void ShowOrActivate()
    {
        if (_open is not null) { _open.Activate(); return; }
        _open = new DmsHistoryWindow();
        _open.Activate();
    }

    private void OnDocumentProcessed(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Load);
    private void Load() => HistoryList.ItemsSource = _database.LoadDmsProcessingLog();
    private void OnRefreshClick(object sender, RoutedEventArgs e) => Load();
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
