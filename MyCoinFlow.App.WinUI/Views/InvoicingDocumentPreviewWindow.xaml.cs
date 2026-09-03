using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.WinUI.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InvoicingDocumentPreviewWindow : PersistentWindow
{
    private readonly int _documentId;
    private readonly InvoicingOutputRepository _repository;
    private InvoicingOutputWorkspace? _workspace;
    private InvoicingPdfArtifact? _artifact;
    private bool _loaded;
    private bool _busy;
    private string? _previewPath;

    public InvoicingDocumentPreviewWindow(
        int documentId,
        InvoicingOutputRepository? repository = null)
    {
        InitializeComponent();
        _documentId = documentId;
        _repository = repository ?? new InvoicingOutputRepository();
        ConfigureDpiAwareSizing(RootGrid, 1180, 860, 760, 620);
        Activated += OnActivated;
        Closed += OnWindowClosed;
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_loaded || _busy) return;
        _loaded = true;
        await LoadWorkspaceAsync();
    }

    private async Task LoadWorkspaceAsync()
    {
        SetBusy(true);
        ErrorInfoBar.IsOpen = false;
        try
        {
            _workspace = await _repository.LoadWorkspaceAsync(_documentId);
            RenderWorkspace();
            if (!_workspace.RequiresPaymentSnapshot || _workspace.Snapshot is not null)
                await BuildAndShowAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            StatusText.Text = "Dokumentvorschau ist nicht verfügbar.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RenderWorkspace()
    {
        if (_workspace is not { } workspace) return;
        var document = workspace.Document;
        Title = $"{document.DocumentTypeDisplay} {document.DocumentNumber} · Vorschau";
        HeaderTitleText.Text = $"{document.DocumentTypeDisplay} {document.DocumentNumber}";
        HeaderSubtitleText.Text =
            $"{document.Subject} · {document.RecipientName} · {document.StatusDisplay}";

        PaymentAccountBox.ItemsSource = workspace.Snapshot is { } snapshot
            ? new[]
            {
                new InvoicingPaymentAccountOption(
                    snapshot.PaymentAccountId,
                    snapshot.PaymentAccountName,
                    snapshot.Iban,
                    snapshot.Bic,
                    snapshot.AccountNumber,
                    snapshot.CurrencyCode,
                    snapshot.IsQrIban)
            }
            : workspace.PaymentAccounts;
        PaymentAccountBox.SelectedIndex =
            PaymentAccountBox.Items.Count == 1 ? 0 : -1;
        PaymentAccountBox.IsEnabled =
            workspace.RequiresPaymentSnapshot && workspace.Snapshot is null;
        SnapshotInfoBar.IsOpen =
            workspace.RequiresPaymentSnapshot && workspace.Snapshot is null;

        if (!workspace.RequiresPaymentSnapshot)
        {
            PaymentAccountBox.Visibility = Visibility.Collapsed;
            PaymentAccountHelpText.Text =
                document.Status == InvoicingDocumentStatusCodes.Draft
                    ? "Entwurf mit sichtbarer Kennzeichnung; kein Zahlungsteil."
                    : "Für dieses Dokument ist kein Zahlungsteil erforderlich.";
            GenerateButton.Content = "Vorschau aktualisieren";
            OutputBadgeText.Text = "PDF · ohne Zahlungsteil";
        }
        else if (workspace.Snapshot is { } frozen)
        {
            PaymentAccountBox.Visibility = Visibility.Visible;
            PaymentAccountHelpText.Text =
                $"Eingefroren: {frozen.OutputKindDisplay} · Vorlagenstand {frozen.TemplateVersion}";
            GenerateButton.Content = "Vorschau aktualisieren";
            OutputBadgeText.Text = frozen.HasSwissQr ? "PDF · Swiss QR" : "PDF · Zahlungsangaben";
        }
        else
        {
            PaymentAccountBox.Visibility = Visibility.Visible;
            PaymentAccountHelpText.Text = workspace.PaymentAccounts.Count == 0
                ? $"Kein aktives Zahlungskonto in {document.CurrencyCode}. Bitte unter Finanzen einrichten."
                : $"Aktives Konto in {document.CurrencyCode} wählen; die Auswahl kann danach nicht geändert werden.";
            GenerateButton.Content = "Konto festlegen und Vorschau erstellen";
            OutputBadgeText.Text = "PDF · Kontowahl offen";
        }
        UpdateActions();
    }

    private async Task BuildAndShowAsync()
    {
        if (_workspace is null) return;
        SetBusy(true);
        ErrorInfoBar.IsOpen = false;
        try
        {
            _artifact = await Task.Run(() => InvoicingPdfDocumentBuilder.Build(_workspace));
            await ShowPreviewAsync(_artifact);
            StatusText.Text =
                $"{_artifact.PageCount} Seite(n) · SHA-256 {_artifact.Sha256[..12]}… · Vorschau und Speicherung sind bytegleich.";
        }
        catch (Exception exception)
        {
            _artifact = null;
            ShowError(exception.Message);
            StatusText.Text = "PDF-Erzeugung fehlgeschlagen.";
        }
        finally
        {
            SetBusy(false);
            UpdateActions();
        }
    }

    private async Task ShowPreviewAsync(InvoicingPdfArtifact artifact)
    {
        var folder = Path.Combine(Path.GetTempPath(), "MyCoinFlow", "PdfPreview");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{artifact.Sha256}.pdf");
        await File.WriteAllBytesAsync(path, artifact.Content);
        var previousPath = _previewPath;
        _previewPath = path;
        await PdfWebView.EnsureCoreWebView2Async();
        PdfWebView.Source = new Uri(path);
        EmptyPreviewPanel.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(previousPath) &&
            !previousPath.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(previousPath);
        }
    }

    private async void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        if (_workspace is null || _busy) return;
        if (_workspace.RequiresPaymentSnapshot && _workspace.Snapshot is null)
        {
            if (PaymentAccountBox.SelectedItem is not InvoicingPaymentAccountOption account)
            {
                ShowError("Bitte zuerst ein Zahlungskonto wählen.");
                return;
            }
            SetBusy(true);
            try
            {
                await _repository.CreateSnapshotAsync(_documentId, account.Id);
                _workspace = await _repository.LoadWorkspaceAsync(_documentId);
                RenderWorkspace();
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
                SetBusy(false);
                return;
            }
            finally
            {
                SetBusy(false);
            }
        }
        await BuildAndShowAsync();
    }

    private async void OnReloadClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _artifact = null;
        await LoadWorkspaceAsync();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveAsync();

    private async Task SaveAsync()
    {
        if (_artifact is null || _busy) return;
        try
        {
            var path = await FilePickerService.PickSaveAsync(
                this,
                Path.GetFileNameWithoutExtension(_artifact.SuggestedFileName),
                "PDF-Dokument",
                ".pdf");
            if (string.IsNullOrWhiteSpace(path)) return;
            await File.WriteAllBytesAsync(path, _artifact.Content);
            StatusText.Text = $"PDF gespeichert: {path}";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void OnPaymentAccountSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateActions();

    private void UpdateActions()
    {
        var canGenerate = _workspace is not null &&
            (!_workspace.RequiresPaymentSnapshot ||
             _workspace.Snapshot is not null ||
             PaymentAccountBox.SelectedItem is InvoicingPaymentAccountOption) &&
            !_busy;
        GenerateButton.IsEnabled = canGenerate;
        SaveButton.IsEnabled = _artifact is not null && !_busy;
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        LoadingOverlay.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        UpdateActions();
    }

    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }

    private void OnPreviewNavigationCompleted(
        WebView2 sender,
        CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.IsSuccess) return;
        ShowError("Die PDF-Datei wurde erzeugt, konnte aber nicht in der Vorschau angezeigt werden.");
    }

    private async void OnSaveShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await SaveAsync();
    }

    private async void OnRefreshShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!_busy) await LoadWorkspaceAsync();
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

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Activated -= OnActivated;
        Closed -= OnWindowClosed;
        PdfWebView.Close();
        if (!string.IsNullOrWhiteSpace(_previewPath))
            TryDelete(_previewPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Temporäre Vorschauen werden spätestens durch die Betriebssystembereinigung entfernt.
        }
    }
}
