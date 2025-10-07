using System;
using System.Collections.Generic;
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
    public class TransactionsViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new();

        public ObservableCollection<TransaktionRowExt> Transaktionen { get; } = new();

        private TransaktionRowExt? _ausgewaehlteTransaktion;
        public TransaktionRowExt? AusgewaehlteTransaktion
        {
            get => _ausgewaehlteTransaktion;
            set
            {
                _ausgewaehlteTransaktion = value;
                OnPropertyChanged();
                (BearbeitenCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (LoeschenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ICommand NeuBuchungCommand { get; }
        public ICommand BearbeitenCommand { get; }
        public ICommand LoeschenCommand { get; }

        // NEU: Bankimport öffnen
        public ICommand OpenBankImportCommand { get; }

        public TransactionsViewModel()
        {
            NeuBuchungCommand = new RelayCommand(_ => NeueBuchung());
            BearbeitenCommand = new RelayCommand(_ => Bearbeiten(), _ => AusgewaehlteTransaktion != null);
            LoeschenCommand = new RelayCommand(_ => Loeschen(), _ => AusgewaehlteTransaktion != null);

            // NEU
            OpenBankImportCommand = new RelayCommand(_ => OpenBankImport());

            LadeListe();
        }


        private void NeueBuchung()
        {
            try
            {
                var dlg = new TransactionsDialog();

                System.Windows.Window? owner = null;
                try
                {
                    owner = System.Windows.Application.Current?.Windows
                                .OfType<System.Windows.Window>()
                                .FirstOrDefault(w => w.IsActive)
                         ?? System.Windows.Application.Current?.MainWindow;
                }
                catch { }

                if (owner != null && !ReferenceEquals(owner, dlg))
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

                if (dlg.ShowDialog() == true)
                    LadeListe();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dialogfehler (Neue Buchung): {ex.Message}", "Transaktionen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Bearbeiten()
        {
            try
            {
                if (AusgewaehlteTransaktion == null) return;

                var t = new Transaktion
                {
                    Id = AusgewaehlteTransaktion.Id,
                    Datum = AusgewaehlteTransaktion.Datum,
                    VonKontoId = AusgewaehlteTransaktion.VonKontoId,
                    NachKontoId = AusgewaehlteTransaktion.NachKontoId,
                    Betrag = AusgewaehlteTransaktion.Betrag,
                    AdresseId = AusgewaehlteTransaktion.AdresseId,
                    GeldinstitutId = AusgewaehlteTransaktion.GeldinstitutId,
                    Notiz = AusgewaehlteTransaktion.Notiz
                };

                var dlg = new TransactionsDialog(t);

                System.Windows.Window? owner = null;
                try
                {
                    owner = System.Windows.Application.Current?.Windows
                                .OfType<System.Windows.Window>()
                                .FirstOrDefault(w => w.IsActive)
                         ?? System.Windows.Application.Current?.MainWindow;
                }
                catch { }

                if (owner != null && !ReferenceEquals(owner, dlg))
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

                if (dlg.ShowDialog() == true)
                    LadeListe();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dialogfehler (Bearbeiten): {ex.Message}", "Transaktionen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void Loeschen()
        {
            if (AusgewaehlteTransaktion == null) return;

            var ask = MessageBox.Show("Ausgewählte Transaktion wirklich löschen?",
                                      "Löschen bestätigen",
                                      MessageBoxButton.YesNo,
                                      MessageBoxImage.Warning);
            if (ask != MessageBoxResult.Yes) return;

            _db.LoescheTransaktion(AusgewaehlteTransaktion.Id);
            LadeListe();
        }

        private void LadeListe()
        {
            Transaktionen.Clear();

            var kontoMap = new Dictionary<int, string>();
            foreach (var k in _db.LadeKontenplan())
            {
                string unter = string.IsNullOrWhiteSpace(k.Untergruppe) ? "" : $"  {k.Untergruppe}";
                kontoMap[k.Id] = $"{k.Kontonummer:D4}{unter}  {k.Detail}";
            }

            var list = _db.LadeTransaktionen();

            foreach (var t in list)
            {
                string von = t.VonKontoId.HasValue
                    ? (kontoMap.TryGetValue(t.VonKontoId.Value, out var vk) ? vk : $"Konto #{t.VonKontoId}")
                    : (t.BankName ?? "Bank");

                string nach = t.NachKontoId.HasValue
                    ? (kontoMap.TryGetValue(t.NachKontoId.Value, out var nk) ? nk : $"Konto #{t.NachKontoId}")
                    : (t.BankName ?? "Bank");

                Transaktionen.Add(new TransaktionRowExt
                {
                    Id = t.Id,
                    Datum = t.Datum,
                    VonAnzeige = von,
                    NachAnzeige = nach,
                    Betrag = t.Betrag,
                    AdresseName = t.AdresseName,
                    BankName = t.BankName,
                    Notiz = t.Notiz,
                    // IDs für Bearbeiten/Löschen
                    VonKontoId = t.VonKontoId,
                    NachKontoId = t.NachKontoId,
                    AdresseId = t.AdresseId,
                    GeldinstitutId = t.GeldinstitutId
                });
            }
        }

        private void OpenBankImport()
        {
            try
            {
                var wnd = new BankImportWindow();

                System.Windows.Window? owner = null;
                try
                {
                    owner = System.Windows.Application.Current?.Windows
                                .OfType<System.Windows.Window>()
                                .FirstOrDefault(w => w.IsActive)
                         ?? System.Windows.Application.Current?.MainWindow;
                }
                catch { }

                if (owner != null && !ReferenceEquals(owner, wnd))
                    wnd.Owner = owner;
                else
                    wnd.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

                wnd.ShowDialog();

                // Wenn notwendig: Liste neu laden
                // LadeListe();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fensterfehler (Bankimport): {ex.Message}", "Transaktionen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class TransaktionRowExt
    {
        public int Id { get; set; }
        public DateTime Datum { get; set; }

        public string VonAnzeige { get; set; } = "";
        public string NachAnzeige { get; set; } = "";
        public decimal Betrag { get; set; }

        public string? AdresseName { get; set; }
        public string? BankName { get; set; }
        public string? Notiz { get; set; }

        // Für Bearbeiten
        public int? VonKontoId { get; set; }
        public int? NachKontoId { get; set; }
        public int? AdresseId { get; set; }
        public int? GeldinstitutId { get; set; }
    }
}
