using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Linq;

namespace MyCoinFlow.Models
{
    public class KontoplanKnoten : INotifyPropertyChanged
    {
        private string _bezeichnung;

        public string Bezeichnung
        {
            get => _bezeichnung;
            set
            {
                if (_bezeichnung != value)
                {
                    _bezeichnung = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AnzeigeText));
                    OnPropertyChanged(nameof(Budgetwert));
                    OnPropertyChanged(nameof(Gebucht));
                    OnPropertyChanged(nameof(Differenz));
                }
            }
        }

        public ObservableCollection<KontoplanKnoten> Kinder { get; } = new();

        private KontoplanEintrag? _originalEintrag;
        public KontoplanEintrag? OriginalEintrag
        {
            get => _originalEintrag;
            set
            {
                if (_originalEintrag != value)
                {
                    _originalEintrag = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AnzeigeText));
                    OnPropertyChanged(nameof(Budgetwert));
                    OnPropertyChanged(nameof(Gebucht));
                    OnPropertyChanged(nameof(Differenz));
                }
            }
        }

        // NEU: Budget/Ist/Differenz für Anzeige und Summen auf Ordnerebene
        public decimal Budgetwert
            => OriginalEintrag != null
                ? (OriginalEintrag.Budgetwert ?? 0m)
                : Kinder.Sum(k => k.Budgetwert);

        public decimal Gebucht
            => OriginalEintrag != null
                ? OriginalEintrag.Gebucht
                : Kinder.Sum(k => k.Gebucht);

        public decimal Differenz => Budgetwert - Gebucht;

        // Ein einziger, fertiger Text für die TreeView (erstmal pragmatisch)
        public string AnzeigeText
        {
            get
            {
                var ch = CultureInfo.GetCultureInfo("de-CH");
                string zahlen = $" | Budget {Budgetwert.ToString("C", ch)} | Ist {Gebucht.ToString("C", ch)} | Δ {Differenz.ToString("C", ch)}";

                if (OriginalEintrag == null)
                    return Bezeichnung + zahlen;

                var nummer = OriginalEintrag.Kontonummer.ToString();
                var detail = string.IsNullOrWhiteSpace(OriginalEintrag.Detail) ? "(ohne Detail)" : OriginalEintrag.Detail;

                return (string.IsNullOrWhiteSpace(nummer) ? detail : $"{nummer} — {detail}") + zahlen;
            }
        }

        public KontoplanKnoten(string bezeichnung) => _bezeichnung = bezeichnung;

        public KontoplanKnoten(string bezeichnung, KontoplanEintrag originalEintrag)
        {
            _bezeichnung = bezeichnung;
            _originalEintrag = originalEintrag;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
