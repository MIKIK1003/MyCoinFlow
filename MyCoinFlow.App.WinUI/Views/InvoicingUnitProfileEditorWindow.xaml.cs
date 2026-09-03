using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InvoicingUnitProfileEditorWindow : PersistentWindow
{
    private readonly InvoicingMasterDataRepository _repository;
    private readonly InvoicingUnitProfileDraft _draft;
    private readonly IReadOnlyList<InvoicingOwnerOption> _owners;
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _completed;
    private bool _focused;
    private bool _initializing = true;
    private bool _saving;

    public InvoicingUnitProfileEditorWindow(
        string propertyAndUnit,
        InvoicingMasterDataSnapshot snapshot,
        InvoicingUnitProfileDraft draft,
        InvoicingMasterDataRepository? repository = null)
    {
        InitializeComponent();
        _repository = repository ?? new InvoicingMasterDataRepository();
        _draft = draft;
        _owners = snapshot.OwnerOptions;

        Title = draft.UsageId == 0
            ? "Fakturierungsprofil der Einheit erfassen"
            : "Fakturierungsprofil der Einheit bearbeiten";
        UnitHeadingText.Text = draft.UsageId == 0
            ? $"Nutzung für {propertyAndUnit} erfassen"
            : $"{propertyAndUnit} · Nutzung bearbeiten";
        AppWindow.Resize(new SizeInt32(1220, 840));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.PreferredMinimumWidth = 780;
            presenter.PreferredMinimumHeight = 640;
        }

        UsageBox.ItemsSource = InvoicingUsageTypes.Options;
        OwnerBox.ItemsSource = _owners;
        OwnerAddressBox.ItemsSource = snapshot.AddressOptions;
        TenantAddressBox.ItemsSource = snapshot.AddressOptions;
        AncillaryModeBox.ItemsSource = InvoicingAncillaryModes.Options;

        UsageBox.SelectedValue = draft.UsageType;
        FromPicker.SelectedDate = draft.ValidFrom;
        ToPicker.SelectedDate = draft.ValidTo;
        OwnerBox.SelectedValue = draft.OwnerId;
        OwnerAddressBox.SelectedValue = draft.OwnerBillingAddressId;
        TenantAddressBox.SelectedValue = draft.TenantAddressId;
        AncillaryModeBox.SelectedValue = draft.AncillaryMode;
        ContractReferenceBox.Text = draft.ContractReference;
        DirectBillingCheck.IsChecked = draft.DirectBillingAllowed;
        DirectBillingApprovalBox.Text = draft.DirectBillingApprovalReference;
        _initializing = false;
        UpdateRentalState();

        RootGrid.SizeChanged += OnRootSizeChanged;
        Activated += OnActivated;
        Closed += OnWindowClosed;
    }

    public bool Saved { get; private set; }

    public Task<bool> ShowAsync()
    {
        Activate();
        return _completion.Task;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        ApplyResponsiveLayout(RootGrid.ActualWidth);
        if (_focused) return;
        _focused = true;
        UsageBox.Focus(FocusState.Programmatic);
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var wide = width >= 940;
        EditorContentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        EditorContentGrid.ColumnDefinitions[1].Width =
            wide ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);

        Grid.SetRow(UsageCard, 0);
        Grid.SetColumn(UsageCard, 0);
        Grid.SetColumnSpan(UsageCard, wide ? 1 : 2);

        Grid.SetRow(OwnerCard, 1);
        Grid.SetColumn(OwnerCard, 0);
        Grid.SetColumnSpan(OwnerCard, wide ? 1 : 2);

        Grid.SetRow(RentalCard, wide ? 0 : 2);
        Grid.SetColumn(RentalCard, wide ? 1 : 0);
        Grid.SetColumnSpan(RentalCard, wide ? 1 : 2);
        Grid.SetRowSpan(RentalCard, wide ? 2 : 1);
    }

    private void OnUsageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) UpdateRentalState();
    }

    private void OnOwnerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || OwnerBox.SelectedValue is not int ownerId) return;
        var owner = _owners.FirstOrDefault(option => option.Id == ownerId);
        OwnerAddressBox.SelectedValue = owner?.BillingAddressId;
    }

    private void OnDirectBillingChanged(object sender, RoutedEventArgs e) => UpdateRentalState();

    private void UpdateRentalState()
    {
        var rented = UsageBox.SelectedValue as string == InvoicingUsageTypes.Rented;
        RentalStatusText.Text = rented
            ? "Mieterangaben gelten für denselben Zeitraum und benötigen eine dokumentierte Freigabe."
            : "Für Selbstnutzung oder Leerstand bleibt der Eigentümer Empfänger; Mietfelder sind gesperrt.";
        TenantAddressBox.IsEnabled = rented;
        AncillaryModeBox.IsEnabled = rented;
        ContractReferenceBox.IsEnabled = rented;
        DirectBillingCheck.IsEnabled = rented;
        DirectBillingApprovalBox.IsEnabled = rented && DirectBillingCheck.IsChecked == true;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveAsync();

    private async Task SaveAsync()
    {
        if (_saving || _completed) return;
        _saving = true;
        SaveButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        EditorError.IsOpen = false;
        try
        {
            if (FromPicker.SelectedDate is null)
                throw new InvoicingMasterDataValidationException(["Gültig ab ist erforderlich."]);

            _draft.UsageType = UsageBox.SelectedValue as string ?? string.Empty;
            _draft.ValidFrom = FromPicker.SelectedDate.Value;
            _draft.ValidTo = ToPicker.SelectedDate;
            _draft.OwnerId = OwnerBox.SelectedValue is int ownerId ? ownerId : null;
            _draft.OwnerBillingAddressId =
                OwnerAddressBox.SelectedValue is int ownerAddressId ? ownerAddressId : null;

            if (_draft.UsageType == InvoicingUsageTypes.Rented)
            {
                _draft.TenantAddressId =
                    TenantAddressBox.SelectedValue is int tenantAddressId ? tenantAddressId : null;
                _draft.AncillaryMode = AncillaryModeBox.SelectedValue as string ?? string.Empty;
                _draft.ContractReference = ContractReferenceBox.Text;
                _draft.DirectBillingAllowed = DirectBillingCheck.IsChecked == true;
                _draft.DirectBillingApprovalReference = DirectBillingApprovalBox.Text;
            }
            else
            {
                _draft.TenantAddressId = null;
                _draft.AncillaryMode = InvoicingAncillaryModes.Included;
                _draft.ContractReference = string.Empty;
                _draft.DirectBillingAllowed = false;
                _draft.DirectBillingApprovalReference = string.Empty;
            }

            await _repository.SaveUnitProfileAsync(_draft);
            Saved = true;
            Complete(true);
        }
        catch (Exception exception)
        {
            EditorError.Message = exception.Message;
            EditorError.IsOpen = true;
        }
        finally
        {
            _saving = false;
            if (!_completed)
            {
                SaveButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
            }
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Complete(false);

    private async void OnSaveShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await SaveAsync();
    }

    private void OnCancelShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Complete(false);
    }

    private void Complete(bool saved)
    {
        if (_completed) return;
        _completed = true;
        _completion.TrySetResult(saved);
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        RootGrid.SizeChanged -= OnRootSizeChanged;
        Activated -= OnActivated;
        Closed -= OnWindowClosed;
        if (_completed) return;
        _completed = true;
        _completion.TrySetResult(false);
    }
}
