using System;
using System.Globalization;
using System.Windows.Data;

namespace MyCoinFlow.Helpers
{
    public class DecimalOrNullConverter : IValueConverter
    {
        // Anzeige: nichts formatieren -> Binding/StringFormat übernimmt das
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value ?? null!; // null bleibt null (leere Zelle)
        }

        // Eingabe -> decimal?; ungültig -> Binding.DoNothing (Quelle unverändert)
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value as string)?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(s)) return null;

            var decSep = culture.NumberFormat.NumberDecimalSeparator;
            if (decSep == "," && s.Contains(".")) s = s.Replace(".", ",");
            else if (decSep == "." && s.Contains(",")) s = s.Replace(",", ".");

            return decimal.TryParse(s, NumberStyles.Number, culture, out var d)
                ? d
                : Binding.DoNothing;
        }
    }
}
