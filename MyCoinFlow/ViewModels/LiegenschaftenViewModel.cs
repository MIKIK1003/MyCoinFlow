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
    public class LiegenschaftenViewModel : BaseViewModel
    {
        private readonly DatabaseService _db = new();

        // ===== Listen =====
        public ObservableCollection<StweLiegenschaft> Liegenschaften { get; } = new();
        public ObservableCollection<StweEinheit> Einheiten { get; } = new();
        public ObservableCollection<StweEigentuemer> Eigentuemer { get; } = new();
        public ObservableCollection<StweEinheitEigentumRow> EigentumRows { get; } = new();
        public ObservableCollection<StweSchluessel> Schluessel { get; } = new();
        public ObservableCollection<StweZaehler> Zaehler { get; } = new();

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
                LoadZaehler();
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
            set { _selectedEigentuemer = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        private StweEinheitEigentumRow? _selectedEigentumRow;
        public StweEinheitEigentumRow? SelectedEigentumRow
        {
            get => _selectedEigentumRow;
            set { _selectedEigentumRow = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        private StweSchluessel? _selectedSchluessel;
        public StweSchluessel? SelectedSchluessel
        {
            get => _selectedSchluessel;
            set
            {
                _selectedSchluessel = value;
                OnPropertyChanged();

                // wichtig: Button-Enable neu bewerten
                SchluesselZeilenBearbeitenCommand?.RaiseCanExecuteChanged();
            }

        }

        private StweZaehler? _selectedZaehler;
        public StweZaehler? SelectedZaehler
        {
            get => _selectedZaehler;
            set { _selectedZaehler = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
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
        public RelayCommand LiegenschaftBearbeitenCommand { get; }
        public RelayCommand LiegenschaftLoeschenCommand { get; }

        public RelayCommand NeueEinheitCommand { get; }
        public RelayCommand EinheitBearbeitenCommand { get; }
        public RelayCommand EinheitLoeschenCommand { get; }

        public RelayCommand NeueEigentuemerCommand { get; }
        public RelayCommand EigentuemerBearbeitenCommand { get; }
        public RelayCommand EigentuemerLoeschenCommand { get; }

        public RelayCommand EigentumZuordnenCommand { get; }
        public RelayCommand ZuordnungBearbeitenCommand { get; }
        public RelayCommand ZuordnungLoeschenCommand { get; }

        public RelayCommand NeuerSchluesselCommand { get; }
        public RelayCommand SchluesselZeilenBearbeitenCommand { get; }
        public RelayCommand SchluesselUmbenennenCommand { get; }
        public RelayCommand SchluesselLoeschenCommand { get; }   // NEU


        // NEU: Zähler
        public RelayCommand NeuerZaehlerCommand { get; }
        public RelayCommand ZaehlerBearbeitenCommand { get; }
        public RelayCommand ZaehlerLoeschenCommand { get; }

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
            LiegenschaftBearbeitenCommand = new RelayCommand(_ => LiegenschaftBearbeiten(), _ => SelectedLiegenschaft != null);
            LiegenschaftLoeschenCommand = new RelayCommand(_ => LiegenschaftLoeschen(), _ => SelectedLiegenschaft != null);

            NeueEinheitCommand = new RelayCommand(_ => NeueEinheit(), _ => SelectedLiegenschaft != null);
            EinheitBearbeitenCommand = new RelayCommand(_ => EinheitBearbeiten(), _ => SelectedEinheit != null);
            EinheitLoeschenCommand = new RelayCommand(_ => EinheitLoeschen(), _ => SelectedEinheit != null);

            NeueEigentuemerCommand = new RelayCommand(_ => NeuerEigentuemer());
            EigentuemerBearbeitenCommand = new RelayCommand(_ => EigentuemerBearbeiten(), _ => SelectedEigentuemer != null);
            EigentuemerLoeschenCommand = new RelayCommand(_ => EigentuemerLoeschen(), _ => SelectedEigentuemer != null);

            EigentumZuordnenCommand = new RelayCommand(_ => EigentumZuordnen(), _ => SelectedEinheit != null);
            ZuordnungBearbeitenCommand = new RelayCommand(_ => ZuordnungBearbeiten(), _ => SelectedEigentumRow != null && SelectedEinheit != null);
            ZuordnungLoeschenCommand = new RelayCommand(_ => ZuordnungLoeschen(), _ => SelectedEigentumRow != null);

            NeuerSchluesselCommand = new RelayCommand(_ => NeuerSchluessel(), _ => SelectedLiegenschaft != null);
            SchluesselZeilenBearbeitenCommand = new RelayCommand(
                _ => SchluesselZeilenBearbeiten(),
                _ => SelectedSchluessel != null
                && (
                string.Equals(SelectedSchluessel.Modus?.Trim(), "FIX", StringComparison.OrdinalIgnoreCase)
                || string.Equals(SelectedSchluessel.Modus?.Trim(), "ENERGIE", StringComparison.OrdinalIgnoreCase)));

            SchluesselLoeschenCommand = new RelayCommand(_ => SchluesselLoeschen(), _ => SelectedSchluessel != null);
            SchluesselUmbenennenCommand = new RelayCommand(_ => SchluesselUmbenennen(), _ => SelectedSchluessel != null);

            // NEU: Zähler Commands
            NeuerZaehlerCommand = new RelayCommand(_ => NeuerZaehler(), _ => SelectedLiegenschaft != null);
            ZaehlerBearbeitenCommand = new RelayCommand(_ => ZaehlerBearbeiten(), _ => SelectedZaehler != null);
            ZaehlerLoeschenCommand = new RelayCommand(_ => ZaehlerLoeschen(), _ => SelectedZaehler != null);

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
                Zaehler.Clear();
                SelectedZaehler = null;
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
            SelectedEigentumRow = null;

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
            SelectedEigentumRow = null;

            if (SelectedEinheit == null) return;

            foreach (var r in _db.StweEinheitEigentumGetByEinheit(SelectedEinheit.Id))
                EigentumRows.Add(r);

            SelectedEigentumRow = EigentumRows.FirstOrDefault();
        }

        private void LoadSchluessel()
        {
            Schluessel.Clear();
            SelectedSchluessel = null;

            if (SelectedLiegenschaft == null) return;

            foreach (var s in _db.StweSchluesselGetByLiegenschaft(SelectedLiegenschaft.Id))
                Schluessel.Add(s);

            SelectedSchluessel = Schluessel.FirstOrDefault();
        }

        private void LoadZaehler()
        {
            Zaehler.Clear();
            SelectedZaehler = null;

            if (SelectedLiegenschaft == null) return;

            foreach (var z in _db.StweZaehlerGetByLiegenschaft(SelectedLiegenschaft.Id))
                Zaehler.Add(z);

            SelectedZaehler = Zaehler.FirstOrDefault();
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

        private void LiegenschaftBearbeiten()
        {
            if (SelectedLiegenschaft == null) return;

            var dlg = new LiegenschaftNeuDialog();
            TrySetOwner(dlg);

            dlg.Model.Id = SelectedLiegenschaft.Id;
            dlg.Model.Name = SelectedLiegenschaft.Name;
            dlg.Model.Strasse = SelectedLiegenschaft.Strasse;
            dlg.Model.PLZ = SelectedLiegenschaft.PLZ;
            dlg.Model.Ort = SelectedLiegenschaft.Ort;
            dlg.Model.Notiz = SelectedLiegenschaft.Notiz;

            if (dlg.ShowDialog() == true)
            {
                _db.StweLiegenschaftUpdate(dlg.Model);
                LoadLiegenschaften();
                StatusText = "Liegenschaft aktualisiert.";
            }
        }

        private void LiegenschaftLoeschen()
        {
            if (SelectedLiegenschaft == null) return;

            var res = MessageBox.Show(
                $"Liegenschaft „{SelectedLiegenschaft.Name}“ wirklich löschen?",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;

            _db.StweLiegenschaftDelete(SelectedLiegenschaft.Id);
            LoadLiegenschaften();
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

        private void EinheitBearbeiten()
        {
            if (SelectedEinheit == null) return;

            var dlg = new EinheitNeuDialog(SelectedEinheit.LiegenschaftId);
            TrySetOwner(dlg);

            dlg.Model.Id = SelectedEinheit.Id;
            dlg.Model.LiegenschaftId = SelectedEinheit.LiegenschaftId;
            dlg.Model.Bezeichnung = SelectedEinheit.Bezeichnung;
            dlg.Model.Typ = SelectedEinheit.Typ;
            dlg.Model.MeaPromille = SelectedEinheit.MeaPromille;
            dlg.Model.FlaecheM2 = SelectedEinheit.FlaecheM2;
            dlg.Model.Notiz = SelectedEinheit.Notiz;

            if (dlg.ShowDialog() == true)
            {
                _db.StweEinheitUpdate(dlg.Model);
                LoadEinheiten();
                StatusText = "Einheit aktualisiert.";
            }
        }

        private void EinheitLoeschen()
        {
            if (SelectedEinheit == null) return;

            var res = MessageBox.Show(
                $"Einheit „{SelectedEinheit.Bezeichnung}“ wirklich löschen?\n\n" +
                "Hinweis: Löschen ist nur möglich, wenn keine Zuordnungen und keine Set-Verwendungen existieren.",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;

            _db.StweEinheitDelete(SelectedEinheit.Id);
            LoadEinheiten();
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

        private void EigentuemerBearbeiten()
        {
            if (SelectedEigentuemer == null) return;

            var dlg = new EigentuemerNeuDialog();
            TrySetOwner(dlg);

            dlg.Model.Id = SelectedEigentuemer.Id;
            dlg.Model.Name = SelectedEigentuemer.Name;
            dlg.Model.Email = SelectedEigentuemer.Email;
            dlg.Model.Telefon = SelectedEigentuemer.Telefon;
            dlg.Model.Notiz = SelectedEigentuemer.Notiz;

            if (dlg.ShowDialog() == true)
            {
                _db.StweEigentuemerUpdate(dlg.Model);
                LoadEigentuemer();
                StatusText = "Eigentümer aktualisiert.";
            }
        }

        private void EigentuemerLoeschen()
        {
            if (SelectedEigentuemer == null) return;

            var res = MessageBox.Show(
                $"Eigentümer „{SelectedEigentuemer.Name}“ wirklich löschen?\n\n" +
                "Hinweis: Löschen ist nur möglich, wenn keine Zuordnungen und keine Set-Verwendungen existieren.",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;

            _db.StweEigentuemerDelete(SelectedEigentuemer.Id);
            LoadEigentuemer();
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
                StatusText = "Zuordnung gespeichert.";
            }
        }

        private void ZuordnungBearbeiten()
        {
            if (SelectedEinheit == null || SelectedEigentumRow == null) return;

            LoadEigentuemer();

            var dlg = new EigentumZuordnenDialog(Eigentuemer);
            TrySetOwner(dlg);

            dlg.SelectedOwner = Eigentuemer.FirstOrDefault(o => o.Id == SelectedEigentumRow.EigentuemerId);
            dlg.Von = SelectedEigentumRow.GueltigVon;
            dlg.Bis = SelectedEigentumRow.GueltigBis;

            if (dlg.ShowDialog() == true)
            {
                if (dlg.SelectedOwner == null || !dlg.Von.HasValue) return;

                _db.StweEinheitEigentumUpdate(
                    id: SelectedEigentumRow.Id,
                    einheitId: SelectedEinheit.Id,
                    eigentuemerId: dlg.SelectedOwner.Id,
                    gueltigVon: dlg.Von.Value,
                    gueltigBis: dlg.Bis
                );

                LoadEigentumRows();
                StatusText = "Zuordnung aktualisiert.";
            }
        }

        private void ZuordnungLoeschen()
        {
            if (SelectedEigentumRow == null) return;

            var bisText = SelectedEigentumRow.GueltigBis.HasValue
                ? SelectedEigentumRow.GueltigBis.Value.ToString("dd.MM.yyyy")
                : "—";

            var res = MessageBox.Show(
                $"Zuordnung wirklich löschen?\n\n{SelectedEigentumRow.EigentuemerName}\n" +
                $"Von: {SelectedEigentumRow.GueltigVon:dd.MM.yyyy}  Bis: {bisText}",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;

            _db.StweEinheitEigentumDelete(SelectedEigentumRow.Id);
            LoadEigentumRows();
            StatusText = "Zuordnung gelöscht.";
        }

        private void NeuerSchluessel()
        {
            if (SelectedLiegenschaft == null) return;

            // ✅ Dialog braucht die LiegenschaftId
            var dlg = new SchluesselNeuDialog(SelectedLiegenschaft.Id);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                // dlg.Model enthält Name + Modus (FIX/MEA/ENERGIE) bereits korrekt.
                _db.StweSchluesselInsert(SelectedLiegenschaft.Id, dlg.Model.Name, dlg.Model.Modus);
                LoadSchluessel();
            }
        }

        private void SchluesselLoeschen()
        {
            if (SelectedSchluessel == null)
                return;

            var s = SelectedSchluessel;

            var res = MessageBox.Show(
                $"Schlüssel wirklich löschen?\n\nName: {s.Name}\nModus: {s.Modus}\n\nHinweis: Wenn ein Zähler auf diesen Schlüssel zeigt, wird das Löschen verhindert.",
                "Schlüssel löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes)
                return;

            try
            {
                _db.StweSchluesselDelete(s.Id);
                LoadSchluessel();
                StatusText = "Schlüssel gelöscht.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Schlüssel löschen", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }



        private void SchluesselZeilenBearbeiten()
        {
            if (SelectedSchluessel == null)
                return;

            var modus = (SelectedSchluessel.Modus ?? "").Trim();
            if (!string.Equals(modus, "FIX", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(modus, "ENERGIE", StringComparison.OrdinalIgnoreCase))
                return;


            LoadEigentuemer();

            var existing = _db.StweSchluesselLinesGet(SelectedSchluessel.Id);
            var dlg = new SchluesselZeilenDialog(SelectedSchluessel.Name, Eigentuemer, existing);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                var lines = dlg.Rows.Select(r => (r.EigentuemerId, r.AnteilProzent)).ToList();
                _db.StweSchluesselLinesReplace(SelectedSchluessel.Id, lines);
            }
        }

        private void SchluesselUmbenennen()
        {
            if (SelectedSchluessel == null) return;

            var dlg = new SchluesselUmbenennenDialog(SelectedSchluessel.Name);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                var neu = (dlg.NeueBezeichnung ?? "").Trim();
                if (string.IsNullOrWhiteSpace(neu)) return;

                _db.StweSchluesselRename(SelectedSchluessel.Id, neu);

                var oldId = SelectedSchluessel.Id;
                LoadSchluessel();
                SelectedSchluessel = Schluessel.FirstOrDefault(x => x.Id == oldId) ?? Schluessel.FirstOrDefault();

                StatusText = "Schlüssel umbenannt.";
            }
        }

        // ===== Zähler Actions (NEU) =====

        private void NeuerZaehler()
        {
            if (SelectedLiegenschaft == null) return;

            // Einheiten müssen verfügbar sein für DIREKT-Zähler
            if (Einheiten.Count == 0)
                LoadEinheiten();

            // Schlüssel müssen verfügbar sein für die Energie-Verteilung
            if (Schluessel.Count == 0)
                LoadSchluessel();

            var dlg = new ZaehlerNeuDialog(SelectedLiegenschaft.Id, Einheiten, Schluessel);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                _db.StweZaehlerInsert(dlg.Model);
                LoadZaehler();
                StatusText = "Zähler gespeichert.";
            }
        }

        private void ZaehlerBearbeiten()
        {
            if (SelectedZaehler == null) return;

            if (Einheiten.Count == 0 && SelectedLiegenschaft != null)
                LoadEinheiten();

            if (Schluessel.Count == 0 && SelectedLiegenschaft != null)
                LoadSchluessel();

            var dlg = new ZaehlerNeuDialog(SelectedZaehler.LiegenschaftId, Einheiten, Schluessel);
            TrySetOwner(dlg);

            dlg.SetModel(SelectedZaehler);

            if (dlg.ShowDialog() == true)
            {
                _db.StweZaehlerUpdate(dlg.Model);
                LoadZaehler();
                StatusText = "Zähler aktualisiert.";
            }
        }


        private void ZaehlerLoeschen()
        {
            if (SelectedZaehler == null) return;

            var res = MessageBox.Show(
                $"Zähler „{SelectedZaehler.Name}“ wirklich löschen?\n\n" +
                "Hinweis: Löschen ist nur möglich, wenn der Zähler noch nie in einem Energie-Set verwendet wurde.",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;

            _db.StweZaehlerDelete(SelectedZaehler.Id);
            LoadZaehler();
            StatusText = "Zähler gelöscht.";
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
