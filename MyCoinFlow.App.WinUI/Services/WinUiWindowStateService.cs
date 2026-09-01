using Microsoft.UI.Windowing;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Services;

internal static class WinUiWindowStateService
{
    private static readonly object SyncRoot = new();
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyCoinFlow");
    private static readonly string FilePath = Path.Combine(Folder, "windowstate.winui.json");

    internal sealed class WindowPlacement
    {
        public int Top { get; set; }
        public int Left { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsMaximized { get; set; }

        [JsonIgnore]
        public RectInt32 Bounds => new(Left, Top, Width, Height);
    }

    public static WindowPlacement? Restore(AppWindow appWindow, string key, bool canResize)
    {
        try
        {
            WindowPlacement? placement;
            lock (SyncRoot)
            {
                var all = LoadAll();
                if (!all.TryGetValue(key, out placement) || !IsValidSize(placement.Bounds))
                    return null;
            }

            var currentSize = appWindow.Size;
            var requested = new RectInt32(
                placement.Left,
                placement.Top,
                canResize ? placement.Width : currentSize.Width,
                canResize ? placement.Height : currentSize.Height);
            var safeBounds = EnsureWindowVisible(requested);

            if (appWindow.Presenter is OverlappedPresenter presenter &&
                presenter.State != OverlappedPresenterState.Restored)
            {
                presenter.Restore();
            }

            appWindow.MoveAndResize(safeBounds);

            if (placement.IsMaximized &&
                appWindow.Presenter is OverlappedPresenter maximizablePresenter &&
                maximizablePresenter.IsMaximizable)
            {
                maximizablePresenter.Maximize();
            }

            return new WindowPlacement
            {
                Left = safeBounds.X,
                Top = safeBounds.Y,
                Width = safeBounds.Width,
                Height = safeBounds.Height,
                IsMaximized = placement.IsMaximized
            };
        }
        catch
        {
            // Die Fensterdarstellung darf wegen einer defekten Zustandsdatei nie scheitern.
            return null;
        }
    }

    public static void Save(string key, RectInt32 normalBounds, bool isMaximized)
    {
        try
        {
            if (!IsValidSize(normalBounds))
                return;

            lock (SyncRoot)
            {
                var all = LoadAll();
                all[key] = new WindowPlacement
                {
                    Left = normalBounds.X,
                    Top = normalBounds.Y,
                    Width = normalBounds.Width,
                    Height = normalBounds.Height,
                    IsMaximized = isMaximized
                };
                SaveAll(all);
            }
        }
        catch
        {
            // Die UI darf durch das Speichern der Fensterposition nie blockiert werden.
        }
    }

    private static Dictionary<string, WindowPlacement> LoadAll()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new Dictionary<string, WindowPlacement>(StringComparer.Ordinal);

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Dictionary<string, WindowPlacement>>(json)
                   ?? new Dictionary<string, WindowPlacement>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, WindowPlacement>(StringComparer.Ordinal);
        }
    }

    private static void SaveAll(Dictionary<string, WindowPlacement> placements)
    {
        Directory.CreateDirectory(Folder);
        var json = JsonSerializer.Serialize(placements, new JsonSerializerOptions { WriteIndented = true });
        var temporaryFile = FilePath + ".tmp";
        File.WriteAllText(temporaryFile, json);
        File.Move(temporaryFile, FilePath, true);
    }

    private static RectInt32 EnsureWindowVisible(RectInt32 bounds)
    {
        if (DisplayArea.GetFromRect(bounds, DisplayAreaFallback.None) is not null)
            return bounds;

        var workArea = DisplayArea.Primary.WorkArea;
        var width = Math.Min(bounds.Width, workArea.Width);
        var height = Math.Min(bounds.Height, workArea.Height);
        var left = workArea.X + (workArea.Width - width) / 2;
        var top = workArea.Y + (workArea.Height - height) / 2;
        return new RectInt32(left, top, width, height);
    }

    private static bool IsValidSize(RectInt32 bounds) =>
        bounds.Width >= 300 &&
        bounds.Height >= 200 &&
        bounds.Width <= 4000 &&
        bounds.Height <= 3000;
}
