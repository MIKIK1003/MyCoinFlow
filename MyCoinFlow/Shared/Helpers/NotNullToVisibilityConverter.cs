using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MyCoinFlow.Helpers
{
    [ValueConversion(typeof(object), typeof(Visibility))]
    public sealed class NotNullToVisibilityConverter : IValueConverter
    {
        public bool Collapse { get; set; } = true;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null
                ? Visibility.Visible
                : (Collapse ? Visibility.Collapsed : Visibility.Hidden);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
