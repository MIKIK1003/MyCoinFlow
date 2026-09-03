using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InvoicingDocumentEditorWindow : PersistentWindow
{
    private readonly BillableObjectRecord? _preferredContext;
    private readonly InvoicingDocumentRepository _repository;
    private readonly InvoicingPositionRepository _positionRepository;
    private InvoicingDocumentCreationWorkspace? _workspace;
    private IReadOnlyList<BillableObjectRecord> _allObjects = [];
    private int _positionCount;
    private bool _loaded;
    private bool _loading;
    private bool _saving;

    public InvoicingDocumentEditorWindow(
        BillableObjectRecord? preferredContext = null,
        InvoicingDocumentRepository? repository = null,
        InvoicingPositionRepository? positionRepository = null)
    {
        _preferredContext = preferredContext;
        _repository = repository ?? new InvoicingDocumentRepository();
        _positionRepository = positionRepository ?? new InvoicingPositionRepository();
        InitializeComponent();

        Title = "Neue Offerte";
        DocumentDatePicker.SelectedDate = new DateTimeOffset(DateTime.Today);
        ConfigureDpiAwareSizing(RootGrid, 1180, 820, 760, 620);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        SubjectBox.TextChanged += OnEntryChanged;
        CurrencyBox.SelectionChanged += OnEntryChanged;
        RootGrid.SizeChanged += OnRootSizeChanged;
        Activated += OnActivated;
        Closed += OnWindowClosed;
    }

    public bool Changed { get; private set; }
    public int CreatedDocumentId { get; private set; }

    private BillableObjectRecord? SelectedObject =>
        ObjectsList.SelectedItem as BillableObjectRecord;

    private InvoicingDocumentRecipientOption? SelectedRecipient =>
        RecipientBox.SelectedItem as InvoicingDocumentRecipientOption;

    private InvoicingDocumentCurrencyOption? SelectedCurrency =>
        CurrencyBox.SelectedItem as InvoicingDocumentCurrencyOption;

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        ApplyResponsiveLayout(RootGrid.ActualWidth);
        if (_loaded) return;
        _loaded = true;
        await LoadWorkspaceAsync(_preferredContext?.StableKey);
        ObjectSearchBox.Focus(FocusState.Programmatic);
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var wide = width >= 900;
        EditorGrid.ColumnDefinitions[0].Width = wide
            ? new GridLength(5, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        EditorGrid.ColumnDefinitions[1].Width = wide
            ? new GridLength(4, GridUnitType.Star)
            : new GridLength(0);
        Grid.SetColumnSpan(ObjectCard, wide ? 1 : 2);
        Grid.SetRow(HeaderColumn, wide ? 0 : 1);
        Grid.SetColumn(HeaderColumn, wide ? 1 : 0);
        Grid.SetColumnSpan(HeaderColumn, wide ? 1 : 2);
    }

    private async Task LoadWorkspaceAsync(string? selectedKey = null)
    {
        if (_loading) return;
        _loading = true;
        LoadingOverlay.Visibility = Visibility.Visible;
        ErrorInfoBar.IsOpen = false;
        SaveButton.IsEnabled = false;
        try
        {
            var date = DateOnly.FromDateTime(
                DocumentDatePicker.SelectedDate?.Date ?? DateTime.Today);
            _workspace = await _repository.LoadCreationWorkspaceAsync(date);
            _allObjects = _workspace.SelectableObjects;
            CurrencyBox.ItemsSource = _workspace.Currencies;
            CurrencyBox.SelectedItem = _workspace.Currencies.FirstOrDefault(currency =>
                currency.Code == _workspace.BaseCurrency) ?? _workspace.Currencies.FirstOrDefault();
            ApplyObjectFilter(selectedKey);
            StatusText.Text =
                $"{_allObjects.Count:N0} auswählbare Objekte · " +
                $"{_workspace.Currencies.Count:N0} Dokumentwährungen · Schema v{InvoicingSchema.CurrentVersion}";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            _workspace = null;
            _allObjects = [];
            ObjectsList.ItemsSource = null;
            CurrencyBox.ItemsSource = null;
            RecipientBox.ItemsSource = null;
            StatusText.Text = "Offertenarbeitsbereich konnte nicht geladen werden.";
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            _loading = false;
            UpdateSaveState();
        }
    }

    private void ApplyObjectFilter(string? selectedKey = null)
    {
        selectedKey ??= SelectedObject?.StableKey;
        var search = ObjectSearchBox.Text.Trim();
        var filtered = _allObjects
            .Where(item => string.IsNullOrWhiteSpace(search) ||
                new[]
                {
                    item.Title, item.Subtitle, item.PropertyName, item.UnitName,
                    item.ResponsibleParty, item.Recipient
                }.Any(value => value.Contains(
                    search, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
        ObjectsList.ItemsSource = filtered;
        ObjectsList.SelectedItem = selectedKey is null
            ? filtered.FirstOrDefault()
            : filtered.FirstOrDefault(item => item.StableKey == selectedKey) ??
              filtered.FirstOrDefault();
        ObjectsList.Visibility = filtered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyObjectsState.Visibility =
            filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnObjectSearchChanged(object sender, TextChangedEventArgs e) =>
        ApplyObjectFilter();

    private async void OnObjectSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var context = SelectedObject;
        _positionCount = 0;
        RecipientBox.ItemsSource = null;
        SelectedObjectText.Text = context?.Title ?? "Kein Objekt ausgewählt";
        PositionCountText.Text = context is null
            ? "Positionen werden nach Auswahl geprüft."
            : "Positionsentwurf wird geprüft …";
        if (context is null || _workspace is null)
        {
            UpdateSaveState();
            return;
        }

        var recipients = _workspace.GetRecipientOptions(context);
        RecipientBox.ItemsSource = recipients;
        RecipientBox.SelectedItem = recipients.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(SubjectBox.Text))
            SubjectBox.Text = context.Title;

        try
        {
            var positions = await _positionRepository.LoadWorkspaceAsync(
                context.SourceCode, context.SourceId, context.Title);
            if (!ReferenceEquals(context, SelectedObject)) return;
            _positionCount = positions.Positions.Count;
            PositionCountText.Text = _positionCount == 0
                ? "Keine Positionen vorhanden · zuerst Positionen verfassen"
                : $"{_positionCount:N0} Position(en) · " +
                  $"{positions.Total.ToString("N2", CultureInfo.GetCultureInfo("de-CH"))} " +
                  "Basiswährung · werden unverändert übernommen";
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(context, SelectedObject)) return;
            ShowError(exception.Message);
            PositionCountText.Text = "Positionsentwurf konnte nicht geprüft werden.";
        }
        UpdateSaveState();
    }

    private void OnRecipientSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var recipient = SelectedRecipient;
        RecipientInfoBar.Title = recipient is null
            ? "Dokumentempfänger fehlt"
            : InvoicingRecipientKinds.DisplayName(recipient.Kind);
        RecipientInfoBar.Message = recipient?.Notice ??
            "Wählen Sie einen gültigen Dokumentempfänger.";
        RecipientInfoBar.Severity = recipient?.Kind == InvoicingRecipientKinds.Tenant
            ? InfoBarSeverity.Warning
            : recipient is null
                ? InfoBarSeverity.Warning
                : InfoBarSeverity.Success;
        UpdateSaveState();
    }

    private async void OnDocumentDateChanged(
        object sender,
        DatePickerValueChangedEventArgs args)
    {
        if (!_loaded || _loading || _saving) return;
        await LoadWorkspaceAsync(SelectedObject?.StableKey);
    }

    private void OnEntryChanged(object sender, object e) => UpdateSaveState();

    private void UpdateSaveState()
    {
        SaveButton.IsEnabled =
            !_loading &&
            !_saving &&
            _workspace is not null &&
            SelectedObject is not null &&
            SelectedRecipient is not null &&
            SelectedCurrency is not null &&
            _positionCount > 0 &&
            !string.IsNullOrWhiteSpace(SubjectBox.Text);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveAsync();

    private async void OnSaveShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (SaveButton.IsEnabled)
            await SaveAsync();
    }

    private void OnSearchShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ObjectSearchBox.Focus(FocusState.Programmatic);
    }

    private void OnCancelShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!_saving) Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (!_saving) Close();
    }

    private async Task SaveAsync()
    {
        if (_saving ||
            SelectedObject is not { } context ||
            SelectedRecipient is not { } recipient ||
            SelectedCurrency is not { } currency)
            return;

        _saving = true;
        LoadingOverlay.Visibility = Visibility.Visible;
        ErrorInfoBar.IsOpen = false;
        UpdateSaveState();
        try
        {
            CreatedDocumentId = await _repository.CreateOfferAsync(new InvoicingDocumentDraft
            {
                DocumentDate = DocumentDatePicker.SelectedDate ?? new DateTimeOffset(DateTime.Today),
                ContextSource = context.SourceCode,
                ContextSourceId = context.SourceId,
                RecipientAddressId = recipient.AddressId,
                RecipientKind = recipient.Kind,
                Subject = SubjectBox.Text,
                CurrencyCode = currency.Code
            });
            Changed = true;
            Close();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            StatusText.Text = "Keine Daten wurden gespeichert.";
        }
        finally
        {
            _saving = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            UpdateSaveState();
        }
    }

    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        SubjectBox.TextChanged -= OnEntryChanged;
        CurrencyBox.SelectionChanged -= OnEntryChanged;
        RootGrid.SizeChanged -= OnRootSizeChanged;
        Activated -= OnActivated;
        Closed -= OnWindowClosed;
    }
}
