using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MyCoinFlow.Models
{
    /// <summary>
    /// Eine Zeile im Budget-Grid: Konto + Budgetwert für den aktiven Zeitraum.
    /// </summary>
    public class BudgetKontoRow : INotifyPropertyChanged
    {
        private decimal? _budgetwert;

        public int KontoId { get; set; }          // k.Id (Kontenplan)
        public int Kontonummer { get; set; }      // k.Kontonummer
        public string Art { get; set; } = "";
        public string Gruppe { get; set; } = "";
        public string Untergruppe { get; set; } = "";
        public string Detail { get; set; } = "";  // Kontobezeichnung

        /// <summary>
        /// Budgetwert des aktiven Zeitraums (null = nicht gesetzt)
        /// </summary>
        public decimal? Budgetwert
        {
            get => _budgetwert;
            set { _budgetwert = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
