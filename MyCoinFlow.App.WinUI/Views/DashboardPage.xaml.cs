using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.ViewModels;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.WinUI.Services;
using System.Globalization;
using System.Printing;
using Windows.Foundation;
using WinUIMedia = Microsoft.UI.Xaml.Media;
using WinUISolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using WinUIShapes = Microsoft.UI.Xaml.Shapes;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class DashboardPage : Page
{
    private const double MaximumBarWidth = 250d;
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private readonly DashboardViewModel _viewModel = new();
    private readonly DashboardStweViewModel? _stweViewModel;
    private List<DashboardDistributionRow> _distributionRows = new();
    private bool _initialized;
    private bool _isStweActive;

    public DashboardPage()
    {
        InitializeComponent();
        GroupingBox.ItemsSource = _viewModel.GroupingOptions;
        GroupingBox.SelectedItem = _viewModel.SelectedGrouping;
        RangesList.ItemsSource = _viewModel.NumberRanges;
        PercentCheckBox.IsChecked = _viewModel.ShowPercent;
        if (AppModules.IsPropertyEnabled)
        {
            _stweViewModel = new DashboardStweViewModel();
            DashboardModeButton.Visibility = Visibility.Visible;
            SetDashboardMode(false);
            RefreshStwePresentation();
        }
        _initialized = true;
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        PeriodText.Text = _viewModel.ZeitraumLabel;
        CreditCardCountText.Text = _viewModel.OpenImportCount.ToString("N0", SwissCulture);
        BankCountText.Text = _viewModel.BankImportItemCount.ToString("N0", SwissCulture);
        ComparisonTitleText.Text = _viewModel.ColumnChartTitle;

        var labels = _viewModel.XAxes.FirstOrDefault()?.Labels?.ToArray() ?? Array.Empty<string>();
        var budget = _viewModel.ColumnSeries
            .OfType<ColumnSeries<double>>()
            .FirstOrDefault(series => string.Equals(series.Name, "Budget", StringComparison.OrdinalIgnoreCase))
            ?.Values?.ToArray() ?? Array.Empty<double>();
        var actual = _viewModel.ColumnSeries
            .OfType<ColumnSeries<double>>()
            .FirstOrDefault(series => string.Equals(series.Name, "IST", StringComparison.OrdinalIgnoreCase))
            ?.Values?.ToArray() ?? Array.Empty<double>();
        var comparisonMaximum = budget.Concat(actual).Select(Math.Abs).DefaultIfEmpty(0d).Max();
        ComparisonList.ItemsSource = labels.Select((label, index) => new DashboardComparisonRow
        {
            Label = label,
            Budget = ValueAt(budget, index),
            Actual = ValueAt(actual, index),
            BudgetBarWidth = ScaleBar(ValueAt(budget, index), comparisonMaximum, 110d),
            ActualBarWidth = ScaleBar(ValueAt(actual, index), comparisonMaximum, 110d)
        }).ToList();

        var pieValues = _viewModel.PieSeries
            .OfType<PieSeries<double>>()
            .Select(series => new
            {
                Label = TrimSeriesLabel(series.Name ?? string.Empty),
                Value = Math.Abs(series.Values?.FirstOrDefault() ?? 0d)
            })
            .ToList();
        var pieTotal = pieValues.Sum(item => item.Value);
        var palette = CreatePiePalette();
        _distributionRows = pieValues.Select((item, index) => new DashboardDistributionRow
        {
            Label = item.Label,
            Value = item.Value,
            Share = pieTotal > 0d ? item.Value / pieTotal : 0d,
            SliceBrush = palette[index % palette.Count],
            DisplayValueText = _viewModel.ShowPercent
                ? (pieTotal > 0d ? item.Value / pieTotal : 0d).ToString("P0", SwissCulture)
                : item.Value.ToString("N2", SwissCulture)
        }).ToList();
        DistributionList.ItemsSource = _distributionRows;
        RenderPieChart(_distributionRows);

        var deviationLabels = _viewModel.TopDevYAxes.FirstOrDefault()?.Labels?.ToArray() ?? Array.Empty<string>();
        var deviations = _viewModel.TopDevSeries.OfType<RowSeries<double>>().FirstOrDefault()?.Values?.ToArray()
                         ?? Array.Empty<double>();
        DeviationList.ItemsSource = CreateValueRows(deviationLabels, deviations);

        var bankLabels = _viewModel.BankXAxes.FirstOrDefault()?.Labels?.ToArray() ?? Array.Empty<string>();
        var bankValues = _viewModel.BankSeries.OfType<ColumnSeries<double>>().FirstOrDefault()?.Values?.ToArray()
                         ?? Array.Empty<double>();
        BanksList.ItemsSource = CreateValueRows(bankLabels, bankValues);
    }

    private static List<DashboardValueRow> CreateValueRows(string[] labels, double[] values, double maximumWidth = 110d)
    {
        var maximum = values.Select(Math.Abs).DefaultIfEmpty(0d).Max();
        return labels.Select((label, index) => new DashboardValueRow
        {
            Label = label,
            Value = ValueAt(values, index),
            BarWidth = ScaleBar(ValueAt(values, index), maximum, maximumWidth)
        }).ToList();
    }

    private static double ValueAt(double[] values, int index) =>
        index >= 0 && index < values.Length ? values[index] : 0d;

    private static double ScaleBar(double value, double maximum, double maximumWidth = MaximumBarWidth) =>
        maximum <= 0d || Math.Abs(value) <= 0.0001d
            ? 0d
            : Math.Max(2d, Math.Abs(value) / maximum * maximumWidth);

    private static string TrimSeriesLabel(string label)
    {
        var separator = label.IndexOf(" – ", StringComparison.Ordinal);
        return separator > 0 ? label[..separator] : label;
    }

    private static List<WinUISolidColorBrush> CreatePiePalette() =>
    [
        new(Microsoft.UI.ColorHelper.FromArgb(255, 164, 130, 216)),
        new(Microsoft.UI.ColorHelper.FromArgb(255, 95, 174, 208)),
        new(Microsoft.UI.ColorHelper.FromArgb(255, 121, 185, 103)),
        new(Microsoft.UI.ColorHelper.FromArgb(255, 235, 181, 88)),
        new(Microsoft.UI.ColorHelper.FromArgb(255, 223, 119, 135)),
        new(Microsoft.UI.ColorHelper.FromArgb(255, 184, 145, 210)),
        new(Microsoft.UI.ColorHelper.FromArgb(255, 75, 174, 163)),
        new(Microsoft.UI.ColorHelper.FromArgb(255, 203, 148, 78)),
        new(Microsoft.UI.ColorHelper.FromArgb(255, 92, 145, 196)),
        new(Microsoft.UI.ColorHelper.FromArgb(255, 145, 145, 154))
    ];

    private void RenderPieChart(IReadOnlyList<DashboardDistributionRow> rows)
    {
        DistributionChart.Children.Clear();
        const double size = 240d;
        const double center = size / 2d;
        const double radius = 108d;
        var total = rows.Sum(row => row.Value);
        if (total <= 0d)
        {
            var emptyCircle = new WinUIShapes.Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = new WinUISolidColorBrush(Microsoft.UI.Colors.Gray),
                StrokeThickness = 1,
                Opacity = 0.45
            };
            Canvas.SetLeft(emptyCircle, center - radius);
            Canvas.SetTop(emptyCircle, center - radius);
            DistributionChart.Children.Add(emptyCircle);
            var emptyText = new TextBlock { Text = "Keine Daten", Width = 120, TextAlignment = TextAlignment.Center, Opacity = 0.7 };
            Canvas.SetLeft(emptyText, center - 60);
            Canvas.SetTop(emptyText, center - 10);
            DistributionChart.Children.Add(emptyText);
            return;
        }

        var startAngle = -90d;
        foreach (var row in rows.Where(row => row.Value > 0d))
        {
            var sweepAngle = row.Value / total * 360d;
            if (sweepAngle >= 359.999d)
            {
                var circle = new WinUIShapes.Ellipse { Width = radius * 2, Height = radius * 2, Fill = row.SliceBrush };
                Canvas.SetLeft(circle, center - radius);
                Canvas.SetTop(circle, center - radius);
                DistributionChart.Children.Add(circle);
            }
            else
            {
                var startPoint = PointOnCircle(center, center, radius, startAngle);
                var endPoint = PointOnCircle(center, center, radius, startAngle + sweepAngle);
                var figure = new WinUIMedia.PathFigure { StartPoint = new Point(center, center), IsClosed = true };
                figure.Segments.Add(new WinUIMedia.LineSegment { Point = startPoint });
                figure.Segments.Add(new WinUIMedia.ArcSegment
                {
                    Point = endPoint,
                    Size = new Size(radius, radius),
                    IsLargeArc = sweepAngle > 180d,
                    SweepDirection = WinUIMedia.SweepDirection.Clockwise
                });
                figure.Segments.Add(new WinUIMedia.LineSegment { Point = new Point(center, center) });
                var geometry = new WinUIMedia.PathGeometry();
                geometry.Figures.Add(figure);
                DistributionChart.Children.Add(new WinUIShapes.Path { Data = geometry, Fill = row.SliceBrush });
            }

            if (row.Share >= 0.055d)
            {
                var labelPoint = PointOnCircle(center, center, radius * 0.63d, startAngle + sweepAngle / 2d);
                var label = new TextBlock
                {
                    Text = row.DisplayValueText,
                    Width = 82,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new WinUISolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 45, 45, 50))
                };
                Canvas.SetLeft(label, labelPoint.X - 41);
                Canvas.SetTop(label, labelPoint.Y - 9);
                DistributionChart.Children.Add(label);
            }
            startAngle += sweepAngle;
        }
    }

    private static Point PointOnCircle(double centerX, double centerY, double radius, double angle)
    {
        var radians = angle * Math.PI / 180d;
        return new Point(centerX + radius * Math.Cos(radians), centerY + radius * Math.Sin(radians));
    }

    private void RefreshStwePresentation()
    {
        if (_stweViewModel is null) return;
        StweHeadingText.Text = _stweViewModel.Header;
        StweStatusText.Text = _stweViewModel.StatusText;

        var quarters = _stweViewModel.EnergieKwhXAxes.FirstOrDefault()?.Labels?.ToArray()
                       ?? new[] { "Q1", "Q2", "Q3", "Q4" };
        var invoice = GetColumnValues(_stweViewModel.EnergieKwhSeries, "Rechnung kWh");
        var internalValues = GetColumnValues(_stweViewModel.EnergieKwhSeries, "Interne kWh");
        var solar = GetColumnValues(_stweViewModel.EnergieKwhSeries, "Solar direkt");
        var energyMaximum = invoice.Concat(internalValues).Concat(solar).Select(Math.Abs).DefaultIfEmpty(0d).Max();
        EnergyList.ItemsSource = quarters.Select((quarter, index) => new DashboardEnergyRow
        {
            Quarter = quarter,
            Invoice = ValueAt(invoice, index),
            Internal = ValueAt(internalValues, index),
            Solar = ValueAt(solar, index),
            InvoiceBarWidth = ScaleBar(ValueAt(invoice, index), energyMaximum),
            InternalBarWidth = ScaleBar(ValueAt(internalValues, index), energyMaximum),
            SolarBarWidth = ScaleBar(ValueAt(solar, index), energyMaximum)
        }).ToList();

        var solarLabels = _stweViewModel.SolarAnteilXAxes.FirstOrDefault()?.Labels?.ToArray() ?? quarters;
        var solarPercent = _stweViewModel.SolarAnteilSeries.OfType<LineSeries<double>>().FirstOrDefault()?.Values?.ToArray()
                           ?? Array.Empty<double>();
        SolarList.ItemsSource = solarLabels.Select((label, index) => new DashboardPercentRow
        {
            Label = label,
            Percent = Math.Clamp(ValueAt(solarPercent, index), 0d, 100d)
        }).ToList();

        OwnerKwhList.ItemsSource = CreateValueRows(
            _stweViewModel.KwhProOwnerXAxes.FirstOrDefault()?.Labels?.ToArray() ?? Array.Empty<string>(),
            _stweViewModel.KwhProOwnerSeries.OfType<ColumnSeries<double>>().FirstOrDefault()?.Values?.ToArray() ?? Array.Empty<double>(), 220d);
        OwnerChfList.ItemsSource = CreateValueRows(
            _stweViewModel.ChfProOwnerXAxes.FirstOrDefault()?.Labels?.ToArray() ?? Array.Empty<string>(),
            _stweViewModel.ChfProOwnerSeries.OfType<ColumnSeries<double>>().FirstOrDefault()?.Values?.ToArray() ?? Array.Empty<double>(), 220d);
    }

    private static double[] GetColumnValues(IEnumerable<ISeries> series, string name) =>
        series.OfType<ColumnSeries<double>>()
            .FirstOrDefault(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Values?.ToArray() ?? Array.Empty<double>();

    private void OnDashboardModeClick(object sender, RoutedEventArgs e) => SetDashboardMode(!_isStweActive);

    private void SetDashboardMode(bool showStwe)
    {
        if (showStwe && _stweViewModel is null) return;
        _isStweActive = showStwe;
        BudgetDashboardContent.Visibility = showStwe ? Visibility.Collapsed : Visibility.Visible;
        StweDashboardContent.Visibility = showStwe ? Visibility.Visible : Visibility.Collapsed;
        DashboardModeButton.Label = showStwe ? "Budget" : "STWE";
        DashboardModeButton.Icon = new FontIcon { Glyph = showStwe ? "\uE80F" : "\uE809" };
        GroupingBox.IsEnabled = !showStwe;
        if (showStwe) RefreshStwePresentation();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isStweActive && _stweViewModel is not null)
            {
                _stweViewModel.RefreshCommand.Execute(null);
                RefreshStwePresentation();
            }
            else
            {
                _viewModel.RefreshCommand.Execute(null);
                RefreshPresentation();
            }
        }
        catch (Exception exception)
        {
            ShowStatus("Dashboard konnte nicht aktualisiert werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnGroupingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || GroupingBox.SelectedItem is not DashboardViewModel.GroupingOption selected) return;
        try
        {
            _viewModel.SelectedGrouping = selected;
            RefreshPresentation();
        }
        catch (Exception exception)
        {
            ShowStatus("Gruppierung konnte nicht angewendet werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnPercentChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        try
        {
            _viewModel.ShowPercent = PercentCheckBox.IsChecked == true;
            RefreshPresentation();
        }
        catch (Exception exception)
        {
            ShowStatus("Darstellung konnte nicht aktualisiert werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnAllRangesClick(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectAllRangesCommand.Execute(null);
        RefreshPresentation();
    }

    private void OnNoRangesClick(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectNoneRangesCommand.Execute(null);
        RefreshPresentation();
    }

    private void OnApplyRangesClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ApplyFiltersCommand.Execute(null);
        RefreshPresentation();
    }

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new System.Windows.Controls.PrintDialog();
            if (dialog.ShowDialog() != true) return;
            dialog.PrintTicket ??= new PrintTicket();
            dialog.PrintTicket.PageOrientation = PageOrientation.Portrait;
            dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
            var paginator = DashboardPrintDocumentBuilder.Build(CreatePrintModel(), dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            dialog.PrintDocument(paginator, _isStweActive ? "MyCoinFlow Dashboard - STWE" : "MyCoinFlow Dashboard - Budget");
        }
        catch (Exception exception)
        {
            ShowStatus("Druck fehlgeschlagen: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private DashboardPrintModel CreatePrintModel()
    {
        var rangeSections = CreateRangePrintSections();
        return new DashboardPrintModel
        {
            IsStwe = _isStweActive && _stweViewModel is not null,
            Subtitle = _isStweActive && _stweViewModel is not null ? _stweViewModel.Header : _viewModel.ZeitraumLabel,
            Grouping = _viewModel.SelectedGrouping?.Label ?? string.Empty,
            StweStatus = _stweViewModel?.StatusText ?? string.Empty,
            CreditCardOpenCount = _viewModel.OpenImportCount,
            BankOpenCount = _viewModel.BankImportItemCount,
            Comparison = (ComparisonList.ItemsSource as IEnumerable<DashboardComparisonRow> ?? Array.Empty<DashboardComparisonRow>()).ToList(),
            Distribution = _distributionRows.ToList(),
            Deviations = (DeviationList.ItemsSource as IEnumerable<DashboardValueRow> ?? Array.Empty<DashboardValueRow>()).ToList(),
            Banks = (BanksList.ItemsSource as IEnumerable<DashboardValueRow> ?? Array.Empty<DashboardValueRow>()).ToList(),
            SelectedNumberRanges = rangeSections.Select(section => section.Title).ToList(),
            Energy = (EnergyList.ItemsSource as IEnumerable<DashboardEnergyRow> ?? Array.Empty<DashboardEnergyRow>()).ToList(),
            Solar = (SolarList.ItemsSource as IEnumerable<DashboardPercentRow> ?? Array.Empty<DashboardPercentRow>()).ToList(),
            OwnerKwh = (OwnerKwhList.ItemsSource as IEnumerable<DashboardValueRow> ?? Array.Empty<DashboardValueRow>()).ToList(),
            OwnerChf = (OwnerChfList.ItemsSource as IEnumerable<DashboardValueRow> ?? Array.Empty<DashboardValueRow>()).ToList(),
            RangeSections = rangeSections
        };
    }

    private List<DashboardRangePrintSection> CreateRangePrintSections()
    {
        var palette = CreatePiePalette();
        return _viewModel.NumberRanges
            .Select(range => new { Range = range, Order = GetPrintRangeOrder(range.Display) })
            .Where(value => value.Order >= 0)
            .OrderBy(value => value.Order)
            .Select(value =>
            {
                var groups = _viewModel.GetGroupedValuesForRange(value.Range.Start, value.Range.End);
                var comparisonMaximum = groups.SelectMany(group => new[] { Math.Abs((double)group.Budget), Math.Abs((double)group.Actual) }).DefaultIfEmpty(0d).Max();
                var comparison = groups.Select(group => new DashboardComparisonRow
                {
                    Label = group.Label,
                    Budget = (double)group.Budget,
                    Actual = (double)group.Actual,
                    BudgetBarWidth = ScaleBar((double)group.Budget, comparisonMaximum, 110d),
                    ActualBarWidth = ScaleBar((double)group.Actual, comparisonMaximum, 110d)
                }).ToList();
                var distributionValues = groups.Where(group => group.Actual != 0m).Select(group => new { group.Label, Value = Math.Abs((double)group.Actual) }).OrderByDescending(group => group.Value).ToList();
                var total = distributionValues.Sum(group => group.Value);
                var distribution = distributionValues.Select((group, index) => new DashboardDistributionRow
                {
                    Label = group.Label,
                    Value = group.Value,
                    Share = total > 0 ? group.Value / total : 0,
                    SliceBrush = palette[index % palette.Count],
                    DisplayValueText = total > 0 ? (group.Value / total).ToString("P0", SwissCulture) : "0 %"
                }).ToList();
                var deviations = groups.Select(group => new { group.Label, Value = (double)(group.Actual - group.Budget) }).OrderByDescending(group => Math.Abs(group.Value)).Take(8).ToList();
                var deviationMaximum = deviations.Select(group => Math.Abs(group.Value)).DefaultIfEmpty(0d).Max();
                return new DashboardRangePrintSection
                {
                    Title = value.Range.Display,
                    RangeStart = value.Range.Start,
                    RangeEnd = value.Range.End,
                    Comparison = comparison,
                    Distribution = distribution,
                    Deviations = deviations.Select(group => new DashboardValueRow { Label = group.Label, Value = group.Value, BarWidth = ScaleBar(group.Value, deviationMaximum, 110d) }).ToList()
                };
            })
            .ToList();
    }

    private static int GetPrintRangeOrder(string label)
    {
        var value = label.Trim().ToLowerInvariant();
        if (value.Contains("ausgab")) return 0;
        if (value.Contains("einnahm")) return 1;
        if (value.Contains("anschaff")) return 2;
        if (value.Contains("invest")) return 3;
        return -1;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
