using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.WinUI.Models;
using System.Collections.ObjectModel;
using System.Globalization;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class AllocationKeyLinesDialog : ContentDialog
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    public AllocationKeyLinesDialog(string keyName, IReadOnlyList<StweEigentuemer> owners,
        IReadOnlyList<StweEinheit> units, IReadOnlyList<StweSchluesselLine> existing)
    {
        InitializeComponent();
        HeadingText.Text = $"Schlüssel: {keyName} (Fix %)";
        Owners = owners.OrderBy(value => value.Name).ToList();
        Units = units.OrderBy(value => value.Bezeichnung).ToList();
        foreach (var line in existing) Rows.Add(AllocationLineEditorRow.From(line));
        RowsList.DataContext = this;
        RowsList.ItemsSource = Rows;
        RefreshSum();
    }
    public IReadOnlyList<StweEigentuemer> Owners { get; }
    public IReadOnlyList<StweEinheit> Units { get; }
    public ObservableCollection<AllocationLineEditorRow> Rows { get; } = new();
    public List<(int? EinheitId, int EigentuemerId, decimal AnteilProzent)> ResultLines { get; private set; } = new();
    public bool Accepted { get; private set; }
    private void OnAddRowClick(object sender, RoutedEventArgs e)
    {
        Rows.Add(new AllocationLineEditorRow());
        RefreshSum();
    }
    private void OnDeleteRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AllocationLineEditorRow row) Rows.Remove(row);
        RefreshSum();
    }
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!TryBuildResult(out var result, out var error))
        {
            args.Cancel = true; ShowError(error); return;
        }
        ResultLines = result;
        Accepted = true;
    }
    private bool TryBuildResult(out List<(int? EinheitId, int EigentuemerId, decimal AnteilProzent)> result, out string error)
    {
        result = new(); error = string.Empty;
        if (Rows.Count == 0) { error = "Bitte mindestens eine Zeile erfassen."; return false; }
        foreach (var row in Rows)
        {
            if (row.OwnerId <= 0) { error = "Bitte in allen Zeilen einen Eigentümer/Fallback wählen."; return false; }
            if (!TryParse(row.PercentageText, out var percentage)) { error = "Bitte in allen Zeilen einen gültigen Anteil erfassen."; return false; }
            if (percentage < 0m) { error = "Anteile dürfen nicht negativ sein."; return false; }
            result.Add((row.UnitId, row.OwnerId, percentage));
        }
        var sum = result.Sum(value => value.AnteilProzent);
        if (Math.Abs((double)(sum - 100m)) > 0.0001) { error = $"Summe muss 100.0000% ergeben. Aktuell: {sum:N4}%"; return false; }
        if (result.Where(value => value.EinheitId.HasValue).GroupBy(value => value.EinheitId!.Value).Any(group => group.Count() > 1))
        { error = "Eine Einheit darf im Schlüssel nur einmal vorkommen."; return false; }
        if (result.Where(value => !value.EinheitId.HasValue).GroupBy(value => value.EigentuemerId).Any(group => group.Count() > 1))
        { error = "Ein Eigentümer ohne Einheit darf im Schlüssel nur einmal vorkommen."; return false; }
        return true;
    }
    private void RefreshSum()
    {
        var sum = Rows.Sum(row => TryParse(row.PercentageText, out var value) ? value : 0m);
        SumText.Text = $"Summe: {sum:N4}%  (muss 100.0000% ergeben)";
    }
    private static bool TryParse(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, SwissCulture, out value) ||
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
    private void ShowError(string message) { EditorError.Message = message; EditorError.IsOpen = true; }
}
