using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.WinUI.ViewModels;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InvoicingPage : Page
{
    private readonly InvoicingViewModel _viewModel = new();
    private IReadOnlyList<BillableObjectRecord> _allObjects = [];
    private string _statusFilter = "ALL";
    private bool _initialized;
    private InvoicingMasterDataWindow? _masterDataWindow;

    public InvoicingPage()
    {
        InitializeComponent();
        EffectiveDatePicker.SelectedDate = new DateTimeOffset(DateTime.Today);
        SourceFilterBox.SelectedIndex = 0;
        PageRoot.SizeChanged += OnPageSizeChanged;
        Loaded += OnLoaded;
    }

    private BillableObjectRecord? SelectedObject =>
        BillableObjectsList.SelectedItem as BillableObjectRecord;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsLoading) return;
        _initialized = true;
        ApplyResponsiveLayout(PageRoot.ActualWidth);
        await ReloadAsync();
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var wide = width >= 820;
        Grid.SetColumnSpan(ObjectListCard, wide ? 1 : 2);
        Grid.SetRow(ObjectDetailCard, wide ? 0 : 1);
        Grid.SetColumn(ObjectDetailCard, wide ? 1 : 0);
        Grid.SetColumnSpan(ObjectDetailCard, wide ? 1 : 2);
        ObjectDetailCard.MaxHeight = wide ? double.PositiveInfinity : 260;
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void OnRefreshShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ReloadAsync();
    }

    private void OnSearchShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void OnFinanceSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Overview?.IsAdmin != true) return;
        (Application.Current as App)?.MainWindow.NavigateToFinanceSettings();
    }

    private void OnMasterDataClick(object sender, RoutedEventArgs e)
    {
        if (_masterDataWindow is not null)
        {
            _masterDataWindow.Activate();
            return;
        }

        _masterDataWindow = new InvoicingMasterDataWindow();
        _masterDataWindow.Closed += OnMasterDataWindowClosed;
        _masterDataWindow.Activate();
    }

    private async void OnMasterDataWindowClosed(object sender, WindowEventArgs args)
    {
        var changed = _masterDataWindow?.Changed == true;
        if (_masterDataWindow is not null)
            _masterDataWindow.Closed -= OnMasterDataWindowClosed;
        _masterDataWindow = null;
        if (changed)
            await ReloadAsync();
    }

    private async void OnEffectiveDateChanged(
        object sender,
        DatePickerValueChangedEventArgs args)
    {
        if (!_initialized || _viewModel.IsLoading || EffectiveDatePicker.SelectedDate is null) return;
        await ReloadAsync();
    }

    private void OnFilterChanged(object sender, object e) => ApplyFilters();

    private void OnStatusFilterClick(object sender, RoutedEventArgs e)
    {
        _statusFilter = ReferenceEquals(sender, ReadyStatusButton)
            ? "READY"
            : ReferenceEquals(sender, ReviewStatusButton)
                ? "REVIEW"
                : "ALL";
        AllStatusButton.IsChecked = _statusFilter == "ALL";
        ReadyStatusButton.IsChecked = _statusFilter == "READY";
        ReviewStatusButton.IsChecked = _statusFilter == "REVIEW";
        ApplyFilters();
    }

    private void OnObjectSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RenderSelectedObject();

    private void OnRecipientChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecipientChoiceBox.SelectedItem is not RecipientChoice choice) return;
        ChainRecipientText.Text = choice.Display;
        SelectionStatusInfo.Severity = choice.IsTenant
            ? InfoBarSeverity.Warning
            : InfoBarSeverity.Success;
        SelectionStatusInfo.Title = choice.IsTenant
            ? "Mieter bewusst gewählt"
            : "Sicherer Standardempfänger";
        SelectionStatusInfo.Message = choice.IsTenant
            ? "Die dokumentierten Voraussetzungen sind vorhanden. Überwälzbarkeit, Leistungsart und Vertragsgrundlage müssen vor jedem Beleg trotzdem manuell geprüft werden."
            : "Eigentümer bleibt die sichtbar verantwortliche Partei und der Standardempfänger.";
    }

    private async Task ReloadAsync()
    {
        var selectedKey = SelectedObject?.StableKey;
        LoadingOverlay.Visibility = Visibility.Visible;
        ErrorInfoBar.IsOpen = false;
        MasterDataButton.IsEnabled = false;
        var effectiveDate = EffectiveDatePicker.SelectedDate?.Date ?? DateTime.Today;
        await _viewModel.LoadAsync(DateOnly.FromDateTime(effectiveDate));
        RenderOverview();
        ApplyFilters(selectedKey);
        MasterDataButton.IsEnabled = _viewModel.Overview is not null;
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void RenderOverview()
    {
        var overview = _viewModel.Overview;
        var workspace = _viewModel.BillableObjects;
        if (overview is null || workspace is null)
        {
            ErrorInfoBar.Message = _viewModel.ErrorMessage ?? "Unbekannter Fehler.";
            ErrorInfoBar.IsOpen = true;
            ContextBadgeText.Text = "Nicht verbunden";
            SettingsActionGroup.Visibility = Visibility.Collapsed;
            SettingsSeparator.Visibility = Visibility.Collapsed;
            _allObjects = [];
            DirectObjectCountText.Text = PropertyObjectCountText.Text =
                SelectableCountText.Text = ReviewCountText.Text = "0";
            StatusText.Text = "Ladefehler · Keine Daten wurden verändert.";
            SchemaText.Text = "Schema nicht geprüft";
            return;
        }

        ContextBadgeText.Text = $"{overview.DatabaseName} · {overview.BaseCurrency}";
        SettingsActionGroup.Visibility = overview.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        SettingsSeparator.Visibility = SettingsActionGroup.Visibility;
        _allObjects = workspace.Objects;
        DirectObjectCountText.Text = workspace.DirectObjectCount.ToString("N0");
        PropertyObjectCountText.Text = workspace.PropertyObjectCount.ToString("N0");
        SelectableCountText.Text = workspace.SelectableCount.ToString("N0");
        ReviewCountText.Text = workspace.ReviewCount.ToString("N0");
        StatusText.Text =
            $"Mandant: {overview.DatabaseName} · Stichtag {workspace.EffectiveDate:dd.MM.yyyy} · " +
            $"{workspace.SelectableCount:N0} von {workspace.Objects.Count:N0} Objekten auswählbar";
        SchemaText.Text = $"Schema v{overview.SchemaVersion}";
    }

    private void ApplyFilters(string? selectedKey = null)
    {
        selectedKey ??= SelectedObject?.StableKey;
        var search = SearchBox.Text.Trim();
        var source = (SourceFilterBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        var filtered = _allObjects
            .Where(item => source == "ALL" || item.SourceCode == source)
            .Where(item => _statusFilter switch
            {
                "READY" => item.IsSelectable,
                "REVIEW" => !item.IsSelectable,
                _ => true
            })
            .Where(item => string.IsNullOrWhiteSpace(search) || new[]
            {
                item.Title,
                item.Subtitle,
                item.PropertyName,
                item.UnitName,
                item.PeriodAndUsage,
                item.ResponsibleParty,
                item.Recipient,
                item.TenantRecipient,
                item.Status
            }.Any(value => value.Contains(search, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();

        BillableObjectsList.ItemsSource = filtered;
        BillableObjectsList.SelectedItem = selectedKey is null
            ? filtered.FirstOrDefault()
            : filtered.FirstOrDefault(item => item.StableKey == selectedKey) ?? filtered.FirstOrDefault();
        BillableObjectsList.Visibility = filtered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyObjectsState.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ObjectDetailCard.Visibility = filtered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RenderSelectedObject();
    }

    private void RenderSelectedObject()
    {
        var item = SelectedObject;
        if (item is null)
        {
            ObjectDetailTitle.Text = "Kein Objekt ausgewählt";
            ObjectDetailSubtitle.Text = "Die Suche oder der Filter liefert aktuell keine Auswahl.";
            ChainPropertyText.Text = ChainUnitText.Text = ChainUsageText.Text =
                ChainOwnerText.Text = ChainRecipientText.Text = "—";
            PropertyChainScroll.Visibility = Visibility.Visible;
            RecipientChoiceBox.ItemsSource = null;
            RecipientChoiceBox.Visibility = Visibility.Collapsed;
            SelectionStatusInfo.Title = "Keine Auswahl";
            SelectionStatusInfo.Message = "Erfassen oder suchen Sie ein fakturierbares Objekt.";
            SelectionStatusInfo.Severity = InfoBarSeverity.Informational;
            LegalInfoBar.Message = "Die Software trifft keine automatische Rechtsentscheidung.";
            return;
        }

        ObjectDetailTitle.Text = item.Title;
        ObjectDetailSubtitle.Text = $"{item.SourceDisplay} · {item.Subtitle}";
        SelectionStatusInfo.Title = item.IsSelectable ? "Objekt auswählbar" : "Prüfung erforderlich";
        SelectionStatusInfo.Message = item.Status;
        SelectionStatusInfo.Severity = item.IsSelectable
            ? InfoBarSeverity.Success
            : InfoBarSeverity.Warning;
        LegalInfoBar.Message = item.LegalHint;

        if (item.SourceCode == "ARTICLE")
        {
            PropertyChainScroll.Visibility = Visibility.Collapsed;
            RecipientChoiceBox.Visibility = Visibility.Collapsed;
            RecipientChoiceBox.ItemsSource = null;
            return;
        }

        PropertyChainScroll.Visibility = Visibility.Visible;
        ChainPropertyText.Text = item.PropertyName;
        ChainUnitText.Text = item.UnitName;
        ChainUsageText.Text = item.PeriodAndUsage;
        ChainOwnerText.Text = item.ResponsibleParty;
        ChainRecipientText.Text = string.IsNullOrWhiteSpace(item.Recipient)
            ? "Eigentümer-Rechnungsadresse fehlt"
            : item.Recipient;

        var choices = new List<RecipientChoice>();
        if (item.RecipientAddressId.HasValue)
        {
            choices.Add(new RecipientChoice(
                item.RecipientAddressId.Value,
                $"{item.Recipient} · Eigentümer (sicherer Standard)",
                false));
        }
        if (item.TenantDirectBillingAvailable &&
            item.TenantRecipientAddressId.HasValue &&
            !string.IsNullOrWhiteSpace(item.TenantRecipient))
        {
            choices.Add(new RecipientChoice(
                item.TenantRecipientAddressId.Value,
                $"{item.TenantRecipient} · Mieter (nur nach manueller Prüfung)",
                true));
        }
        RecipientChoiceBox.ItemsSource = choices;
        RecipientChoiceBox.Visibility = choices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecipientChoiceBox.SelectedIndex = choices.Count > 0 ? 0 : -1;
    }

    private sealed record RecipientChoice(int AddressId, string Display, bool IsTenant);
}
