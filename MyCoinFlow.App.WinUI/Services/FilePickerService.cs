using Windows.Storage.Pickers;

namespace MyCoinFlow.WinUI.Services;

public static class FilePickerService
{
    public static async Task<string?> PickOpenAsync(params string[] extensions)
        => await PickOpenAsync(((App)Microsoft.UI.Xaml.Application.Current).MainWindow, extensions);

    public static async Task<string?> PickOpenAsync(Microsoft.UI.Xaml.Window owner, params string[] extensions)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, ViewMode = PickerViewMode.List };
        foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public static async Task<string?> PickSaveCsvAsync(string suggestedName)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add("CSV-Datei", new List<string> { ".csv" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(((App)Microsoft.UI.Xaml.Application.Current).MainWindow));
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public static async Task<string?> PickSaveAsync(string suggestedName, string description, string extension)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add(description, new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(((App)Microsoft.UI.Xaml.Application.Current).MainWindow));
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(((App)Microsoft.UI.Xaml.Application.Current).MainWindow));
        return (await picker.PickSingleFolderAsync())?.Path;
    }
}
