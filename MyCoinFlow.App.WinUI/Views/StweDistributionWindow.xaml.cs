using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class StweDistributionWindow : PersistentWindow
{
    private static readonly CultureInfo Swiss = CultureInfo.GetCultureInfo("de-CH");
    private readonly DatabaseService _database = new();
    private readonly StweSetRow _set;
    private readonly List<StweEigentuemer> _owners = new();
    private readonly ObservableCollection<StweDistributionRow> _rows = new();
    private readonly List<StweSchluessel> _keys = new();
    private readonly List<StweEnergyDataSetOption> _energySets = new();
    private readonly List<StweEnergyDiffRow> _energyDiffs = new();
    private StweZaehlerdatenSet? _previousEnergySet;
    private string? _lastSource;
    private bool _ready;
    private bool _allowClose;

    private bool IsEditable => !_set.IsClosed;
    private bool IsCredit => _set.IsCredit;
    private decimal SignedTotal => IsCredit ? -Math.Abs(_set.Betrag) : Math.Abs(_set.Betrag);
    private StweSchluessel? SelectedKey => KeyBox.SelectedItem as StweSchluessel;
    private StweEnergyDataSetOption? SelectedEnergySet => EnergySetBox.SelectedItem as StweEnergyDataSetOption;
    private bool IsEnergy => string.Equals(SelectedKey?.Modus?.Trim(), "ENERGIE", StringComparison.OrdinalIgnoreCase);

    public StweDistributionWindow(StweSetRow set)
    {
        InitializeComponent();
        _set = set ?? throw new ArgumentNullException(nameof(set));
        AppWindow.Resize(new SizeInt32(1420, 860));
        if (AppWindow.Presenter is OverlappedPresenter presenter) { presenter.PreferredMinimumWidth = 1080; presenter.PreferredMinimumHeight = 680; }
        HeaderText.Text = $"{_set.Datum:yyyy-MM-dd}  |  Verteil-Stichtag: {_set.VerteilDatum:yyyy-MM-dd}  |  {_set.Titel}";
        SetStatusText.Text = $"Status: {(_set.IsClosed ? "GESCHLOSSEN" : "OFFEN")}  |  Typ: {(IsCredit ? "GUTSCHRIFT" : "BELASTUNG")}  |  Verteil-Stichtag: {_set.VerteilDatum:yyyy-MM-dd}";
        _rows.CollectionChanged += OnRowsChanged;
        LoadOwners();
        LoadExistingLines();
        LoadKeys();
        LoadEnergySets();
        RowsList.ItemsSource = _rows;
        RowsList.IsEnabled = IsEditable;
        SaveButton.IsEnabled = IsEditable;
        AddRowButton.IsEnabled = IsEditable;
        DeleteRowButton.IsEnabled = IsEditable;
        ClearRowsButton.IsEnabled = IsEditable;
        AppWindow.Closing += OnWindowClosing;
        _ready = true;
        ApplyKeyState();
        RefreshTotals();
    }

    private void LoadOwners() => _owners.AddRange(_database.StweEigentuemerGetAll());

    private void LoadExistingLines()
    {
        var sources = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in _database.StweSetLinesGet(_set.Id))
        {
            var source = string.IsNullOrWhiteSpace(line.Schluessel) ? "MANUELL" : line.Schluessel.Trim();
            sources[source] = sources.TryGetValue(source, out var count) ? count + 1 : 1;
            AddRow(new StweDistributionRow(_owners) { OwnerId = line.EigentuemerId, AmountText = line.Betrag.ToString("0.00", CultureInfo.InvariantCulture), Note = line.Notiz, Source = source });
        }
        _lastSource = sources.Where(value => !value.Key.Equals("MANUELL", StringComparison.OrdinalIgnoreCase)).OrderByDescending(value => value.Value).Select(value => value.Key).FirstOrDefault();
    }

    private void LoadKeys()
    {
        var energy = new StweSchluessel { Id = -1, LiegenschaftId = _set.LiegenschaftId, Name = "Energie (Zähler)", Modus = "ENERGIE" };
        _keys.Add(energy);
        _keys.AddRange(_database.StweSchluesselGetByLiegenschaft(_set.LiegenschaftId));
        KeyBox.ItemsSource = _keys;
        StweSchluessel? selected = null;
        if (!string.IsNullOrWhiteSpace(_lastSource))
        {
            if (_lastSource.Equals("ENERGIE", StringComparison.OrdinalIgnoreCase)) selected = energy;
            else if ((_lastSource.StartsWith("MEA:", StringComparison.OrdinalIgnoreCase) || _lastSource.StartsWith("FIX:", StringComparison.OrdinalIgnoreCase)) && int.TryParse(_lastSource[4..], out var id)) selected = _keys.FirstOrDefault(value => value.Id == id);
        }
        selected ??= _keys.FirstOrDefault(value => value.Modus.Equals("MEA", StringComparison.OrdinalIgnoreCase))
                     ?? _keys.FirstOrDefault(value => value.Modus.Equals("FIX", StringComparison.OrdinalIgnoreCase))
                     ?? _keys.FirstOrDefault(value => value.Id != -1) ?? energy;
        KeyBox.SelectedItem = selected;
    }

    private void LoadEnergySets()
    {
        var values = _database.StweZaehlerdatenSetsGetByLiegenschaft(_set.LiegenschaftId).OrderByDescending(value => value.ErfasstAm).ThenByDescending(value => value.Id).ToList();
        if (values.Count == 0) { EnergySetBox.ItemsSource = _energySets; return; }
        var oldest = values.OrderBy(value => value.ErfasstAm).ThenBy(value => value.Id).First();
        foreach (var value in values)
        {
            var label = string.IsNullOrWhiteSpace(value.Notiz) ? value.ErfasstAm.ToString("dd.MM.yyyy") : $"{value.ErfasstAm:dd.MM.yyyy} – {value.Notiz.Trim()}";
            label += value.ErfassungsTyp == 1 ? value.MonatsAnzahl is > 0 ? $" [Monatswerte, {value.MonatsAnzahl} Monate]" : " [Monatswerte]" : " [Differenz]";
            if (value.Id == oldest.Id) label += " (erstes Set)";
            if (value.RechnungKwhTotal is null or <= 0m) label += " [kWh?]";
            _energySets.Add(new StweEnergyDataSetOption { Model = value, DisplayText = label });
        }
        EnergySetBox.ItemsSource = _energySets;
        int? savedId = null;
        try { savedId = _database.StweSetGetEnergieZaehlerdatenSetId(_set.Id); } catch { }
        var selected = savedId.HasValue ? _energySets.FirstOrDefault(value => value.Model.Id == savedId.Value) : null;
        selected ??= _energySets.Where(value => value.Model.ErfasstAm.Date <= _set.VerteilDatum.Date).OrderByDescending(value => value.Model.ErfasstAm).ThenByDescending(value => value.Model.Id).FirstOrDefault();
        EnergySetBox.SelectedItem = selected ?? _energySets.FirstOrDefault();
    }

    private void OnKeyChanged(object sender, SelectionChangedEventArgs e) { if (_ready) ApplyKeyState(); }
    private void ApplyKeyState()
    {
        var energy = IsEnergy;
        EnergySetPanel.Visibility = energy ? Visibility.Visible : Visibility.Collapsed;
        EnergyInfoPanel.Visibility = energy ? Visibility.Visible : Visibility.Collapsed;
        EnergyDiffPanel.Visibility = energy ? Visibility.Visible : Visibility.Collapsed;
        EnergyColumn.Width = energy ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        EnergyButton.Visibility = energy ? Visibility.Visible : Visibility.Collapsed;
        EnergyButton.IsEnabled = IsEditable && energy;
        AutoFixButton.IsEnabled = IsEditable && !energy;
        AutoMeaButton.IsEnabled = IsEditable && !energy;
        if (energy) RefreshEnergyInfo();
    }
    private void OnEnergySetChanged(object sender, SelectionChangedEventArgs e) { if (_ready && IsEnergy) RefreshEnergyInfo(); }

    private void RefreshEnergyInfo()
    {
        _energyDiffs.Clear();
        _previousEnergySet = null;
        var option = SelectedEnergySet;
        if (!IsEnergy || option is null)
        {
            EnergyPeriodText.Text = "—"; EnergyNoteText.Text = "—"; EnergyInvoiceText.Text = "—"; EnergyCreditText.Text = "—"; EnergyDiffList.ItemsSource = null; return;
        }
        var current = option.Model;
        var monthly = current.ErfassungsTyp == 1;
        if (!monthly) _previousEnergySet = _database.StweZaehlerdatenGetPreviousSet(_set.LiegenschaftId, current.ErfasstAm, current.Id);
        if (monthly)
        {
            var months = current.MonatsAnzahl is > 0 ? $" ({current.MonatsAnzahl} Monat(e))" : string.Empty;
            EnergyPeriodText.Text = $"Monatswerte bis {current.ErfasstAm:dd.MM.yyyy}{months}";
        }
        else EnergyPeriodText.Text = $"{(_previousEnergySet is null ? "—" : _previousEnergySet.ErfasstAm.ToString("dd.MM.yyyy"))} – {current.ErfasstAm:dd.MM.yyyy}";
        EnergyNoteText.Text = string.IsNullOrWhiteSpace(current.Notiz) ? "—" : current.Notiz.Trim();
        EnergyInvoiceText.Text = current.RechnungKwhTotal?.ToString("0.###", CultureInfo.InvariantCulture) ?? "—";
        EnergyCreditText.Text = current.GutschriftChf?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—";
        var currentLines = _database.StweZaehlerdatenLinesGetBySet(current.Id).ToDictionary(value => value.ZaehlerId, value => value.NeuWert);
        var previousLines = !monthly && _previousEnergySet is not null ? _database.StweZaehlerdatenLinesGetBySet(_previousEnergySet.Id).ToDictionary(value => value.ZaehlerId, value => value.NeuWert) : new Dictionary<int, decimal>();
        foreach (var meter in _database.StweZaehlerGetByLiegenschaft(_set.LiegenschaftId))
        {
            currentLines.TryGetValue(meter.Id, out var currentValue); previousLines.TryGetValue(meter.Id, out var previousValue);
            _energyDiffs.Add(new StweEnergyDiffRow { MeterId = meter.Id, Type = meter.Typ, Name = meter.Name, UnitId = meter.EinheitId, AllocationKeyId = meter.SchluesselId, OldValue = monthly ? 0m : previousValue, NewValue = currentValue });
        }
        EnergyDiffList.ItemsSource = null; EnergyDiffList.ItemsSource = _energyDiffs;
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) foreach (StweDistributionRow row in e.NewItems) row.PropertyChanged += OnRowChanged;
        if (e.OldItems is not null) foreach (StweDistributionRow row in e.OldItems) row.PropertyChanged -= OnRowChanged;
        RefreshTotals();
    }
    private void AddRow(StweDistributionRow row) => _rows.Add(row);
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(StweDistributionRow.AmountText) or nameof(StweDistributionRow.Amount)) RefreshTotals(); }
    private void OnAddRowClick(object sender, RoutedEventArgs e) { if (IsEditable) AddRow(new StweDistributionRow(_owners) { AmountText = IsCredit ? "-0.00" : "0.00", Source = "MANUELL" }); }
    private void OnDeleteRowClick(object sender, RoutedEventArgs e) { if (IsEditable && RowsList.SelectedItem is StweDistributionRow row) _rows.Remove(row); }
    private void OnClearRowsClick(object sender, RoutedEventArgs e) { if (IsEditable) _rows.Clear(); }

    private async void OnAutoFixClick(object sender, RoutedEventArgs e)
    {
        if (!IsEditable) return;
        if (SelectedKey is null) { await ShowMessageAsync("Auto verteilen", "Bitte zuerst einen Schlüssel auswählen."); return; }
        if (!SelectedKey.Modus.Equals("FIX", StringComparison.OrdinalIgnoreCase)) { await ShowMessageAsync("Auto verteilen", "Dieser Schlüssel ist nicht FIX."); return; }
        var lines = _database.StweSchluesselLinesGet(SelectedKey.Id);
        if (lines.Count == 0) { await ShowMessageAsync("Auto verteilen", "Dieser FIX-Schlüssel hat noch keine Zeilen.\n\nBitte unter „Liegenschaften → Schlüssel“ erfassen."); return; }
        var sum = lines.Sum(value => value.AnteilProzent);
        if (Math.Abs(sum - 100m) > 0.0001m) { await ShowMessageAsync("Auto verteilen", $"Schlüssel ist ungültig: Summe ist {sum:N4}% (muss 100.0000% sein)."); return; }
        var values = new Dictionary<int, decimal>(); var missing = new List<string>();
        foreach (var line in lines)
        {
            var ownerId = ResolveOwner(line.EinheitId, line.EigentuemerId);
            if (!ownerId.HasValue) { missing.Add(!string.IsNullOrWhiteSpace(line.EinheitBezeichnung) ? line.EinheitBezeichnung : line.EigentuemerName); continue; }
            values[ownerId.Value] = values.TryGetValue(ownerId.Value, out var current) ? current + SignedTotal * line.AnteilProzent / 100m : SignedTotal * line.AnteilProzent / 100m;
        }
        if (missing.Count > 0) { await ShowMissingOwnersAsync("Auto verteilen", missing); return; }
        ApplyRounded(values.Select(value => new RawShare(value.Key, value.Value)).ToList(), $"Auto (FIX): {SelectedKey.Name}", $"FIX:{SelectedKey.Id}");
    }

    private async void OnAutoMeaClick(object sender, RoutedEventArgs e)
    {
        if (!IsEditable) return;
        if (SelectedKey is null) { await ShowMessageAsync("Auto verteilen", "Bitte zuerst einen Schlüssel auswählen."); return; }
        if (!SelectedKey.Modus.Equals("MEA", StringComparison.OrdinalIgnoreCase)) { await ShowMessageAsync("Auto verteilen", "Dieser Schlüssel ist nicht MEA."); return; }
        var shares = await GetOwnerMeaAsync("Auto verteilen");
        if (shares is null) return;
        var sum = shares.Values.Sum();
        ApplyRounded(shares.Select(value => new RawShare(value.Key, SignedTotal * value.Value / sum)).ToList(), $"Auto (MEA): {SelectedKey.Name}", $"MEA:{SelectedKey.Id}");
    }

    private async Task<Dictionary<int, decimal>?> GetOwnerMeaAsync(string title)
    {
        var units = _database.StweEinheitenGetByLiegenschaft(_set.LiegenschaftId).Where(value => value.MeaPromille is > 0m).ToList();
        if (units.Count == 0) { await ShowMessageAsync(title, "Keine Einheiten mit MEA (‰) gefunden.\n\nBitte MEA bei den Einheiten erfassen."); return null; }
        var values = new Dictionary<int, decimal>(); var missing = new List<string>();
        foreach (var unit in units)
        {
            var ownerId = _database.StweEigentuemerGetByEinheitAtDate(unit.Id, _set.VerteilDatum);
            if (!ownerId.HasValue) { missing.Add(unit.Bezeichnung); continue; }
            values[ownerId.Value] = values.TryGetValue(ownerId.Value, out var current) ? current + unit.MeaPromille!.Value : unit.MeaPromille!.Value;
        }
        if (missing.Count > 0) { await ShowMissingOwnersAsync(title, missing, true); return null; }
        if (values.Values.Sum() <= 0m) { await ShowMessageAsync(title, "Summe MEA ist 0 – keine Verteilung möglich."); return null; }
        return values;
    }

    private async void OnEnergyClick(object sender, RoutedEventArgs e)
    {
        if (!IsEditable) return;
        if (!IsEnergy) { await ShowMessageAsync("Energie berechnen", "Bitte zuerst den Schlüssel „ENERGIE“ wählen."); return; }
        var option = SelectedEnergySet;
        if (option is null) { await ShowMessageAsync("Energie berechnen", "Bitte zuerst ein Zählerdaten-Set auswählen."); return; }
        if (option.Model.RechnungKwhTotal is null or <= 0m) { await ShowMessageAsync("Energie berechnen", "Im Zählerdaten-Set fehlt „Rechnung kWh total“.\n\nBitte unter „Zählerdaten“ nachtragen."); return; }
        try { _database.StweSetUpdateEnergieZaehlerdatenSetId(_set.Id, option.Model.Id); } catch { }
        if (_rows.Count > 0 && !await ConfirmAsync("Energie berechnen", "Die Energie-Berechnung ersetzt die bestehenden Verteilzeilen.\n\nMöchtest du fortfahren?")) return;
        if (_energyDiffs.Count == 0) RefreshEnergyInfo();
        if (IsCredit) { await ApplyEnergyMeaOnlyAsync(); return; }
        var ownerMea = await GetOwnerMeaAsync("Energie berechnen");
        if (ownerMea is null) return;
        var sumMea = ownerMea.Values.Sum();
        var price = SignedTotal / option.Model.RechnungKwhTotal.Value;
        var ownerAmounts = new Dictionary<int, decimal>();
        var ownerNotes = new Dictionary<int, List<string>>();
        void Add(int ownerId, decimal amount, string note) { ownerAmounts[ownerId] = ownerAmounts.TryGetValue(ownerId, out var current) ? current + amount : amount; if (!ownerNotes.TryGetValue(ownerId, out var notes)) ownerNotes[ownerId] = notes = new(); notes.Add(note); }
        foreach (var diffRow in _energyDiffs)
        {
            var difference = diffRow.DifferenceKwh;
            if (difference <= 0m || diffRow.Type.Trim().Equals("EVU", StringComparison.OrdinalIgnoreCase)) continue;
            var meterTotal = difference * price;
            var type = diffRow.Type.Trim().ToUpperInvariant();
            if (type == "DIREKT")
            {
                if (diffRow.UnitId is null or <= 0) { await ShowMessageAsync("Energie berechnen", $"DIREKT-Zähler „{diffRow.Name}“ hat keine Einheit."); return; }
                var ownerId = _database.StweEigentuemerGetByEinheitAtDate(diffRow.UnitId.Value, _set.VerteilDatum);
                if (!ownerId.HasValue) { await ShowMessageAsync("Energie berechnen", $"Für DIREKT-Zähler „{diffRow.Name}“ ist am Verteil-Stichtag ({_set.VerteilDatum:yyyy-MM-dd}) kein Eigentümer zugeordnet."); return; }
                Add(ownerId.Value, meterTotal, $"{diffRow.Name}: DIREKT {difference:0.###}kWh × {price:0.####} = {meterTotal:0.00}");
            }
            else if (type is "ALLG" or "HEIZ")
            {
                foreach (var owner in ownerMea) { var part = meterTotal * owner.Value / sumMea; Add(owner.Key, part, $"{diffRow.Name}: {type} {difference:0.###}kWh × {price:0.####} × MEA {owner.Value:0.###}/{sumMea:0.###} = {part:0.00}"); }
            }
            else { await ShowMessageAsync("Energie berechnen", $"Unbekannter Zählertyp „{diffRow.Type}“ bei Zähler „{diffRow.Name}“."); return; }
        }
        var sumOwner = ownerAmounts.Values.Sum();
        if (sumOwner == 0m) { await ShowMessageAsync("Energie berechnen", "Es konnte kein Betrag berechnet werden (Summe = 0)."); return; }
        var scale = SignedTotal / sumOwner;
        var notesByOwner = ownerNotes.ToDictionary(value => value.Key, value => { var raw = ownerAmounts[value.Key]; return string.Join(" | ", value.Value.Take(6)) + (value.Value.Count > 6 ? " | …" : string.Empty) + $" | Sum={raw:0.00}" + (Math.Abs(scale - 1m) > 0.0000001m ? $" | Scale×{scale:0.######}" : string.Empty); });
        ApplyRounded(ownerAmounts.Select(value => new RawShare(value.Key, value.Value * scale)).ToList(), notesByOwner, "ENERGIE");
    }

    private async Task ApplyEnergyMeaOnlyAsync()
    {
        var shares = await GetOwnerMeaAsync("Energie berechnen");
        if (shares is null) return;
        var sum = shares.Values.Sum();
        var notes = shares.ToDictionary(value => value.Key, value => $"MEA {value.Value:0.###}/{sum:0.###} → {SignedTotal:0.00}");
        ApplyRounded(shares.Select(value => new RawShare(value.Key, SignedTotal * value.Value / sum)).ToList(), notes, "ENERGIE");
    }

    private int? ResolveOwner(int? unitId, int fallbackOwnerId)
    {
        if (unitId is > 0) return _database.StweEigentuemerGetByEinheitAtDate(unitId.Value, _set.VerteilDatum);
        return fallbackOwnerId > 0 ? fallbackOwnerId : null;
    }

    private void ApplyRounded(List<RawShare> raw, string note, string source) => ApplyRounded(raw, raw.ToDictionary(value => value.OwnerId, _ => note), source);
    private void ApplyRounded(List<RawShare> raw, Dictionary<int, string> notes, string source)
    {
        var values = raw.Select(value => (value.OwnerId, Amount: Math.Round(value.RawAmount, 2, MidpointRounding.AwayFromZero))).ToList();
        var difference = SignedTotal - values.Sum(value => value.Amount);
        if (difference != 0m && values.Count > 0)
        {
            var index = values.Select((value, i) => (Abs: Math.Abs(value.Amount), Index: i)).OrderByDescending(value => value.Abs).First().Index;
            var item = values[index]; values[index] = (item.OwnerId, item.Amount + difference);
            if (notes.TryGetValue(item.OwnerId, out var old)) notes[item.OwnerId] = $"{old} | Diff {difference:+0.00;-0.00;0.00}";
        }
        _rows.Clear();
        foreach (var value in values) AddRow(new StweDistributionRow(_owners) { OwnerId = value.OwnerId, AmountText = value.Amount.ToString("0.00", CultureInfo.InvariantCulture), Note = notes.TryGetValue(value.OwnerId, out var note) ? note : null, Source = source });
        RefreshTotals();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!IsEditable) return;
        var error = ValidateRows();
        if (error is not null) { await ShowMessageAsync("Set verteilen", error); return; }
        try { SaveRows(); ShowStatus("Verteilung gespeichert.", InfoBarSeverity.Success); }
        catch (Exception exception) { await ShowMessageAsync("Set verteilen", "Speichern fehlgeschlagen:\n" + exception.Message); }
    }
    private void SaveRows()
    {
        _database.StweSetLinesDeleteBySet(_set.Id);
        foreach (var row in _rows) _database.StweSetLineInsert(_set.Id, null, row.OwnerId, row.Source, row.Amount, row.Note);
    }
    private string? ValidateRows()
    {
        if (_rows.Count == 0) return null;
        if (_rows.Any(value => value.OwnerId is null or <= 0)) return "Bitte in allen Zeilen einen Eigentümer wählen.";
        if (_rows.GroupBy(value => value.OwnerId!.Value).Any(value => value.Count() > 1)) return "Ein Eigentümer darf im Set nur einmal vorkommen (V1).";
        if (IsCredit && _rows.Any(value => value.Amount > 0m)) return "Bei Gutschriften müssen die Zeilenbeträge negativ sein.";
        if (!IsCredit && _rows.Any(value => value.Amount < 0m)) return "Bei Belastungen dürfen die Zeilenbeträge nicht negativ sein.";
        var sum = _rows.Sum(value => value.Amount);
        if (!IsCredit && sum > SignedTotal + 0.0001m) return "Summe der Zeilen darf den Set-Betrag nicht überschreiten.";
        if (IsCredit && sum < SignedTotal - 0.0001m) return "Summe der Zeilen darf den (negativen) Set-Betrag nicht unterschreiten.";
        return null;
    }
    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || !IsEditable) return;
        var error = ValidateRows();
        if (error is not null) { args.Cancel = true; _ = ShowMessageAsync("Set verteilen", error); return; }
        try { SaveRows(); }
        catch (Exception exception) { args.Cancel = true; _ = ShowMessageAsync("Set verteilen", "Speichern fehlgeschlagen:\n" + exception.Message); }
    }
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (!IsEditable) { _allowClose = true; Close(); return; }
        var error = ValidateRows();
        if (error is not null) { _ = ShowMessageAsync("Set verteilen", error); return; }
        try { SaveRows(); _allowClose = true; Close(); }
        catch (Exception exception) { _ = ShowMessageAsync("Set verteilen", "Speichern fehlgeschlagen:\n" + exception.Message); }
    }
    private void RefreshTotals()
    {
        TotalText.Text = $"Total: {SignedTotal.ToString("C", Swiss)}";
        var distributed = _rows.Sum(value => value.Amount);
        DistributedText.Text = $"Verteilt: {distributed.ToString("C", Swiss)}";
        RestText.Text = $"Rest: {(SignedTotal - distributed).ToString("C", Swiss)}";
    }
    private async Task ShowMissingOwnersAsync(string title, List<string> missing, bool addHint = false)
    {
        var message = $"Für folgende Einheiten ist am Verteil-Stichtag ({_set.VerteilDatum:yyyy-MM-dd}) kein Eigentümer zugeordnet:\n\n• " + string.Join("\n• ", missing.Take(10)) + (missing.Count > 10 ? "\n…" : string.Empty) + (addHint ? "\n\nBitte unter „Liegenschaften → Eigentümer & Zuordnung“ nachpflegen." : string.Empty);
        await ShowMessageAsync(title, message);
    }
    private async Task<bool> ConfirmAsync(string title, string message) => await new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = title, Content = message, PrimaryButtonText = "Ja", CloseButtonText = "Nein", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Primary;
    private async Task ShowMessageAsync(string title, string message) { if (RootGrid.XamlRoot is null) return; await new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = title, Content = message, CloseButtonText = "Schließen" }.ShowAsync(); }
    private void ShowStatus(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
    private readonly record struct RawShare(int OwnerId, decimal RawAmount);
}
