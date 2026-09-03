using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using MyCoinFlow.WinUI.Services;
using Windows.Graphics;

namespace MyCoinFlow.WinUI;

/// <summary>
/// Gemeinsame Basis aller echten WinUI-Fenster. Sie stellt Position, normale Größe und
/// Maximierungszustand wieder her und hält Fenster bei geänderter Monitoranordnung sichtbar.
/// </summary>
public abstract class PersistentWindow : Window
{
    private bool _wasRestored;
    private RectInt32 _normalBounds;
    private bool _isMaximized;
    private FrameworkElement? _dpiAwareRoot;
    private XamlRoot? _dpiAwareXamlRoot;
    private int _minimumWidthInViewPixels;
    private int _minimumHeightInViewPixels;

    protected PersistentWindow()
    {
        Activated += OnFirstActivated;
        Closed += OnClosed;
        AppWindow.Changed += OnAppWindowChanged;
    }

    /// <summary>
    /// Configures a window in XAML view pixels (DIPs), while AppWindow itself works in
    /// physical pixels. Restored sizes are clamped after activation so that a size saved
    /// at another display scale cannot make the window unusably small.
    /// </summary>
    protected void ConfigureDpiAwareSizing(
        FrameworkElement layoutRoot,
        int defaultWidthInViewPixels,
        int defaultHeightInViewPixels,
        int minimumWidthInViewPixels,
        int minimumHeightInViewPixels)
    {
        ArgumentNullException.ThrowIfNull(layoutRoot);
        _dpiAwareRoot = layoutRoot;
        _minimumWidthInViewPixels = minimumWidthInViewPixels;
        _minimumHeightInViewPixels = minimumHeightInViewPixels;

        var scale = GetRasterizationScale();
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new SizeInt32(
            Math.Min(ToPhysicalPixels(defaultWidthInViewPixels, scale), workArea.Width),
            Math.Min(ToPhysicalPixels(defaultHeightInViewPixels, scale), workArea.Height)));
        ApplyDpiAwareMinimumSize();

        layoutRoot.Loaded += OnDpiAwareRootLoaded;
        Activated += OnDpiAwareActivated;
        Closed += OnDpiAwareClosed;
    }

    private void OnDpiAwareRootLoaded(object sender, RoutedEventArgs args)
    {
        if (_dpiAwareRoot?.XamlRoot is { } xamlRoot && !ReferenceEquals(_dpiAwareXamlRoot, xamlRoot))
        {
            if (_dpiAwareXamlRoot is not null)
                _dpiAwareXamlRoot.Changed -= OnDpiAwareXamlRootChanged;
            _dpiAwareXamlRoot = xamlRoot;
            _dpiAwareXamlRoot.Changed += OnDpiAwareXamlRootChanged;
        }
        ApplyDpiAwareMinimumSize();
    }

    private void OnDpiAwareActivated(object sender, WindowActivatedEventArgs args) =>
        ApplyDpiAwareMinimumSize();

    private void OnDpiAwareXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) =>
        ApplyDpiAwareMinimumSize();

    private void ApplyDpiAwareMinimumSize()
    {
        if (_minimumWidthInViewPixels <= 0 || _minimumHeightInViewPixels <= 0)
            return;

        var scale = GetRasterizationScale();
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var minimumWidth = Math.Min(ToPhysicalPixels(_minimumWidthInViewPixels, scale), workArea.Width);
        var minimumHeight = Math.Min(ToPhysicalPixels(_minimumHeightInViewPixels, scale), workArea.Height);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = minimumWidth;
            presenter.PreferredMinimumHeight = minimumHeight;
            if (presenter.State != OverlappedPresenterState.Restored)
                return;
        }

        var current = AppWindow.Size;
        if (current.Width < minimumWidth || current.Height < minimumHeight)
        {
            AppWindow.Resize(new SizeInt32(
                Math.Max(current.Width, minimumWidth),
                Math.Max(current.Height, minimumHeight)));
        }
    }

    private double GetRasterizationScale()
    {
        if (_dpiAwareRoot?.XamlRoot?.RasterizationScale is > 0 and var scale)
            return scale;

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = NativeMethods.GetDpiForWindow(windowHandle);
        return dpi > 0 ? dpi / 96d : 1d;
    }

    private static int ToPhysicalPixels(int viewPixels, double scale) =>
        Math.Max(1, (int)Math.Ceiling(viewPixels * scale));

    private void OnDpiAwareClosed(object sender, WindowEventArgs args)
    {
        if (_dpiAwareRoot is not null)
            _dpiAwareRoot.Loaded -= OnDpiAwareRootLoaded;
        if (_dpiAwareXamlRoot is not null)
            _dpiAwareXamlRoot.Changed -= OnDpiAwareXamlRootChanged;
        Activated -= OnDpiAwareActivated;
        Closed -= OnDpiAwareClosed;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_wasRestored)
            return;

        _wasRestored = true;
        var restored = WinUiWindowStateService.Restore(AppWindow, GetType().Name, CanResize());
        if (restored is not null)
        {
            _normalBounds = restored.Bounds;
            _isMaximized = restored.IsMaximized;
        }
        else
        {
            CaptureNormalBounds();
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        try
        {
            if (sender.Presenter is not OverlappedPresenter presenter)
                return;

            _isMaximized = presenter.State == OverlappedPresenterState.Maximized;
            if (presenter.State == OverlappedPresenterState.Restored &&
                (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange))
            {
                CaptureNormalBounds();
            }
        }
        catch
        {
            // Fensterbewegungen dürfen niemals UI-Fehler auslösen.
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        try
        {
            if (!_wasRestored || !IsValid(_normalBounds))
                CaptureNormalBounds();

            WinUiWindowStateService.Save(GetType().Name, _normalBounds, _isMaximized);
        }
        finally
        {
            Activated -= OnFirstActivated;
            Closed -= OnClosed;
            AppWindow.Changed -= OnAppWindowChanged;
        }
    }

    private void CaptureNormalBounds()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State != OverlappedPresenterState.Restored)
        {
            return;
        }

        var position = AppWindow.Position;
        var size = AppWindow.Size;
        var bounds = new RectInt32(position.X, position.Y, size.Width, size.Height);
        if (IsValid(bounds))
            _normalBounds = bounds;
    }

    private bool CanResize() =>
        AppWindow.Presenter is not OverlappedPresenter presenter || presenter.IsResizable;

    private static bool IsValid(RectInt32 bounds) => bounds.Width >= 300 && bounds.Height >= 200;

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr windowHandle);
    }
}
