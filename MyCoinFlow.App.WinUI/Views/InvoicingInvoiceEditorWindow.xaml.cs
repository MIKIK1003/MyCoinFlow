using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InvoicingInvoiceEditorWindow : PersistentWindow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");

    private readonly InvoicingDocumentRecord _document;
    private readonly InvoicingInvoiceRepository _repository;
    private readonly string? _preferredKind;
    private InvoicingInvoiceEditorWorkspace? _workspace;
    private InvoicingInvoiceCalculationPreview? _preview;
    private bool _loaded;
    private bool _loading;
    private bool _saving;

    public InvoicingInvoiceEditorWindow(
        InvoicingDocumentRecord document,
        InvoicingInvoiceRepository? repository = null,
        string? preferredKind = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _repository = repository ?? new InvoicingInvoiceRepository();
        _preferredKind = preferredKind;
        InitializeComponent();

        Title = $"Rechnung {document.DocumentNumber} definitiv setzen";
        ConfigureDpiAwareSizing(RootGrid, 1080, 840, 760, 620);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        DiscountBox.Value = 0;
        RoundingBox.Value = 0;
        PaymentDaysBox.Value = 30;
        SkontoPercentBox.Value = 2;
        SkontoDaysBox.Value = 10;
        InstallmentCountBox.Value = 3;

        RootGrid.SizeChanged += OnRootSizeChanged;
        Activated += OnActivated;
        Closed += OnWindowClosed;
    }

    public bool Changed { get; private set; }
    public int FinalizedDocumentId { get; private set; }

    private InvoicingCodeOption? SelectedKind => KindBox.SelectedItem as InvoicingCodeOption;

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        ApplyResponsiveLayout(RootGrid.ActualWidth);
        if (_loaded) return;
        _loaded = true;
        await LoadWorkspaceAsync();
        KindBox.Focus(FocusState.Programmatic);
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var wide = width >= 880;
        EditorGrid.ColumnDefinitions[0].Width = wide
            ? new GridLength(5, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        EditorGrid.ColumnDefinitions[1].Width = wide
            ? new GridLength(4, GridUnitType.Star)
            : new GridLength(0);
        Grid.SetColumnSpan(TermsColumn, wide ? 1 : 2);
        Grid.SetRow(PreviewColumn, wide ? 0 : 1);
        Grid.SetColumn(PreviewColumn, wide ? 1 : 0);
        Grid.SetColumnSpan(PreviewColumn, wide ? 1 : 2);
    }

    private async Task LoadWorkspaceAsync()
    {
        _loading = true;
        LoadingOverlay.Visibility = Visibility.Visible;
        ErrorInfoBar.IsOpen = false;
        SaveButton.IsEnabled = false;
        try
        {
            _workspace = await _repository.LoadEditorWorkspaceAsync(_document);
            DocumentTitleText.Text = _document.Title;
            DocumentContextText.Text = $"{_document.Subject} · {_document.ContextTitleSnapshot}";
            DocumentDateText.Text = _document.DateDisplay;
            CurrencyText.Text =
                $"{_document.CurrencyCode} · Kurs {_document.ExchangeRateToBase:N6} · " +
                $"Basis {_workspace.BaseCurrencyCode}";
            HeaderSubtitleText.Text =
                $"{_document.DocumentNumber} · {_document.RecipientName} · {_document.ContextTitleSnapshot}";

            KindBox.ItemsSource = _workspace.AllowedKinds;
            KindBox.SelectedItem = _workspace.AllowedKinds.FirstOrDefault(option =>
                option.Code == (_preferredKind ?? _workspace.SuggestedInvoiceKind)) ??
                _workspace.AllowedKinds.FirstOrDefault();
            DiscountBox.Value = (double)_workspace.LockedDiscountPercent;
            RoundingBox.Value = (double)_workspace.LockedFullRoundingAdjustment;
            DiscountBox.IsEnabled = !_workspace.TermsLocked;
            RoundingBox.IsEnabled = !_workspace.TermsLocked;
            PaymentDaysBox.Value = _workspace.DefaultPaymentDays;
            LockedTermsInfoBar.Title = _workspace.TermsLocked
                ? "Rechnungsbasis eingefroren"
                : "Rechnungsbasis wird jetzt festgelegt";
            LockedTermsInfoBar.Message = _workspace.TermsLocked
                ? "Rabatt und Rundung stammen aus der ersten definitiven Teilrechnung und können in dieser Folge nicht mehr verändert werden."
                : "Rabatt und Rundung werden mit dieser ersten definitiven Rechnung für eine mögliche Teilrechnungsfolge eingefroren.";

            var remaining = _workspace.AgreedFullGrossBasis.HasValue
                ? _workspace.AgreedFullGrossBasis.Value - _workspace.PreviouslyInvoicedGross
                : Math.Max(0m, _document.PositionsTotal);
            PartialAmountBox.Value = (double)InvoicingInvoiceCalculator.RoundMoney(remaining / 2m);
            RecalculatePreview();
        }
        catch (Exception exception)
        {
            _workspace = null;
            ShowError(exception.Message);
            StatusText.Text = "Rechnungsarbeitsbereich konnte nicht geladen werden.";
        }
        finally
        {
            _loading = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            RecalculatePreview();
        }
    }

    private void OnInputChanged(object sender, SelectionChangedEventArgs e) => RecalculatePreview();

    private void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        RecalculatePreview();

    private void OnSkontoToggled(object sender, RoutedEventArgs e)
    {
        SkontoPercentBox.IsEnabled = SkontoSwitch.IsOn;
        SkontoDaysBox.IsEnabled = SkontoSwitch.IsOn;
        RecalculatePreview();
    }

    private void OnInstallmentToggled(object sender, RoutedEventArgs e)
    {
        InstallmentCountBox.IsEnabled = InstallmentSwitch.IsOn;
        InstallmentPreviewCard.Visibility = InstallmentSwitch.IsOn
            ? Visibility.Visible
            : Visibility.Collapsed;
        RecalculatePreview();
    }

    private void RecalculatePreview()
    {
        var selectedKind = SelectedKind;
        var kind = selectedKind?.Code;
        PartialAmountPanel.Visibility = kind == InvoicingInvoiceKindCodes.Partial
            ? Visibility.Visible
            : Visibility.Collapsed;
        SaveButton.IsEnabled = false;
        _preview = null;
        if (_workspace is null || _loading || _saving || kind is null)
            return;

        try
        {
            var draft = BuildDraft(includeInstallments: false);
            _preview = InvoicingInvoiceCalculator.Calculate(
                _document.Positions,
                _document.DocumentDate,
                _document.ExchangeRateToBase,
                draft,
                _workspace.PreviouslyInvoicedGross,
                _workspace.AgreedFullGrossBasis);
            RenderPreview(_preview);
            RenderInstallments(_preview);
            StatusText.Text =
                $"{selectedKind!.DisplayName} · {_preview.GrossDisplay(_document.CurrencyCode)} · " +
                $"fällig {_preview.DueDate:dd.MM.yyyy}";
            SaveButton.IsEnabled = true;
        }
        catch (Exception exception) when (exception is InvoicingInvoiceValidationException or OverflowException)
        {
            ClearPreview();
            InstallmentList.ItemsSource = null;
            StatusText.Text = exception.Message.Replace(Environment.NewLine, " · ");
        }
    }

    private InvoicingInvoiceDraft BuildDraft(bool includeInstallments)
    {
        var kind = SelectedKind?.Code ?? string.Empty;
        var draft = new InvoicingInvoiceDraft
        {
            DocumentId = _document.Id,
            InvoiceKind = kind,
            DiscountPercent = DecimalValue(DiscountBox),
            FullRoundingAdjustment = DecimalValue(RoundingBox),
            PaymentDays = IntegerValue(PaymentDaysBox),
            PartialGrossAmount = kind == InvoicingInvoiceKindCodes.Partial
                ? DecimalValue(PartialAmountBox)
                : null,
            SkontoPercent = SkontoSwitch.IsOn ? DecimalValue(SkontoPercentBox) : null,
            SkontoDays = SkontoSwitch.IsOn ? IntegerValue(SkontoDaysBox) : null
        };
        if (includeInstallments && InstallmentSwitch.IsOn && _preview is not null)
            draft.Installments.AddRange(CreateInstallments(_preview));
        return draft;
    }

    private void RenderPreview(InvoicingInvoiceCalculationPreview preview)
    {
        FullBasisText.Text = Money(preview.FullGrossBasis, _document.CurrencyCode);
        PreviouslyInvoicedText.Text = Money(preview.PreviouslyInvoicedGross, _document.CurrencyCode);
        NetAmountText.Text = Money(preview.NetAmount, _document.CurrencyCode);
        VatAmountText.Text = Money(preview.VatAmount, _document.CurrencyCode);
        InvoiceRoundingText.Text = Money(preview.RoundingAdjustment, _document.CurrencyCode);
        GrossAmountText.Text = Money(preview.GrossAmount, _document.CurrencyCode);
        DueDateText.Text = preview.DueDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        BaseAmountText.Text =
            $"Basiswährungsbetrag: {Money(preview.BaseGrossAmount, _workspace?.BaseCurrencyCode ?? string.Empty)}";
        SkontoPreviewText.Text = preview.SkontoAmount.HasValue
            ? $"Skonto: {Money(preview.SkontoAmount.Value, _document.CurrencyCode)} bis " +
              $"{preview.SkontoDueDate:dd.MM.yyyy}"
            : "Kein Skonto";
    }

    private void ClearPreview()
    {
        FullBasisText.Text = PreviouslyInvoicedText.Text = NetAmountText.Text =
            VatAmountText.Text = InvoiceRoundingText.Text = GrossAmountText.Text =
            DueDateText.Text = BaseAmountText.Text = "—";
        SkontoPreviewText.Text = "Kein Skonto";
    }

    private void RenderInstallments(InvoicingInvoiceCalculationPreview preview)
    {
        if (!InstallmentSwitch.IsOn)
        {
            InstallmentList.ItemsSource = null;
            return;
        }
        InstallmentList.ItemsSource = CreateInstallments(preview)
            .Select((rate, index) => new InstallmentPreviewRow(
                $"{index + 1}. {rate.Label} · {rate.DueDate:dd.MM.yyyy}",
                Money(rate.Amount, _document.CurrencyCode)))
            .ToList();
    }

    private IReadOnlyList<InvoicingInstallmentDraft> CreateInstallments(
        InvoicingInvoiceCalculationPreview preview)
    {
        var count = Math.Clamp(IntegerValue(InstallmentCountBox), 2, 24);
        var regularAmount = InvoicingInvoiceCalculator.RoundMoney(preview.GrossAmount / count);
        var result = new List<InvoicingInstallmentDraft>(count);
        decimal allocated = 0m;
        for (var index = 0; index < count; index++)
        {
            var amount = index == count - 1
                ? InvoicingInvoiceCalculator.RoundMoney(preview.GrossAmount - allocated)
                : regularAmount;
            var dueDate = preview.DueDate.AddMonths(index);
            result.Add(new InvoicingInstallmentDraft
            {
                DueDate = new DateTimeOffset(dueDate.ToDateTime(TimeOnly.MinValue)),
                Amount = amount,
                Label = $"Rate {index + 1} von {count}"
            });
            allocated += amount;
        }
        return result;
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

    private async Task SaveAsync()
    {
        if (_saving || _workspace is null || _preview is null || !SaveButton.IsEnabled)
            return;

        var confirmation = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = $"{_document.DocumentNumber} definitiv setzen?",
            Content =
                $"{SelectedKind?.DisplayName}: {Money(_preview.GrossAmount, _document.CurrencyCode)}. " +
                "Danach sind Dokument- und Finanzsnapshot unveränderlich; ein Fehler wird nur über Korrektur oder Storno berichtigt.",
            PrimaryButtonText = "Definitiv setzen",
            CloseButtonText = "Noch prüfen",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            return;

        _saving = true;
        LoadingOverlay.Visibility = Visibility.Visible;
        ErrorInfoBar.IsOpen = false;
        SaveButton.IsEnabled = false;
        try
        {
            var draft = BuildDraft(includeInstallments: true);
            await _repository.FinalizeAsync(draft);
            FinalizedDocumentId = _document.Id;
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
            RecalculatePreview();
        }
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

    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }

    private static decimal DecimalValue(NumberBox box) =>
        double.IsNaN(box.Value) ? 0m : Convert.ToDecimal(box.Value, CultureInfo.InvariantCulture);

    private static int IntegerValue(NumberBox box) =>
        double.IsNaN(box.Value) ? 0 : Convert.ToInt32(Math.Round(box.Value));

    private static string Money(decimal value, string currency) =>
        $"{value.ToString("N2", SwissCulture)} {currency}".TrimEnd();

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        RootGrid.SizeChanged -= OnRootSizeChanged;
        Activated -= OnActivated;
        Closed -= OnWindowClosed;
    }

    private sealed record InstallmentPreviewRow(string Label, string AmountDisplay);
}
