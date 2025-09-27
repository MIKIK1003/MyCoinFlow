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
    public class AddressesViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new();

        public ObservableCollection<Adresse> Adressen { get; } = new();

        private Adresse? _ausgewaehlteAdresse;
        public Adresse? AusgewaehlteAdresse
        {
            get => _ausgewaehlteAdresse;
            set
            {
                _ausgewaehlteAdresse = value;
                OnPropertyChanged();
                (BearbeitenCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (LoeschenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ICommand NeuCommand { get; }
        public ICommand BearbeitenCommand { get; }
        public ICommand LoeschenCommand { get; }

        public AddressesViewModel()
        {
            NeuCommand = new RelayCommand(_ => Neu());
            BearbeitenCommand = new RelayCommand(_ => Bearbeiten(), _ => AusgewaehlteAdresse != null);
            LoeschenCommand = new RelayCommand(_ => Loeschen(), _ => AusgewaehlteAdresse != null);
            Lade();
        }

        private void Lade()
        {
            Adressen.Clear();
            foreach (var a in _db.LadeAdressen())
                Adressen.Add(a);
        }

        private void Neu()
        {
            var dlg = new AddressDialog();
            if (dlg.ShowDialog() == true && dlg.Ergebnis != null)
            {
                var id = _db.SpeichereAdresse(dlg.Ergebnis);
                dlg.Ergebnis.Id = id;
                Adressen.Add(dlg.Ergebnis);
            }
        }

        private void Bearbeiten()
        {
            if (AusgewaehlteAdresse == null) return;

            var kopie = new Adresse
            {
                Id = AusgewaehlteAdresse.Id,
                Name = AusgewaehlteAdresse.Name,
                Strasse = AusgewaehlteAdresse.Strasse,
                PLZ = AusgewaehlteAdresse.PLZ,
                Ort = AusgewaehlteAdresse.Ort,
                Land = AusgewaehlteAdresse.Land,
                Typ = AusgewaehlteAdresse.Typ,
                IBAN = AusgewaehlteAdresse.IBAN,
                Notiz = AusgewaehlteAdresse.Notiz
            };

            var dlg = new AddressDialog(kopie);
            if (dlg.ShowDialog() == true && dlg.Ergebnis != null)
            {
                _db.AktualisiereAdresse(dlg.Ergebnis);

                // UI aktualisieren
                var src = AusgewaehlteAdresse;
                src.Name = dlg.Ergebnis.Name;
                src.Strasse = dlg.Ergebnis.Strasse;
                src.PLZ = dlg.Ergebnis.PLZ;
                src.Ort = dlg.Ergebnis.Ort;
                src.Land = dlg.Ergebnis.Land;
                src.Typ = dlg.Ergebnis.Typ;
                src.IBAN = dlg.Ergebnis.IBAN;
                src.Notiz = dlg.Ergebnis.Notiz;

                OnPropertyChanged(nameof(Adressen));
            }
        }

        private void Loeschen()
        {
            if (AusgewaehlteAdresse == null) return;

            var ok = MessageBox.Show($"Adresse „{AusgewaehlteAdresse.Name}“ wirklich löschen?",
                                     "Löschen bestätigen",
                                     MessageBoxButton.YesNo,
                                     MessageBoxImage.Warning);
            if (ok != MessageBoxResult.Yes) return;

            _db.LoescheAdresse(AusgewaehlteAdresse.Id);
            Adressen.Remove(AusgewaehlteAdresse);
            AusgewaehlteAdresse = null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
