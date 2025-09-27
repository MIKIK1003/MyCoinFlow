using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.Views;

namespace MyCoinFlow.ViewModels
{
    public class InstitutionsViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new();

        public ObservableCollection<GeldinstitutSaldo> Institute { get; } = new();

        private GeldinstitutSaldo? _ausgewaehltesInstitut;
        public GeldinstitutSaldo? AusgewaehltesInstitut
        {
            get => _ausgewaehltesInstitut;
            set
            {
                _ausgewaehltesInstitut = value;
                OnPropertyChanged();
                (BearbeitenCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (LoeschenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        // --- NEU: Abgrenzungsdatum ---
        private DateTime _abgrenzungsdatum = DateTime.Today;
        public DateTime Abgrenzungsdatum
        {
            get => _abgrenzungsdatum;
            set
            {
                if (_abgrenzungsdatum != value)
                {
                    _abgrenzungsdatum = value;
                    OnPropertyChanged();
                    Lade(); // bei Änderung sofort neu laden
                }
            }
        }

        public ICommand NeuCommand { get; }
        public ICommand BearbeitenCommand { get; }
        public ICommand LoeschenCommand { get; }

        public InstitutionsViewModel()
        {
            NeuCommand = new RelayCommand(_ => Neu());
            BearbeitenCommand = new RelayCommand(_ => Bearbeiten(), _ => AusgewaehltesInstitut != null);
            LoeschenCommand = new RelayCommand(_ => Loeschen(), _ => AusgewaehltesInstitut != null);
            Lade();
        }

        private void Lade()
        {
            Institute.Clear();
            foreach (var g in _db.LadeGeldinstituteMitSaldo(Abgrenzungsdatum))
                Institute.Add(g);
        }

        private void Neu()
        {
            var dlg = new InstitutionDialog();
            if (dlg.ShowDialog() == true && dlg.Ergebnis != null)
            {
                var id = _db.SpeichereGeldinstitut(dlg.Ergebnis);

                // Nach Neuanlage neu laden, damit Gebucht/Schlussaldo berechnet sind
                Lade();
            }
        }

        private void Bearbeiten()
        {
            if (AusgewaehltesInstitut == null) return;

            var kopie = new Geldinstitut
            {
                Id = AusgewaehltesInstitut.Id,
                Name = AusgewaehltesInstitut.Name,
                BIC = AusgewaehltesInstitut.BIC,
                IBAN = AusgewaehltesInstitut.IBAN,
                KontoNummer = AusgewaehltesInstitut.KontoNummer,
                Notiz = AusgewaehltesInstitut.Notiz,
                Anfangsbestand = AusgewaehltesInstitut.Anfangsbestand,
                Anfangsdatum = AusgewaehltesInstitut.Anfangsdatum
            };

            var dlg = new InstitutionDialog(kopie);
            if (dlg.ShowDialog() == true && dlg.Ergebnis != null)
            {
                _db.AktualisiereGeldinstitut(dlg.Ergebnis);
                // Neu laden, damit die berechneten Spalten stimmen
                Lade();
            }
        }

        private void Loeschen()
        {
            if (AusgewaehltesInstitut == null) return;

            var ok = MessageBox.Show($"„{AusgewaehltesInstitut.Name}“ wirklich löschen?",
                                     "Löschen bestätigen",
                                     MessageBoxButton.YesNo,
                                     MessageBoxImage.Warning);
            if (ok != MessageBoxResult.Yes) return;

            _db.LoescheGeldinstitut(AusgewaehltesInstitut.Id);
            Lade();
            AusgewaehltesInstitut = null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

}
