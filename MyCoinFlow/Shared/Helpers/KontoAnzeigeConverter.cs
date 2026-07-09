using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace MyCoinFlow.Helpers
{
    public class KontoAnzeigeConverter : IMultiValueConverter
    {
        // values[0] = KontoId (int?), values[1] = Dictionary<int,string> (KontoMap)
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return "";
            int? id = null;
            if (values[0] is int i) id = i;
            else if (values[0] is int?) id = (int?)values[0];

            var map = values[1] as IDictionary<int, string>;
            if (id.HasValue && map != null && map.TryGetValue(id.Value, out var text))
                return text;

            return "";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
