using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using System.Globalization;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class PropertyUnitEditorDialog : ContentDialog
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private readonly int _propertyId;
    private readonly StweEinheit? _source;
    public PropertyUnitEditorDialog(int propertyId, StweEinheit? source = null)
    {
        InitializeComponent();
        _propertyId = propertyId;
        _source = source;
        HeadingText.Text = source is null ? "Neue Einheit" : $"{source.Bezeichnung} bearbeiten";
        if (source is null) return;
        NameBox.Text = source.Bezeichnung;
        TypeBox.Text = source.Typ ?? string.Empty;
        MeaBox.Text = source.MeaPromille?.ToString("N2", SwissCulture) ?? string.Empty;
        AreaBox.Text = source.FlaecheM2?.ToString("N2", SwissCulture) ?? string.Empty;
        NoteBox.Text = source.Notiz ?? string.Empty;
    }
    public StweEinheit? Result { get; private set; }
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            args.Cancel = true;
            ShowError("Bitte eine Bezeichnung eingeben.");
            return;
        }
        if (!TryParseNullable(MeaBox.Text, out var mea) || !TryParseNullable(AreaBox.Text, out var area))
        {
            args.Cancel = true;
            ShowError("MEA und Fläche müssen gültige Zahlen oder leer sein.");
            return;
        }
        Result = new StweEinheit
        {
            Id = _source?.Id ?? 0,
            LiegenschaftId = _propertyId,
            Bezeichnung = NameBox.Text.Trim(),
            Typ = Normalize(TypeBox.Text),
            MeaPromille = mea,
            FlaecheM2 = area,
            Notiz = Normalize(NoteBox.Text)
        };
    }
    private void ShowError(string message) { EditorError.Message = message; EditorError.IsOpen = true; }
    private static bool TryParseNullable(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (decimal.TryParse(text, NumberStyles.Number, SwissCulture, out var parsed) ||
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed))
        { value = parsed; return true; }
        return false;
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
