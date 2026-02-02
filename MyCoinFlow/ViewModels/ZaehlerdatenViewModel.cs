using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.Views;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace MyCoinFlow.ViewModels
{
    public class ZaehlerdatenViewModel : BaseViewModel
    {
        private readonly DatabaseService _db = new();
        private readonly int _liegenschaftId;
        private readonly string _liegenschaftName;

        public ObservableCollection<StweZaehlerdatenSet> Sets { get; } = new();

        private StweZaehlerdatenSet? _selectedSet;
        public StweZaehlerdatenSet? SelectedSet
        {
            get => _selectedSet;
            set { _selectedSet = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        private string _statusText = "Bereit.";
        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        public string HeaderText => $"Zählerdaten – {_liegenschaftName}";

        public RelayCommand NeuesSetCommand { get; }
        public RelayCommand BearbeitenCommand { get; }
        public RelayCommand LoeschenCommand { get; }
       
        public ZaehlerdatenViewModel(int liegenschaftId, string liegenschaftName)
        {
            _liegenschaftId = liegenschaftId;
            _liegenschaftName = liegenschaftName;

            NeuesSetCommand = new RelayCommand(_ => Neu(), _ => _liegenschaftId > 0);
            BearbeitenCommand = new RelayCommand(_ => Bearbeiten(), _ => SelectedSet != null);
            LoeschenCommand = new RelayCommand(_ => Loeschen(), _ => SelectedSet != null);

            Load();
        }

        private void Load()
        {
            Sets.Clear();
            foreach (var s in _db.StweZaehlerdatenSetsGetByLiegenschaft(_liegenschaftId))
                Sets.Add(s);

            SelectedSet = Sets.FirstOrDefault();
            StatusText = Sets.Count == 0 ? "Keine Zählerdaten vorhanden." : $"{Sets.Count} Set(s) vorhanden.";
        }

        private void Neu()
        {
            var dlg = new ZaehlerdatenEditDialog(_liegenschaftId);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                var id = _db.StweZaehlerdatenSetInsert(dlg.Model);
                var lines = dlg.Rows.Select(r => (r.ZaehlerId, r.NeuWert)).ToList();
                _db.StweZaehlerdatenLinesReplace(id, lines);

                Load();
                StatusText = "Zählerdaten gespeichert.";
            }
        }

        private void Bearbeiten()
        {
            if (SelectedSet == null) return;

            var dlg = new ZaehlerdatenEditDialog(_liegenschaftId, SelectedSet);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                _db.StweZaehlerdatenSetUpdate(dlg.Model);
                var lines = dlg.Rows.Select(r => (r.ZaehlerId, r.NeuWert)).ToList();
                _db.StweZaehlerdatenLinesReplace(SelectedSet.Id, lines);

                Load();
                StatusText = "Zählerdaten aktualisiert.";
            }
        }

        private void Loeschen()
        {
            if (SelectedSet == null) return;

            var res = MessageBox.Show(
                $"Zählerdaten wirklich löschen?\n\n{SelectedSet.ErfasstAm:dd.MM.yyyy}\n{SelectedSet.Notiz}",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;

            _db.StweZaehlerdatenSetDelete(SelectedSet.Id);
            Load();
            StatusText = "Zählerdaten gelöscht.";
        }

        private static void TrySetOwner(Window dlg)
        {
            try
            {
                if (Application.Current?.MainWindow != null)
                    dlg.Owner = Application.Current.MainWindow;
            }
            catch { }
        }
    }
}
