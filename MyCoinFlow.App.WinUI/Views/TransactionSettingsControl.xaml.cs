using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.WinUI.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class TransactionSettingsControl : UserControl
{
    private readonly DatabaseService _database = new();
    private List<DatabaseService.AdressAliasAnzeige> _aliases = new();
    private List<DatabaseService.AdressBuchungsregelAnzeige> _rules = new();
    private string Section => (Sections.SelectedItem as TabViewItem)?.Tag as string ?? "Quick";

    public TransactionSettingsControl() { InitializeComponent(); ReloadAll(); }
    private void ReloadAll() { LoadQuick(); LoadLearned(); LoadCategories(); }
    private void LoadQuick() { _database.EnsureKontoSchnellwahlSchema(); var selected = _database.LadeKontoSchnellwahl(CurrentUserContext.Username).ToHashSet(); QuickAccountsList.ItemsSource = _database.LadeKontoLookup().Select(value => new QuickAccountSettingRow { Id = value.Id, Display = value.Anzeige, IsSelected = selected.Contains(value.Id) }).ToList(); }
    private void LoadLearned() { _aliases = _database.LadeAdressAliaseMitNamen(); _rules = _database.LadeAlleAdressBuchungsregelnMitNamen(); ApplyAliasFilter(); ApplyRuleFilter(); }
    private void LoadCategories() { var accounts = _database.LadeKontoLookup(); CategoryAccountsList.ItemsSource = _database.LadeKategorieStandardkonten().Select(value => new CategoryAccountSettingRow { Id = value.Id, Category = value.Kategorie, AccountId = value.KontoId, Accounts = accounts }).ToList(); }
    private void OnRefreshClick(object sender, RoutedEventArgs e) { ReloadAll(); Show("Daten aktualisiert.", InfoBarSeverity.Success); }
    private void OnTabChanged(object sender, SelectionChangedEventArgs e) { if (!ReferenceEquals(e.OriginalSource, Sections)) return; SaveButton.Visibility = Section == "Learned" ? Visibility.Collapsed : Visibility.Visible; ImportCategoriesButton.Visibility = Section == "Categories" ? Visibility.Visible : Visibility.Collapsed; }
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Section == "Quick") { var rows = (QuickAccountsList.ItemsSource as IEnumerable<QuickAccountSettingRow>) ?? []; var ids = rows.Where(value => value.IsSelected).Select(value => value.Id).ToList(); _database.SpeichereKontoSchnellwahl(CurrentUserContext.Username, ids); Show($"Gespeichert: {ids.Count} Konto(en) in der Schnellwahl.", InfoBarSeverity.Success); }
            else if (Section == "Categories") { var rows = (CategoryAccountsList.ItemsSource as IEnumerable<CategoryAccountSettingRow>) ?? []; var values = rows.Select(value => (value.Id, value.AccountId)).ToList(); _database.SpeichereKategorieStandardkonten(values); Show($"Gespeichert: {values.Count(value => value.AccountId.HasValue)} von {values.Count} Kategorie(n) mit Konto.", InfoBarSeverity.Success); }
        }
        catch (Exception ex) { Show(ex.Message, InfoBarSeverity.Error); }
    }
    private void OnAliasFilterChanged(object sender, TextChangedEventArgs e) => ApplyAliasFilter();
    private void ApplyAliasFilter() { var term = AliasFilterBox?.Text?.Trim() ?? string.Empty; AliasesList.ItemsSource = string.IsNullOrWhiteSpace(term) ? _aliases : _aliases.Where(value => value.AdresseName.Contains(term, StringComparison.OrdinalIgnoreCase) || value.Text.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList(); }
    private void OnRuleFilterChanged(object sender, TextChangedEventArgs e) => ApplyRuleFilter();
    private void ApplyRuleFilter() { var term = RuleFilterBox?.Text?.Trim() ?? string.Empty; RulesList.ItemsSource = string.IsNullOrWhiteSpace(term) ? _rules : _rules.Where(value => value.AdresseName.Contains(term, StringComparison.OrdinalIgnoreCase) || value.TextPattern.Contains(term, StringComparison.OrdinalIgnoreCase) || value.KontoAnzeige.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList(); }
    private async void OnAliasDeleteClick(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is not int id || !await ConfirmAsync("Alias löschen", "Diesen Alias wirklich löschen?")) return; _database.LoescheAdressAlias(id); LoadLearned(); }
    private async void OnRuleDeleteClick(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is not int id || !await ConfirmAsync("Regel löschen", "Diese Buchungsregel und die zugehörige Lernhistorie für dieses Konto wirklich löschen?")) return; _database.LoescheAdressBuchungsregel(id); LoadLearned(); }
    private async void OnImportCategoriesClick(object sender, RoutedEventArgs e) { try { var path = await FilePickerService.PickOpenAsync(".xlsx", ".xls", ".csv"); if (path is null) return; var categories = _database.LeseCreditCardExcel(path).Select(value => value.Kategorie).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToList(); _database.SeedKategorienOhneKonto(categories!); LoadCategories(); Show($"{categories.Count} Kategorie(n) aus der Datei geprüft/ergänzt.", InfoBarSeverity.Success); } catch (Exception ex) { Show("Fehler beim Einlesen: " + ex.Message, InfoBarSeverity.Error); } }
    private async Task<bool> ConfirmAsync(string title, string text) => await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = text, PrimaryButtonText = "Ja", CloseButtonText = "Nein", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Primary;
    private void Show(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
}
