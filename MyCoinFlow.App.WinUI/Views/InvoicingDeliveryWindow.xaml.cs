using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.WinUI.Services;

namespace MyCoinFlow.WinUI.Views;

public enum InvoicingDeliverySection
{
    Email,
    Dms
}

public sealed partial class InvoicingDeliveryWindow : PersistentWindow
{
    private readonly int _documentId;
    private readonly InvoicingDeliveryService _service;
    private InvoicingDeliveryWorkspace? _workspace;
    private bool _loaded;
    private bool _busy;
    private bool _initialValuesApplied;
    private InvoicingDeliverySection _preferredSection;

    public InvoicingDeliveryWindow(
        int documentId,
        InvoicingDeliverySection preferredSection = InvoicingDeliverySection.Email,
        InvoicingDeliveryService? service = null)
    {
        InitializeComponent();
        _documentId = documentId;
        _preferredSection = preferredSection;
        _service = service ?? new InvoicingDeliveryService();
        ConfigureDpiAwareSizing(RootGrid, 980, 820, 720, 620);
        Activated += OnActivated;
        Closed += OnClosed;
    }

    public bool Changed { get; private set; }

    public void FocusSection(InvoicingDeliverySection section)
    {
        _preferredSection = section;
        if (!_loaded) return;
        if (section == InvoicingDeliverySection.Email)
            RecipientBox.Focus(FocusState.Programmatic);
        else if (AddAttachmentButton.IsEnabled)
            AddAttachmentButton.Focus(FocusState.Programmatic);
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_loaded || _busy) return;
        _loaded = true;
        await ReloadAsync();
        FocusSection(_preferredSection);
    }

    private async Task ReloadAsync(bool preserveStatus = false)
    {
        if (_busy) return;
        var selectedIds = DmsAttachmentList.SelectedItems
            .OfType<InvoicingDmsAttachment>()
            .Select(value => value.Id)
            .ToHashSet();
        SetBusy(true);
        if (!preserveStatus)
            StatusInfoBar.IsOpen = false;
        try
        {
            _workspace = await _service.LoadAsync(_documentId);
            RenderWorkspace(selectedIds);
            FooterStatusText.Text =
                $"{_workspace.DmsAttachments.Count} DMS-Datei(en) · {_workspace.Attempts.Count} Versandversuch(e)";
        }
        catch (Exception exception)
        {
            _workspace = null;
            Show(exception.Message, InfoBarSeverity.Error, "Versanddaten nicht verfügbar");
            FooterStatusText.Text = "E-Mail und DMS konnten nicht geladen werden.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RenderWorkspace(IReadOnlySet<int> selectedIds)
    {
        if (_workspace is not { } workspace) return;
        Title = $"{workspace.DocumentTitle} · E-Mail und DMS";
        HeaderTitleText.Text = workspace.DocumentTitle;
        HeaderSubtitleText.Text =
            $"Empfänger {workspace.RecipientName} · PDF, Beilagen und Versandnachweise";
        PdfStatusText.Text = workspace.PdfStatus;
        if (!_initialValuesApplied)
        {
            RecipientBox.Text = workspace.SuggestedRecipientAddress;
            SubjectBox.Text = workspace.DefaultSubject;
            BodyBox.Text = workspace.DefaultBody;
            _initialValuesApplied = true;
        }

        SmtpInfoBar.Message = workspace.Smtp.Display +
            (!string.IsNullOrWhiteSpace(workspace.Smtp.UserName)
                ? workspace.HasStoredSmtpPassword
                    ? " · Kennwort lokal gespeichert."
                    : " · Kennwort fehlt."
                : " · Kein SMTP-Benutzer erforderlich.");
        SmtpInfoBar.Severity = workspace.Smtp.IsConfigured &&
            (string.IsNullOrWhiteSpace(workspace.Smtp.UserName) || workspace.HasStoredSmtpPassword)
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Warning;

        DmsHelpText.Text = workspace.DmsEnabled
            ? "Dokumentbezogene DMS-Dateien können als zusätzliche Beilage gewählt werden. Die PDF-Ablage ist je Inhaltshash idempotent."
            : "Das DMS-Modul ist nicht freigeschaltet. Der E-Mail-Versand bleibt ohne DMS-Beilagen verfügbar.";
        ArchivePdfButton.IsEnabled = workspace.DmsEnabled && workspace.PdfReady && !_busy;
        AddAttachmentButton.IsEnabled = workspace.DmsEnabled && !_busy;
        DmsAttachmentList.IsEnabled = workspace.DmsEnabled && !_busy;
        DmsAttachmentList.ItemsSource = workspace.DmsAttachments;
        DmsAttachmentList.SelectedItems.Clear();
        foreach (var row in workspace.DmsAttachments.Where(value => selectedIds.Contains(value.Id)))
            DmsAttachmentList.SelectedItems.Add(row);
        DmsAttachmentList.Visibility = workspace.DmsAttachments.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyDmsPanel.Visibility = workspace.DmsAttachments.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        AttemptList.ItemsSource = workspace.Attempts;
        AttemptList.Visibility = workspace.Attempts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyAttemptText.Visibility = workspace.Attempts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateActions();
    }

    private async void OnArchivePdfClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _workspace is null) return;
        SetBusy(true);
        try
        {
            var archived = await _service.ArchivePdfAsync(_documentId);
            Changed = true;
            Show(
                $"{archived.DisplayTitle} ist im DMS abgelegt. Eine identische Ausgabe wird nicht doppelt angelegt.",
                InfoBarSeverity.Success,
                "PDF im DMS");
        }
        catch (Exception exception)
        {
            Show(exception.Message, InfoBarSeverity.Error, "DMS-Ablage fehlgeschlagen");
        }
        finally
        {
            SetBusy(false);
        }
        await ReloadAsync(preserveStatus: true);
    }

    private async void OnAddAttachmentClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _workspace is null) return;
        var path = await FilePickerService.PickOpenAsync(this, ".pdf", ".jpg", ".jpeg", ".png");
        if (string.IsNullOrWhiteSpace(path)) return;
        SetBusy(true);
        try
        {
            var archived = await _service.AddDmsAttachmentAsync(_documentId, path);
            Changed = true;
            Show(
                $"„{archived.OriginalName}“ wurde in das DMS kopiert und mit dem Dokument verknüpft.",
                InfoBarSeverity.Success,
                "Beilage hinzugefügt");
        }
        catch (Exception exception)
        {
            Show(exception.Message, InfoBarSeverity.Error, "Beilage nicht hinzugefügt");
        }
        finally
        {
            SetBusy(false);
        }
        await ReloadAsync(preserveStatus: true);
    }

    private async void OnSendClick(object sender, RoutedEventArgs e) => await SendAsync();

    private async Task SendAsync()
    {
        if (_busy || _workspace is null || RecipientConfirmedBox.IsChecked != true) return;
        SetBusy(true);
        StatusInfoBar.IsOpen = false;
        try
        {
            var result = await _service.SendAsync(new InvoicingDeliveryDraft(
                _documentId,
                RecipientBox.Text.Trim(),
                SubjectBox.Text.Trim(),
                BodyBox.Text.Trim(),
                DmsAttachmentList.SelectedItems
                    .OfType<InvoicingDmsAttachment>()
                    .Select(value => value.Id)
                    .ToArray(),
                RememberRecipientBox.IsChecked == true));
            Changed = true;
            RecipientConfirmedBox.IsChecked = false;
            var severity = result.Status switch
            {
                InvoicingDeliveryStatuses.Sent => InfoBarSeverity.Success,
                InvoicingDeliveryStatuses.Failed => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Warning
            };
            Show(result.Message, severity, result.Status switch
            {
                InvoicingDeliveryStatuses.Sent => $"Versuch {result.AttemptNumber} versendet",
                InvoicingDeliveryStatuses.Failed => $"Versuch {result.AttemptNumber} fehlgeschlagen",
                _ => $"Versuch {result.AttemptNumber} prüfen"
            });
        }
        catch (InvoicingDeliveryValidationException exception)
        {
            Show(string.Join("  •  ", exception.Errors), InfoBarSeverity.Warning, "Bitte prüfen");
        }
        catch (Exception exception)
        {
            Show(exception.Message, InfoBarSeverity.Error, "Versand fehlgeschlagen");
        }
        finally
        {
            SetBusy(false);
        }
        await ReloadAsync(preserveStatus: true);
    }

    private void OnDmsSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActions();
    private void OnDeliveryInputChanged(object sender, object e) => UpdateActions();

    private void UpdateActions()
    {
        var selectedCount = DmsAttachmentList.SelectedItems.Count;
        AttachmentSummaryText.Text = selectedCount == 0
            ? "Die Dokument-PDF wird immer angehängt; keine zusätzliche DMS-Beilage gewählt."
            : $"Die Dokument-PDF und {selectedCount} zusätzliche DMS-Beilage(n) werden angehängt.";
        SendButton.IsEnabled = !_busy &&
            _workspace?.CanSend == true &&
            RecipientConfirmedBox.IsChecked == true &&
            !string.IsNullOrWhiteSpace(RecipientBox.Text) &&
            !string.IsNullOrWhiteSpace(SubjectBox.Text) &&
            !string.IsNullOrWhiteSpace(BodyBox.Text) &&
            (string.IsNullOrWhiteSpace(_workspace.Smtp.UserName) || _workspace.HasStoredSmtpPassword);
        ArchivePdfButton.IsEnabled = !_busy && _workspace is { DmsEnabled: true, PdfReady: true };
        AddAttachmentButton.IsEnabled = !_busy && _workspace?.DmsEnabled == true;
        DmsAttachmentList.IsEnabled = !_busy && _workspace?.DmsEnabled == true;
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        BusyRing.IsActive = value;
        EditorScrollViewer.IsEnabled = !value;
        UpdateActions();
    }

    private void Show(string message, InfoBarSeverity severity, string title)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void OnSendShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (SendButton.IsEnabled) await SendAsync();
    }

    private async void OnReloadShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ReloadAsync();
    }

    private void OnCloseShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!_busy) Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (!_busy) Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Activated -= OnActivated;
        Closed -= OnClosed;
    }
}
