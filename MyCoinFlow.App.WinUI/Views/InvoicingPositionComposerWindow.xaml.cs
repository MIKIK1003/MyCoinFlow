using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InvoicingPositionComposerWindow : PersistentWindow
{
    private readonly BillableObjectRecord _context;
    private readonly InvoicingPositionRepository _repository;
    private InvoicingComposerWorkspace? _workspace;
    private InvoicingPositionDraft _draft;
    private InvoicingFormattedTextSnapshot _mainText = new(string.Empty, null);
    private InvoicingFormattedTextSnapshot _additionalText = new(string.Empty, null);
    private InvoicingTextTemplateManagerWindow? _templateWindow;
    private bool _loaded;
    private bool _saving;
    private bool _suppressPositionTypeChange;
    private bool _articleTextIsProgrammatic;

    public InvoicingPositionComposerWindow(
        BillableObjectRecord context,
        InvoicingPositionRepository? repository = null)
    {
        _context = context;
        _repository = repository ?? new InvoicingPositionRepository();
        _draft = CreateEmptyDraft();
        InitializeComponent();

        Title = $"Positionen verfassen · {context.Title}";
        ContextText.Text = $"{context.SourceDisplay} · {context.Title}";
        ConfigureDpiAwareSizing(RootGrid, 1380, 900, 780, 640);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        RootGrid.SizeChanged += OnRootSizeChanged;
        Activated += OnActivated;
        Closed += OnWindowClosed;
    }

    public bool Changed { get; private set; }

    private InvoicingPositionRecord? SelectedPosition =>
        PositionsList.SelectedItem as InvoicingPositionRecord;

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        ApplyResponsiveLayout(RootGrid.ActualWidth);
        if (_loaded) return;
        _loaded = true;
        await LoadWorkspaceAsync(beginNew: true);
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var wide = width >= 980;
        ContentGrid.ColumnDefinitions[0].Width = wide
            ? new GridLength(5, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        ContentGrid.ColumnDefinitions[1].Width = wide
            ? new GridLength(7, GridUnitType.Star)
            : new GridLength(0);
        ContentGrid.RowDefinitions[0].Height = wide
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(2, GridUnitType.Star);
        ContentGrid.RowDefinitions[1].Height = wide
            ? GridLength.Auto
            : new GridLength(3, GridUnitType.Star);

        Grid.SetColumnSpan(PositionsCard, wide ? 1 : 2);
        Grid.SetRow(EntryCard, wide ? 0 : 1);
        Grid.SetColumn(EntryCard, wide ? 1 : 0);
        Grid.SetColumnSpan(EntryCard, wide ? 1 : 2);
        PositionsCard.MaxHeight = wide ? double.PositiveInfinity : 260;
    }

    private async Task LoadWorkspaceAsync(
        int? selectedPositionId = null,
        bool beginNew = false)
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        ErrorInfoBar.IsOpen = false;
        try
        {
            _workspace = await _repository.LoadWorkspaceAsync(
                _context.SourceCode,
                _context.SourceId,
                _context.Title);
            PositionsList.ItemsSource = _workspace.Positions;
            EmptyState.Visibility = _workspace.Positions.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            PositionsList.Visibility = _workspace.Positions.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
            TemplateBox.ItemsSource = _workspace.TextTemplates;
            VatBox.ItemsSource = _workspace.VatOptions;
            RevenueAccountBox.ItemsSource = _workspace.RevenueAccountOptions;
            PositionsList.SelectedItem = selectedPositionId.HasValue
                ? _workspace.Positions.FirstOrDefault(position => position.Id == selectedPositionId.Value)
                : _workspace.Positions.FirstOrDefault();
            UpdateSelectionActions();
            if (beginNew)
                BeginNew(prefillContextArticle: true);
            RenderStatus();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            StatusText.Text = "Positionsentwurf konnte nicht geladen werden.";
            AcceptButton.IsEnabled = false;
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void RenderStatus()
    {
        if (_workspace is null) return;
        StatusText.Text =
            $"{_workspace.Positions.Count:N0} Position(en) · " +
            $"{_workspace.Total.ToString("N2", CultureInfo.GetCultureInfo("de-CH"))} Basiswährung · " +
            "unmittelbar im objektbezogenen Entwurf gespeichert";
    }

    private void OnPositionSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectionActions();

    private void UpdateSelectionActions()
    {
        var selected = SelectedPosition;
        var positions = _workspace?.Positions ?? [];
        var index = selected is null
            ? -1
            : positions.ToList().FindIndex(position => position.Id == selected.Id);
        EditButton.IsEnabled = selected is not null;
        DeleteButton.IsEnabled = selected is not null;
        MoveUpButton.IsEnabled =
            index > 0 && positions[index - 1].IsFooter == positions[index].IsFooter;
        MoveDownButton.IsEnabled =
            index >= 0 && index < positions.Count - 1 &&
            positions[index + 1].IsFooter == positions[index].IsFooter;
    }

    private void OnPositionDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        EditSelectedPosition();
    }

    private void OnNewClick(object sender, RoutedEventArgs e) => BeginNew(prefillContextArticle: false);

    private void OnNewShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        BeginNew(prefillContextArticle: false);
    }

    private void BeginNew(bool prefillContextArticle)
    {
        _draft = CreateEmptyDraft();
        _mainText = new InvoicingFormattedTextSnapshot(string.Empty, null);
        _additionalText = new InvoicingFormattedTextSnapshot(string.Empty, null);
        _suppressPositionTypeChange = true;
        ArticlePositionRadio.IsChecked = true;
        TextPositionRadio.IsChecked = false;
        _suppressPositionTypeChange = false;
        EntryTitle.Text = "Neue Position";
        AcceptButton.Content = "Position übernehmen";
        ArticleSearchBox.Text = string.Empty;
        ArticleSearchBox.ItemsSource = null;
        TemplateBox.SelectedItem = null;
        DesignationBox.Text = string.Empty;
        CategoryBox.Text = string.Empty;
        UnitBox.Text = string.Empty;
        QuantityBox.Value = 1;
        UnitPriceBox.Value = 0;
        VatBox.SelectedValue = _workspace?.VatOptions.FirstOrDefault()?.Id;
        RevenueAccountBox.SelectedValue = _workspace?.RevenueAccountOptions.FirstOrDefault()?.Id;
        FooterToggle.IsOn = false;
        ClassificationText.Text = "Nebenkostenklassifikation: Allgemeine Sach-/Leistung";
        RenderTextSnapshots();
        ApplyPositionTypeLayout();
        AcceptButton.IsEnabled = _workspace is not null;

        if (prefillContextArticle &&
            _context.SourceCode == InvoicingPositionTypes.Article &&
            _workspace?.Articles.FirstOrDefault(article => article.Id == _context.SourceId) is { } article)
        {
            ApplyArticle(article);
        }
        ArticleSearchBox.Focus(FocusState.Programmatic);
    }

    private InvoicingPositionDraft CreateEmptyDraft() => new()
    {
        ContextSource = _context.SourceCode,
        ContextSourceId = _context.SourceId,
        PositionType = InvoicingPositionTypes.Article,
        Quantity = 1m,
        AncillaryClassificationSnapshot = InvoicingAncillaryClassifications.Standard
    };

    private void OnPositionTypeChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressPositionTypeChange || ArticleSearchBox is null) return;
        var isText = TextPositionRadio.IsChecked == true;
        _draft = CreateEmptyDraft();
        _draft.PositionType = isText ? InvoicingPositionTypes.Text : InvoicingPositionTypes.Article;
        _mainText = new InvoicingFormattedTextSnapshot(string.Empty, null);
        _additionalText = new InvoicingFormattedTextSnapshot(string.Empty, null);
        ArticleSearchBox.Text = string.Empty;
        ArticleSearchBox.ItemsSource = null;
        TemplateBox.SelectedItem = null;
        DesignationBox.Text = string.Empty;
        CategoryBox.Text = string.Empty;
        UnitBox.Text = string.Empty;
        QuantityBox.Value = 1;
        UnitPriceBox.Value = 0;
        FooterToggle.IsOn = false;
        ClassificationText.Text = "Nebenkostenklassifikation: Allgemeine Sach-/Leistung";
        RenderTextSnapshots();
        ApplyPositionTypeLayout();
        if (isText)
            TemplateBox.Focus(FocusState.Programmatic);
        else
            ArticleSearchBox.Focus(FocusState.Programmatic);
    }

    private void ApplyPositionTypeLayout()
    {
        var isText = TextPositionRadio.IsChecked == true;
        ArticleSearchBox.Visibility = isText ? Visibility.Collapsed : Visibility.Visible;
        TemplateBox.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        AdditionalTextCard.Visibility = isText ? Visibility.Collapsed : Visibility.Visible;
        ArticleValuesCard.Visibility = isText ? Visibility.Collapsed : Visibility.Visible;
        TextOptionsCard.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnArticleSearchTextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_articleTextIsProgrammatic ||
            args.Reason != AutoSuggestionBoxTextChangeReason.UserInput ||
            _workspace is null)
            return;

        _draft.ArticleId = null;
        var search = sender.Text.Trim();
        sender.ItemsSource = _workspace.Articles
            .Where(article => string.IsNullOrWhiteSpace(search) || new[]
            {
                article.ArticleNumber,
                article.Designation,
                article.Category,
                article.Description
            }.Any(value => value.Contains(search, StringComparison.CurrentCultureIgnoreCase)))
            .Take(50)
            .ToList();
    }

    private void OnArticleSearchGotFocus(object sender, RoutedEventArgs e)
    {
        if (_workspace is not null && ArticleSearchBox.ItemsSource is null)
            ArticleSearchBox.ItemsSource = _workspace.Articles.Take(50).ToList();
    }

    private void OnArticleSuggestionChosen(
        AutoSuggestBox sender,
        AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is InvoicingArticleRecord article)
            ApplyArticle(article);
    }

    private void OnArticleQuerySubmitted(
        AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is InvoicingArticleRecord article)
            ApplyArticle(article);
    }

    private void ApplyArticle(InvoicingArticleRecord article)
    {
        _draft.ArticleId = article.Id;
        _draft.AncillaryClassificationSnapshot = article.AncillaryClassification;
        _articleTextIsProgrammatic = true;
        ArticleSearchBox.Text = $"{article.ArticleNumber} · {article.Designation}";
        _articleTextIsProgrammatic = false;
        DesignationBox.Text = article.Designation;
        CategoryBox.Text = article.Category;
        UnitBox.Text = article.Unit;
        UnitPriceBox.Value = decimal.ToDouble(article.SalePrice);
        VatBox.SelectedValue = article.VatRateId;
        RevenueAccountBox.SelectedValue = article.RevenueAccountId;
        _mainText = new InvoicingFormattedTextSnapshot(article.Description, null);
        _additionalText = new InvoicingFormattedTextSnapshot(string.Empty, null);
        ClassificationText.Text =
            $"Nebenkostenklassifikation: {article.AncillaryDisplay}";
        RenderTextSnapshots();
    }

    private void OnTemplateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TextPositionRadio.IsChecked != true ||
            TemplateBox.SelectedItem is not InvoicingTextTemplateRecord template)
            return;
        _mainText = new InvoicingFormattedTextSnapshot(template.PlainText, template.FormattedText);
        if (string.IsNullOrWhiteSpace(DesignationBox.Text))
            DesignationBox.Text = template.Name;
        RenderTextSnapshots();
        StatusText.Text = $"Textbaustein «{template.Name}» als Ausgangspunkt übernommen.";
    }

    private async void OnEditMainTextClick(object sender, RoutedEventArgs e) =>
        await EditTextAsync(isAdditionalText: false);

    private async void OnEditAdditionalTextClick(object sender, RoutedEventArgs e) =>
        await EditTextAsync(isAdditionalText: true);

    private async Task EditTextAsync(bool isAdditionalText)
    {
        if (_workspace is null) return;
        var editor = new InvoicingFormattedTextEditorWindow(
            isAdditionalText ? _additionalText : _mainText,
            _workspace.TextTemplates,
            isAdditionalText ? "Zusatztext bearbeiten" : "Haupttext bearbeiten",
            isAdditionalText
                ? "Der Zusatztext erscheint direkt unter der Artikelposition."
                : "Der Haupttext bleibt als eigenes Klartext-/Formatpaar an dieser Position.");
        if (!await editor.ShowAsync() || editor.Snapshot is null) return;
        if (isAdditionalText)
            _additionalText = editor.Snapshot;
        else
            _mainText = editor.Snapshot;
        TemplateBox.SelectedItem = null;
        RenderTextSnapshots();
        StatusText.Text = isAdditionalText
            ? "Zusatztext übernommen · Position noch übernehmen."
            : "Haupttext übernommen · Position noch übernehmen.";
    }

    private void OnClearAdditionalTextClick(object sender, RoutedEventArgs e)
    {
        _additionalText = new InvoicingFormattedTextSnapshot(string.Empty, null);
        RenderTextSnapshots();
        StatusText.Text = "Zusatztext entfernt · Position noch übernehmen.";
    }

    private void RenderTextSnapshots()
    {
        MainTextPreview.Text = _mainText.PlainText;
        AdditionalTextPreview.Text = _additionalText.PlainText;
    }

    private void OnEditClick(object sender, RoutedEventArgs e) => EditSelectedPosition();

    private void OnEditShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        EditSelectedPosition();
    }

    private void EditSelectedPosition()
    {
        if (SelectedPosition is not { } position) return;
        _draft = position.ToDraft();
        _mainText = new InvoicingFormattedTextSnapshot(
            position.MainTextPlain,
            position.MainTextFormatted);
        _additionalText = new InvoicingFormattedTextSnapshot(
            position.AdditionalTextPlain,
            position.AdditionalTextFormatted);
        _suppressPositionTypeChange = true;
        ArticlePositionRadio.IsChecked = !position.IsTextPosition;
        TextPositionRadio.IsChecked = position.IsTextPosition;
        _suppressPositionTypeChange = false;
        EntryTitle.Text = $"Position {position.SequenceNumber} bearbeiten";
        AcceptButton.Content = "Position aktualisieren";
        TemplateBox.SelectedItem = null;
        _articleTextIsProgrammatic = true;
        ArticleSearchBox.Text = position.ArticleId.HasValue
            ? _workspace?.Articles.FirstOrDefault(article => article.Id == position.ArticleId.Value)
                is { } article
                    ? $"{article.ArticleNumber} · {article.Designation}"
                    : $"Artikel-ID {position.ArticleId.Value}"
            : string.Empty;
        _articleTextIsProgrammatic = false;
        DesignationBox.Text = position.Designation;
        CategoryBox.Text = position.Category;
        UnitBox.Text = position.Unit;
        QuantityBox.Value = decimal.ToDouble(position.Quantity);
        UnitPriceBox.Value = decimal.ToDouble(position.UnitPrice);
        VatBox.SelectedValue = position.VatRateId;
        RevenueAccountBox.SelectedValue = position.RevenueAccountId;
        FooterToggle.IsOn = position.IsFooter;
        ClassificationText.Text =
            $"Nebenkostenklassifikation: {InvoicingAncillaryClassifications.DisplayName(position.AncillaryClassificationSnapshot)}";
        RenderTextSnapshots();
        ApplyPositionTypeLayout();
        if (position.IsTextPosition)
            DesignationBox.Focus(FocusState.Programmatic);
        else
            ArticleSearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnAcceptClick(object sender, RoutedEventArgs e) => await AcceptAsync();

    private async void OnAcceptShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        if (_saving || _workspace is null) return;
        _saving = true;
        AcceptButton.IsEnabled = false;
        ErrorInfoBar.IsOpen = false;
        try
        {
            _draft.ContextSource = _context.SourceCode;
            _draft.ContextSourceId = _context.SourceId;
            _draft.PositionType = TextPositionRadio.IsChecked == true
                ? InvoicingPositionTypes.Text
                : InvoicingPositionTypes.Article;
            _draft.Designation = DesignationBox.Text;
            _draft.Category = CategoryBox.Text;
            _draft.Unit = UnitBox.Text;
            _draft.Quantity = ToDecimal(QuantityBox.Value, invalidValue: -1m);
            _draft.UnitPrice = ToDecimal(UnitPriceBox.Value, invalidValue: -1m);
            _draft.VatRateId = VatBox.SelectedValue is int vatId ? vatId : null;
            if (VatBox.SelectedItem is InvoicingVatOption vat)
            {
                _draft.VatCodeSnapshot = vat.Code;
                _draft.VatRatePercentSnapshot = vat.RatePercent;
            }
            else
            {
                _draft.VatCodeSnapshot = string.Empty;
                _draft.VatRatePercentSnapshot = null;
            }
            _draft.RevenueAccountId =
                RevenueAccountBox.SelectedValue is int revenueId ? revenueId : null;
            _draft.RevenueAccountSnapshot =
                (RevenueAccountBox.SelectedItem as InvoicingRevenueAccountOption)?.Display ?? string.Empty;
            _draft.MainTextPlain = _mainText.PlainText;
            _draft.MainTextFormatted = _mainText.FormattedText;
            _draft.AdditionalTextPlain = _additionalText.PlainText;
            _draft.AdditionalTextFormatted = _additionalText.FormattedText;
            _draft.IsFooter = FooterToggle.IsOn;

            var id = await _repository.SavePositionAsync(_draft);
            Changed = true;
            await LoadWorkspaceAsync(id, beginNew: true);
            StatusText.Text = "Position gespeichert · nächste Position kann sofort erfasst werden.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            StatusText.Text = "Position wurde nicht gespeichert.";
        }
        finally
        {
            _saving = false;
            AcceptButton.IsEnabled = _workspace is not null;
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e) => await DeleteSelectedAsync();

    private async void OnDeleteShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await DeleteSelectedAsync();
    }

    private async Task DeleteSelectedAsync()
    {
        if (_saving || SelectedPosition is not { } position) return;
        if (!await ConfirmAsync(
                "Position entfernen?",
                $"Position {position.SequenceNumber} «{position.Designation}» wird aus diesem Entwurf entfernt."))
            return;

        _saving = true;
        ErrorInfoBar.IsOpen = false;
        try
        {
            await _repository.DeletePositionAsync(
                _context.SourceCode,
                _context.SourceId,
                position.Id);
            Changed = true;
            await LoadWorkspaceAsync(beginNew: true);
            StatusText.Text = "Position entfernt und Reihenfolge neu nummeriert.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            _saving = false;
        }
    }

    private async void OnMoveUpClick(object sender, RoutedEventArgs e) => await MoveSelectedAsync(-1);

    private async void OnMoveDownClick(object sender, RoutedEventArgs e) => await MoveSelectedAsync(1);

    private async void OnMoveUpShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await MoveSelectedAsync(-1);
    }

    private async void OnMoveDownShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await MoveSelectedAsync(1);
    }

    private async Task MoveSelectedAsync(int direction)
    {
        if (_saving || SelectedPosition is not { } position) return;
        _saving = true;
        ErrorInfoBar.IsOpen = false;
        try
        {
            await _repository.MovePositionAsync(
                _context.SourceCode,
                _context.SourceId,
                position.Id,
                direction);
            Changed = true;
            await LoadWorkspaceAsync(position.Id);
            StatusText.Text = direction < 0
                ? "Position nach oben verschoben."
                : "Position nach unten verschoben.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            _saving = false;
        }
    }

    private void OnSearchShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ArticlePositionRadio.IsChecked = true;
        ArticleSearchBox.Focus(FocusState.Programmatic);
    }

    private void OnManageTemplatesClick(object sender, RoutedEventArgs e)
    {
        if (_templateWindow is not null)
        {
            _templateWindow.Activate();
            return;
        }
        _templateWindow = new InvoicingTextTemplateManagerWindow(_repository);
        _templateWindow.Closed += OnTemplateWindowClosed;
        _templateWindow.Activate();
    }

    private async void OnTemplateWindowClosed(object sender, WindowEventArgs args)
    {
        var changed = _templateWindow?.Changed == true;
        if (_templateWindow is not null)
            _templateWindow.Closed -= OnTemplateWindowClosed;
        _templateWindow = null;
        if (!changed) return;
        try
        {
            var templates = await _repository.LoadTextTemplatesAsync();
            TemplateBox.ItemsSource = templates;
            if (_workspace is not null)
                _workspace = _workspace with { TextTemplates = templates };
            StatusText.Text = "Textbausteine aktualisiert.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = "Entfernen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static decimal ToDecimal(double value, decimal invalidValue) =>
        double.IsFinite(value) && value <= decimal.ToDouble(decimal.MaxValue)
            ? Convert.ToDecimal(value)
            : invalidValue;

    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnCloseShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        RootGrid.SizeChanged -= OnRootSizeChanged;
        Activated -= OnActivated;
        Closed -= OnWindowClosed;
        if (_templateWindow is not null)
        {
            _templateWindow.Closed -= OnTemplateWindowClosed;
            _templateWindow.Close();
            _templateWindow = null;
        }
    }
}
