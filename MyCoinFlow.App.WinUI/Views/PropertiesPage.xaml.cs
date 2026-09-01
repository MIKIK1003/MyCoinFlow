using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class PropertiesPage : Page
{
    private readonly DatabaseService _database = new();
    private List<PropertyDisplayRow> _properties = new();
    private List<PropertyOwnerDisplayRow> _owners = new();
    private List<PropertyUnitDisplayRow> _units = new();
    private List<OwnershipDisplayRow> _ownerships = new();
    private List<AllocationKeyDisplayRow> _keys = new();
    private List<MeterDisplayRow> _meters = new();
    private bool _initialized;
    private bool _loading;
    private bool _suppressSelectionChanges;

    public PropertiesPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private PropertyDisplayRow? SelectedProperty => PropertyBox.SelectedItem as PropertyDisplayRow;
    private PropertyUnitDisplayRow? SelectedUnit => UnitsList.SelectedItem as PropertyUnitDisplayRow;
    private PropertyOwnerDisplayRow? SelectedOwner => OwnersList.SelectedItem as PropertyOwnerDisplayRow;
    private OwnershipDisplayRow? SelectedOwnership => OwnershipsList.SelectedItem as OwnershipDisplayRow;
    private AllocationKeyDisplayRow? SelectedKey => KeysList.SelectedItem as AllocationKeyDisplayRow;
    private MeterDisplayRow? SelectedMeter => MetersList.SelectedItem as MeterDisplayRow;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        await LoadAllAsync();
    }

    private async Task LoadAllAsync(int? propertyId = null)
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var data = await Task.Run(() =>
            {
                _database.EnsureStweSchema();
                return new
                {
                    Properties = _database.StweLiegenschaftenGetAll(),
                    Owners = _database.StweEigentuemerGetAll()
                };
            });
            propertyId ??= SelectedProperty?.Id;
            var unitId = SelectedUnit?.Id;
            var ownershipId = SelectedOwnership?.Id;
            var keyId = SelectedKey?.Id;
            var meterId = SelectedMeter?.Id;
            _properties = data.Properties.Select(value => new PropertyDisplayRow(value)).ToList();
            _owners = data.Owners.Select(value => new PropertyOwnerDisplayRow(value)).ToList();
            OwnersList.ItemsSource = _owners;
            OwnersCountText.Text = $"{_owners.Count:N0} Eigentümer im globalen Stamm";

            _suppressSelectionChanges = true;
            PropertyBox.ItemsSource = _properties;
            PropertyBox.SelectedItem = propertyId.HasValue
                ? _properties.FirstOrDefault(row => row.Id == propertyId.Value)
                : _properties.FirstOrDefault();
            _suppressSelectionChanges = false;
            await LoadPropertyChildrenAsync(unitId, keyId, meterId, ownershipId);
            ShowStatus(_properties.Count == 0
                ? "Noch keine Liegenschaften vorhanden."
                : $"{_properties.Count:N0} Liegenschaften geladen.", InfoBarSeverity.Informational);
        }
        catch (Exception exception)
        {
            ShowStatus("Liegenschaften konnten nicht geladen werden: " + exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _loading = false;
            UpdateActions();
        }
    }

    private async Task LoadPropertyChildrenAsync(
        int? unitId = null,
        int? keyId = null,
        int? meterId = null,
        int? ownershipId = null)
    {
        var property = SelectedProperty;
        if (property is null)
        {
            _units.Clear();
            _ownerships.Clear();
            _keys.Clear();
            _meters.Clear();
            UnitsList.ItemsSource = null;
            OwnershipsList.ItemsSource = null;
            KeysList.ItemsSource = null;
            MetersList.ItemsSource = null;
            UpdateContextOverview();
            UpdateActions();
            return;
        }

        try
        {
            var data = await Task.Run(() => new
            {
                Units = _database.StweEinheitenGetByLiegenschaft(property.Id),
                Keys = _database.StweSchluesselGetByLiegenschaft(property.Id),
                Meters = _database.StweZaehlerGetByLiegenschaft(property.Id)
            });
            _units = data.Units.Select(value => new PropertyUnitDisplayRow(value)).ToList();
            _keys = data.Keys.Select(value => new AllocationKeyDisplayRow(value)).ToList();
            var unitNames = _units.ToDictionary(row => row.Id, row => row.Name);
            var keyNames = _keys.ToDictionary(row => row.Id, row => row.Name);
            _meters = data.Meters.Select(value => new MeterDisplayRow(value, unitNames, keyNames)).ToList();

            _suppressSelectionChanges = true;
            UnitsList.ItemsSource = _units;
            KeysList.ItemsSource = _keys;
            MetersList.ItemsSource = _meters;
            UnitsList.SelectedItem = unitId.HasValue ? _units.FirstOrDefault(row => row.Id == unitId.Value) : _units.FirstOrDefault();
            KeysList.SelectedItem = keyId.HasValue ? _keys.FirstOrDefault(row => row.Id == keyId.Value) : _keys.FirstOrDefault();
            MetersList.SelectedItem = meterId.HasValue ? _meters.FirstOrDefault(row => row.Id == meterId.Value) : _meters.FirstOrDefault();
            _suppressSelectionChanges = false;
            await LoadOwnershipsAsync(ownershipId);
        }
        catch (Exception exception)
        {
            ShowStatus("STWE-Stammdaten konnten nicht geladen werden: " + exception.Message, InfoBarSeverity.Error);
        }
        UpdateContextOverview();
        UpdateActions();
    }

    private async Task LoadOwnershipsAsync(int? ownershipId = null)
    {
        var unit = SelectedUnit;
        if (unit is null)
        {
            _ownerships.Clear();
            OwnershipsList.ItemsSource = null;
            UpdateContextOverview();
            return;
        }
        var values = await Task.Run(() => _database.StweEinheitEigentumGetByEinheit(unit.Id));
        _ownerships = values.Select(value => new OwnershipDisplayRow(value)).ToList();
        _suppressSelectionChanges = true;
        OwnershipsList.ItemsSource = _ownerships;
        OwnershipsList.SelectedItem = ownershipId.HasValue
            ? _ownerships.FirstOrDefault(row => row.Id == ownershipId.Value)
            : _ownerships.FirstOrDefault();
        _suppressSelectionChanges = false;
        UpdateContextOverview();
        UpdateActions();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadAllAsync();

    private async void OnPropertyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanges || !_initialized) return;
        await LoadPropertyChildrenAsync();
    }

    private async void OnUnitSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanges) return;
        await LoadOwnershipsAsync();
    }

    private void OnRelatedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanges) return;
        UpdateContextOverview();
        UpdateActions();
    }

    private void UpdateActions()
    {
        var hasProperty = SelectedProperty is not null;
        EditPropertyButton.IsEnabled = hasProperty;
        DeletePropertyButton.IsEnabled = hasProperty;

        NewUnitButton.IsEnabled = hasProperty;
        EditUnitButton.IsEnabled = SelectedUnit is not null;
        DeleteUnitButton.IsEnabled = SelectedUnit is not null;

        NewOwnershipButton.IsEnabled = SelectedUnit is not null;
        EditOwnershipButton.IsEnabled = SelectedUnit is not null && SelectedOwnership is not null;
        DeleteOwnershipButton.IsEnabled = SelectedOwnership is not null;

        EditOwnerButton.IsEnabled = SelectedOwner is not null;
        DeleteOwnerButton.IsEnabled = SelectedOwner is not null;

        NewKeyButton.IsEnabled = hasProperty;
        EditKeyButton.IsEnabled = SelectedKey is not null;
        DeleteKeyButton.IsEnabled = SelectedKey is not null;
        KeyLinesButton.IsEnabled = SelectedKey is not null &&
            (SelectedKey.Mode.Equals("FIX", StringComparison.OrdinalIgnoreCase) ||
             SelectedKey.Mode.Equals("ENERGIE", StringComparison.OrdinalIgnoreCase));

        NewMeterButton.IsEnabled = hasProperty;
        EditMeterButton.IsEnabled = SelectedMeter is not null;
        DeleteMeterButton.IsEnabled = SelectedMeter is not null;
    }

    private void UpdateContextOverview()
    {
        var property = SelectedProperty;
        PropertyAddressText.Text = property is null
            ? ""
            : string.Join(" · ", new[] { property.Street, property.Location, property.Note }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        PropertyContextTitle.Text = property?.Name ?? "Keine Liegenschaft ausgewählt";
        PropertyContextSubtitle.Text = property is null
            ? "Bitte eine Liegenschaft auswählen oder neu erfassen"
            : $"{_units.Count:N0} Einheiten · {_keys.Count:N0} Schlüssel · {_meters.Count:N0} Zähler";

        UnitsCountText.Text = property is null
            ? "Keine Liegenschaft ausgewählt"
            : $"{_units.Count:N0} Einheiten in {property.Name}";
        KeysCountText.Text = property is null
            ? "Keine Liegenschaft ausgewählt"
            : $"{_keys.Count:N0} Schlüssel in {property.Name}";
        MetersCountText.Text = property is null
            ? "Keine Liegenschaft ausgewählt"
            : $"{_meters.Count:N0} Zähler in {property.Name}";
        OwnersCountText.Text = $"{_owners.Count:N0} Eigentümer im globalen Stamm";

        var unit = SelectedUnit;
        UnitContextTitle.Text = unit?.Name ?? "Keine Einheit ausgewählt";
        UnitContextSubtitle.Text = unit is null
            ? "Die Auswahl in der Einheitenliste steuert die Zuordnungen"
            : string.Join(" · ", new[]
            {
                unit.Type,
                string.IsNullOrWhiteSpace(unit.Mea) ? null : $"MEA {unit.Mea} ‰",
                string.IsNullOrWhiteSpace(unit.Area) ? null : $"{unit.Area} m²"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        OwnershipsCountText.Text = unit is null
            ? "Zuerst links eine Einheit auswählen"
            : $"{_ownerships.Count:N0} Zeiträume für {unit.Name}";

        var today = DateTime.Today;
        var activeOwners = _ownerships
            .Where(row => row.Value.GueltigVon.Date <= today &&
                          (!row.Value.GueltigBis.HasValue || row.Value.GueltigBis.Value.Date >= today))
            .Select(row => row.Owner)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        OwnershipContextTitle.Text = unit is null
            ? "Keine Einheit ausgewählt"
            : activeOwners.Count == 0
                ? "Heute keine Zuordnung wirksam"
                : string.Join(", ", activeOwners);
        OwnershipContextSubtitle.Text = unit is null
            ? ""
            : $"Stichtag {today:dd.MM.yyyy} · {_ownerships.Count:N0} erfasste Zeiträume";

        var key = SelectedKey;
        if (key is null)
        {
            KeyRelationshipText.Text = "Ein Schlüssel definiert die Verteilung; Zähler können auf ihn verweisen.";
        }
        else
        {
            var linkedMeters = _meters.Count(row => row.Value.SchluesselId == key.Id);
            KeyRelationshipText.Text = $"Ausgewählt: {key.Name} ({key.Mode}) · von {linkedMeters:N0} Zählern verwendet";
        }

        var meter = SelectedMeter;
        MeterRelationshipText.Text = meter is null
            ? "Zähler können direkt einer Einheit und/oder einem Verteilschlüssel zugeordnet sein."
            : $"Ausgewählt: {meter.Name} · Einheit: {meter.Unit} · Schlüssel: {meter.AllocationKey}";
    }

    private async Task RunActionAsync(Func<Task> action, string failurePrefix)
    {
        try { await action(); }
        catch (Exception exception) { ShowStatus(failurePrefix + exception.Message, InfoBarSeverity.Error); }
    }

    private async void OnNewPropertyClick(object sender, RoutedEventArgs e) => await RunActionAsync(() => EditPropertyAsync(null), "Speichern fehlgeschlagen: ");
    private async void OnEditPropertyClick(object sender, RoutedEventArgs e) => await RunActionAsync(() => EditPropertyAsync(SelectedProperty), "Bearbeiten fehlgeschlagen: ");
    private async void OnNewUnitClick(object sender, RoutedEventArgs e) => await RunActionAsync(() => EditUnitAsync(null), "Speichern fehlgeschlagen: ");
    private async void OnEditUnitClick(object sender, RoutedEventArgs e) => await RunActionAsync(() => EditUnitAsync(SelectedUnit), "Bearbeiten fehlgeschlagen: ");
    private async void OnNewOwnerClick(object sender, RoutedEventArgs e) => await RunActionAsync(() => EditOwnerAsync(null), "Speichern fehlgeschlagen: ");
    private async void OnEditOwnerClick(object sender, RoutedEventArgs e) => await RunActionAsync(() => EditOwnerAsync(SelectedOwner), "Bearbeiten fehlgeschlagen: ");
    private async void OnNewOwnershipClick(object sender, RoutedEventArgs e) => await RunActionAsync(() => EditOwnershipAsync(null), "Zuordnen fehlgeschlagen: ");
    private async void OnEditOwnershipClick(object sender, RoutedEventArgs e) => await RunActionAsync(() => EditOwnershipAsync(SelectedOwnership), "Bearbeiten fehlgeschlagen: ");
    private async void OnNewKeyClick(object sender, RoutedEventArgs e) => await RunActionAsync(() => EditKeyAsync(null), "Speichern fehlgeschlagen: ");
    private async void OnEditKeyClick(object sender, RoutedEventArgs e) => await RunActionAsync(RenameKeyAsync, "Umbenennen fehlgeschlagen: ");
    private async void OnNewMeterClick(object sender, RoutedEventArgs e) => await RunActionAsync(() => EditMeterAsync(null), "Speichern fehlgeschlagen: ");
    private async void OnEditMeterClick(object sender, RoutedEventArgs e) => await RunActionAsync(() => EditMeterAsync(SelectedMeter), "Bearbeiten fehlgeschlagen: ");

    private async Task EditPropertyAsync(PropertyDisplayRow? row)
    {
        var dialog = new PropertyEditorDialog(row?.Value) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        var id = await Task.Run(() =>
        {
            if (row is null) return _database.StweLiegenschaftInsert(dialog.Result);
            _database.StweLiegenschaftUpdate(dialog.Result); return row.Id;
        });
        await LoadAllAsync(id);
        ShowStatus(row is null ? "Liegenschaft gespeichert." : "Liegenschaft aktualisiert.", InfoBarSeverity.Success);
    }

    private async Task EditUnitAsync(PropertyUnitDisplayRow? row)
    {
        if (SelectedProperty is null) return;
        var dialog = new PropertyUnitEditorDialog(SelectedProperty.Id, row?.Value) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        var id = await Task.Run(() =>
        {
            if (row is null) return _database.StweEinheitInsert(dialog.Result);
            _database.StweEinheitUpdate(dialog.Result); return row.Id;
        });
        await LoadPropertyChildrenAsync(id, SelectedKey?.Id, SelectedMeter?.Id, row is null ? null : SelectedOwnership?.Id);
        ShowStatus(row is null ? "Einheit gespeichert." : "Einheit aktualisiert.", InfoBarSeverity.Success);
    }

    private async Task EditOwnerAsync(PropertyOwnerDisplayRow? row)
    {
        var dialog = new PropertyOwnerEditorDialog(row?.Value) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        await Task.Run(() =>
        {
            if (row is null) _database.StweEigentuemerInsert(dialog.Result);
            else _database.StweEigentuemerUpdate(dialog.Result);
        });
        await LoadAllAsync(SelectedProperty?.Id);
        ShowStatus(row is null ? "Eigentümer gespeichert." : "Eigentümer aktualisiert.", InfoBarSeverity.Success);
    }

    private async Task EditOwnershipAsync(OwnershipDisplayRow? row)
    {
        if (SelectedUnit is null) return;
        var owners = _owners.Select(owner => owner.Value).ToList();
        var dialog = new OwnershipEditorDialog(owners, row?.Value) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || !dialog.Accepted) return;
        var id = await Task.Run(() =>
        {
            if (row is null)
                return _database.StweEinheitEigentumInsert(SelectedUnit.Id, dialog.OwnerId, dialog.From, dialog.To);
            _database.StweEinheitEigentumUpdate(row.Id, SelectedUnit.Id, dialog.OwnerId, dialog.From, dialog.To);
            return row.Id;
        });
        await LoadOwnershipsAsync(id);
        ShowStatus(row is null ? "Zuordnung gespeichert." : "Zuordnung aktualisiert.", InfoBarSeverity.Success);
    }

    private async Task EditKeyAsync(AllocationKeyDisplayRow? row)
    {
        if (SelectedProperty is null || row is not null) return;
        var dialog = new AllocationKeyEditorDialog(SelectedProperty.Id) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        var id = await Task.Run(() => _database.StweSchluesselInsert(
            SelectedProperty.Id, dialog.Result.Name, dialog.Result.Modus));
        await LoadPropertyChildrenAsync(SelectedUnit?.Id, id, SelectedMeter?.Id, SelectedOwnership?.Id);
        ShowStatus("Schlüssel gespeichert.", InfoBarSeverity.Success);
    }

    private async Task RenameKeyAsync()
    {
        if (SelectedKey is null) return;
        var dialog = new TextValueDialog("Schlüssel umbenennen", "Schlüssel umbenennen", "Neue Bezeichnung", SelectedKey.Name)
        { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || !dialog.Accepted) return;
        await Task.Run(() => _database.StweSchluesselRename(SelectedKey.Id, dialog.Value));
        await LoadPropertyChildrenAsync(SelectedUnit?.Id, SelectedKey.Id, SelectedMeter?.Id, SelectedOwnership?.Id);
        ShowStatus("Schlüssel umbenannt.", InfoBarSeverity.Success);
    }

    private async Task EditMeterAsync(MeterDisplayRow? row)
    {
        if (SelectedProperty is null) return;
        var units = (UnitsList.ItemsSource as IEnumerable<PropertyUnitDisplayRow> ?? Array.Empty<PropertyUnitDisplayRow>())
            .Select(item => item.Value).ToList();
        var owners = _owners.Select(owner => owner.Value).ToList();
        var existingLines = row is null
            ? new List<StweZaehlerLine>()
            : await Task.Run(() => _database.StweZaehlerLinesGet(row.Id));
        var dialog = new MeterEditorDialog(SelectedProperty.Id, units, owners, row?.Value, existingLines)
        { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        var id = await Task.Run(() =>
        {
            var meterId = row is null ? _database.StweZaehlerInsert(dialog.Result) : row.Id;
            if (row is not null) _database.StweZaehlerUpdate(dialog.Result);
            _database.StweZaehlerLinesReplace(meterId, dialog.ResultLines);
            return meterId;
        });
        await LoadPropertyChildrenAsync(SelectedUnit?.Id, SelectedKey?.Id, id, SelectedOwnership?.Id);
        ShowStatus(row is null ? "Zähler gespeichert." : "Zähler aktualisiert.", InfoBarSeverity.Success);
    }

    private async Task RunDeleteAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) { ShowStatus("Löschen fehlgeschlagen: " + exception.Message, InfoBarSeverity.Warning); }
    }

    private async void OnDeletePropertyClick(object sender, RoutedEventArgs e) => await RunDeleteAsync(DeletePropertyAsync);
    private async void OnDeleteUnitClick(object sender, RoutedEventArgs e) => await RunDeleteAsync(DeleteUnitAsync);
    private async void OnDeleteOwnerClick(object sender, RoutedEventArgs e) => await RunDeleteAsync(DeleteOwnerAsync);
    private async void OnDeleteOwnershipClick(object sender, RoutedEventArgs e) => await RunDeleteAsync(DeleteOwnershipAsync);
    private async void OnDeleteKeyClick(object sender, RoutedEventArgs e) => await RunDeleteAsync(DeleteKeyAsync);
    private async void OnDeleteMeterClick(object sender, RoutedEventArgs e) => await RunDeleteAsync(DeleteMeterAsync);

    private async Task DeletePropertyAsync()
    {
        if (SelectedProperty is null || !await ConfirmAsync("Liegenschaft löschen?", $"Liegenschaft „{SelectedProperty.Name}“ wirklich löschen?")) return;
        await Task.Run(() => _database.StweLiegenschaftDelete(SelectedProperty.Id));
        await LoadAllAsync();
    }
    private async Task DeleteUnitAsync()
    {
        if (SelectedUnit is null || !await ConfirmAsync("Einheit löschen?", $"Einheit „{SelectedUnit.Name}“ wirklich löschen?\n\nHinweis: Löschen ist nur möglich, wenn keine Zuordnungen und keine Set-Verwendungen existieren.")) return;
        await Task.Run(() => _database.StweEinheitDelete(SelectedUnit.Id));
        await LoadPropertyChildrenAsync(keyId: SelectedKey?.Id, meterId: SelectedMeter?.Id);
    }
    private async Task DeleteOwnerAsync()
    {
        if (SelectedOwner is null || !await ConfirmAsync("Eigentümer löschen?", $"Eigentümer „{SelectedOwner.Name}“ wirklich löschen?\n\nHinweis: Löschen ist nur möglich, wenn keine Zuordnungen und keine Set-Verwendungen existieren.")) return;
        await Task.Run(() => _database.StweEigentuemerDelete(SelectedOwner.Id));
        await LoadAllAsync(SelectedProperty?.Id);
    }
    private async Task DeleteOwnershipAsync()
    {
        if (SelectedOwnership is null || !await ConfirmAsync("Zuordnung löschen?", $"Zuordnung wirklich löschen?\n\n{SelectedOwnership.Owner}\nVon: {SelectedOwnership.From}  Bis: {SelectedOwnership.To}")) return;
        await Task.Run(() => _database.StweEinheitEigentumDelete(SelectedOwnership.Id));
        await LoadOwnershipsAsync();
    }
    private async Task DeleteKeyAsync()
    {
        if (SelectedKey is null || !await ConfirmAsync("Schlüssel löschen?", $"Schlüssel wirklich löschen?\n\nName: {SelectedKey.Name}\nModus: {SelectedKey.Mode}\n\nHinweis: Wenn ein Zähler auf diesen Schlüssel zeigt, wird das Löschen verhindert.")) return;
        await Task.Run(() => _database.StweSchluesselDelete(SelectedKey.Id));
        await LoadPropertyChildrenAsync(SelectedUnit?.Id, meterId: SelectedMeter?.Id, ownershipId: SelectedOwnership?.Id);
    }
    private async Task DeleteMeterAsync()
    {
        if (SelectedMeter is null || !await ConfirmAsync("Zähler löschen?", $"Zähler „{SelectedMeter.Name}“ wirklich löschen?\n\nHinweis: Löschen ist nur möglich, wenn der Zähler noch nie in einem Energie-Set verwendet wurde.")) return;
        await Task.Run(() => _database.StweZaehlerDelete(SelectedMeter.Id));
        await LoadPropertyChildrenAsync(SelectedUnit?.Id, SelectedKey?.Id, ownershipId: SelectedOwnership?.Id);
    }

    private async void OnKeyLinesClick(object sender, RoutedEventArgs e)
    {
        if (SelectedKey is null || SelectedProperty is null) return;
        try
        {
            var units = await Task.Run(() => _database.StweEinheitenGetByLiegenschaft(SelectedProperty.Id));
            var existing = await Task.Run(() => _database.StweSchluesselLinesGet(SelectedKey.Id));
            var dialog = new AllocationKeyLinesDialog(SelectedKey.Name, _owners.Select(owner => owner.Value).ToList(), units, existing)
            { XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || !dialog.Accepted) return;
            await Task.Run(() => _database.StweSchluesselLinesReplace(SelectedKey.Id, dialog.ResultLines));
            ShowStatus("Schlüsselzeilen gespeichert.", InfoBarSeverity.Success);
        }
        catch (Exception exception) { ShowStatus("Schlüsselzeilen konnten nicht gespeichert werden: " + exception.Message, InfoBarSeverity.Error); }
    }

    private async Task<bool> ConfirmAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = "Löschen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
