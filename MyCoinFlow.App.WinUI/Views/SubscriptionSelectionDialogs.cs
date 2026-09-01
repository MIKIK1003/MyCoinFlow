using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Globalization;

namespace MyCoinFlow.WinUI.Views;

public sealed class SubscriptionCandidateDialog : ContentDialog
{
    public SubscriptionCandidateDialog(IReadOnlyList<AboKandidat> candidates)
    {
        Title = "Gefundene Zahlungsserien"; PrimaryButtonText = "Übernehmen"; CloseButtonText = "Abbrechen"; DefaultButton = ContentDialogButton.Primary;
        var panel = new StackPanel { Width = 880, Spacing = 8 }; panel.Children.Add(new TextBlock { Text = "Erkannt werden regelmässige Einnahmen und Ausgaben. Richtung und Serienart sind Vorschläge und können nach der Übernahme bearbeitet werden. Abgewählte Muster bleiben dauerhaft ausgeblendet.", TextWrapping = TextWrapping.Wrap });
        foreach (var item in candidates)
        {
            var rhythm = AboPerioden.Anzeige(item.Periodizitaet) + (item.RhythmusNurVermutet ? " (vorläufig)" : string.Empty);
            var content = new StackPanel { Spacing = 2 };
            content.Children.Add(new TextBlock { Text = $"{item.AdresseName}  ·  {Zahlungsrichtungen.Anzeige(item.Richtung)}  ·  {AboKategorien.Anzeige(item.Kategorie)}  ·  {rhythm}  ·  {item.MedianBetrag:N2}  ·  {item.AnzahlZahlungen} Zahlung(en)  ·  {item.ErsteZahlung:dd.MM.yyyy}–{item.LetzteZahlung:dd.MM.yyyy}", TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = item.Erkennungsgrund, Opacity = 0.68, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            var check = new CheckBox { IsChecked = item.Uebernehmen, Content = content };
            check.Checked += (_, _) => item.Uebernehmen = true; check.Unchecked += (_, _) => item.Uebernehmen = false; panel.Children.Add(check);
        }
        Content = new ScrollViewer { MaxHeight = 560, Style = Application.Current.Resources["FormScrollViewerStyle"] as Style, Content = panel };
    }
}

public sealed class SubscriptionGapDialog : ContentDialog
{
    public SubscriptionGapDialog(IReadOnlyList<AboLueckeKandidat> candidates, IReadOnlyList<DateTime> withoutCandidates)
    {
        Title = "Lücken füllen"; PrimaryButtonText = "Übernehmen"; CloseButtonText = "Abbrechen"; DefaultButton = ContentDialogButton.Primary;
        var panel = new StackPanel { Width = 900, Spacing = 8 };
        foreach (var item in candidates)
        {
            var check = new CheckBox { IsChecked = item.Uebernehmen, Content = $"Erwartet {item.ErwartetAm:dd.MM.yyyy}  →  {item.Datum:dd.MM.yyyy}  ·  {item.Betrag:N2}  ·  {item.AdresseName}  ·  {item.MatchInfo}" };
            check.Checked += (_, _) => item.Uebernehmen = true; check.Unchecked += (_, _) => item.Uebernehmen = false; panel.Children.Add(check);
        }
        if (withoutCandidates.Count > 0) panel.Children.Add(new TextBlock { Text = "Ohne Kandidat: " + string.Join(", ", withoutCandidates.Select(value => value.ToString("dd.MM.yyyy"))), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        Content = new ScrollViewer { MaxHeight = 560, Style = Application.Current.Resources["FormScrollViewerStyle"] as Style, Content = panel };
    }
}

public sealed class SubscriptionTransactionDialog : ContentDialog
{
    private readonly DatabaseService _database = new(); private readonly TextBox _search = new() { PlaceholderText = "Suchtext" }; private readonly TextBox _amount = new() { PlaceholderText = "Betrag" }; private readonly CalendarDatePicker _from = new() { Date = DateTime.Today.AddYears(-2) }; private readonly CalendarDatePicker _to = new() { Date = DateTime.Today }; private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 470 };
    public SubscriptionTransactionDialog(string? preset)
    {
        Title = "Transaktion zuordnen"; PrimaryButtonText = "Zuordnen"; CloseButtonText = "Abbrechen"; DefaultButton = ContentDialogButton.Primary; PrimaryButtonClick += (_, args) => { if (_list.SelectedItems.Count == 0) args.Cancel = true; };
        _search.Text = preset ?? string.Empty; var searchButton = new Button { Content = "Suchen" }; searchButton.Click += (_, _) => Search(); var filters = new Grid { ColumnSpacing = 8 }; for (var i = 0; i < 5; i++) filters.ColumnDefinitions.Add(new ColumnDefinition { Width = i < 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto }); filters.Children.Add(_search); Grid.SetColumn(_amount, 1); filters.Children.Add(_amount); Grid.SetColumn(_from, 2); filters.Children.Add(_from); Grid.SetColumn(_to, 3); filters.Children.Add(_to); Grid.SetColumn(searchButton, 4); filters.Children.Add(searchButton);
        var panel = new StackPanel { Width = 940, Spacing = 10 }; panel.Children.Add(filters); panel.Children.Add(_list); Content = panel; Search();
    }
    public List<int> SelectedIds => _list.SelectedItems.OfType<Transaktion>().Select(value => value.Id).ToList();
    private void Search() { decimal? amount = null; if (decimal.TryParse((_amount.Text ?? "").Trim().Replace("'", ""), NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)) amount = parsed; var values = _database.SearchTransaktionenForZuordnung(string.IsNullOrWhiteSpace(_search.Text) ? null : _search.Text.Trim(), amount, _from.Date?.Date, _to.Date?.Date, 200); _list.ItemsSource = values; _list.ItemTemplate = BuildTemplate(); }
    private static DataTemplate BuildTemplate() { const string xaml = "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Padding='8' ColumnSpacing='10'><Grid.ColumnDefinitions><ColumnDefinition Width='110'/><ColumnDefinition Width='120'/><ColumnDefinition Width='1.5*'/><ColumnDefinition Width='2*'/></Grid.ColumnDefinitions><TextBlock Text='{Binding Datum}'/><TextBlock Grid.Column='1' Text='{Binding Betrag}'/><TextBlock Grid.Column='2' Text='{Binding AdresseName}'/><TextBlock Grid.Column='3' Text='{Binding Notiz}' TextTrimming='CharacterEllipsis'/></Grid></DataTemplate>"; return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml); }
}
