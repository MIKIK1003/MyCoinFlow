using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MyCoinFlow.Import; // KreditDebit

namespace MyCoinFlow.Converters
{
    public sealed class IstVollstaendigConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Erwartet: [0]=Direction, [1]=AccountIban, [2]=VorschlagGeldinstitutId, [3]=VorschlagAdresseId, [4]=VorschlagNachKontoId
            if (values == null || values.Length < 5) return false;

            var direction = ToKreditDebit(values[0]);
            string? accountIban = (values[1] == DependencyProperty.UnsetValue) ? null : values[1] as string;

            int? giId = ToNullableInt(values[2]);
            int? adresseId = ToNullableInt(values[3]);
            int? nachKontoId = ToNullableInt(values[4]);

            bool hatOderKannBank = !string.IsNullOrWhiteSpace(accountIban) || giId.HasValue;

            if (direction == KreditDebit.Debit)      // Bank -> Konto
                return hatOderKannBank && adresseId.HasValue && nachKontoId.HasValue;

            // Adresse -> Bank
            return hatOderKannBank && adresseId.HasValue;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static KreditDebit ToKreditDebit(object? v)
        {
            if (v == null || v == DependencyProperty.UnsetValue) return KreditDebit.Credit;
            if (v is KreditDebit kd) return kd;

            if (v is string s)
            {
                s = s.Trim().ToUpperInvariant();
                if (s.StartsWith("DEB") || s is "SOLL") return KreditDebit.Debit;
                return KreditDebit.Credit;
            }

            if (v is int i)
            {
                try { return (KreditDebit)Enum.ToObject(typeof(KreditDebit), i); }
                catch { return KreditDebit.Credit; }
            }

            return KreditDebit.Credit;
        }

        private static int? ToNullableInt(object? v)
        {
            if (v == null || v == DependencyProperty.UnsetValue) return null;
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (int.TryParse(v.ToString(), out var ii)) return ii;
            return null;
        }
    }
}
