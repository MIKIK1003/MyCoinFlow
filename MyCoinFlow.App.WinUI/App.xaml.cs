using Microsoft.UI.Xaml;

namespace MyCoinFlow.WinUI;

public partial class App : Application
{
    private Window? _window;
    public MainWindow MainWindow => (MainWindow)(_window ?? throw new InvalidOperationException("Das Hauptfenster ist noch nicht initialisiert."));

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
