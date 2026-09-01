using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class PropertyEditorDialog : ContentDialog
{
    private readonly StweLiegenschaft? _source;
    public PropertyEditorDialog(StweLiegenschaft? source = null)
    {
        InitializeComponent();
        _source = source;
        HeadingText.Text = source is null ? "Neue Liegenschaft" : $"{source.Name} bearbeiten";
        if (source is null) return;
        NameBox.Text = source.Name;
        StreetBox.Text = source.Strasse ?? string.Empty;
        PostalCodeBox.Text = source.PLZ ?? string.Empty;
        CityBox.Text = source.Ort ?? string.Empty;
        NoteBox.Text = source.Notiz ?? string.Empty;
    }
    public StweLiegenschaft? Result { get; private set; }
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            args.Cancel = true;
            EditorError.Message = "Bitte einen Namen eingeben.";
            EditorError.IsOpen = true;
            return;
        }
        Result = new StweLiegenschaft
        {
            Id = _source?.Id ?? 0,
            Name = NameBox.Text.Trim(),
            Strasse = Normalize(StreetBox.Text),
            PLZ = Normalize(PostalCodeBox.Text),
            Ort = Normalize(CityBox.Text),
            Notiz = Normalize(NoteBox.Text)
        };
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
