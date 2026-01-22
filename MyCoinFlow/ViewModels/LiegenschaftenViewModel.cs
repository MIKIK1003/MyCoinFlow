using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.Views;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace MyCoinFlow.ViewModels
{
    /// <summary>
    /// Liegenschaften-Modul (STWE):
    /// - Liegenschaften anlegen/anzeigen
    /// - Einheiten anlegen/anzeigen
    /// - Eigentümer anlegen/anzeigen
    /// - Eigentümer zeitabhängig einer Einheit zuordnen (Von/Bis)
    /// </summary>
    public class LiegenschaftenViewModel : BaseViewModel
    {
        private readonly DatabaseService _db = new();

        // ===== Listen =====
        public ObservableCollection<StweLiegenschaft> Liegenschaften { get; } = new();
        public ObservableCollection<StweEinheit> Einheiten { get; } = new();
        public ObservableCollection<StweEigentuemer> Eigentuemer { get; } = new();
        public ObservableCollection<StweEinheitEigentumRow> EigentumRows { get; } = new();

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

        public LiegenschaftenViewModel()
        {
            // Schema beim ersten Öffnen sicherstellen
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

            if (SelectedLiegenschaft == null)
                return;

            foreach (var e in _db.StweEinheitenGetByLiegenschaft(SelectedLiegenschaft.Id))
                Einheiten.Add(e);

            if (Einheiten.Count > 0)
            {
                if (SelectedEinheit == null)
                    SelectedEinheit = Einheiten[0];
                else
                    LoadEigentumRows();
            }
            else
            {
                SelectedEinheit = null;
            }
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

            // aktuelle Eigentümerliste sicherstellen
            LoadEigentuemer();

            var dlg = new EigentumZuordnenDialog(Eigentuemer);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                var owner = dlg.SelectedOwner;
                if (owner == null || !dlg.Von.HasValue) return;

                _db.StweEinheitEigentumInsert(
                    einheitId: SelectedEinheit.Id,
                    eigentuemerId: owner.Id,
                    gueltigVon: dlg.Von.Value,
                    gueltigBis: dlg.Bis
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
