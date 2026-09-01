using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.ViewModels;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.WinUI.Services;
using System.Globalization;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class SubscriptionsPage : Page
{
    private static readonly CultureInfo Swiss = CultureInfo.GetCultureInfo("de-CH");
    private readonly DatabaseService _database = new();
    private AbosViewModel? _viewModel;
    private List<SubscriptionDisplayRow> _allRows = new();
    private List<AboKategorie> _categories = new();
    private Dictionary<string, AboKategorie> _categoryByCode = new(StringComparer.OrdinalIgnoreCase);
    private SubscriptionDisplayRow? _selected;
    private ListView? _activeSubscriptionList;
    private int? _selectionToRestoreId;
    private bool _isReloading;
    private bool _filtersReady;

    public SubscriptionsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _database.EnsureAboSchema();
            InitializeFilters();
            Reload();
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void InitializeFilters()
    {
        if (_filtersReady)
            return;
        _filtersReady = true;
        DirectionFilterBox.ItemsSource = new[] { "Alle", "Einnahmen", "Ausgaben", "Richtung prüfen" };
        CategoryFilterBox.ItemsSource = new[] { new SubscriptionCategoryFilterOption(string.Empty, "Alle") };
        CategoryFilterBox.DisplayMemberPath = nameof(SubscriptionCategoryFilterOption.Display);
        StatusFilterBox.ItemsSource = new[] { "Alle", "Aktiv", "Gekündigt", "Beendet" };
        DirectionFilterBox.SelectedIndex = 0;
        CategoryFilterBox.SelectedIndex = 0;
        StatusFilterBox.SelectedIndex = 0;
    }

    private void Reload(int? selectedId = null)
    {
        if (_isReloading)
            return;
        _isReloading = true;
        try
        {
            selectedId ??= _selected?.Id;
            var selectedCategory = (CategoryFilterBox.SelectedItem as SubscriptionCategoryFilterOption)?.Code ?? string.Empty;
            _categories = _database.AboKategorienLaden(true);
            _categoryByCode = _categories.ToDictionary(value => value.Code, StringComparer.OrdinalIgnoreCase);
            _viewModel = new AbosViewModel();
            _allRows = _viewModel.Abos.Select(row => new SubscriptionDisplayRow(row, CategoryFor(row.Abo.Kategorie))).ToList();
            var used = _allRows.Select(row => row.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var categoryOptions = new[] { new SubscriptionCategoryFilterOption(string.Empty, "Alle") }
                .Concat(_categories.Where(value => value.IstAktiv || used.Contains(value.Code))
                    .OrderBy(value => value.Sortierung).ThenBy(value => value.Bezeichnung)
                    .Select(value => new SubscriptionCategoryFilterOption(value.Code, value.Bezeichnung)))
                .ToList();
            CategoryFilterBox.ItemsSource = categoryOptions;
            CategoryFilterBox.SelectedItem = categoryOptions.FirstOrDefault(value => value.Code.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase)) ?? categoryOptions[0];
            UpdateSummary();
            ApplyFilters(selectedId);
        }
        finally
        {
            _isReloading = false;
        }
    }

    private void UpdateSummary()
    {
        var active = _allRows.Where(row => row.IsReportable && row.Value.Abo.Status == AboStatus.Aktiv).ToList();
        var incomeAnnual = active.Where(row => row.Direction == Zahlungsrichtungen.Einnahme).Sum(row => row.AnnualCostValue);
        var expenseAnnual = active.Where(row => row.Direction == Zahlungsrichtungen.Ausgabe).Sum(row => row.AnnualCostValue);
        var deadlineLimit = DateTime.Today.AddDays(90);
        var deadlines = active.Count(row => row.Value.KuendigenBis is DateTime date
                                             && date.Date >= DateTime.Today
                                             && date.Date <= deadlineLimit);
        var review = _allRows.Count(row => row.Direction == Zahlungsrichtungen.Unklar);

        ActiveCountText.Text = active.Count.ToString("N0", Swiss);
        AnnualCostText.Text = Currency(incomeAnnual);
        MonthlyCostText.Text = Currency(expenseAnnual);
        DeadlineCountText.Text = deadlines.ToString("N0", Swiss);
        ReviewCountText.Text = review == 0 ? string.Empty : $"{review} Richtung prüfen";
    }

    private void ApplyFilters(int? selectedId = null)
    {
        if (!_filtersReady)
            return;

        var category = (CategoryFilterBox.SelectedItem as SubscriptionCategoryFilterOption)?.Code ?? string.Empty;
        var direction = DirectionFilterBox.SelectedItem as string ?? "Alle";
        var status = StatusFilterBox.SelectedItem as string ?? "Alle";
        var search = SearchBox.Text?.Trim();
        IEnumerable<SubscriptionDisplayRow> query = _allRows;

        query = direction switch
        {
            "Einnahmen" => query.Where(row => row.Direction == Zahlungsrichtungen.Einnahme),
            "Ausgaben" => query.Where(row => row.Direction == Zahlungsrichtungen.Ausgabe),
            "Richtung prüfen" => query.Where(row => row.Direction == Zahlungsrichtungen.Unklar),
            _ => query
        };
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(row => row.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (status != "Alle")
            query = query.Where(row => row.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(row => row.SearchText.Contains(search, StringComparison.CurrentCultureIgnoreCase));

        var rows = query
            .OrderBy(row => DirectionOrder(row.Direction))
            .ThenBy(row => row.CategoryOrder)
            .ThenBy(row => row.Value.KuendigenBis ?? DateTime.MaxValue)
            .ThenBy(row => row.Name)
            .ToList();
        var groups = rows
            .GroupBy(row => new { row.Direction, row.Category })
            .Select(group =>
            {
                var first = group.First();
                var values = group.ToList();
                return new SubscriptionGroup(first.CategoryName, first.DirectionName,
                    $"{values.Count} Serie(n) · {Currency(values.Sum(value => value.AnnualCostValue))} pro Jahr",
                    first.DirectionSurface, first.DirectionAccent, values);
            }).ToList();
        var restored = selectedId.HasValue ? rows.FirstOrDefault(row => row.Id == selectedId.Value) : rows.FirstOrDefault();
        _activeSubscriptionList = null;
        _selectionToRestoreId = restored?.Id;
        SubscriptionGroupsList.ItemsSource = groups;
        ResultCountText.Text = $"{rows.Count} angezeigt";
        ShowSelected(restored);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Reload(_selected?.Id);

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReloading)
            ApplyFilters(_selected?.Id);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isReloading)
            ApplyFilters(_selected?.Id);
    }

    private void OnSubscriptionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView list || list.SelectedItem is not SubscriptionDisplayRow row)
            return;
        if (_activeSubscriptionList is not null && _activeSubscriptionList != list)
            _activeSubscriptionList.SelectedItem = null;
        _activeSubscriptionList = list;
        _selectionToRestoreId = row.Id;
        ShowSelected(row);
    }

    private void OnSubscriptionListLoaded(object sender, RoutedEventArgs e)
    {
        if (_selectionToRestoreId is not int selectedId || sender is not ListView list
            || list.ItemsSource is not IEnumerable<SubscriptionDisplayRow> rows)
            return;
        var row = rows.FirstOrDefault(value => value.Id == selectedId);
        if (row is not null)
            list.SelectedItem = row;
    }

    private void ShowSelected(SubscriptionDisplayRow? row)
    {
        _selected = row;
        EmptyDetailPanel.Visibility = row is null ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        if (row is null)
        {
            PaymentsList.ItemsSource = null;
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.AusgewaehltesAbo = row.Value;
            PaymentsList.ItemsSource = _viewModel.Zahlungen
                .Select(value => new SubscriptionPaymentDisplayRow(value))
                .ToList();
        }

        DetailCategoryBadge.Background = row.CategorySurface;
        DetailCategoryText.Foreground = row.CategoryAccent;
        DetailCategoryText.Text = row.CategoryName;
        DetailDirectionBadge.Background = row.DirectionSurface;
        DetailDirectionText.Foreground = row.DirectionAccent;
        DetailDirectionText.Text = row.DirectionName;
        DetailStatusBadge.Background = row.StatusBackground;
        DetailStatusText.Foreground = row.StatusForeground;
        DetailStatusText.Text = row.Status;
        DetailNameText.Text = row.Name;
        DetailProviderText.Text = row.Provider;
        DetailMonthlyText.Text = row.MonthlyCost;
        DetailAnnualText.Text = row.AnnualCost;
        DetailHistoricalTotalText.Text = row.HistoricalTotal;
        DetailNextText.Text = row.NextDate;
        DetailPaymentCountText.Text = row.PaymentCount;
        DetailCancelByText.Text = row.CancelBy;
        DetailEndText.Text = row.ContractEnd;
        DetailRouteText.Text = row.CancellationRoute;
        DetailPeriodText.Text = $"{row.Period} · erwartet {row.ExpectedAmount}";
        DetailLastText.Text = $"{row.LastDate} · CHF {row.LastAmount}";
        DetailAccountText.Text = row.Account;
        DetailIndicatorText.Text = row.Indicator;
        DetailNoteText.Text = string.IsNullOrWhiteSpace(row.Note) ? "Keine Notiz" : row.Note;
        SelectionInfoBar.IsOpen = !string.IsNullOrWhiteSpace(row.Hint);
        SelectionInfoBar.Message = row.Hint;
        SelectionInfoBar.Severity = row.Hint.Contains("verpasst", StringComparison.CurrentCultureIgnoreCase)
            || row.Hint.Contains("Abbuchung", StringComparison.CurrentCultureIgnoreCase)
            ? InfoBarSeverity.Warning
            : InfoBarSeverity.Informational;
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => OnEditClick(sender, e);
    private async void OnNewClick(object sender, RoutedEventArgs e) => await EditAsync(null);
    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not null)
            await EditAsync(_selected.Value.Abo);
    }

    private async Task EditAsync(Abo? source)
    {
        var dialog = new SubscriptionEditorDialog(source);
        if (!await dialog.ShowAsync() || dialog.Result is null)
            return;
        var id = source is null ? _database.AboInsert(dialog.Result) : source.Id;
        if (source is not null)
            _database.AboUpdate(dialog.Result);
        Reload(id);
    }

    private async void OnCancelSubscriptionClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;
        var abo = _selected.Value.Abo;
        if (!await ConfirmAsync("Als gekündigt markieren",
                $"„{abo.Name}“ als gekündigt markieren (per heute)?\n\nNeue Abbuchungen nach diesem Datum werden rot markiert."))
            return;
        abo.Status = AboStatus.Gekuendigt;
        abo.GekuendigtAm = DateTime.Today;
        _database.AboUpdate(abo);
        Reload(abo.Id);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !await ConfirmAsync("Zahlungsserie löschen",
                $"Zahlungsserie „{_selected.Name}“ wirklich löschen?\n\nDie Transaktionen bleiben erhalten; nur die Serien-Zuordnung wird entfernt."))
            return;
        _database.AboDelete(_selected.Id);
        Reload();
    }

    private async void OnFindCandidatesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var subscriptions = _database.AbosLaden();
            var candidates = AboErkennungService.FindeKandidaten(
                _database.AboLadeTransaktionenMitAdresse(),
                _database.AboZugeordneteTransaktionIds(),
                subscriptions.Where(value => value.AdresseId.HasValue)
                    .Select(value => value.AdresseId!.Value).ToHashSet(),
                _database.AboKandidatAusschluesseLaden(),
                _database.IstEinnahmenKonto);

            if (candidates.Count == 0)
            {
                await MessageAsync("Zahlungsserien",
                    "Keine neuen regelmässigen Zahlungsserien gefunden. Bereits abgewählte Muster bleiben ausgeblendet.");
                return;
            }

            var dialog = new SubscriptionCandidateDialog(candidates) { XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            _database.AboKandidatenIgnorieren(candidates.Where(value => !value.Uebernehmen));
            var count = 0;
            foreach (var candidate in candidates.Where(value => value.Uebernehmen))
            {
                var multiple = candidate.AdresseHatAbo
                               || candidates.Count(value => value.AdresseId == candidate.AdresseId) > 1;
                var abo = new Abo
                {
                    Name = multiple ? $"{candidate.AdresseName} (ca. {candidate.MedianBetrag:N2})" : candidate.AdresseName,
                    AdresseId = candidate.AdresseId,
                    Periodizitaet = candidate.Periodizitaet,
                    Richtung = candidate.Richtung,
                    Kategorie = candidate.Kategorie,
                    ErwarteterBetrag = candidate.MedianBetrag,
                    ErwartetesKontoId = candidate.HaeufigstesKontoId,
                    Status = AboStatus.Aktiv
                };
                var id = _database.AboInsert(abo);
                foreach (var transactionId in candidate.TransaktionIds)
                    _database.AboTransaktionZuordnen(id, transactionId, false);
                count++;
            }

            Reload();
            await MessageAsync("Zahlungsserien", $"{count} Zahlungsserie(n) übernommen. Abgewählte Vorschläge bleiben dauerhaft ausgeblendet.");
        }
        catch (Exception ex)
        {
            ShowStatus("Kandidaten-Suche fehlgeschlagen: " + ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void OnAssignNewPaymentsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var subscriptions = _database.AbosLaden();
            var transactions = _database.AboLadeTransaktionenMitAdresse();
            var assigned = _database.AboZugeordneteTransaktionIds();
            var byAddress = subscriptions
                .Where(value => value.Status != AboStatus.Beendet
                                && AboKategorien.IstZahlungsserie(value.Kategorie)
                                && value.AdresseId.HasValue)
                .GroupBy(value => value.AdresseId!.Value)
                .ToDictionary(value => value.Key, value => value.ToList());
            var found = 0;
            foreach (var transaction in transactions)
            {
                if (assigned.Contains(transaction.Id)
                    || !transaction.AdresseId.HasValue
                    || !byAddress.TryGetValue(transaction.AdresseId.Value, out var options))
                    continue;
                Abo? best = null;
                var bestDifference = decimal.MaxValue;
                foreach (var abo in options)
                {
                    if (abo.ErwarteterBetrag is null or 0m)
                    {
                        if (options.Count == 1 && best is null)
                            best = abo;
                        continue;
                    }
                    var expected = Math.Abs(abo.ErwarteterBetrag.Value);
                    var difference = Math.Abs(Math.Abs(transaction.Betrag) - expected);
                    if (difference <= expected * Math.Max(0m, abo.BetragToleranzProzent) / 100m
                        && difference < bestDifference)
                    {
                        best = abo;
                        bestDifference = difference;
                    }
                }
                if (best is null)
                    continue;
                _database.AboTransaktionZuordnen(best.Id, transaction.Id, false);
                assigned.Add(transaction.Id);
                found++;
            }
            Reload(_selected?.Id);
            await MessageAsync("Neue Zahlungen", found > 0 ? $"{found} neue Zahlung(en) zugeordnet." : "Keine neuen Zahlungen gefunden.");
        }
        catch (Exception ex)
        {
            ShowStatus("Zuordnung fehlgeschlagen: " + ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void OnAssignPaymentClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;
        var dialog = new SubscriptionTransactionDialog(_selected.Value.AdresseName ?? _selected.Name) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        foreach (var id in dialog.SelectedIds)
            _database.AboTransaktionZuordnen(_selected.Id, id, true);
        Reload(_selected.Id);
    }

    private async void OnFillGapsClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;
        var abo = _selected.Value.Abo;
        var payments = _database.AboZahlungenLaden()
            .Where(value => value.AboId == abo.Id && !value.IstEinmalig).ToList();
        if (payments.Count < 2)
        {
            await MessageAsync("Lücken füllen", "Für die Lücken-Suche braucht die Serie mindestens zwei wiederkehrende Zahlungen.");
            return;
        }
        var periodDays = AboPerioden.Tage(abo.Periodizitaet);
        var gaps = AboErkennungService.FindeLuecken(payments.Select(value => value.Datum).ToList(), periodDays);
        if (gaps.Count == 0)
        {
            await MessageAsync("Lücken füllen", "Keine Lücken in der Zahlungsreihe erkennbar – die Abstände passen zum Rhythmus.");
            return;
        }
        var reference = abo.ErwarteterBetrag ?? Math.Abs(payments.OrderByDescending(value => value.Datum).First().Betrag);
        var window = Math.Max(7, periodDays / 3);
        var candidates = AboErkennungService.FindeLueckenKandidaten(
            abo, gaps, reference,
            _database.AboLadeNichtZugeordneteTransaktionen(gaps.Min().AddDays(-window), gaps.Max().AddDays(window)));
        var withCandidate = candidates.Select(value => value.ErwartetAm).Distinct().ToHashSet();
        var without = gaps.Where(value => !withCandidate.Contains(value)).ToList();
        if (candidates.Count == 0)
        {
            await MessageAsync("Lücken füllen", $"{gaps.Count} Lücke(n) erkannt, aber keine passende Transaktion gefunden.");
            return;
        }
        var dialog = new SubscriptionGapDialog(candidates, without) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        var selected = candidates.Where(value => value.Uebernehmen)
            .GroupBy(value => value.ErwartetAm).Select(value => value.First()).ToList();
        foreach (var candidate in selected)
            _database.AboTransaktionZuordnen(abo.Id, candidate.TransaktionId, true);
        Reload(abo.Id);
        if (selected.Count > 0)
            await MessageAsync("Lücken füllen", $"{selected.Count} Zahlung(en) zugeordnet.");
    }

    private async void OnCleanAccountsClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;
        var abo = _selected.Value.Abo;
        var payments = _database.AboZahlungenLaden()
            .Where(value => value.AboId == abo.Id && !value.IstEinmalig).ToList();
        var accounts = _database.LadeKontenplan().ToDictionary(value => value.Id, value => $"{value.Kontonummer:D4}  {value.Detail}");
        var options = payments.Where(value => value.BuchungsKontoId.HasValue)
            .GroupBy(value => value.BuchungsKontoId!.Value)
            .Select(value => new AccountChoice(value.Key,
                accounts.TryGetValue(value.Key, out var name) ? name : $"Konto #{value.Key}", value.Count()))
            .OrderByDescending(value => value.Count).ToList();
        if (abo.ErwartetesKontoId.HasValue && options.All(value => value.Id != abo.ErwartetesKontoId))
            options.Insert(0, new AccountChoice(abo.ErwartetesKontoId.Value,
                accounts.GetValueOrDefault(abo.ErwartetesKontoId.Value, $"Konto #{abo.ErwartetesKontoId}"), 0));
        if (options.Count == 0)
        {
            await MessageAsync("Konten bereinigen", "Die Zahlungen dieser Serie haben kein Buchungskonto – hier gibt es nichts zu bereinigen.");
            return;
        }
        var combo = new ComboBox { ItemsSource = options, DisplayMemberPath = "Display", SelectedItem = options.FirstOrDefault(value => value.Id == abo.ErwartetesKontoId) ?? options[0], Width = 520 };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Zielkonto wählen", Content = combo, PrimaryButtonText = "Übernehmen", CloseButtonText = "Abbrechen" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || combo.SelectedItem is not AccountChoice target)
            return;
        var changed = payments.Where(value => value.BuchungsKontoId.HasValue && value.BuchungsKontoId != target.Id).ToList();
        foreach (var payment in changed)
            _database.AboSetzeBuchungsKonto(payment.TransaktionId, target.Id);
        if (abo.ErwartetesKontoId != target.Id)
        {
            abo.ErwartetesKontoId = target.Id;
            _database.AboUpdate(abo);
        }
        Reload(abo.Id);
        await MessageAsync("Konten bereinigen", changed.Count > 0
            ? $"{changed.Count} Zahlung(en) auf „{target.Display}“ umgebucht."
            : $"„{target.Display}“ als erwartetes Konto gespeichert.");
    }

    private sealed record AccountChoice(int Id, string Display, int Count);

    private void OnHomepageClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;
        var abo = _selected.Value.Abo;
        var url = string.IsNullOrWhiteSpace(abo.WebseiteUrl)
            ? "https://www.google.com/search?q=" + Uri.EscapeDataString((abo.AdresseName ?? abo.Name) + " Konto Anmeldung kündigen")
            : abo.WebseiteUrl.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OnResearchClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;
        var query = $"{_selected.Provider} {_selected.Name} Vertrag kündigen Kündigungsfrist";
        var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async void OnRemovePaymentClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null
            || PaymentsList.SelectedItem is not SubscriptionPaymentDisplayRow payment
            || !await ConfirmAsync("Zuordnung entfernen", "Die Zahlung bleibt als Transaktion erhalten."))
            return;
        _database.AboTransaktionEntfernen(_selected.Id, payment.Value.TransaktionId);
        Reload(_selected.Id);
    }

    private void OnOneTimeClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || sender is not CheckBox checkBox
            || checkBox.Tag is not SubscriptionPaymentDisplayRow payment)
            return;
        try
        {
            _database.AboTransaktionEinmaligSetzen(
                _selected.Id,
                payment.Value.TransaktionId,
                checkBox.IsChecked == true);
            Reload(_selected.Id);
        }
        catch (Exception ex)
        {
            ShowStatus("Zahlungsart konnte nicht geändert werden: " + ex.Message, InfoBarSeverity.Error);
            Reload(_selected.Id);
        }
    }

    private async void OnPrintClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var reportRows = _allRows
                .Where(row => row.IsReportable && row.Value.Abo.Status != AboStatus.Beendet)
                .Select(row => row.Value)
                .ToList();
            if (reportRows.Count == 0)
            {
                await MessageAsync("PDF-Bericht", "Für den Bericht sind keine aktiven oder gekündigten Zahlungsserien vorhanden.");
                return;
            }
            var printDialog = new System.Windows.Controls.PrintDialog();
            if (printDialog.ShowDialog() != true)
                return;
            var paginator = SubscriptionReportDocumentBuilder.Build(
                reportRows,
                _categories,
                printDialog.PrintableAreaWidth,
                printDialog.PrintableAreaHeight);
            printDialog.PrintDocument(paginator, "MyCoinFlow – Zahlungsserien");
        }
        catch (Exception ex)
        {
            await MessageAsync("PDF-Bericht", "Der Bericht konnte nicht erstellt werden:\n" + ex.Message);
        }
    }

    private async Task<bool> ConfirmAsync(string title, string text) =>
        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = text,
            PrimaryButtonText = "Ja",
            CloseButtonText = "Nein",
            DefaultButton = ContentDialogButton.Close
        }.ShowAsync() == ContentDialogResult.Primary;

    private async Task MessageAsync(string title, string text) =>
        await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = text, CloseButtonText = "Schließen" }.ShowAsync();

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private static int DirectionOrder(string direction) => direction switch
    {
        Zahlungsrichtungen.Einnahme => 0,
        Zahlungsrichtungen.Ausgabe => 1,
        _ => 2
    };

    private AboKategorie? CategoryFor(string code) =>
        _categoryByCode.TryGetValue(code, out var value) ? value : null;

    private static string Currency(decimal amount) => $"CHF {amount.ToString("N2", Swiss)}";
}
