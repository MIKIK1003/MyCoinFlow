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
    public class TransaktionSetsViewModel : BaseViewModel
    {
        private readonly DatabaseService _db = new();

        public ObservableCollection<StweLiegenschaft> Liegenschaften { get; } = new();
        public ObservableCollection<StweSetRow> Sets { get; } = new();

        private StweLiegenschaft? _selectedLiegenschaft;
        public StweLiegenschaft? SelectedLiegenschaft
        {
            get => _selectedLiegenschaft;
            set
            {
                _selectedLiegenschaft = value;
                OnPropertyChanged();
                LoadSets();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private StweSetRow? _selectedSet;
        public StweSetRow? SelectedSet
        {
            get => _selectedSet;
            set
            {
                _selectedSet = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private DateTime? _von;
        public DateTime? Von
        {
            get => _von;
            set
            {
                _von = value;
                OnPropertyChanged();
                LoadSets();
            }
        }

        private DateTime? _bis;
        public DateTime? Bis
        {
            get => _bis;
            set
            {
                _bis = value;
                OnPropertyChanged();
                LoadSets();
            }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        public RelayCommand NeuesSetAusTransaktionCommand { get; }
        public RelayCommand SetVerteilenCommand { get; }
        public RelayCommand ShowAuswertungCommand { get; }
        public RelayCommand ShowZaehlerdatenCommand { get; }

        public RelayCommand SetTitelBearbeitenCommand { get; }
        public RelayCommand SetLoeschenCommand { get; }
        public RelayCommand SetAbschliessenCommand { get; }
        public RelayCommand SetWiederOeffnenCommand { get; }

        public RelayCommand SetAlsGutschriftCommand { get; }
        public RelayCommand SetAlsBelastungCommand { get; }

        public TransaktionSetsViewModel()
        {
            try
            {
                _db.EnsureStweSchema();
                StatusText = "Bereit.";
            }
            catch (Exception ex)
            {
                StatusText = "Fehler beim Initialisieren:\n" + ex.Message;
            }

            // Zeitraum vorbelegen: aktiver Budgetzeitraum
            try
            {
                var activeId = _db.HoleAktivenBudgetzeitraumId();
                if (activeId.HasValue)
                {
                    var bz = _db.HoleBudgetzeitraum(activeId.Value);
                    if (bz != null)
                    {
                        _von = bz.Startdatum.Date;
                        _bis = bz.Enddatum.Date;
                    }
                }
            }
            catch { }

            NeuesSetAusTransaktionCommand = new RelayCommand(_ => NeuesSetAusTransaktion(), _ => SelectedLiegenschaft != null);
            SetVerteilenCommand = new RelayCommand(_ => SetVerteilen(), _ => SelectedSet != null);
            ShowAuswertungCommand = new RelayCommand(_ => ShowAuswertung(), _ => SelectedLiegenschaft != null);
            ShowZaehlerdatenCommand = new RelayCommand(_ => ShowZaehlerdaten(), _ => SelectedLiegenschaft != null);


            SetTitelBearbeitenCommand = new RelayCommand(_ => SetTitelBearbeiten(), _ => SelectedSet != null && !SelectedSet.IsClosed);
            SetLoeschenCommand = new RelayCommand(_ => SetLoeschen(), _ => SelectedSet != null && !SelectedSet.IsClosed);

            SetAbschliessenCommand = new RelayCommand(_ => SetStatus(true), _ => CanCloseSet());
            SetWiederOeffnenCommand = new RelayCommand(_ => SetStatus(false), _ => SelectedSet != null && SelectedSet.IsClosed);

            SetAlsGutschriftCommand = new RelayCommand(_ => SetType(true), _ => SelectedSet != null && !SelectedSet.IsClosed && !SelectedSet.IsCredit);
            SetAlsBelastungCommand  = new RelayCommand(_ => SetType(false), _ => SelectedSet != null && !SelectedSet.IsClosed && SelectedSet.IsCredit);

            LoadLiegenschaften();
            OnPropertyChanged(nameof(Von));
            OnPropertyChanged(nameof(Bis));
        }

        private void LoadLiegenschaften()
        {
            Liegenschaften.Clear();
            foreach (var l in _db.StweLiegenschaftenGetAll())
                Liegenschaften.Add(l);

            if (Liegenschaften.Count == 0)
            {
                StatusText = "Keine Liegenschaften vorhanden.";
                SelectedLiegenschaft = null;
                Sets.Clear();
                return;
            }

            if (SelectedLiegenschaft == null)
                SelectedLiegenschaft = Liegenschaften[0];
            else
                LoadSets();
        }

        // =========================
        // 🔴 HIER IST DER FIX 🔴
        // =========================
        private void LoadSets()
        {
            Sets.Clear();
            SelectedSet = null;

            if (SelectedLiegenschaft == null)
                return;

            foreach (var s in _db.StweSetsGetByLiegenschaft(SelectedLiegenschaft.Id, Von, Bis))
            {
                // UI-NORMALISIERUNG – EINMAL UND NUR HIER
                var absTotal = Math.Abs(s.Betrag);
                var signedTotal = s.IsCredit ? -absTotal : absTotal;

                s.Betrag = signedTotal;
                s.Rest = signedTotal - s.Verteilt;

                Sets.Add(s);
            }

            StatusText = Sets.Count == 0
                ? "Keine Sets im gewählten Zeitraum."
                : $"{Sets.Count} Set(s) gefunden.";

            if (Sets.Count > 0)
                SelectedSet = Sets[0];
        }

        // ===== Aktionen (unverändert) =====

        private void NeuesSetAusTransaktion()
        {
            if (SelectedLiegenschaft == null) return;

            var dlg = new TransaktionAuswahlDialog();
            TrySetOwner(dlg);

            if (dlg.ShowDialog() != true || dlg.Result == null)
                return;

            var t = dlg.Result;
            var titel = string.IsNullOrWhiteSpace(t.Notiz)
                ? (t.AdresseName ?? "(ohne Text)")
                : t.Notiz.Trim();

            try
            {
                _db.StweSetInsert(SelectedLiegenschaft.Id, t.Id, titel);
                LoadSets();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Set erstellen",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SetType(bool isCredit)
        {
            if (SelectedSet == null || SelectedSet.IsClosed) return;

            var res = MessageBox.Show(
                "Set-Typ ändern?\n\nVorhandene Verteilzeilen werden automatisch gespiegelt.",
                "Set-Typ ändern",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            _db.StweSetFlipCreditAndLines(SelectedSet.Id, isCredit);
            LoadSets();
        }

        private void SetVerteilen()
        {
            if (SelectedSet == null) return;
            var dlg = new SetVerteilenDialog(SelectedSet);
            TrySetOwner(dlg);
            dlg.ShowDialog();
            LoadSets();
        }

        private void SetTitelBearbeiten()
        {
            if (SelectedSet == null || SelectedSet.IsClosed) return;

            var dlg = new SchluesselUmbenennenDialog(SelectedSet.Titel);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                _db.StweSetUpdateTitel(SelectedSet.Id, dlg.NeueBezeichnung);
                LoadSets();
            }
        }

        private void SetLoeschen()
        {
            if (SelectedSet == null || SelectedSet.IsClosed) return;
            if (MessageBox.Show("Set löschen?", "Bestätigen", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            _db.StweSetDelete(SelectedSet.Id);
            LoadSets();
        }

        private bool CanCloseSet()
            => SelectedSet != null && !SelectedSet.IsClosed && Math.Abs((double)SelectedSet.Rest) < 0.0001;

        private void SetStatus(bool close)
        {
            if (SelectedSet == null) return;
            _db.StweSetSetClosed(SelectedSet.Id, close);
            LoadSets();
        }

        private void ShowAuswertung()
        {
            if (SelectedLiegenschaft == null) return;
            var dlg = new StweAuswertungDialog(SelectedLiegenschaft);
            TrySetOwner(dlg);
            dlg.ShowDialog();
        }

        private void ShowZaehlerdaten()
        {
            if (SelectedLiegenschaft == null) return;

            // ViewModel für das Zählerdaten-Fenster
            var vm = new ZaehlerdatenViewModel(SelectedLiegenschaft.Id, SelectedLiegenschaft.Name);

            // Window öffnen
            var win = new ZaehlerdatenWindow
            {
                DataContext = vm
            };

            TrySetOwner(win);
            win.ShowDialog();
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
