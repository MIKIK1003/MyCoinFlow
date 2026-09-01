using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.ViewModels;
using MyCoinFlow.WinUI.Models;
using System.Globalization;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class BudgetDetailWindow : PersistentWindow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private readonly BudgetDetailViewModel _viewModel;
    private readonly List<BudgetAccountDisplayRow> _rows;

    public BudgetDetailWindow(int periodId, string periodName)
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1320, 820));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 980;
            presenter.PreferredMinimumHeight = 620;
        }

        _viewModel = new BudgetDetailViewModel(periodId);
        _rows = _viewModel.Zeilen.Select(row => new BudgetAccountDisplayRow(row)).ToList();
        AccountsList.ItemsSource = _rows;
        Title = $"Budget erfassen – {periodName}";
        TitleText.Text = Title;
        ResultText.Text = $"{_rows.Count:N0} budgetierbare Konten";
        Closed += OnClosed;
    }

    private void OnBudgetLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BudgetAccountDisplayRow displayRow) return;
        try
        {
            var value = ParseNullableAmount(displayRow.BudgetText);
            displayRow.Row.Budgetwert = value;
            _viewModel.SaveOne(displayRow.Row);
            displayRow.ResetText();
            if (sender is TextBox box)
                box.Text = displayRow.BudgetText;
        }
        catch (Exception exception)
        {
            displayRow.ResetText();
            if (sender is TextBox box)
                box.Text = displayRow.BudgetText;
            ShowStatus("Ungültiger Budgetwert: " + exception.Message, InfoBarSeverity.Warning);
        }
    }

    private static decimal? ParseNullableAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, SwissCulture, out var swiss))
            return swiss;
        if (decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.CurrentCulture, out var current))
            return current;
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant))
            return invariant;
        throw new FormatException("Bitte eine gültige Zahl eingeben.");
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        try
        {
            _viewModel.SaveAll();
        }
        catch
        {
            // Das WPF-Fenster speichert beim Schließen ebenfalls ohne weiteren UI-Dialog.
        }
        finally
        {
            Closed -= OnClosed;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
