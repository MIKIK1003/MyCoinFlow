using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InvoicingArticleEditorWindow : PersistentWindow
{
    private readonly InvoicingMasterDataRepository _repository;
    private readonly InvoicingArticleDraft _draft;
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _completed;
    private bool _focused;
    private bool _saving;

    public InvoicingArticleEditorWindow(
        InvoicingMasterDataSnapshot snapshot,
        InvoicingArticleDraft? draft = null,
        InvoicingMasterDataRepository? repository = null)
    {
        InitializeComponent();
        _repository = repository ?? new InvoicingMasterDataRepository();
        _draft = draft ?? new InvoicingArticleDraft();

        Title = _draft.Id == 0 ? "Artikel / Leistung erfassen" : "Artikel / Leistung bearbeiten";
        HeadingText.Text = _draft.Id == 0
            ? "Neuen Artikel oder neue Leistung erfassen"
            : $"{_draft.ArticleNumber} bearbeiten";
        AppWindow.Resize(new SizeInt32(1180, 800));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.PreferredMinimumWidth = 760;
            presenter.PreferredMinimumHeight = 600;
        }

        VatBox.ItemsSource = snapshot.VatOptions;
        RevenueAccountBox.ItemsSource = snapshot.RevenueAccountOptions;
        ClassificationBox.ItemsSource = InvoicingAncillaryClassifications.Options;

        ArticleNumberBox.Text = _draft.ArticleNumber;
        DesignationBox.Text = _draft.Designation;
        DescriptionBox.Text = _draft.Description;
        UnitBox.Text = _draft.Unit;
        CategoryBox.Text = _draft.Category;
        PriceBox.Value = decimal.ToDouble(_draft.SalePrice);
        VatBox.SelectedValue = _draft.VatRateId > 0
            ? _draft.VatRateId
            : snapshot.VatOptions.FirstOrDefault()?.Id;
        RevenueAccountBox.SelectedValue = _draft.RevenueAccountId > 0
            ? _draft.RevenueAccountId
            : snapshot.RevenueAccountOptions.FirstOrDefault()?.Id;
        ClassificationBox.SelectedValue = _draft.AncillaryClassification;
        ActiveToggle.IsOn = _draft.IsActive;

        RootGrid.SizeChanged += OnRootSizeChanged;
        Activated += OnActivated;
        Closed += OnWindowClosed;
    }

    public bool Saved { get; private set; }
    public int SavedArticleId { get; private set; }

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
        ArticleNumberBox.Focus(FocusState.Programmatic);
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var wide = width >= 900;
        EditorContentGrid.ColumnDefinitions[0].Width =
            wide ? new GridLength(1.2, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        EditorContentGrid.ColumnDefinitions[1].Width =
            wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        Grid.SetRow(BaseDataCard, 0);
        Grid.SetColumn(BaseDataCard, 0);
        Grid.SetColumnSpan(BaseDataCard, wide ? 1 : 2);
        Grid.SetRowSpan(BaseDataCard, wide ? 2 : 1);

        Grid.SetRow(AccountingCard, wide ? 0 : 1);
        Grid.SetColumn(AccountingCard, wide ? 1 : 0);
        Grid.SetColumnSpan(AccountingCard, wide ? 1 : 2);

        Grid.SetRow(StatusCard, wide ? 1 : 2);
        Grid.SetColumn(StatusCard, wide ? 1 : 0);
        Grid.SetColumnSpan(StatusCard, wide ? 1 : 2);
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
            _draft.ArticleNumber = ArticleNumberBox.Text;
            _draft.Designation = DesignationBox.Text;
            _draft.Description = DescriptionBox.Text;
            _draft.Unit = UnitBox.Text;
            _draft.Category = CategoryBox.Text;
            _draft.SalePrice = double.IsFinite(PriceBox.Value) && PriceBox.Value >= 0
                ? Convert.ToDecimal(PriceBox.Value)
                : -1m;
            _draft.VatRateId = VatBox.SelectedValue is int vatId ? vatId : 0;
            _draft.RevenueAccountId = RevenueAccountBox.SelectedValue is int accountId ? accountId : 0;
            _draft.AncillaryClassification =
                ClassificationBox.SelectedValue as string ?? string.Empty;
            _draft.IsActive = ActiveToggle.IsOn;

            SavedArticleId = await _repository.SaveArticleAsync(_draft);
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
