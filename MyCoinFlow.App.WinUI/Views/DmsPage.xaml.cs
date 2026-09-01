using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.WinUI.Services;
using System.ComponentModel;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class DmsPage : Page
{
    private const string TaxDocumentsCategory = "Steuerunterlagen";
    private const string TaxExportFolderSettingKey = "DmsTaxExportFolder";
    private readonly DatabaseService _database = new();
    private readonly AttachmentService _attachments = new();
    private List<DmsDocument> _all = new();
    private bool _ready;
    private bool _filtering;
    private DmsDocument? _selected;
    private ListView? _activeDocumentList;
    private int? _selectionToRestoreId;
    private readonly IReadOnlyList<FunctionGroupLayout> _functionGroups;
    private bool _functionBarLayoutQueued;

    public DmsPage()
    {
        InitializeComponent();
        _functionGroups =
        [
            new(DocumentActionsGrid),
            new(SelectedDocumentActionsGrid),
            new(AssignmentActionsGrid)
        ];
        ApplyFunctionBarLayout(rows: 2, columnWidth: 150);
        FunctionBarScroller.SizeChanged += (_, _) => QueueFunctionBarLayout();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            _ready = true;
            GroupBox.ItemsSource = new[] { "Kategorie", "Belegart", "Bearbeitungsstatus" };
            StatusBox.ItemsSource = new[] { "Alle", "Neu", "In Prüfung", "Freigegeben", "Erledigt" };
            GroupBox.SelectedIndex = 0;
            StatusBox.SelectedIndex = 0;
            _database.EnsureAttachmentsSchema();
            _attachments.InitializeExistingDocumentHashes();
            try
            {
                var metadataRefresh = _attachments.RefreshLinkedTransactionMetadata();
                if (metadataRefresh.Updated > 0 || metadataRefresh.Failed > 0)
                {
                    var message = $"{metadataRefresh.Updated} bereits verknüpfte DMS-Dokumente wurden aus ihren Transaktionen ergänzt.";
                    if (metadataRefresh.Failed > 0)
                        message += $" {metadataRefresh.Failed} Dokumente konnten nicht ergänzt werden.";
                    ShowStatus(message, metadataRefresh.Failed > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
                }
            }
            catch (Exception ex)
            {
                ShowStatus("Bestehende Transaktionsverknüpfungen konnten nicht ergänzt werden: " + ex.Message, InfoBarSeverity.Warning);
            }
        }
        UpdateTaxFolderButtonState();
        DmsWatcherService.Instance.DocumentProcessed -= OnDocumentProcessed;
        DmsWatcherService.Instance.DocumentProcessed += OnDocumentProcessed;
        DmsWatcherService.Instance.PropertyChanged -= OnWatcherPropertyChanged;
        DmsWatcherService.Instance.PropertyChanged += OnWatcherPropertyChanged;
        UpdateWatcherState();
        Reload();
        QueueFunctionBarLayout();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DmsWatcherService.Instance.DocumentProcessed -= OnDocumentProcessed;
        DmsWatcherService.Instance.PropertyChanged -= OnWatcherPropertyChanged;
    }

    private void QueueFunctionBarLayout()
    {
        if (_functionBarLayoutQueued) return;
        _functionBarLayoutQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _functionBarLayoutQueued = false;
                var availableWidth = FunctionBarScroller.ActualWidth;
                if (availableWidth <= 0) return;

                const double separatorsAndReserve = 42;
                const double minimumColumnWidth = 124;
                var rows = 2;
                var columns = _functionGroups.Sum(group =>
                    (int)Math.Ceiling(group.Actions.Count / (double)rows));
                var columnWidth = (availableWidth - separatorsAndReserve) / columns;
                while (columnWidth < minimumColumnWidth && rows < 6)
                {
                    rows++;
                    columns = _functionGroups.Sum(group =>
                        (int)Math.Ceiling(group.Actions.Count / (double)rows));
                    columnWidth = (availableWidth - separatorsAndReserve) / columns;
                }

                ApplyFunctionBarLayout(
                    rows,
                    Math.Clamp(Math.Floor(columnWidth), 118, 190));
            }))
        {
            _functionBarLayoutQueued = false;
        }
    }

    private void ApplyFunctionBarLayout(int rows, double columnWidth)
    {
        foreach (var group in _functionGroups)
        {
            group.Grid.RowDefinitions.Clear();
            group.Grid.ColumnDefinitions.Clear();
            for (var row = 0; row < rows; row++)
                group.Grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            for (var column = 0; column < (group.Actions.Count + rows - 1) / rows; column++)
                group.Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columnWidth) });

            for (var index = 0; index < group.Actions.Count; index++)
            {
                var button = group.Actions[index];
                button.Width = Math.Max(112, columnWidth - 4);
                Grid.SetRow(button, index % rows);
                Grid.SetColumn(button, index / rows);
                if (button.Content is StackPanel content
                    && content.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
                {
                    label.MaxWidth = Math.Max(66, columnWidth - 38);
                }
            }
        }
    }

    private sealed class FunctionGroupLayout
    {
        public FunctionGroupLayout(Grid grid)
        {
            Grid = grid;
            Actions = grid.Children.OfType<Button>().ToList();
        }

        public Grid Grid { get; }
        public IReadOnlyList<Button> Actions { get; }
    }
    private void OnDocumentProcessed(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => Reload());
    private void OnWatcherPropertyChanged(object? sender, PropertyChangedEventArgs e) => DispatcherQueue.TryEnqueue(UpdateWatcherState);

    private void UpdateWatcherState()
    {
        var watcher = DmsWatcherService.Instance;
        WatcherPanel.Visibility = watcher.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        WatcherFileText.Text = watcher.CurrentFileName;
        WatcherPhaseText.Text = watcher.CurrentPhase;
        WatcherQueueText.Text = watcher.QueueCount > 0 ? $" · {watcher.QueueCount} in Warteschlange" : string.Empty;
    }

    private void Reload(int? selectedId = null)
    {
        try
        {
            selectedId ??= _selected?.Id;
            _all = _database.LoadAllDocuments(string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim(), null);
            var category = CategoryBox.SelectedItem as string ?? "Alle";
            _filtering = true;
            CategoryBox.ItemsSource = new[] { "Alle", TaxDocumentsCategory }
                .Concat(_database.GetDistinctKategorien().Where(value =>
                    !string.Equals(value, TaxDocumentsCategory, StringComparison.CurrentCultureIgnoreCase)))
                .ToList();
            CategoryBox.SelectedItem = ((IEnumerable<string>)CategoryBox.ItemsSource).Contains(category) ? category : "Alle";
            _filtering = false;
            ApplyFilters(selectedId);
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }

    private void ApplyFilters(int? selectedId = null)
    {
        if (!_ready || _filtering) return;
        IEnumerable<DmsDocument> result = _all;
        var category = CategoryBox.SelectedItem as string ?? "Alle";
        var status = StatusBox.SelectedItem as string ?? "Alle";
        if (category == TaxDocumentsCategory)
            result = result.Where(value => value.IstSteuerunterlage);
        else if (category != "Alle")
            result = result.Where(value => value.Kategorie == category);
        if (status != "Alle") result = result.Where(value => value.BearbeitungsstatusAnzeige == status);
        if (FavoritesOnlyBox.IsOn) result = result.Where(value => value.IstFavorit);
        if (OverdueOnlyBox.IsChecked == true) result = result.Where(value => value.IstUeberfaellig);
        if (UnlinkedOnlyBox.IsChecked == true) result = result.Where(value => value.EntityType is null);
        var groupMode = GroupBox.SelectedItem as string ?? "Kategorie";
        string Group(DmsDocument value) => groupMode switch
        {
            "Belegart" => value.BelegartAnzeige,
            "Bearbeitungsstatus" => value.BearbeitungsstatusAnzeige,
            _ => value.IstSteuerunterlage ? TaxDocumentsCategory : value.KategorieAnzeige
        };
        var rows = result
            .OrderBy(Group)
            .ThenByDescending(value => value.IstFavorit)
            .ThenByDescending(value => value.DokumentDatum ?? value.ImportedAtUtc)
            .Select(value => new DmsDisplayRow(value, Group(value)))
            .ToList();
        var groups = rows
            .GroupBy(value => value.Group)
            .OrderBy(group => group.Key == TaxDocumentsCategory ? 0 : 1)
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new DmsDisplayGroup(group.Key, group))
            .ToList();

        _activeDocumentList = null;
        _selectionToRestoreId = selectedId;
        GroupsList.ItemsSource = groups;

        CounterText.Text = rows.Count == 1 ? "1 Treffer" : $"{rows.Count} Treffer";
        DocumentsCountText.Text = _all.Count.ToString();
        NewCountText.Text = _all.Count(value => value.Bearbeitungsstatus == DmsBearbeitungsstatus.Neu).ToString();
        OverdueCountText.Text = _all.Count(value => value.IstUeberfaellig).ToString();
        FavoritesCountText.Text = _all.Count(value => value.IstFavorit).ToString();

        var restored = selectedId.HasValue ? rows.FirstOrDefault(value => value.Id == selectedId.Value) : null;
        SetSelectedDocument(restored?.Value);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Reload();
    private void OnSearchClick(object sender, RoutedEventArgs e) => Reload();
    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == Windows.System.VirtualKey.Enter) Reload(); }
    private void OnFilterChanged(object sender, object e) => ApplyFilters(_selected?.Id);

    private void OnDocumentSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView list || list.SelectedItem is not DmsDisplayRow row)
            return;

        if (_activeDocumentList is not null && _activeDocumentList != list)
            _activeDocumentList.SelectedItem = null;
        _activeDocumentList = list;
        _selectionToRestoreId = row.Id;
        SetSelectedDocument(row.Value);
    }

    private void OnDocumentListLoaded(object sender, RoutedEventArgs e)
    {
        if (_selectionToRestoreId is not int selectedId || sender is not ListView list || list.ItemsSource is not IEnumerable<DmsDisplayRow> rows)
            return;
        var row = rows.FirstOrDefault(value => value.Id == selectedId);
        if (row is not null)
            list.SelectedItem = row;
    }

    private void SetSelectedDocument(DmsDocument? document)
    {
        _selected = document;
        DetailPanel.DataContext = _selected;
        var enabled = _selected is not null;
        OpenButton.IsEnabled = EditButton.IsEnabled = FavoriteButton.IsEnabled = NewVersionButton.IsEnabled = DeleteButton.IsEnabled = AssignButton.IsEnabled = RetryButton.IsEnabled = enabled;
        OpenVersionButton.IsEnabled = false;
        FavoriteButtonText.Text = _selected?.IstFavorit == true ? "Favorit entfernen" : "Als Favorit";
        ToolTipService.SetToolTip(FavoriteButton, FavoriteButtonText.Text);
        UnlinkButton.IsEnabled = _selected?.EntityType == "Transaktion";
        GoToTransactionButton.IsEnabled = _selected?.EntityType == "Transaktion" && (_selected.EntityId ?? _selected.TransaktionId) > 0;
        EmptyDetailPanel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        DetailScroll.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (_selected is null) { VersionsList.ItemsSource = null; ActivitiesList.ItemsSource = null; return; }
        if (_selected.IstNeu) { try { _database.MarkDocumentSeen(_selected.Id); _selected.IstNeu = false; } catch { } }
        LoadDocumentFile(_selected);
    }

    private void LoadDocumentFile(DmsDocument document)
    {
        var versions = _database.LoadDmsVersions(document.Id);
        versions.Insert(0, new DmsVersionEntry { AttachmentId = document.Id, VersionNumber = document.AktuelleVersion, FileName = document.FileName, FolderRel = document.FolderRel, SizeBytes = document.SizeBytes ?? 0, CreatedAtUtc = document.LetzteAenderungAmUtc ?? document.ImportedAtUtc, CreatedBy = CurrentUserContext.Username, Comment = "Aktuelle Fassung", IsCurrent = true });
        VersionsList.ItemsSource = versions;
        VersionsList.SelectedIndex = versions.Count > 0 ? 0 : -1;
        OpenVersionButton.IsEnabled = VersionsList.SelectedItem is DmsVersionEntry;
        ActivitiesList.ItemsSource = _database.LoadDmsActivities(document.Id);
    }

    private async void OnScanClick(object sender, RoutedEventArgs e)
    {
        var folder = DmsWatcherService.Instance.GetWorkingFolder();
        if (string.IsNullOrWhiteSpace(folder)) { await MessageAsync("Scannen", "Bitte zuerst in den Einstellungen (Dateianhänge und OCR > Verzeichnisse) einen Arbeitsordner festlegen."); return; }
        try { ScannerService.ScanToFolder(folder); } catch (Exception ex) { ShowStatus("Scannen fehlgeschlagen: " + ex.Message, InfoBarSeverity.Error); }
    }

    private async void OnUploadClick(object sender, RoutedEventArgs e)
    {
        var dialog = new DmsDocumentEditorDialog();
        if (!await dialog.ShowAsync() || string.IsNullOrWhiteSpace(dialog.SelectedFilePath) || dialog.Changes is null) return;
        try
        {
            var (_, id) = _attachments.AttachFreestanding(dialog.SelectedFilePath, dialog.Changes.Title, dialog.Changes.Category);
            _database.UpdateDmsDocument(id, dialog.Changes, dialog.IsTaxDocument);
            Reload(id);
        }
        catch (Exception ex)
        {
            ShowStatus("Hochladen fehlgeschlagen: " + ex.Message, InfoBarSeverity.Error);
            return;
        }

        if (dialog.IsTaxDocument)
        {
            try { await SynchronizeWithConfiguredTaxFolderAsync(showMissingFolderWarning: true); }
            catch (Exception ex) { ShowStatus("Das Dokument wurde gespeichert, der Steuerordner konnte aber nicht aktualisiert werden: " + ex.Message, InfoBarSeverity.Warning); }
        }
    }

    private async void OnExportTaxDocumentsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var targetFolder = GetConfiguredTaxFolder();
            if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
            {
                targetFolder = await SelectAndStoreTaxFolderAsync();
                if (string.IsNullOrWhiteSpace(targetFolder)) return;
            }

            await SynchronizeTaxDocumentsAsync(targetFolder);
        }
        catch (Exception ex)
        {
            ShowStatus("Steuerunterlagen konnten nicht übertragen werden: " + ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void OnSelectTaxFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var targetFolder = await SelectAndStoreTaxFolderAsync();
            if (string.IsNullOrWhiteSpace(targetFolder)) return;
            await SynchronizeTaxDocumentsAsync(targetFolder);
        }
        catch (Exception ex)
        {
            ShowStatus("Steuerunterlagen konnten nicht übertragen werden: " + ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task<string?> SelectAndStoreTaxFolderAsync()
    {
        var targetFolder = await FilePickerService.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(targetFolder)) return null;

        targetFolder = Path.GetFullPath(targetFolder);
        _database.SetAppSetting(TaxExportFolderSettingKey, targetFolder);
        UpdateTaxFolderButtonState();
        return targetFolder;
    }

    private async Task SynchronizeWithConfiguredTaxFolderAsync(bool showMissingFolderWarning)
    {
        var targetFolder = GetConfiguredTaxFolder();
        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            if (showMissingFolderWarning)
                ShowStatus("Das Dokument ist als Steuerunterlage markiert. Bitte einmal den Zielordner festlegen; danach erfolgt die Übertragung automatisch.", InfoBarSeverity.Warning);
            return;
        }

        if (!Directory.Exists(targetFolder))
        {
            if (showMissingFolderWarning)
                ShowStatus($"Das Dokument ist als Steuerunterlage markiert, aber der hinterlegte Steuerordner ist nicht erreichbar: {targetFolder}", InfoBarSeverity.Warning);
            return;
        }

        await SynchronizeTaxDocumentsAsync(targetFolder);
    }

    private async Task SynchronizeTaxDocumentsAsync(string targetFolder)
    {
        var documents = _database.LoadAllDocuments(null, null)
            .Where(document => document.IstSteuerunterlage)
            .ToList();
        if (documents.Count == 0)
        {
            await MessageAsync("Steuerunterlagen", "Es sind noch keine Dokumente als Steuerunterlage oder Steuerbeilage markiert. Der Zielordner bleibt gespeichert.");
            return;
        }

        ShowStatus($"{documents.Count} Steuerunterlagen werden geprüft und übertragen …", InfoBarSeverity.Informational);
        var result = await _attachments.ExportTaxDocumentsAsync(documents, targetFolder);
        var parts = new List<string> { $"{result.CopiedDocuments} kopiert" };
        if (result.DuplicateDocuments > 0)
            parts.Add($"{result.DuplicateDocuments} bereits vorhanden");
        if (result.RenamedDocuments > 0)
            parts.Add($"{result.RenamedDocuments} wegen gleicher Titel nummeriert");
        if (result.MissingDocuments > 0)
            parts.Add($"{result.MissingDocuments} Quelldateien fehlen");
        ShowStatus(
            $"Steuerordner aktualisiert: {string.Join(", ", parts)}. Ziel: {targetFolder}",
            result.MissingDocuments == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private string? GetConfiguredTaxFolder()
    {
        var folder = _database.GetAppSetting(TaxExportFolderSettingKey);
        return string.IsNullOrWhiteSpace(folder) ? null : folder.Trim();
    }

    private void UpdateTaxFolderButtonState()
    {
        var targetFolder = GetConfiguredTaxFolder();
        var isConfigured = !string.IsNullOrWhiteSpace(targetFolder);
        UpdateTaxFolderMenuItem.Text = isConfigured ? "Jetzt aktualisieren" : "Zielordner festlegen und übertragen";
        ToolTipService.SetToolTip(
            TaxFolderButton,
            isConfigured
                ? $"Steuerordner verwalten\n{targetFolder}"
                : "Steuerordner festlegen und alle markierten Steuerunterlagen kopieren");
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var dialog = new DmsDocumentEditorDialog(_selected);
        if (!await dialog.ShowAsync() || dialog.Changes is null) return;
        try
        {
            var changes = dialog.Changes;
            if (_selected.EntityType is not null) changes = changes with { RecognizedAmount = _selected.ErkannterBetrag };
            _database.UpdateDmsDocument(_selected.Id, changes, dialog.IsTaxDocument);
            Reload(_selected.Id);
        }
        catch (Exception ex)
        {
            ShowStatus("Speichern fehlgeschlagen: " + ex.Message, InfoBarSeverity.Error);
            return;
        }

        if (dialog.IsTaxDocument)
        {
            try { await SynchronizeWithConfiguredTaxFolderAsync(showMissingFolderWarning: true); }
            catch (Exception ex) { ShowStatus("Das Dokument wurde gespeichert, der Steuerordner konnte aber nicht aktualisiert werden: " + ex.Message, InfoBarSeverity.Warning); }
        }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        try { _attachments.OpenAttachment(_selected.Id); LoadDocumentFile(_selected); }
        catch (Exception ex) { ShowStatus("Öffnen fehlgeschlagen: " + ex.Message, InfoBarSeverity.Error); }
    }
    private void OnDocumentDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => OnOpenClick(sender, e);
    private void OnFavoriteClick(object sender, RoutedEventArgs e) { if (_selected is null) return; try { _database.SetDmsFavorite(_selected.Id, !_selected.IstFavorit); Reload(_selected.Id); } catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); } }

    private async void OnNewVersionClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var dialog = new DmsNewVersionDialog { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(dialog.SelectedFilePath)) return;
        try { _attachments.ReplaceWithNewVersion(_selected.Id, dialog.SelectedFilePath, dialog.Comment); Reload(_selected.Id); }
        catch (Exception ex) { ShowStatus("Neue Version konnte nicht eingespielt werden: " + ex.Message, InfoBarSeverity.Error); }
    }

    private async void OnOpenVersionClick(object sender, RoutedEventArgs e)
    {
        var version = (sender as FrameworkElement)?.Tag as DmsVersionEntry
            ?? VersionsList.SelectedItem as DmsVersionEntry;
        if (version is null) return;
        try { _attachments.OpenVersion(version); }
        catch (Exception ex) { ShowStatus("Version konnte nicht geöffnet werden: " + ex.Message, InfoBarSeverity.Error); }
        await Task.CompletedTask;
    }

    private void OnVersionSelectionChanged(object sender, SelectionChangedEventArgs e)
        => OpenVersionButton.IsEnabled = _selected is not null && VersionsList.SelectedItem is DmsVersionEntry;

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !await ConfirmAsync("Entfernen bestätigen", $"Dokument „{_selected.TitelAnzeige}“ aus dem aktiven DMS entfernen?\n\nDie aktuelle Datei und alle Versionen werden in das wiederherstellbare Archiv verschoben.")) return;
        try { _attachments.DeleteAttachment(_selected.Id); Reload(); }
        catch (Exception ex) { ShowStatus("Dokument konnte nicht entfernt werden: " + ex.Message, InfoBarSeverity.Error); }
    }

    private async void OnAssignClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var request = new DmsTransactionSelectionRequest(
            _selected.Id,
            _selected.TitelAnzeige,
            _selected.FileName,
            _selected.DokumentDatum,
            _selected.TransBetrag ?? _selected.ErkannterBetrag,
            string.IsNullOrWhiteSpace(_selected.AdresseAnzeige) ? null : _selected.AdresseAnzeige,
            Array.Empty<Transaktion>());
        var window = new DmsTransactionWindow(request);
        var transactionId = await window.ShowAsync();
        if (!transactionId.HasValue) return;
        try { _attachments.LinkToTransaktion(_selected.Id, transactionId.Value); Reload(_selected.Id); }
        catch (Exception ex) { ShowStatus("Zuweisen fehlgeschlagen: " + ex.Message, InfoBarSeverity.Error); }
    }

    private async void OnUnlinkClick(object sender, RoutedEventArgs e)
    {
        if (_selected?.EntityType != "Transaktion" || !await ConfirmAsync("Verknüpfung lösen", "Verknüpfung zur Transaktion lösen? Das Dokument bleibt vollständig im DMS erhalten.")) return;
        try { _attachments.UnlinkFromTransaktion(_selected.Id); Reload(_selected.Id); }
        catch (Exception ex) { ShowStatus("Verknüpfung konnte nicht gelöst werden: " + ex.Message, InfoBarSeverity.Error); }
    }

    private void OnRetryClick(object sender, RoutedEventArgs e) { if (_selected is not null) DmsWatcherService.Instance.RequeueForMatching(_selected.Id, _selected.TitelAnzeige); }
    private void OnRetryAllClick(object sender, RoutedEventArgs e) => DmsWatcherService.Instance.RequeueAllUnmatched();
    private void OnGoToTransactionClick(object sender, RoutedEventArgs e)
    {
        var transactionId = _selected?.EntityId ?? _selected?.TransaktionId;
        if (transactionId is > 0) AppNavigation.ZeigeTransaktion(transactionId.Value);
    }
    private void OnHistoryClick(object sender, RoutedEventArgs e) => DmsHistoryWindow.ShowOrActivate();
    private async Task<bool> ConfirmAsync(string title, string text) => await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = text, PrimaryButtonText = "Ja", CloseButtonText = "Nein", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Primary;
    private async Task MessageAsync(string title, string text) => await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = text, CloseButtonText = "Schließen" }.ShowAsync();
    private void ShowStatus(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
}
