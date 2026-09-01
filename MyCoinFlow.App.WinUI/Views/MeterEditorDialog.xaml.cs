using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using System.Collections.ObjectModel;
using System.Globalization;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class MeterEditorDialog : ContentDialog
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private readonly DatabaseService _database = new();
    private readonly int _propertyId;
    private readonly StweZaehler? _source;
    private bool _ready;

    public MeterEditorDialog(int propertyId, IReadOnlyList<StweEinheit> units,
        IReadOnlyList<StweEigentuemer> owners, StweZaehler? source,
        IReadOnlyList<StweZaehlerLine> existingLines)
    {
        InitializeComponent();
        _propertyId = propertyId;
        _source = source;
        Units = units;
        Owners = owners.OrderBy(value => value.Name).ToList();
        UnitBox.ItemsSource = Units;
        RowsList.DataContext = this;
        foreach (var line in existingLines) Rows.Add(AllocationLineEditorRow.From(line));
        RowsList.ItemsSource = Rows;
        HeadingText.Text = source is null ? "Zähler neu" : "Zähler bearbeiten";
        NameBox.Text = source?.Name ?? string.Empty;
        NoteBox.Text = source?.Notiz ?? string.Empty;
        TypeBox.SelectedItem = string.IsNullOrWhiteSpace(source?.Typ) ? "DIREKT" : source.Typ.Trim().ToUpperInvariant();
        UnitBox.SelectedValue = source?.EinheitId;
        _ready = true;
        ApplyTypeState();
        EnsureDirectAutoLinesIfPossible();
        RefreshLinesInfo();
    }

    public IReadOnlyList<StweEinheit> Units { get; }
    public IReadOnlyList<StweEigentuemer> Owners { get; }
    public ObservableCollection<AllocationLineEditorRow> Rows { get; } = new();
    public StweZaehler? Result { get; private set; }
    public List<(int? EinheitId, int EigentuemerId, decimal AnteilProzent)> ResultLines { get; private set; } = new();

    private string SelectedType => TypeBox.SelectedItem as string ?? "DIREKT";

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        ApplyTypeState();
        EnsureDirectAutoLinesIfPossible();
    }
    private void OnUnitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) EnsureDirectAutoLinesIfPossible();
    }
    private void ApplyTypeState()
    {
        var direct = SelectedType.Equals("DIREKT", StringComparison.OrdinalIgnoreCase);
        UnitBox.IsEnabled = direct;
        UnitBox.Opacity = direct ? 1d : 0.5d;
        if (!direct) UnitBox.SelectedItem = null;
    }
    private void EnsureDirectAutoLinesIfPossible()
    {
        if (!SelectedType.Equals("DIREKT", StringComparison.OrdinalIgnoreCase) || UnitBox.SelectedValue is not int unitId) return;
        int? ownerId;
        try { ownerId = _database.StweEigentuemerGetByEinheitAtDate(unitId, DateTime.Today); }
        catch { ownerId = null; }
        if (!ownerId.HasValue || ownerId.Value <= 0) return;
        Rows.Clear();
        Rows.Add(new AllocationLineEditorRow
        {
            UnitId = unitId,
            OwnerId = ownerId.Value,
            PercentageText = 100m.ToString("N4", SwissCulture)
        });
        RefreshLinesInfo();
    }
    private void OnAddRowClick(object sender, RoutedEventArgs e)
    {
        Rows.Add(new AllocationLineEditorRow()); RefreshLinesInfo();
    }
    private void OnDeleteRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AllocationLineEditorRow row) Rows.Remove(row);
        RefreshLinesInfo();
    }
    private void OnPercentageLostFocus(object sender, RoutedEventArgs e) => RefreshLinesInfo();
    private void RefreshLinesInfo()
    {
        var sum = Rows.Sum(row => TryParse(row.PercentageText, out var value) ? value : 0m);
        LinesInfoText.Text = Rows.Count == 0 ? "keine Zeilen" : $"{Rows.Count} Zeile(n), Summe {sum:N4}%";
    }
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = NameBox.Text?.Trim() ?? string.Empty;
        var type = SelectedType.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name)) { args.Cancel = true; ShowError("Bitte einen Namen erfassen."); return; }
        if (type is not ("DIREKT" or "ALLG" or "HEIZ" or "EVU")) { args.Cancel = true; ShowError("Bitte einen gültigen Typ wählen (DIREKT/ALLG/HEIZ/EVU)."); return; }
        int? unitId = type == "DIREKT" && UnitBox.SelectedValue is int selectedUnit ? selectedUnit : null;
        if (type == "DIREKT" && !unitId.HasValue) { args.Cancel = true; ShowError("Bei Typ DIREKT muss eine Einheit gewählt werden."); return; }
        EnsureDirectAutoLinesIfPossible();
        var resultLines = new List<(int? EinheitId, int EigentuemerId, decimal AnteilProzent)>();
        foreach (var row in Rows)
        {
            if (row.OwnerId <= 0 || !TryParse(row.PercentageText, out var percentage))
            { args.Cancel = true; ShowError("Bitte in allen Verteilzeilen Eigentümer und gültigen Anteil erfassen."); return; }
            resultLines.Add((row.UnitId, row.OwnerId, percentage));
        }
        if (type != "EVU")
        {
            if (resultLines.Count == 0) { args.Cancel = true; ShowError("Bitte Verteilzeilen erfassen (Summe 100%)."); return; }
            var sum = resultLines.Sum(value => value.AnteilProzent);
            if (Math.Abs((double)(sum - 100m)) > 0.0001) { args.Cancel = true; ShowError($"Summe der Verteilzeilen muss 100.0000% ergeben. Aktuell: {sum:N4}%."); return; }
            if (resultLines.GroupBy(value => value.EigentuemerId).Any(group => group.Count() > 1))
            { args.Cancel = true; ShowError("Ein Eigentümer darf im Zähler nur einmal vorkommen."); return; }
        }
        Result = new StweZaehler
        {
            Id = _source?.Id ?? 0,
            LiegenschaftId = _propertyId,
            Name = name,
            Typ = type,
            EinheitId = unitId,
            SchluesselId = _source?.SchluesselId,
            Notiz = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim()
        };
        ResultLines = resultLines;
    }
    private static bool TryParse(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, SwissCulture, out value) ||
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
    private void ShowError(string message) { EditorError.Message = message; EditorError.IsOpen = true; }
}
