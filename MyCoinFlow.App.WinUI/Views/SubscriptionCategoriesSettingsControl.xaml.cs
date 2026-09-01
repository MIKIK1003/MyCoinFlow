using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class SubscriptionCategoriesSettingsControl : UserControl
{
    private sealed record ColorOption(string Name, string Hex);
    private sealed class CategoryRow
    {
        public CategoryRow(AboKategorie value) => Value = value;
        public AboKategorie Value { get; }
        public SolidColorBrush ColorBrush => Brush(Value.FarbeHex);
        public string Usage => Value.AnzahlSerien.ToString("N0");
        public string Type => Value.IstSystem ? "Vordefiniert" : "Eigene Kategorie";
        public string State => Value.IstAktiv ? $"Reihenfolge {Value.Sortierung}" : "Nicht aktiv";
    }

    private readonly DatabaseService _database = new();
    private AboKategorie? _selected;
    private readonly List<ColorOption> _colors = new()
    {
        new("Tannengrün", "#2F7D6D"), new("Blau", "#3867A8"), new("Petrol", "#1B8396"),
        new("Violett", "#684CB9"), new("Pflaume", "#8C5AB5"), new("Terrakotta", "#A45A44"),
        new("Bernstein", "#B06C1F"), new("Braun", "#8A6540"), new("Grün", "#3C7C54"),
        new("Schiefer", "#536274"), new("Rauchviolett", "#70628F"), new("Türkis", "#167E91")
    };

    public SubscriptionCategoriesSettingsControl()
    {
        InitializeComponent();
        ColorBox.ItemsSource = _colors;
        try { _database.EnsureAboSchema(); Reload(); }
        catch (Exception ex) { Show(ex.Message, InfoBarSeverity.Error); }
    }

    private void Reload(int? id = null)
    {
        var rows = _database.AboKategorienLaden(true).Select(value => new CategoryRow(value)).ToList();
        CategoriesList.ItemsSource = rows;
        CountText.Text = rows.Count == 1 ? "1 Kategorie" : $"{rows.Count} Kategorien";
        CategoriesList.SelectedItem = id.HasValue ? rows.FirstOrDefault(value => value.Value.Id == id.Value) : null;
    }

    private void OnReloadClick(object sender, RoutedEventArgs e) => Reload(_selected?.Id);
    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        CategoriesList.SelectedItem = null;
        _selected = new AboKategorie { Sortierung = 100, FarbeHex = "#5B2DA9", IstAktiv = true };
        FillEditor();
        NameBox.Focus(FocusState.Programmatic);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = (CategoriesList.SelectedItem as CategoryRow)?.Value;
        if (_selected is not null) FillEditor();
    }

    private void FillEditor()
    {
        if (_selected is null) return;
        NameBox.Text = _selected.Bezeichnung;
        DescriptionBox.Text = _selected.Beschreibung;
        ColorBox.SelectedValue = _selected.FarbeHex;
        if (ColorBox.SelectedIndex < 0) ColorBox.SelectedIndex = 0;
        SortBox.Value = _selected.Sortierung;
        ActiveBox.IsOn = _selected.IstAktiv;
        TypeHintText.Text = _selected.IstSystem
            ? "Vordefinierte Kategorie: Inhalt, Farbe und Reihenfolge sind anpassbar; der technische Schlüssel bleibt erhalten."
            : "Eigene Kategorie: Sie kann frei bearbeitet und bei Bedarf wieder gelöscht werden.";
        UpdateColorPreview();
    }

    private void OnColorChanged(object sender, SelectionChangedEventArgs e) => UpdateColorPreview();
    private void UpdateColorPreview() { if (ColorBox.SelectedValue is string hex) ColorPreview.Background = Brush(hex); }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _selected ??= new AboKategorie();
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { Show("Bitte eine Bezeichnung erfassen.", InfoBarSeverity.Warning); return; }
        _selected.Bezeichnung = NameBox.Text.Trim();
        _selected.Beschreibung = DescriptionBox.Text?.Trim() ?? string.Empty;
        _selected.FarbeHex = ColorBox.SelectedValue as string ?? "#5B2DA9";
        _selected.Sortierung = double.IsFinite(SortBox.Value) ? Math.Max(0, (int)SortBox.Value) : 100;
        _selected.IstAktiv = ActiveBox.IsOn;
        if (_selected.Id == 0) _selected.Id = _database.AboKategorieInsert(_selected); else _database.AboKategorieUpdate(_selected);
        Reload(_selected.Id);
        Show("Kategorie gespeichert.", InfoBarSeverity.Success);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.Id == 0) return;
        if (_selected.IstSystem) { Show("Vordefinierte Kategorien können deaktiviert und umbenannt, aber nicht gelöscht werden.", InfoBarSeverity.Informational); return; }
        var content = _selected.AnzahlSerien == 0 ? "Die Kategorie wird dauerhaft gelöscht." : $"{_selected.AnzahlSerien} Zahlungsserie(n) werden dabei auf ‚Sonstige Serien‘ umgestellt.";
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = $"„{_selected.Bezeichnung}“ löschen?", Content = content, PrimaryButtonText = "Löschen", CloseButtonText = "Abbrechen", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _database.AboKategorieDelete(_selected.Id);
        _selected = null;
        Reload();
        Show("Kategorie gelöscht.", InfoBarSeverity.Success);
    }

    private void Show(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
    private static SolidColorBrush Brush(string hex)
    {
        var value = string.IsNullOrWhiteSpace(hex) ? "5B2DA9" : hex.Trim().TrimStart('#');
        if (value.Length != 6 || !byte.TryParse(value[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r) || !byte.TryParse(value[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) || !byte.TryParse(value[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
            return new SolidColorBrush(ColorHelper.FromArgb(255, 91, 45, 169));
        return new SolidColorBrush(ColorHelper.FromArgb(255, r, g, b));
    }
}
