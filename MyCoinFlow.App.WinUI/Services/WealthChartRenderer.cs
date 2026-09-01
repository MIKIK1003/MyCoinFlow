using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Globalization;
using Windows.Foundation;

namespace MyCoinFlow.WinUI.Services;

internal sealed record WealthChartPoint(DateTime Date, double Value, string ToolTipText);

internal sealed record WealthChartSlice(string Label, double Value);

internal static class WealthChartRenderer
{
    private static readonly SolidColorBrush LineBrush = Brush(128, 96, 201);
    private static readonly SolidColorBrush GridBrush = Brush(211, 212, 219);
    private static readonly SolidColorBrush TextBrush = Brush(75, 75, 84);
    private static readonly SolidColorBrush[] SliceBrushes =
    [
        Brush(128, 96, 201),
        Brush(77, 155, 193),
        Brush(105, 174, 120),
        Brush(222, 160, 74),
        Brush(211, 104, 124),
        Brush(153, 112, 185),
        Brush(60, 154, 145),
        Brush(187, 129, 71)
    ];

    public static void RenderLine(
        Canvas canvas,
        IReadOnlyList<WealthChartPoint> points,
        Func<double, string> valueFormatter)
    {
        canvas.Children.Clear();

        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        if (width < 180 || height < 100)
            return;

        if (points.Count == 0)
        {
            AddEmptyText(canvas, width, height, "Keine Verlaufsdaten");
            return;
        }

        const double left = 72;
        const double right = 18;
        const double top = 12;
        const double bottom = 34;
        var plotWidth = Math.Max(1, width - left - right);
        var plotHeight = Math.Max(1, height - top - bottom);

        var minimum = points.Min(point => point.Value);
        var maximum = points.Max(point => point.Value);
        var range = maximum - minimum;
        if (Math.Abs(range) < 0.0000001)
        {
            var padding = Math.Max(Math.Abs(maximum) * 0.05, 1d);
            minimum -= padding;
            maximum += padding;
        }
        else
        {
            var padding = range * 0.08;
            minimum -= padding;
            maximum += padding;
        }

        range = maximum - minimum;
        const int yTicks = 4;
        for (var index = 0; index <= yTicks; index++)
        {
            var y = top + plotHeight * index / yTicks;
            canvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = GridBrush,
                StrokeThickness = 1
            });

