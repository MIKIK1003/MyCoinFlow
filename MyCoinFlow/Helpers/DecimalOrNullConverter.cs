using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MyCoinFlow.Helpers
{
    /// <summary>
    /// Konverter für decimal? <-> Text.
    /// Convert (Anzeige): null -> UnsetValue (leere Anzeige, ohne Binding-Error).
    /// ConvertBack (Eingabe): Leerstring -> null (bei decimal?), oder DoNothing (bei decimal).
    /// Ungültige Eingaben -> Binding.DoNothing.
    /// </summary>
    public class DecimalOrNullConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // WPF-idiomatisch: bei fehlendem Wert kein Target setzen
            return value ?? DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value as string)?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(s))
            {
                // Bei nicht-nullable decimal darf kein null zurückgehen -> Wert belassen
                if (targetType == typeof(decimal))
                    return Binding.DoNothing;

                // Bei nullable decimal (decimal?) explizit null schreiben
#pragma warning disable CS8603 // Possible null reference return.
                return null;
#pragma warning restore CS8603
            }

            // Dezimaltrennzeichen tolerant behandeln
            var decSep = culture.NumberFormat.NumberDecimalSeparator;
            if (decSep == "," && s.Contains(".")) s = s.Replace(".", ",");
            else if (decSep == "." && s.Contains(",")) s = s.Replace(",", ".");

            return decimal.TryParse(s, NumberStyles.Number, culture, out var d)
                ? d
                : Binding.DoNothing;
        }
    }
}
