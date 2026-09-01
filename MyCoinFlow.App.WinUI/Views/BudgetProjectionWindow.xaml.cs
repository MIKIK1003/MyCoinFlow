using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using MyCoinFlow.ViewModels;
using System.Globalization;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class BudgetProjectionWindow : PersistentWindow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private readonly BudgetProjectionPreviewViewModel _viewModel;

    public BudgetProjectionWindow(BudgetProjectionPreviewViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1160, 760));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 950;
            presenter.PreferredMinimumHeight = 620;
        }

        PeriodInfoText.Text = _viewModel.ZeitraumInfo;
        QualityBar.Message = _viewModel.Datenqualitaet;
        RowsList.ItemsSource = _viewModel.Zeilen;
        RefreshSummary();
    }

    public event EventHandler? ApplyRequested;

    private void OnAllClick(object sender, RoutedEventArgs e)
    {
        _viewModel.AlleCommand.Execute(null);
        RefreshSummary();
    }

    private void OnNoneClick(object sender, RoutedEventArgs e)
    {
        _viewModel.KeineCommand.Execute(null);
        RefreshSummary();
    }

    private void OnUseProjectionClick(object sender, RoutedEventArgs e)
    {
        _viewModel.HochrechnungEinsetzenCommand.Execute(null);
        RefreshSummary();
    }

    private void OnRowChanged(object sender, RoutedEventArgs e) => RefreshSummary();
    private void OnNewValueLostFocus(object sender, RoutedEventArgs e) => RefreshSummary();

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        ApplyRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void RefreshSummary()
    {
        SummaryText.Text =
            $"{_viewModel.Ausgewaehlt} markiert · Alt {_viewModel.SummeAlt.ToString("N2", SwissCulture)} · " +
            $"Neu {_viewModel.SummeNeu.ToString("N2", SwissCulture)} · Δ {_viewModel.Differenz.ToString("N2", SwissCulture)}";
    }
}