            var value = maximum - range * index / yTicks;
            var label = new TextBlock
            {
                Text = valueFormatter(value),
                Width = left - 9,
                FontSize = 11,
                Foreground = TextBrush,
                TextAlignment = TextAlignment.Right
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 8);
            canvas.Children.Add(label);
        }

        var linePoints = new PointCollection();
        for (var index = 0; index < points.Count; index++)
        {
            var x = points.Count == 1
                ? left + plotWidth / 2
                : left + plotWidth * index / (points.Count - 1d);
            var y = top + (maximum - points[index].Value) / range * plotHeight;
            linePoints.Add(new Point(x, y));
        }

        canvas.Children.Add(new Polyline
        {
            Points = linePoints,
            Stroke = LineBrush,
            StrokeThickness = 3,
            StrokeLineJoin = PenLineJoin.Round
        });

        if (points.Count <= 60)
        {
            for (var index = 0; index < points.Count; index++)
            {
                var point = linePoints[index];
                var marker = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = LineBrush,
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 1.5
                };
                ToolTipService.SetToolTip(marker, points[index].ToolTipText);
                Canvas.SetLeft(marker, point.X - 4);
                Canvas.SetTop(marker, point.Y - 4);
                canvas.Children.Add(marker);
            }
        }

        var xLabelCount = Math.Min(5, points.Count);
        var usedIndexes = new HashSet<int>();
        for (var labelIndex = 0; labelIndex < xLabelCount; labelIndex++)
        {
            var pointIndex = xLabelCount == 1
                ? 0
                : (int)Math.Round(labelIndex * (points.Count - 1d) / (xLabelCount - 1d));
            if (!usedIndexes.Add(pointIndex))
                continue;

            var x = points.Count == 1
                ? left + plotWidth / 2
                : left + plotWidth * pointIndex / (points.Count - 1d);
            var label = new TextBlock
            {
                Text = points[pointIndex].Date.ToString("dd.MM.yy", CultureInfo.GetCultureInfo("de-CH")),
                Width = 70,
                FontSize = 11,
                Foreground = TextBrush,
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(label, Math.Clamp(x - 35, left - 20, width - 70));
            Canvas.SetTop(label, top + plotHeight + 8);
            canvas.Children.Add(label);
        }
    }

    public static void RenderDonut(
        Canvas canvas,
        Panel legend,
        IReadOnlyList<WealthChartSlice> slices)
    {
        canvas.Children.Clear();
        legend.Children.Clear();

        var validSlices = slices.Where(slice => slice.Value > 0).ToArray();
        var total = validSlices.Sum(slice => slice.Value);
        if (total <= 0)
        {
            AddEmptyText(canvas, canvas.Width, canvas.Height, "Keine Daten");
            return;
        }

        var size = Math.Min(canvas.Width, canvas.Height);
        var center = size / 2d;
        var radius = size / 2d - 5;
        var startAngle = -90d;

        for (var index = 0; index < validSlices.Length; index++)
        {
            var slice = validSlices[index];
            var brush = SliceBrushes[index % SliceBrushes.Length];
            var sweepAngle = slice.Value / total * 360d;

            if (sweepAngle >= 359.999d)
            {
                var circle = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = brush };
                Canvas.SetLeft(circle, center - radius);
                Canvas.SetTop(circle, center - radius);
                canvas.Children.Add(circle);
            }
            else
            {
                var startPoint = PointOnCircle(center, radius, startAngle);
                var endPoint = PointOnCircle(center, radius, startAngle + sweepAngle);
                var figure = new PathFigure { StartPoint = new Point(center, center), IsClosed = true };
                figure.Segments.Add(new LineSegment { Point = startPoint });
                figure.Segments.Add(new ArcSegment
                {
                    Point = endPoint,
                    Size = new Size(radius, radius),
                    IsLargeArc = sweepAngle > 180d,
                    SweepDirection = SweepDirection.Clockwise
                });
                figure.Segments.Add(new LineSegment { Point = new Point(center, center) });
                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);
                canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Fill = brush });
            }

            legend.Children.Add(CreateLegendRow(brush, slice.Label));
            startAngle += sweepAngle;
        }

        var innerRadius = radius * 0.50;
        var centerCircle = new Ellipse
        {
            Width = innerRadius * 2,
            Height = innerRadius * 2,
            Fill = canvas.Background ?? new SolidColorBrush(Colors.White)
        };
        Canvas.SetLeft(centerCircle, center - innerRadius);
        Canvas.SetTop(centerCircle, center - innerRadius);
        canvas.Children.Add(centerCircle);
    }

    private static FrameworkElement CreateLegendRow(Brush brush, string text)
    {
        var panel = new Grid { ColumnSpacing = 8 };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.Children.Add(new Border
        {
            Width = 11,
            Height = 11,
            CornerRadius = new CornerRadius(3),
            Background = brush,
            VerticalAlignment = VerticalAlignment.Center
        });
        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = TextBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);
        panel.Children.Add(label);
        return panel;
    }

    private static Point PointOnCircle(double center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180d;
        return new Point(center + radius * Math.Cos(radians), center + radius * Math.Sin(radians));
    }

    private static void AddEmptyText(Canvas canvas, double width, double height, string text)
    {
        var label = new TextBlock
        {
            Text = text,
            Width = Math.Max(120, width),
            Foreground = TextBrush,
            TextAlignment = TextAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };
        Canvas.SetLeft(label, 0);
        Canvas.SetTop(label, Math.Max(0, height / 2d - 10));
        canvas.Children.Add(label);
    }

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(ColorHelper.FromArgb(255, red, green, blue));
}
