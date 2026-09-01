using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class NumberRangesSettingsControl : UserControl
{
    private readonly DatabaseService _database = new();
    private NumberRangeRule? _selected;
    private static readonly string[] Directions = { "Ausgabe", "Einnahme", "Neutral" };
    private static readonly string[] Descriptions = { "Einnahmen (Budgetiert)", "Ausgaben (Budgetiert)", "Anschaffungen (Budgetiert)", "Investitionen (Budgetiert)", "Amortisationen (Budgetiert)", "Durchlaufkonten (nicht budgetiert)" };

    public NumberRangesSettingsControl()
    {
        InitializeComponent();
        DirectionBox.ItemsSource = Directions;
        DescriptionBox.ItemsSource = Descriptions;
        try { _database.AssertNumberRangeRulesSchema(); Reload(); } catch (Exception ex) { Show(ex.Message, InfoBarSeverity.Error); }
    }

    private void Reload(int? id = null) { var values = _database.LadeNummernRegeln(); RulesList.ItemsSource = values; RulesList.SelectedItem = id.HasValue ? values.FirstOrDefault(value => value.Id == id) : null; }
    private void OnReloadClick(object sender, RoutedEventArgs e) => Reload(_selected?.Id);
    private void OnNewClick(object sender, RoutedEventArgs e) { RulesList.SelectedItem = null; _selected = new NumberRangeRule(); StartBox.Value = EndBox.Value = 0; DirectionBox.SelectedItem = "Ausgabe"; DescriptionBox.SelectedItem = "Ausgaben (Budgetiert)"; BudgetBox.IsChecked = ExcludeStweBox.IsChecked = false; }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) { _selected = RulesList.SelectedItem as NumberRangeRule; if (_selected is null) return; StartBox.Value = _selected.RangeStart; EndBox.Value = _selected.RangeEnd; DirectionBox.SelectedItem = _selected.Richtung; DescriptionBox.SelectedItem = _selected.Bezeichnung; BudgetBox.IsChecked = _selected.IstBudgetkonto; ExcludeStweBox.IsChecked = _selected.ExcludeFromStweSets; }
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _selected ??= new NumberRangeRule();
        if (!double.IsFinite(StartBox.Value) || !double.IsFinite(EndBox.Value))
        {
            Show("Bitte Von- und Bis-Nummer vollständig erfassen.", InfoBarSeverity.Warning);
            return;
        }
        var start = (int)StartBox.Value; var end = (int)EndBox.Value; var direction = DirectionBox.SelectedItem as string; var description = DescriptionBox.SelectedItem as string;
        if (start < 0 || end < 0 || start > end || !Directions.Contains(direction) || !Descriptions.Contains(description)) { Show("Bitte einen gültigen Bereich, eine Richtung und eine Bezeichnung erfassen.", InfoBarSeverity.Warning); return; }
        _selected.RangeStart = start; _selected.RangeEnd = end; _selected.Richtung = direction!; _selected.Bezeichnung = description!; _selected.IstBudgetkonto = BudgetBox.IsChecked == true; _selected.ExcludeFromStweSets = ExcludeStweBox.IsChecked == true;
        if (_selected.Id == 0) _selected.Id = _database.SpeichereNummernRegel(_selected); else _database.AktualisiereNummernRegel(_selected);
        Reload(_selected.Id); Show("Regel gespeichert.", InfoBarSeverity.Success);
    }
    private async void OnDeleteClick(object sender, RoutedEventArgs e) { if (_selected is null) return; if (_selected.Id != 0 && !await ConfirmAsync("Nummernkreis löschen?")) return; if (_selected.Id != 0) _database.LoescheNummernRegel(_selected.Id); Reload(); }
    private async Task<bool> ConfirmAsync(string text) => await new ContentDialog { XamlRoot = XamlRoot, Title = text, Content = "Die ausgewählte Regel wird gelöscht.", PrimaryButtonText = "Löschen", CloseButtonText = "Abbrechen", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Primary;
    private void Show(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
}
