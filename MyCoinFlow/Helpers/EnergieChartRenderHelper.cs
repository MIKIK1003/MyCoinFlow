using MyCoinFlow.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyCoinFlow.Helpers
{
    public static class EnergiePrintChartRenderer
    {
        public static BitmapSource RenderKwhChart(IList<StweEnergieChartPoint> data, int width, int height)
        {
            var dv = new DrawingVisual();
            using var dc = dv.RenderOpen();

            // Background
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

            var marginL = 55;
            var marginR = 20;
            var marginT = 18;
            var marginB = 45;

            var plotW = width - marginL - marginR;
            var plotH = height - marginT - marginB;

            if (data == null || data.Count == 0)
            {
                DrawCentered(dc, "Keine Daten", width, height);
                return ToBitmap(dv, width, height);
            }

            // Max kWh
            var maxVal = (double)Math.Max(
                1m,
                data.Max(x => Math.Max(x.RechnungKwh, Math.Max(x.InterneKwh, x.SolarDirektKwh))));

            // Axes
            var axisPen = new Pen(Brushes.Gray, 1);
            dc.DrawLine(axisPen, new Point(marginL, marginT), new Point(marginL, marginT + plotH));
            dc.DrawLine(axisPen, new Point(marginL, marginT + plotH), new Point(marginL + plotW, marginT + plotH));

            // Y ticks (4)
            var ticks = 4;
            var ft = CultureInfo.GetCultureInfo("de-CH");
            for (int i = 0; i <= ticks; i++)
            {
                var y = marginT + plotH - (plotH * i / (double)ticks);
                dc.DrawLine(new Pen(Brushes.LightGray, 1), new Point(marginL, y), new Point(marginL + plotW, y));

                var val = maxVal * i / ticks;
                DrawText(dc, $"{val:0}", 10, marginL - 50, y - 8, Brushes.DimGray);
            }

            // Bars grouped
            var n = data.Count;
            var groupW = plotW / (double)n;
            var barW = Math.Max(6, groupW * 0.18); // 3 bars

            // Colors (simple, print-safe)
            var bRechnung = new SolidColorBrush(Color.FromRgb(60, 120, 200));
            var bIntern = new SolidColorBrush(Color.FromRgb(80, 160, 80));
            var bSolar = new SolidColorBrush(Color.FromRgb(240, 180, 60));
            bRechnung.Freeze(); bIntern.Freeze(); bSolar.Freeze();

            for (int i = 0; i < n; i++)
            {
                var x0 = marginL + i * groupW;

                double v1 = (double)data[i].RechnungKwh;
                double v2 = (double)data[i].InterneKwh;
                double v3 = (double)data[i].SolarDirektKwh;

                DrawBar(dc, x0 + groupW * 0.20, v1, maxVal, marginT, plotH, barW, bRechnung);
                DrawBar(dc, x0 + groupW * 0.45, v2, maxVal, marginT, plotH, barW, bIntern);
                DrawBar(dc, x0 + groupW * 0.70, v3, maxVal, marginT, plotH, barW, bSolar);

                // X label
                DrawText(dc, data[i].Label, 10, x0 + groupW * 0.15, marginT + plotH + 8, Brushes.Black);
            }

            // Legend
            var lx = marginL;
            var ly = 2;
            DrawLegend(dc, lx, ly, bRechnung, "Rechnung kWh");
            DrawLegend(dc, lx + 170, ly, bIntern, "Interne kWh");
            DrawLegend(dc, lx + 330, ly, bSolar, "Solar direkt kWh");

            // Title
            DrawText(dc, "Energie (kWh)", 12, marginL, 0, Brushes.Black, bold: true);

            return ToBitmap(dv, width, height);
        }

        public static BitmapSource RenderSolarPctChart(IList<StweEnergieChartPoint> data, int width, int height)
        {
            var dv = new DrawingVisual();
            using var dc = dv.RenderOpen();

            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

            var marginL = 55;
            var marginR = 20;
            var marginT = 18;
            var marginB = 35;

            var plotW = width - marginL - marginR;
            var plotH = height - marginT - marginB;

            if (data == null || data.Count == 0)
            {
                DrawCentered(dc, "Keine Daten", width, height);
                return ToBitmap(dv, width, height);
            }

            var axisPen = new Pen(Brushes.Gray, 1);
            dc.DrawLine(axisPen, new Point(marginL, marginT), new Point(marginL, marginT + plotH));
            dc.DrawLine(axisPen, new Point(marginL, marginT + plotH), new Point(marginL + plotW, marginT + plotH));

            // Y ticks 0..100
            for (int i = 0; i <= 4; i++)
            {
                var pct = i * 25;
                var y = marginT + plotH - plotH * pct / 100.0;
                dc.DrawLine(new Pen(Brushes.LightGray, 1), new Point(marginL, y), new Point(marginL + plotW, y));
                DrawText(dc, $"{pct}%", 10, marginL - 50, y - 8, Brushes.DimGray);
            }

            // Line
            var n = data.Count;
            if (n == 1) n = 2; // avoid div by zero
            var step = plotW / (double)(data.Count - 1 <= 0 ? 1 : data.Count - 1);

            var linePen = new Pen(new SolidColorBrush(Color.FromRgb(220, 80, 80)), 2);
            linePen.Freeze();

            Point? prev = null;
            for (int i = 0; i < data.Count; i++)
            {
                var pct = (double)data[i].SolarAnteilProzent;
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;

                var x = marginL + i * step;
                var y = marginT + plotH - plotH * pct / 100.0;

                var p = new Point(x, y);
                if (prev.HasValue) dc.DrawLine(linePen, prev.Value, p);
                dc.DrawEllipse(linePen.Brush, null, p, 3, 3);
                prev = p;

                DrawText(dc, data[i].Label, 10, x - 18, marginT + plotH + 6, Brushes.Black);
            }

            DrawText(dc, "Solar-Anteil (%)", 12, marginL, 0, Brushes.Black, bold: true);

            return ToBitmap(dv, width, height);
        }

        // ---------- Helpers ----------

        private static void DrawBar(DrawingContext dc, double x, double v, double max, double top, double height, double w, Brush brush)
        {
            if (v < 0) v = 0;
            var h = height * (v / max);
            dc.DrawRectangle(brush, null, new Rect(x, top + (height - h), w, h));
        }

        private static void DrawLegend(DrawingContext dc, double x, double y, Brush brush, string text)
        {
            dc.DrawRectangle(brush, null, new Rect(x, y + 4, 10, 10));
            DrawText(dc, text, 10, x + 14, y, Brushes.Black);
        }

        private static void DrawCentered(DrawingContext dc, string text, int width, int height)
        {
            DrawText(dc, text, 12, width / 2.0 - 40, height / 2.0 - 8, Brushes.DimGray);
        }

        private static void DrawText(DrawingContext dc, string text, double fontSize, double x, double y, Brush brush, bool bold = false)
        {
            var typeface = new Typeface(
                new FontFamily("Segoe UI"),
                FontStyles.Normal,
                bold ? FontWeights.SemiBold : FontWeights.Normal,
                FontStretches.Normal);

            var ft = new FormattedText(
                text ?? "",
                CultureInfo.GetCultureInfo("de-CH"),
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                brush,
                1.0);

            dc.DrawText(ft, new Point(x, y));
        }

        private static BitmapSource ToBitmap(DrawingVisual dv, int width, int height)
        {
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }
}
