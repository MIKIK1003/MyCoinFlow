using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MyCoinFlow.Import; // KreditDebit

namespace MyCoinFlow.Converters
{
    public sealed class IstVollstaendigToVisibilityConverter : IMultiValueConverter
    {
        // Erwartet: [0]=Direction, [1]=AccountIban, [2]=VorschlagGeldinstitutId, [3]=VorschlagAdresseId, [4]=VorschlagNachKontoId
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 5) return Visibility.Collapsed;

            var direction = ToKreditDebit(values[0]);
            string? accountIban = GetString(values[1]);
            int? giId = ToNullableInt(values[2]);
            int? adresseId = ToNullableInt(values[3]);
            int? nachKontoId = ToNullableInt(values[4]);

            bool hatOderKannBank = !string.IsNullOrWhiteSpace(accountIban) || giId.HasValue;

            bool voll;
            if (direction == KreditDebit.Debit)      // Bank -> Konto
                voll = hatOderKannBank && adresseId.HasValue && nachKontoId.HasValue;
            else                                      // Adresse -> Bank
                voll = hatOderKannBank && adresseId.HasValue;

            return voll ? Visibility.Visible : Visibility.Collapsed;
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
                if (s.StartsWith("DEB") || s == "SOLL") return KreditDebit.Debit;
                return KreditDebit.Credit;
            }

            if (v is int i)
            {
                try { return (KreditDebit)Enum.ToObject(typeof(KreditDebit), i); } catch { }
            }
            return KreditDebit.Credit;
        }

        private static string? GetString(object? v)
        {
            if (v == null || v == DependencyProperty.UnsetValue) return null;
            return v as string ?? v.ToString();
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
