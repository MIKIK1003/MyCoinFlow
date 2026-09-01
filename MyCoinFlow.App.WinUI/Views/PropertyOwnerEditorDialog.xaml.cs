using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class PropertyOwnerEditorDialog : ContentDialog
{
    private readonly StweEigentuemer? _source;
    public PropertyOwnerEditorDialog(StweEigentuemer? source = null)
    {
        InitializeComponent();
        _source = source;
        HeadingText.Text = source is null ? "Neuer Eigentümer" : $"{source.Name} bearbeiten";
        if (source is null) return;
        NameBox.Text = source.Name;
        EmailBox.Text = source.Email ?? string.Empty;
        PhoneBox.Text = source.Telefon ?? string.Empty;
        NoteBox.Text = source.Notiz ?? string.Empty;
    }
    public StweEigentuemer? Result { get; private set; }
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            args.Cancel = true;
            EditorError.Message = "Bitte Name eingeben.";
            EditorError.IsOpen = true;
            return;
        }
        Result = new StweEigentuemer
        {
            Id = _source?.Id ?? 0,
            Name = NameBox.Text.Trim(),
            Email = Normalize(EmailBox.Text),
            Telefon = Normalize(PhoneBox.Text),
            Notiz = Normalize(NoteBox.Text)
        };
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
