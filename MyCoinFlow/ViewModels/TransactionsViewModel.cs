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
            var dlg = new TransactionsDialog { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() == true) LadeListe();
        }

        private void Bearbeiten()
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

            var dlg = new TransactionsDialog(t) { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() == true) LadeListe();
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

        // NEU: Bankimport-Fenster öffnen, hostet die vorhandene BankImportView
        private void OpenBankImport()
        {
            var wnd = new BankImportWindow
            {
                Owner = Application.Current.MainWindow
            };
            wnd.ShowDialog();

            // Optional: Nach Schließen neu laden, falls später „Übernehmen…“ Buchungen erzeugt.
            // LadeListe();
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
