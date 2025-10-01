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

        // AddressesViewModel.cs
        // Vollständige Methode zum Ersetzen deiner bisherigen Lösch-Methode
        private void Loeschen()
        {
            if (AusgewaehlteAdresse == null) return;

            var ask = System.Windows.MessageBox.Show(
                $"Adresse „{AusgewaehlteAdresse.Name}“ wirklich löschen?",
                "Löschen bestätigen",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (ask != System.Windows.MessageBoxResult.Yes) return;

            var id = AusgewaehlteAdresse.Id;

            // 1) Löschversuch (DB fängt FK-Blockaden benutzerfreundlich ab)
            try
            {
                _db.LoescheAdresse(id);
            }
            catch
            {
                // DB-Service hat bereits eine Meldung gezeigt; kein Abbruch hier.
            }

            // 2) Erfolg prüfen: existiert die Adresse noch?
            //    HoleAdresse(id) → null, wenn gelöscht; sonst Objekt wenn noch vorhanden.
            //    (Siehe DatabaseService.HoleAdresse) :contentReference[oaicite:0]{index=0}
            var nochDa = _db.HoleAdresse(id);

            if (nochDa == null)
            {
                // 3) Lokal aus der Liste entfernen (sofort sichtbar, kein Reload nötig)
                object? victim = null;
                foreach (var a in Adressen)
                {
                    if (a.Id == id) { victim = a; break; }
                }
                if (victim != null) Adressen.Remove((MyCoinFlow.Models.Adresse)victim);

                AusgewaehlteAdresse = null;
                // Falls du einen Command nutzt, CanExecute neu bewerten:
                (LoeschenCommand as MyCoinFlow.Helpers.RelayCommand)?.RaiseCanExecuteChanged();
            }
            else
            {
                // 4) Nicht gelöscht (z. B. wegen Transaktionen) → nichts aus der Liste entfernen.
                //    UI bleibt korrekt, da Datensatz weiterhin existiert.
            }
        }



        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
