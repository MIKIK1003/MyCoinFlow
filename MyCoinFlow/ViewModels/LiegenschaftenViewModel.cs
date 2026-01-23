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
    /// <summary>
    /// Liegenschaften-Modul (STWE):
    /// - Liegenschaften, Einheiten, Eigentümer
    /// - Eigentümer-Zuordnung (Von/Bis)
    /// - Schlüssel (Verteilpläne) als Stammdaten
    /// </summary>
    public class LiegenschaftenViewModel : BaseViewModel
    {
        private readonly DatabaseService _db = new();

        // ===== Listen =====
        public ObservableCollection<StweLiegenschaft> Liegenschaften { get; } = new();
        public ObservableCollection<StweEinheit> Einheiten { get; } = new();
        public ObservableCollection<StweEigentuemer> Eigentuemer { get; } = new();
        public ObservableCollection<StweEinheitEigentumRow> EigentumRows { get; } = new();

        // NEU: Schlüssel
        public ObservableCollection<StweSchluessel> Schluessel { get; } = new();

        // ===== Selektionen =====
        private StweLiegenschaft? _selectedLiegenschaft;
        public StweLiegenschaft? SelectedLiegenschaft
        {
            get => _selectedLiegenschaft;
            set
            {
                _selectedLiegenschaft = value;
                OnPropertyChanged();
                LoadEinheiten();
                LoadSchluessel();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private StweEinheit? _selectedEinheit;
        public StweEinheit? SelectedEinheit
        {
            get => _selectedEinheit;
            set
            {
                _selectedEinheit = value;
                OnPropertyChanged();
                LoadEigentumRows();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private StweEigentuemer? _selectedEigentuemer;
        public StweEigentuemer? SelectedEigentuemer
        {
            get => _selectedEigentuemer;
            set { _selectedEigentuemer = value; OnPropertyChanged(); }
        }

        private StweSchluessel? _selectedSchluessel;
        public StweSchluessel? SelectedSchluessel
        {
            get => _selectedSchluessel;
            set { _selectedSchluessel = value; OnPropertyChanged(); }
        }

        // ===== Status =====
        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        // ===== Commands =====
        public RelayCommand NeueLiegenschaftCommand { get; }
        public RelayCommand NeueEinheitCommand { get; }
        public RelayCommand NeueEigentuemerCommand { get; }
        public RelayCommand EigentumZuordnenCommand { get; }

        // NEU: Schlüssel
        public RelayCommand NeuerSchluesselCommand { get; }
        public RelayCommand SchluesselZeilenBearbeitenCommand { get; }

        public LiegenschaftenViewModel()
        {
            try
            {
                _db.EnsureStweSchema();
                StatusText = "Bereit. Lege Daten an oder wähle bestehende Einträge.";
            }
            catch (Exception ex)
            {
                StatusText = "Fehler beim Initialisieren:\n" + ex.Message;
            }

            NeueLiegenschaftCommand = new RelayCommand(_ => NeueLiegenschaft());
            NeueEinheitCommand = new RelayCommand(_ => NeueEinheit(), _ => SelectedLiegenschaft != null);
            NeueEigentuemerCommand = new RelayCommand(_ => NeuerEigentuemer());
            EigentumZuordnenCommand = new RelayCommand(_ => EigentumZuordnen(), _ => SelectedEinheit != null);

            NeuerSchluesselCommand = new RelayCommand(_ => NeuerSchluessel(), _ => SelectedLiegenschaft != null);
            SchluesselZeilenBearbeitenCommand = new RelayCommand(
                _ => SchluesselZeilenBearbeiten(),
                _ => SelectedSchluessel != null && SelectedSchluessel.Modus == "FIX"
            );

            LoadLiegenschaften();
            LoadEigentuemer();
        }

        // ===== Load =====

        private void LoadLiegenschaften()
        {
            Liegenschaften.Clear();
            foreach (var l in _db.StweLiegenschaftenGetAll())
                Liegenschaften.Add(l);

            if (Liegenschaften.Count == 0)
            {
                StatusText = "Noch keine Liegenschaften vorhanden. Klicke auf „Neu“.";
                SelectedLiegenschaft = null;
                Einheiten.Clear();
                EigentumRows.Clear();
                Schluessel.Clear();
                return;
            }

            if (SelectedLiegenschaft == null)
                SelectedLiegenschaft = Liegenschaften[0];
            else
                LoadEinheiten();
        }

        private void LoadEinheiten()
        {
            Einheiten.Clear();
            EigentumRows.Clear();

            if (SelectedLiegenschaft == null) return;

            foreach (var e in _db.StweEinheitenGetByLiegenschaft(SelectedLiegenschaft.Id))
                Einheiten.Add(e);

            SelectedEinheit = Einheiten.FirstOrDefault();
        }

        private void LoadEigentuemer()
        {
            Eigentuemer.Clear();
            foreach (var o in _db.StweEigentuemerGetAll())
                Eigentuemer.Add(o);
        }

        private void LoadEigentumRows()
        {
            EigentumRows.Clear();
            if (SelectedEinheit == null) return;

            foreach (var r in _db.StweEinheitEigentumGetByEinheit(SelectedEinheit.Id))
                EigentumRows.Add(r);
        }

        // ===== Schlüssel =====

        private void LoadSchluessel()
        {
            Schluessel.Clear();
            SelectedSchluessel = null;

            if (SelectedLiegenschaft == null) return;

            foreach (var s in _db.StweSchluesselGetByLiegenschaft(SelectedLiegenschaft.Id))
                Schluessel.Add(s);

            SelectedSchluessel = Schluessel.FirstOrDefault();
        }

        private void NeuerSchluessel()
        {
            if (SelectedLiegenschaft == null) return;

            var dlg = new SchluesselNeuDialog();
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                _db.StweSchluesselInsert(
                    SelectedLiegenschaft.Id,
                    dlg.Model.Name,
                    dlg.Model.Modus
                );

                LoadSchluessel();
            }
        }

        private void SchluesselZeilenBearbeiten()
        {
            if (SelectedSchluessel == null || SelectedSchluessel.Modus != "FIX")
                return;

            LoadEigentuemer();

            var existing = _db.StweSchluesselLinesGet(SelectedSchluessel.Id);
            var dlg = new SchluesselZeilenDialog(
                SelectedSchluessel.Name,
                Eigentuemer,
                existing
            );

            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                var lines = dlg.Rows
                    .Select(r => (r.EigentuemerId, r.AnteilProzent))
                    .ToList();

                _db.StweSchluesselLinesReplace(SelectedSchluessel.Id, lines);
            }

        }

        // ===== Actions =====

        private void NeueLiegenschaft()
        {
            var dlg = new LiegenschaftNeuDialog();
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                _db.StweLiegenschaftInsert(dlg.Model);
                LoadLiegenschaften();
            }
        }

        private void NeueEinheit()
        {
            if (SelectedLiegenschaft == null) return;

            var dlg = new EinheitNeuDialog(SelectedLiegenschaft.Id);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                _db.StweEinheitInsert(dlg.Model);
                LoadEinheiten();
            }
        }

        private void NeuerEigentuemer()
        {
            var dlg = new EigentuemerNeuDialog();
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                _db.StweEigentuemerInsert(dlg.Model);
                LoadEigentuemer();
            }
        }

        private void EigentumZuordnen()
        {
            if (SelectedEinheit == null) return;

            LoadEigentuemer();

            var dlg = new EigentumZuordnenDialog(Eigentuemer);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                var owner = dlg.SelectedOwner;
                if (owner == null || !dlg.Von.HasValue) return;

                _db.StweEinheitEigentumInsert(
                    SelectedEinheit.Id,
                    owner.Id,
                    dlg.Von.Value,
                    dlg.Bis
                );

                LoadEigentumRows();
            }
        }

        private static void TrySetOwner(Window dlg)
        {
            try
            {
                if (Application.Current?.MainWindow != null)
                    dlg.Owner = Application.Current.MainWindow;
            }
            catch { /* still */ }
        }
    }
}
