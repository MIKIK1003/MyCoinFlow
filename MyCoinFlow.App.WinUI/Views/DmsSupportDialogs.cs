using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.WinUI.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed class DmsNewVersionDialog : ContentDialog
{
    private readonly TextBox _path = new() { Header = "Datei", IsReadOnly = true };
    private readonly TextBox _comment = new() { Header = "Versionsnotiz", AcceptsReturn = true, MinHeight = 80 };

    public DmsNewVersionDialog()
    {
        Title = "Neue Dokumentversion";
        PrimaryButtonText = "Speichern";
        CloseButtonText = "Abbrechen";
        DefaultButton = ContentDialogButton.Primary;
        var select = new Button { Content = "Datei wählen", HorizontalAlignment = HorizontalAlignment.Left };
        select.Click += OnSelectClick;
        Content = new StackPanel { Width = 620, Spacing = 10, Children = { _path, select, _comment } };
        PrimaryButtonClick += (_, args) => { if (string.IsNullOrWhiteSpace(SelectedFilePath)) args.Cancel = true; };
    }

    public string? SelectedFilePath { get; private set; }
    public string? Comment => string.IsNullOrWhiteSpace(_comment.Text) ? null : _comment.Text.Trim();
    private async void OnSelectClick(object sender, RoutedEventArgs e) { SelectedFilePath = await FilePickerService.PickOpenAsync(".pdf", ".jpg", ".jpeg", ".png"); _path.Text = SelectedFilePath ?? string.Empty; }
}
