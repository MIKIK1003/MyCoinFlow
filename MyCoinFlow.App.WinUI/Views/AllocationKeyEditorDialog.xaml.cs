using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class AllocationKeyEditorDialog : ContentDialog
{
    private readonly int _propertyId;
    private readonly StweSchluessel? _source;
    public AllocationKeyEditorDialog(int propertyId, StweSchluessel? source = null)
    {
        InitializeComponent();
        _propertyId = propertyId;
        _source = source;
        HeadingText.Text = source is null ? "Neuer Schlüssel" : $"{source.Name} bearbeiten";
        NameBox.Text = source?.Name ?? string.Empty;
        ModeBox.SelectedValue = source?.Modus ?? "FIX";
    }
    public StweSchluessel? Result { get; private set; }
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            args.Cancel = true; EditorError.Message = "Bitte einen Namen erfassen."; EditorError.IsOpen = true; return;
        }
        var mode = (ModeBox.SelectedValue as string ?? "FIX").Trim().ToUpperInvariant();
        if (mode is not ("FIX" or "MEA" or "ENERGIE")) mode = "FIX";
        Result = new StweSchluessel
        {
            Id = _source?.Id ?? 0,
            LiegenschaftId = _propertyId,
            Name = NameBox.Text.Trim(),
            Modus = mode
        };
    }
}
