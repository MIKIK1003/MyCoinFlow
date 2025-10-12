using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;
using MyCoinFlow.Models; // für NumberRangeRule

namespace MyCoinFlow.Views
{
    /// <summary>
    /// Entfernt alle Klammerzusätze aus der Bezeichnung,
    /// z. B. "Einnahmen (Budgetiert)" -> "Einnahmen".
    /// </summary>
    public sealed class RangeLabelConverter : IValueConverter
    {
        // entfernt " (…)" überall im Text
        private static readonly Regex Paren = new(@"\s*\(.*?\)", RegexOptions.Compiled);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // akzeptiert String ODER NumberRangeRule
            string input = value switch
            {
                string s => s,
                NumberRangeRule r => r.Bezeichnung ?? string.Empty,
                _ => value?.ToString() ?? string.Empty
            };

            var cleaned = Paren.Replace(input, string.Empty);
            return cleaned.Trim();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
