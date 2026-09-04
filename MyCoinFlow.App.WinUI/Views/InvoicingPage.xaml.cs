using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.WinUI.ViewModels;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InvoicingPage : Page
{
    private readonly InvoicingViewModel _viewModel = new();
    private readonly InvoicingDocumentRepository _documentRepository = new();
    private readonly InvoicingInvoiceRepository _invoiceRepository = new();
    private readonly InvoicingPaymentRepository _paymentRepository = new();
    private IReadOnlyList<BillableObjectRecord> _allObjects = [];
    private IReadOnlyList<InvoicingDocumentRecord> _allDocuments = [];
    private string _statusFilter = "ALL";
    private string _documentStatusFilter = "ALL";
    private bool _showDocuments = true;
    private bool _initialized;
    private bool _wideLayout = true;
    private bool _transitioning;
    private InvoicingMasterDataWindow? _masterDataWindow;
    private InvoicingPositionComposerWindow? _positionComposerWindow;
    private InvoicingTextTemplateManagerWindow? _textTemplateWindow;
    private InvoicingDocumentEditorWindow? _documentEditorWindow;
    private InvoicingInvoiceEditorWindow? _invoiceEditorWindow;
    private InvoicingDocumentPreviewWindow? _documentPreviewWindow;
    private int? _previewDocumentId;
    private InvoicingDeliveryWindow? _deliveryWindow;
    private int? _deliveryDocumentId;

    public InvoicingPage()
    {
        InitializeComponent();
        EffectiveDatePicker.SelectedDate = new DateTimeOffset(DateTime.Today);
        SourceFilterBox.SelectedIndex = 0;
        DocumentTypeFilterBox.SelectedIndex = 0;
        PageRoot.SizeChanged += OnPageSizeChanged;
        Loaded += OnLoaded;
    }

    private BillableObjectRecord? SelectedObject =>
        BillableObjectsList.SelectedItem as BillableObjectRecord;
    private InvoicingDocumentRecord? SelectedDocument =>
        DocumentsList.SelectedItem as InvoicingDocumentRecord;

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
        var wide = width >= 1120;
        _wideLayout = wide;
        Grid.SetColumnSpan(ObjectListCard, wide ? 1 : 2);
        Grid.SetRow(ObjectDetailCard, wide ? 0 : 1);
        Grid.SetColumn(ObjectDetailCard, wide ? 1 : 0);
        Grid.SetColumnSpan(ObjectDetailCard, wide ? 1 : 2);
        ObjectListCard.MinHeight = wide ? 240 : 170;
        ObjectDetailCard.MaxHeight = wide ? double.PositiveInfinity : 220;
        Grid.SetColumnSpan(DocumentListCard, wide ? 1 : 2);
        Grid.SetRow(DocumentDetailCard, wide ? 0 : 1);
        Grid.SetColumn(DocumentDetailCard, wide ? 1 : 0);
        Grid.SetColumnSpan(DocumentDetailCard, wide ? 1 : 2);
        DocumentListCard.MinHeight = wide ? 260 : 96;
        DocumentDetailCard.MaxHeight = wide ? double.PositiveInfinity : 220;
        DocumentMetricsPanel.Visibility = _showDocuments && wide
            ? Visibility.Visible
            : Visibility.Collapsed;
        ObjectMetricsPanel.Visibility = !_showDocuments && wide
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyDocumentFilterLayout(wide);
        DocumentSnapshotGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        DocumentSnapshotGrid.ColumnDefinitions[1].Width = wide
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        PlaceSnapshotCard(DocumentHeaderSnapshotCard, 0, 0, wide);
        PlaceSnapshotCard(DocumentRecipientSnapshotCard, wide ? 1 : 0, wide ? 0 : 1, wide);
        PlaceSnapshotCard(DocumentIssuerSnapshotCard, 0, wide ? 1 : 2, wide);
        PlaceSnapshotCard(DocumentTransitionSnapshotCard, wide ? 1 : 0, wide ? 1 : 3, wide);
        FinancialSnapshotGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        FinancialSnapshotGrid.ColumnDefinitions[1].Width = wide
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        PlaceSnapshotCard(FinancialSnapshotCard, 0, 0, wide);
        PlaceSnapshotCard(OpenItemSnapshotCard, wide ? 1 : 0, wide ? 0 : 1, wide);
    }

    private void ApplyDocumentFilterLayout(bool wide)
    {
        DocumentFilterGrid.MinWidth = wide ? 900 : 0;
        DocumentFilterGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        DocumentFilterGrid.ColumnDefinitions[1].Width = GridLength.Auto;
        DocumentFilterGrid.ColumnDefinitions[2].Width = wide ? GridLength.Auto : new GridLength(0);
        DocumentFilterGrid.ColumnDefinitions[3].Width = wide ? GridLength.Auto : new GridLength(0);

        Grid.SetColumn(DocumentSearchZone, 0);
        Grid.SetRow(DocumentSearchZone, 0);
        Grid.SetColumn(DocumentStatusLegendZone, 1);
        Grid.SetRow(DocumentStatusLegendZone, 0);
        Grid.SetColumn(DocumentQuickFilterZone, wide ? 2 : 0);
        Grid.SetRow(DocumentQuickFilterZone, wide ? 0 : 1);
        Grid.SetColumn(DocumentTypeFilterZone, wide ? 3 : 1);
        Grid.SetRow(DocumentTypeFilterZone, wide ? 0 : 1);
    }

    private static void PlaceSnapshotCard(FrameworkElement card, int column, int row, bool wide)
    {
        Grid.SetColumn(card, column);
        Grid.SetRow(card, row);
        Grid.SetColumnSpan(card, wide ? 1 : 2);
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
        (_showDocuments ? DocumentSearchBox : SearchBox).Focus(FocusState.Programmatic);
    }

    private void OnShowDocumentsClick(object sender, RoutedEventArgs e) =>
        SetWorkspaceMode(true);

    private void OnShowObjectsClick(object sender, RoutedEventArgs e) =>
        SetWorkspaceMode(false);

    private void SetWorkspaceMode(bool showDocuments)
    {
        _showDocuments = showDocuments;
        DocumentFilterBar.Visibility = showDocuments ? Visibility.Visible : Visibility.Collapsed;
        DocumentMetricsPanel.Visibility = showDocuments && _wideLayout
            ? Visibility.Visible
            : Visibility.Collapsed;
        DocumentWorkspaceGrid.Visibility = showDocuments ? Visibility.Visible : Visibility.Collapsed;
        ObjectFilterBar.Visibility = showDocuments ? Visibility.Collapsed : Visibility.Visible;
        ObjectMetricsPanel.Visibility = !showDocuments && _wideLayout
            ? Visibility.Visible
            : Visibility.Collapsed;
        ObjectWorkspaceGrid.Visibility = showDocuments ? Visibility.Collapsed : Visibility.Visible;
        ShowDocumentsButton.IsEnabled = !showDocuments;
        ShowObjectsButton.IsEnabled = showDocuments;
        if (showDocuments)
        {
            ComposeButton.IsEnabled = false;
            RenderSelectedDocument();
        }
        else
        {
            NextStepButton.IsEnabled = false;
            AdjustmentButton.IsEnabled = false;
            DunningButton.IsEnabled = false;
            PreviewPdfButton.IsEnabled = false;
            RenderSelectedObject();
        }
        RenderWorkspaceStatus();
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

    private void OnComposeClick(object sender, RoutedEventArgs e)
    {
        if (SelectedObject is not { IsSelectable: true } context) return;
        if (_positionComposerWindow is not null)
        {
            _positionComposerWindow.Activate();
            return;
        }

        _positionComposerWindow = new InvoicingPositionComposerWindow(context);
        _positionComposerWindow.Closed += OnPositionComposerWindowClosed;
        _positionComposerWindow.Activate();
    }

    private void OnPositionComposerWindowClosed(object sender, WindowEventArgs args)
    {
        if (_positionComposerWindow is not null)
            _positionComposerWindow.Closed -= OnPositionComposerWindowClosed;
        _positionComposerWindow = null;
    }

    private void OnTextTemplatesClick(object sender, RoutedEventArgs e)
    {
        if (_textTemplateWindow is not null)
        {
            _textTemplateWindow.Activate();
            return;
        }

        _textTemplateWindow = new InvoicingTextTemplateManagerWindow();
        _textTemplateWindow.Closed += OnTextTemplateWindowClosed;
        _textTemplateWindow.Activate();
    }

    private void OnTextTemplateWindowClosed(object sender, WindowEventArgs args)
    {
        if (_textTemplateWindow is not null)
            _textTemplateWindow.Closed -= OnTextTemplateWindowClosed;
        _textTemplateWindow = null;
    }

    private void OnNewOfferClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Overview is not { IsConfigured: true } ||
            _viewModel.BillableObjects is not { SelectableCount: > 0 })
        {
            ErrorInfoBar.Message =
                "Für eine neue Offerte sind eine vollständige Fakturierungseinrichtung und mindestens ein auswählbares Objekt erforderlich.";
            ErrorInfoBar.IsOpen = true;
            return;
        }
        if (_documentEditorWindow is not null)
        {
            _documentEditorWindow.Activate();
            return;
        }

        _documentEditorWindow = new InvoicingDocumentEditorWindow(SelectedObject, _documentRepository);
        _documentEditorWindow.Closed += OnDocumentEditorWindowClosed;
        _documentEditorWindow.Activate();
    }

    private async void OnDocumentEditorWindowClosed(object sender, WindowEventArgs args)
    {
        var changed = _documentEditorWindow?.Changed == true;
        var createdDocumentId = _documentEditorWindow?.CreatedDocumentId;
        if (_documentEditorWindow is not null)
            _documentEditorWindow.Closed -= OnDocumentEditorWindowClosed;
        _documentEditorWindow = null;
        if (changed && createdDocumentId is > 0)
        {
            SetWorkspaceMode(true);
            await ReloadAsync(createdDocumentId);
        }
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

    private void OnDocumentFilterChanged(object sender, object e) => ApplyDocumentFilters();

    private void OnDocumentStatusFilterClick(object sender, RoutedEventArgs e)
    {
        _documentStatusFilter = ReferenceEquals(sender, DraftDocumentStatusButton)
            ? InvoicingDocumentStatusCodes.Draft
            : ReferenceEquals(sender, TransferredDocumentStatusButton)
                ? InvoicingDocumentStatusCodes.Transferred
                : ReferenceEquals(sender, DefinitiveDocumentStatusButton)
                    ? InvoicingDocumentStatusCodes.Definitive
                    : "ALL";
        AllDocumentStatusButton.IsChecked = _documentStatusFilter == "ALL";
        DraftDocumentStatusButton.IsChecked = _documentStatusFilter == InvoicingDocumentStatusCodes.Draft;
        TransferredDocumentStatusButton.IsChecked = _documentStatusFilter == InvoicingDocumentStatusCodes.Transferred;
        DefinitiveDocumentStatusButton.IsChecked = _documentStatusFilter == InvoicingDocumentStatusCodes.Definitive;
        ApplyDocumentFilters();
    }

    private void OnDocumentSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RenderSelectedDocument();

    private void OnPreviewPdfClick(object sender, RoutedEventArgs e)
    {
        if (SelectedDocument is not { } document) return;
        if (_documentPreviewWindow is not null && _previewDocumentId == document.Id)
        {
            _documentPreviewWindow.Activate();
            return;
        }
        if (_documentPreviewWindow is not null)
        {
            _documentPreviewWindow.Close();
            _documentPreviewWindow = null;
        }
        _previewDocumentId = document.Id;
        _documentPreviewWindow = new InvoicingDocumentPreviewWindow(document.Id);
        _documentPreviewWindow.Closed += OnDocumentPreviewWindowClosed;
        _documentPreviewWindow.Activate();
    }

    private void OnDocumentPreviewWindowClosed(object sender, WindowEventArgs args)
    {
        if (_documentPreviewWindow is not null)
            _documentPreviewWindow.Closed -= OnDocumentPreviewWindowClosed;
        _documentPreviewWindow = null;
        _previewDocumentId = null;
    }

    private void OnEmailClick(object sender, RoutedEventArgs e) =>
        OpenDeliveryWindow(InvoicingDeliverySection.Email);

    private void OnDmsClick(object sender, RoutedEventArgs e) =>
        OpenDeliveryWindow(InvoicingDeliverySection.Dms);

    private void OpenDeliveryWindow(InvoicingDeliverySection section)
    {
        if (SelectedDocument is not { } document) return;
        if (_deliveryWindow is not null && _deliveryDocumentId == document.Id)
        {
            _deliveryWindow.FocusSection(section);
            _deliveryWindow.Activate();
            return;
        }
        if (_deliveryWindow is not null)
        {
            _deliveryWindow.Close();
            _deliveryWindow = null;
        }
        _deliveryDocumentId = document.Id;
        _deliveryWindow = new InvoicingDeliveryWindow(document.Id, section);
        _deliveryWindow.Closed += OnDeliveryWindowClosed;
        _deliveryWindow.Activate();
    }

    private async void OnDeliveryWindowClosed(object sender, WindowEventArgs args)
    {
        var changed = _deliveryWindow?.Changed == true;
        var documentId = _deliveryDocumentId;
        if (_deliveryWindow is not null)
            _deliveryWindow.Closed -= OnDeliveryWindowClosed;
        _deliveryWindow = null;
        _deliveryDocumentId = null;
        if (changed) await ReloadAsync(documentId);
    }

    private void OnDocumentFlowSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DocumentFlowList.SelectedItem is not InvoicingDocumentFlowStep step ||
            step.DocumentId == SelectedDocument?.Id)
        {
            return;
        }

        _documentStatusFilter = "ALL";
        AllDocumentStatusButton.IsChecked = true;
        DraftDocumentStatusButton.IsChecked = false;
        TransferredDocumentStatusButton.IsChecked = false;
        DefinitiveDocumentStatusButton.IsChecked = false;
        DocumentSearchBox.Text = string.Empty;
        DocumentTypeFilterBox.SelectedIndex = 0;
        ApplyDocumentFilters(step.DocumentId);
    }

    private async void OnNextStepClick(object sender, RoutedEventArgs e)
    {
        var document = SelectedDocument;
        if (document is null || _transitioning) return;

        if (document.CanFinalizeInvoice)
        {
            OpenInvoiceEditor(document);
            return;
        }
        if (document.CanCreateNextInvoice)
        {
            await CreateNextInvoiceAsync(document);
            return;
        }
        if (document.CanTransition)
            await TransitionDocumentAsync(document);
    }

    private void OpenInvoiceEditor(InvoicingDocumentRecord document, string? preferredKind = null)
    {
        if (_invoiceEditorWindow is not null)
        {
            _invoiceEditorWindow.Activate();
            return;
        }

        _invoiceEditorWindow = new InvoicingInvoiceEditorWindow(
            document,
            _invoiceRepository,
            preferredKind);
        _invoiceEditorWindow.Closed += OnInvoiceEditorWindowClosed;
        _invoiceEditorWindow.Activate();
    }

    private async void OnInvoiceEditorWindowClosed(object sender, WindowEventArgs args)
    {
        var changed = _invoiceEditorWindow?.Changed == true;
        var documentId = _invoiceEditorWindow?.FinalizedDocumentId;
        if (_invoiceEditorWindow is not null)
            _invoiceEditorWindow.Closed -= OnInvoiceEditorWindowClosed;
        _invoiceEditorWindow = null;
        if (changed && documentId is > 0)
            await ReloadAsync(documentId);
    }

    private async Task TransitionDocumentAsync(InvoicingDocumentRecord document)
    {
        if (!document.CanTransition) return;

        var targetType = InvoicingDocumentTypeCodes.DisplayName(document.NextDocumentType);
        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"{document.DocumentNumber} weiterführen?",
            Content =
                $"Es entsteht eine neue {targetType} mit eigener Nummer. Dokumentkopf, Aussteller, " +
                "Empfänger, Währung und Positionen werden als unveränderliche Snapshots übernommen; " +
                "das Ausgangsdokument wird als «Weitergeführt» markiert.",
            PrimaryButtonText = document.NextActionLabel,
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        _transitioning = true;
        ErrorInfoBar.IsOpen = false;
        RenderSelectedDocument();
        try
        {
            var createdId = await _documentRepository.TransitionAsync(
                document.Id,
                DateOnly.FromDateTime(DateTime.Today));
            await ReloadAsync(createdId);
        }
        catch (Exception exception)
        {
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
        }
        finally
        {
            _transitioning = false;
            RenderSelectedDocument();
        }
    }

    private async Task CreateNextInvoiceAsync(InvoicingDocumentRecord document)
    {
        if (!document.CanCreateNextInvoice || document.Financial is null) return;

        var kinds = InvoicingInvoiceKindCodes.PositiveOptions
            .Where(option => option.Code is
                InvoicingInvoiceKindCodes.Partial or InvoicingInvoiceKindCodes.Final)
            .ToList();
        var kindBox = new ComboBox
        {
            Header = "Folgerechnung",
            DisplayMemberPath = "Display",
            ItemsSource = kinds,
            SelectedItem = kinds[0],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var datePicker = new DatePicker
        {
            Header = "Dokumentdatum",
            SelectedDate = new DateTimeOffset(DateTime.Today),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var content = new StackPanel { Spacing = 10, MinWidth = 420 };
        content.Children.Add(new TextBlock
        {
            Text =
                $"Verbleibende Rechnungsbasis: {document.Financial.BillingRemaining:N2} " +
                $"{document.CurrencyCode}. Der neue Entwurf erhält eine eigene Rechnungsnummer.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(kindBox);
        content.Children.Add(datePicker);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"{document.DocumentNumber} weiter fakturieren",
            Content = content,
            PrimaryButtonText = "Entwurf anlegen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            kindBox.SelectedItem is not InvoicingCodeOption kind)
            return;

        _transitioning = true;
        ErrorInfoBar.IsOpen = false;
        RenderSelectedDocument();
        try
        {
            var createdId = await _invoiceRepository.CreateNextInvoiceDraftAsync(
                document.Id,
                kind.Code,
                DateOnly.FromDateTime(datePicker.SelectedDate?.Date ?? DateTime.Today));
            await ReloadAsync(createdId);
            if (SelectedDocument is { CanFinalizeInvoice: true } created)
                OpenInvoiceEditor(created, kind.Code);
        }
        catch (Exception exception)
        {
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
        }
        finally
        {
            _transitioning = false;
            RenderSelectedDocument();
        }
    }

    private async void OnAdjustmentClick(object sender, RoutedEventArgs e)
    {
        var document = SelectedDocument;
        if (document is not { CanCreateAdjustment: true } ||
            document.Financial?.OpenItem is not { } openItem ||
            _transitioning)
            return;

        var kinds = new List<InvoicingCodeOption>();
        if (openItem.OpenAmount >= 0.02m)
            kinds.Add(new(InvoicingInvoiceKindCodes.Correction, "Teilbetrag korrigieren"));
        kinds.Add(new(InvoicingInvoiceKindCodes.Cancellation, "Offenen Rest stornieren"));
        var kindBox = new ComboBox
        {
            Header = "Art",
            DisplayMemberPath = "Display",
            ItemsSource = kinds,
            SelectedItem = kinds[0],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var suggestedCorrectionAmount = InvoicingInvoiceCalculator.RoundMoney(openItem.OpenAmount / 2m);
        var amountBox = new NumberBox
        {
            Header = $"Korrekturbetrag in {document.CurrencyCode}",
            Minimum = 0.01,
            Maximum = (double)(openItem.OpenAmount - 0.01m),
            Value = (double)suggestedCorrectionAmount,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        void ApplyAdjustmentKind()
        {
            var isCancellation = kindBox.SelectedItem is InvoicingCodeOption option &&
                                 option.Code == InvoicingInvoiceKindCodes.Cancellation;
            amountBox.IsEnabled = !isCancellation;
            amountBox.Header = isCancellation
                ? $"Stornobetrag in {document.CurrencyCode} · offener Rest wird automatisch übernommen"
                : $"Korrekturbetrag in {document.CurrencyCode}";
            amountBox.Maximum = (double)(isCancellation
                ? openItem.OpenAmount
                : openItem.OpenAmount - 0.01m);
            amountBox.Value = (double)(isCancellation
                ? openItem.OpenAmount
                : suggestedCorrectionAmount);
        }
        kindBox.SelectionChanged += (_, _) => ApplyAdjustmentKind();
        ApplyAdjustmentKind();
        var reasonBox = new TextBox
        {
            Header = "Begründung",
            PlaceholderText = "Grund der Korrektur oder des Stornos vollständig festhalten",
            MaxLength = 500,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 88
        };
        var datePicker = new DatePicker
        {
            Header = "Belegdatum",
            SelectedDate = new DateTimeOffset(DateTime.Today),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var content = new StackPanel { Spacing = 10, MinWidth = 460 };
        content.Children.Add(new TextBlock
        {
            Text =
                $"Bezugsrechnung {document.DocumentNumber} · offen {openItem.OpenAmount:N2} " +
                $"{document.CurrencyCode}. Der Originalbeleg bleibt unverändert.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(kindBox);
        content.Children.Add(amountBox);
        content.Children.Add(reasonBox);
        content.Children.Add(datePicker);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Korrektur oder Storno erstellen",
            Content = content,
            PrimaryButtonText = "Definitiven Beleg erstellen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            kindBox.SelectedItem is not InvoicingCodeOption kind)
            return;

        _transitioning = true;
        ErrorInfoBar.IsOpen = false;
        RenderSelectedDocument();
        try
        {
            var amount = double.IsNaN(amountBox.Value)
                ? 0m
                : Convert.ToDecimal(amountBox.Value);
            var createdId = await _invoiceRepository.CreateAdjustmentAsync(
                new InvoicingAdjustmentDraft
                {
                    ReferenceInvoiceDocumentId = document.Id,
                    AdjustmentKind = kind.Code,
                    Amount = amount,
                    Reason = reasonBox.Text
                },
                DateOnly.FromDateTime(datePicker.SelectedDate?.Date ?? DateTime.Today));
            await ReloadAsync(createdId);
        }
        catch (Exception exception)
        {
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
        }
        finally
        {
            _transitioning = false;
            RenderSelectedDocument();
        }
    }

    private async void OnDunningClick(object sender, RoutedEventArgs e)
    {
        var document = SelectedDocument;
        var openItem = document?.Financial?.OpenItem;
        if (document is null || openItem is not { OpenAmount: > 0m } || _transitioning) return;

        var levelBox = new NumberBox
        {
            Header = "Mahnstufe (0 bis 4)",
            Minimum = 0,
            Maximum = 4,
            SmallChange = 1,
            Value = openItem.DunningLevel,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        var blockedSwitch = new ToggleSwitch
        {
            Header = "Mahnsperre",
            OffContent = "Nicht gesperrt",
            OnContent = "Gesperrt",
            IsOn = openItem.IsDunningBlocked
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = $"{document.DocumentNumber} · offen {openItem.OpenAmountDisplay}",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(levelBox);
        content.Children.Add(blockedSwitch);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Mahnstatus bearbeiten",
            Content = content,
            PrimaryButtonText = "Mahnstatus speichern",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        _transitioning = true;
        RenderSelectedDocument();
        try
        {
            await _paymentRepository.SetDunningAsync(
                document.Id,
                (byte)Math.Clamp((int)Math.Round(levelBox.Value), 0, 4),
                blockedSwitch.IsOn);
            await ReloadAsync(document.Id);
        }
        catch (Exception exception)
        {
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
        }
        finally
        {
            _transitioning = false;
            RenderSelectedDocument();
        }
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

    private async Task ReloadAsync(int? selectedDocumentId = null)
    {
        selectedDocumentId ??= SelectedDocument?.Id;
        var selectedKey = SelectedObject?.StableKey;
        LoadingOverlay.Visibility = Visibility.Visible;
        DocumentLoadingOverlay.Visibility = Visibility.Visible;
        ErrorInfoBar.IsOpen = false;
        MasterDataButton.IsEnabled = false;
        TextTemplatesButton.IsEnabled = false;
        ComposeButton.IsEnabled = false;
        NewOfferButton.IsEnabled = false;
        NextStepButton.IsEnabled = false;
        AdjustmentButton.IsEnabled = false;
        DunningButton.IsEnabled = false;

        var effectiveDate = EffectiveDatePicker.SelectedDate?.Date ?? DateTime.Today;
        await _viewModel.LoadAsync(DateOnly.FromDateTime(effectiveDate));
        RenderOverview();
        RenderDocuments(selectedDocumentId);
        ApplyFilters(selectedKey);

        var overview = _viewModel.Overview;
        var objects = _viewModel.BillableObjects;
        MasterDataButton.IsEnabled = overview is not null;
        TextTemplatesButton.IsEnabled = overview is not null;
        var canCreateOffer =
            overview is { IsConfigured: true } && objects is { SelectableCount: > 0 };
        NewOfferButton.IsEnabled = canCreateOffer;
        EmptyNewOfferButton.IsEnabled = canCreateOffer;
        ToolTipService.SetToolTip(
            NewOfferButton,
            canCreateOffer
                ? "Neue Offerte aus einem auswählbaren Objekt und seinen Positionen erstellen."
                : "Vollständige Fakturierungseinrichtung und mindestens ein auswählbares Objekt erforderlich.");
        SetWorkspaceMode(_showDocuments);
        LoadingOverlay.Visibility = Visibility.Collapsed;
        DocumentLoadingOverlay.Visibility = Visibility.Collapsed;
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

    private void RenderDocuments(int? selectedDocumentId = null)
    {
        var workspace = _viewModel.Documents;
        _allDocuments = workspace?.Documents ?? [];
        DocumentCountText.Text = _allDocuments.Count.ToString("N0");
        DocumentFlowCountText.Text = (workspace?.FlowCount ?? 0).ToString("N0");
        DocumentDraftCountText.Text = (workspace?.DraftCount ?? 0).ToString("N0");
        InvoiceDraftCountText.Text = (workspace?.InvoiceDraftCount ?? 0).ToString("N0");
        DefinitiveInvoiceCountText.Text = (workspace?.DefinitiveInvoiceCount ?? 0).ToString("N0");
        OpenItemCountText.Text = (workspace?.OpenItemCount ?? 0).ToString("N0");
        ApplyDocumentFilters(selectedDocumentId);
    }

    private void ApplyDocumentFilters(int? selectedDocumentId = null)
    {
        selectedDocumentId ??= SelectedDocument?.Id;
        var search = DocumentSearchBox.Text.Trim();
        var documentType = (DocumentTypeFilterBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        var filtered = _allDocuments
            .Where(document => documentType == "ALL" || document.DocumentType == documentType)
            .Where(document => _documentStatusFilter == "ALL" || document.Status == _documentStatusFilter)
            .Where(document => string.IsNullOrWhiteSpace(search) ||
                document.SearchText.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        DocumentsList.ItemsSource = filtered;
        DocumentsList.SelectedItem = selectedDocumentId is null
            ? filtered.FirstOrDefault()
            : filtered.FirstOrDefault(document => document.Id == selectedDocumentId) ?? filtered.FirstOrDefault();
        DocumentsList.Visibility = filtered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyDocumentsState.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DocumentDetailCard.Visibility = filtered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RenderSelectedDocument();
    }

    private void RenderSelectedDocument()
    {
        var document = SelectedDocument;
        if (document is null)
        {
            NextStepButton.IsEnabled = false;
            AdjustmentButton.IsEnabled = false;
            DunningButton.IsEnabled = false;
            PreviewPdfButton.IsEnabled = false;
            EmailButton.IsEnabled = false;
            DmsButton.IsEnabled = false;
            NextStepButtonText.Text = "Nächster Schritt";
            ToolTipService.SetToolTip(NextStepButton, "Zuerst ein weiterführbares Dokument wählen.");
            ToolTipService.SetToolTip(
                AdjustmentButton,
                "Zuerst eine korrigierbare definitive Rechnung wählen.");
            DocumentFlowList.ItemsSource = null;
            DocumentPositionsList.ItemsSource = null;
            FinancialPanel.Visibility = Visibility.Collapsed;
            InstallmentDetailList.ItemsSource = null;
            RevisionDetailList.ItemsSource = null;
            return;
        }

        DocumentDetailTitle.Text = document.Title;
        DocumentDetailSubtitle.Text =
            $"{document.Subject} · {document.ContextTitleSnapshot} · {document.DateDisplay}";
        var isInvoiceDraft = document.CanFinalizeInvoice;
        var isDefinitive = document.Status == InvoicingDocumentStatusCodes.Definitive;
        DocumentStatusInfoBar.Severity = isInvoiceDraft
            ? InfoBarSeverity.Warning
            : isDefinitive
                ? InfoBarSeverity.Success
            : document.Status == InvoicingDocumentStatusCodes.Draft
                ? InfoBarSeverity.Informational
                : InfoBarSeverity.Success;
        DocumentStatusInfoBar.Title = isInvoiceDraft
            ? "Rechnungsentwurf · bereit zur Definitivsetzung"
            : isDefinitive
                ? $"Definitiv · {document.DocumentTypeDisplay}"
            : document.Status == InvoicingDocumentStatusCodes.Draft
                ? "Entwurf · nächster Schritt wird bewusst ausgelöst"
                : "Weitergeführt · Snapshot bleibt unverändert";
        DocumentStatusInfoBar.Message = isInvoiceDraft
            ? $"Noch ohne Finanzwirkung; erstellt von {document.CreatedBy} am {document.CreatedAt:dd.MM.yyyy HH:mm}."
            : isDefinitive
                ? $"Definitiv gesetzt von {document.TransitionedBy} am {document.TransitionedAt:dd.MM.yyyy HH:mm}; Änderungen erfolgen nur über einen neuen Beleg."
            : document.Status == InvoicingDocumentStatusCodes.Draft
                ? $"Erstellt von {document.CreatedBy} am {document.CreatedAt:dd.MM.yyyy HH:mm}."
                : $"Weitergeführt von {document.TransitionedBy} am {document.TransitionedAt:dd.MM.yyyy HH:mm}.";

        DocumentFlowList.ItemsSource = document.Flow.OrderBy(step => step.Step).ToList();
        DocumentFlowList.SelectedItem = document.Flow.FirstOrDefault(step => step.DocumentId == document.Id);
        DocumentSubjectText.Text = document.Subject;
        DocumentContextText.Text =
            $"{document.ContextTitleSnapshot} · {document.ContextSubtitleSnapshot} · " +
            $"{document.ContextSource} #{document.ContextSourceId}";
        DocumentDateText.Text = $"Dokumentdatum: {document.DateDisplay}";
        DocumentCurrencyText.Text =
            $"{document.CurrencyCode} · Kurs zur Basis {document.ExchangeRateToBase:N6} · " +
            $"{document.ExchangeRateSource}";
        DocumentRecipientKindText.Text = document.RecipientKindDisplay;
        DocumentRecipientAddressText.Text = document.RecipientDisplay;
        DocumentIssuerText.Text = string.Join(
            Environment.NewLine,
            new[]
            {
                document.IssuerDisplay,
                string.IsNullOrWhiteSpace(document.IssuerVatNumber) ? null : $"MWST {document.IssuerVatNumber}",
                string.IsNullOrWhiteSpace(document.IssuerEmail) ? null : document.IssuerEmail,
                string.IsNullOrWhiteSpace(document.IssuerPhone) ? null : document.IssuerPhone
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        DocumentPreviousText.Text = string.IsNullOrWhiteSpace(document.PreviousDocumentNumber)
            ? "Vorgänger: —"
            : $"Vorgänger: {document.PreviousDocumentNumber}";
        DocumentNextText.Text = string.IsNullOrWhiteSpace(document.NextDocumentNumber)
            ? "Nachfolger: —"
            : $"Nachfolger: {document.NextDocumentNumber}";
        DocumentPositionsList.ItemsSource = document.Positions;
        DocumentPositionsTotalText.Text = document.PositionsTotalDisplay;
        InvoiceDraftInfoBar.Visibility = isInvoiceDraft
            ? Visibility.Visible
            : Visibility.Collapsed;

        var financial = document.Financial;
        FinancialPanel.Visibility = financial is null ? Visibility.Collapsed : Visibility.Visible;
        if (financial is not null)
        {
            InvoiceKindValueText.Text = financial.InvoiceKindDisplay;
            InvoiceAmountsText.Text =
                $"Netto {financial.NetAmount:N2} · MWST {financial.VatAmount:N2} · " +
                $"Brutto {financial.GrossAmount:N2} {document.CurrencyCode}";
            InvoiceBasisText.Text = financial.IsPositiveInvoice
                ? $"Gesamtbasis {financial.FullGrossBasis:N2} · zuvor fakturiert " +
                  $"{financial.PreviouslyInvoicedGross:N2} · im Fluss {financial.FlowInvoicedGross:N2} " +
                  document.CurrencyCode
                : $"Bezug Dokument #{financial.ReferenceInvoiceDocumentId} · {financial.AdjustmentReason}";
            InvoiceTermsText.Text = financial.IsPositiveInvoice
                ? $"Rabatt {financial.DiscountPercent:N2} % · Rundung {financial.RoundingAdjustment:N2} · " +
                  $"Zahlungsziel {financial.PaymentDays ?? 0} Tage · fällig {financial.DueDateDisplay} · " +
                  financial.SkontoDisplay
                : "Korrekturbeleg ohne eigenes Zahlungsziel oder Skonto";
            PaymentReferenceText.Text = string.IsNullOrWhiteSpace(financial.PaymentReference)
                ? "Zahlungsreferenz: —"
                : $"Zahlungsreferenz: {financial.PaymentReference}";

            if (financial.OpenItem is { } openItem)
            {
                OpenItemAmountText.Text = openItem.OpenAmountDisplay;
                OpenItemStatusText.Text =
                    $"{openItem.StatusDisplay} · bezahlt {openItem.PaidAmountDisplay} · ursprünglich {openItem.OriginalAmount:N2} · " +
                    $"Korrekturen {openItem.CorrectionAmount:N2} {openItem.CurrencyCode}";
                OpenItemDueDateText.Text =
                    $"Fällig {openItem.DueDateDisplay} · Basis offen {openItem.BaseOpenAmountDisplay}";
                OpenItemDunningText.Text = $"Mahnstatus: {openItem.DunningStatusDisplay}";
            }
            else
            {
                OpenItemAmountText.Text = "Kein eigener offener Posten";
                OpenItemStatusText.Text = financial.IsAdjustment
                    ? "Der Beleg reduziert den offenen Posten seiner Bezugsrechnung."
                    : "—";
                OpenItemDueDateText.Text = "—";
                OpenItemDunningText.Text = "—";
            }

            InstallmentDetailPanel.Visibility = financial.Installments.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            InstallmentDetailList.ItemsSource = financial.Installments
                .Select(rate => new InstallmentDetailRow(
                    rate.DueDateDisplay,
                    rate.Label,
                    rate.AmountDisplay(document.CurrencyCode)))
                .ToList();
            RevisionDetailPanel.Visibility = financial.Revisions.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            RevisionDetailList.ItemsSource = financial.Revisions;
        }

        NextStepButtonText.Text = document.CanTransition ||
                                  document.CanFinalizeInvoice ||
                                  document.CanCreateNextInvoice
            ? document.NextActionLabel
            : "Nächster Schritt";
        NextStepButton.IsEnabled = _showDocuments &&
                                   (document.CanTransition ||
                                    document.CanFinalizeInvoice ||
                                    document.CanCreateNextInvoice) &&
                                   !_transitioning;
        ToolTipService.SetToolTip(NextStepButton, document.NextActionLabel);
        AdjustmentButton.IsEnabled =
            _showDocuments && document.CanCreateAdjustment && !_transitioning;
        DunningButton.IsEnabled = _showDocuments &&
                                  financial?.OpenItem is { OpenAmount: > 0m } &&
                                  !_transitioning;
        PreviewPdfButton.IsEnabled = _showDocuments && !_transitioning;
        EmailButton.IsEnabled = _showDocuments && !_transitioning;
        DmsButton.IsEnabled = _showDocuments && !_transitioning && AppModules.IsDmsEnabled;
        ToolTipService.SetToolTip(
            PreviewPdfButton,
            $"Vorschau öffnen und {document.DocumentTypeDisplay} {document.DocumentNumber} als PDF speichern.");
        ToolTipService.SetToolTip(
            EmailButton,
            $"{document.DocumentTypeDisplay} {document.DocumentNumber} als PDF per E-Mail senden.");
        ToolTipService.SetToolTip(
            DmsButton,
            AppModules.IsDmsEnabled
                ? $"PDF und Beilagen zu {document.DocumentNumber} im DMS verwalten."
                : "Das DMS-Modul ist nicht freigeschaltet.");
        ToolTipService.SetToolTip(
            AdjustmentButton,
            document.CanCreateAdjustment
                ? $"Korrektur oder Storno zu {document.DocumentNumber} erstellen."
                : "Nur eine vollständig fakturierte definitive Rechnung mit offenem Posten kann korrigiert werden.");
    }

    private void RenderWorkspaceStatus()
    {
        var overview = _viewModel.Overview;
        if (overview is null) return;

        if (_showDocuments)
        {
            var documents = _viewModel.Documents;
            StatusText.Text =
                $"Mandant: {overview.DatabaseName} · {_allDocuments.Count:N0} Dokumente in " +
                $"{documents?.FlowCount ?? 0:N0} Abläufen · {documents?.DraftCount ?? 0:N0} Entwürfe";
        }
        else if (_viewModel.BillableObjects is { } objects)
        {
            StatusText.Text =
                $"Mandant: {overview.DatabaseName} · Stichtag {objects.EffectiveDate:dd.MM.yyyy} · " +
                $"{objects.SelectableCount:N0} von {objects.Objects.Count:N0} Objekten auswählbar";
        }

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
            ComposeButton.IsEnabled = false;
            ToolTipService.SetToolTip(ComposeButton, "Zuerst ein auswählbares Objekt wählen.");
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

        ComposeButton.IsEnabled = !_showDocuments && item.IsSelectable;
        ToolTipService.SetToolTip(
            ComposeButton,
            item.IsSelectable
                ? $"Positionen für «{item.Title}» verfassen"
                : "Der Objektkontext muss vor der Positionserfassung vollständig geprüft sein.");

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
    private sealed record InstallmentDetailRow(
        string DueDateDisplay,
        string Label,
        string AmountDisplay);
}
