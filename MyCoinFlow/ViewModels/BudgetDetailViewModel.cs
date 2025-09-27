using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.ViewModels
{
    public class BudgetDetailViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new DatabaseService();

        public ObservableCollection<BudgetKontoRow> Zeilen { get; } = new();

        private int? _presetZeitraumId;
        private int? _aktiverZeitraumId;
        public int? AktiverZeitraumId
        {
            get => _aktiverZeitraumId;
            private set { _aktiverZeitraumId = value; OnPropertyChanged(); }
        }

        public ICommand ReloadCommand { get; }
        public ICommand SaveAllCommand { get; }
        public ICommand SetEmptyToZeroCommand { get; }

        // EINZIGER gültiger Konstruktor
        public BudgetDetailViewModel(int? zeitraumId = null)
        {
            _presetZeitraumId = zeitraumId;

            ReloadCommand = new RelayCommand(_ => Reload());
            SaveAllCommand = new RelayCommand(_ => SaveAll(), _ => AktiverZeitraumId != null);
            SetEmptyToZeroCommand = new RelayCommand(_ => SetEmptyToZero());

            Reload();
        }

        public void Reload()
        {
            Zeilen.Clear();

            // wenn ein Zeitraum vorgegeben wurde, nutze den; sonst aktiven holen
            AktiverZeitraumId = _presetZeitraumId ?? _db.HoleAktivenBudgetzeitraumId();

            var list = _db.LadeBudgetKontenFuerZeitraum(AktiverZeitraumId);

            foreach (var row in list)
                Zeilen.Add(row);

            (SaveAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Speichert alle Zeilen (nur bei gesetztem aktivem Zeitraum sinnvoll).
        /// </summary>
        public void SaveAll()
        {
            if (AktiverZeitraumId == null)
            {
                return;
            }

            foreach (var row in Zeilen)
            {
                _db.UpsertBudgetwert(AktiverZeitraumId.Value, row.KontoId, row.Budgetwert);
            }
                        
        }

        public void SaveOne(BudgetKontoRow row)
        {
            if (row == null) return;
            if (AktiverZeitraumId == null) return; // sollte gesetzt sein

            // Schreibt sofort in die DB (null => Eintrag löschen, Zahl => upsert)
            _db.UpsertBudgetwert(AktiverZeitraumId.Value, row.KontoId, row.Budgetwert);
        }


        /// <summary>
        /// Setzt alle NULL/leer auf 0 (bequem für Erstbefüllung).
        /// </summary>
        private void SetEmptyToZero()
        {
            foreach (var row in Zeilen)
            {
                if (row.Budgetwert == null) row.Budgetwert = 0m;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
