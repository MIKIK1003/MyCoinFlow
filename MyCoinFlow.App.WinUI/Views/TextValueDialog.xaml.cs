using Microsoft.UI.Xaml.Controls;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class TextValueDialog : ContentDialog
{
    private readonly bool _required;
    public TextValueDialog(string title, string heading, string fieldLabel, string? value = null, bool required = true)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = heading;
        ValueBox.Header = fieldLabel;
        ValueBox.Text = value ?? string.Empty;
        _required = required;
    }
    public string Value { get; private set; } = string.Empty;
    public bool Accepted { get; private set; }
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var value = ValueBox.Text?.Trim() ?? string.Empty;
        if (_required && string.IsNullOrWhiteSpace(value))
        {
            args.Cancel = true;
            EditorError.Message = "Bitte einen Wert eingeben.";
            EditorError.IsOpen = true;
            return;
        }
        Value = value;
        Accepted = true;
    }
}
