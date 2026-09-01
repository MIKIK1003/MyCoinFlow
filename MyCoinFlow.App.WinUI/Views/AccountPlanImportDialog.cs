using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Importing;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed class AccountPlanImportDialog : ContentDialog
{
    private readonly KontenplanExcelImporter _importer = new();
    private readonly TextBox _path = new() { Header = "Excel-Datei", IsReadOnly = true };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListView _preview = new() { MaxHeight = 430 };
    private readonly CheckBox _onlyNew = new() { Content = "Nur neue Datensätze anlegen", IsChecked = true };
    private readonly CheckBox _budget = new() { Content = "BudgetJ importieren" };
    private readonly ComboBox _period = new() { DisplayMemberPath = "Bezeichnung", SelectedValuePath = "Id", IsEnabled = false, MinWidth = 330 };
    private List<KontenplanExcelImporter.PreviewRow> _rows = new();

    public AccountPlanImportDialog()
    {
        Title = "Kontenplan importieren"; PrimaryButtonText = "Importieren"; CloseButtonText = "Abbrechen"; DefaultButton = ContentDialogButton.Primary;
        var select = new Button { Content = "Datei wählen" }; select.Click += OnSelectClick; _budget.Checked += (_, _) => _period.IsEnabled = true; _budget.Unchecked += (_, _) => _period.IsEnabled = false;
        var periods = new DatabaseService().LadeBudgetzeitraeume().OrderByDescending(value => value.IstAktiv).ThenByDescending(value => value.Startdatum).ToList(); foreach (var value in periods) value.Bezeichnung = $"{value.Bezeichnung} ({value.Startdatum:dd.MM.yyyy} – {value.Enddatum:dd.MM.yyyy}){(value.IstAktiv ? " [aktiv]" : "")}"; _period.ItemsSource = periods;
        _preview.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Padding='6' ColumnSpacing='8'><Grid.ColumnDefinitions><ColumnDefinition Width='70'/><ColumnDefinition Width='110'/><ColumnDefinition Width='1.3*'/><ColumnDefinition Width='1.3*'/><ColumnDefinition Width='2*'/></Grid.ColumnDefinitions><TextBlock Text='{Binding RowNo}'/><TextBlock Grid.Column='1' Text='{Binding Konto}'/><TextBlock Grid.Column='2' Text='{Binding ArtBezeichnung}'/><TextBlock Grid.Column='3' Text='{Binding Gruppe}'/><TextBlock Grid.Column='4' Text='{Binding Warning}' TextTrimming='CharacterEllipsis'/></Grid></DataTemplate>");
        Content = new StackPanel { Width = 950, Spacing = 10, Children = { _path, select, _summary, _preview, _onlyNew, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _budget, _period } } } };
        PrimaryButtonClick += OnImportClick;
    }

    private async void OnSelectClick(object sender, RoutedEventArgs e) { var path = await FilePickerService.PickOpenAsync(".xlsx", ".xls"); if (path is null) return; _path.Text = path; try { _rows = _importer.Analyze(path).Rows; _preview.ItemsSource = _rows; _summary.Text = $"Zeilen: {_rows.Count} · Fehler: {_rows.Count(value => value.HasError)} · Duplikate: {_rows.Count(value => value.DuplicateKontoInFile)} · neue Konten: {_rows.Count(value => !value.ExistsKonto && value.Konto.HasValue)}"; } catch (Exception ex) { _summary.Text = "Vorschau fehlgeschlagen: " + ex.Message; } }
    private void OnImportClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_rows.Count == 0 || (_budget.IsChecked == true && _period.SelectedValue is null)) { args.Cancel = true; _summary.Text = _rows.Count == 0 ? "Keine Vorschau-Daten. Bitte eine Excel-Datei wählen." : "Bitte einen Budgetzeitraum wählen oder Budget-Import deaktivieren."; return; }
        try { var result = _importer.ImportFromPreview(_rows, _onlyNew.IsChecked == true, _budget.IsChecked == true ? _period.SelectedValue as int? : null); _summary.Text = $"Import abgeschlossen: {result.RowsProcessed} verarbeitet, {result.RowsSkipped} übersprungen, {result.RowsWithErrors} fehlerhaft; neu: {result.KontenNeu} Konten, {result.ArtenNeu} Arten, {result.GruppenNeu} Gruppen, {result.UntergruppenNeu} Untergruppen; {result.BudgetsGesetzt} Budgetwerte."; }
        catch (Exception ex) { args.Cancel = true; _summary.Text = "Import fehlgeschlagen: " + ex.Message; }
    }
}
