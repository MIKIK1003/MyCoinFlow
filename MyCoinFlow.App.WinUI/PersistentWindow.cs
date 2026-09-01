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

    protected PersistentWindow()
    {
        Activated += OnFirstActivated;
        Closed += OnClosed;
        AppWindow.Changed += OnAppWindowChanged;
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
}
