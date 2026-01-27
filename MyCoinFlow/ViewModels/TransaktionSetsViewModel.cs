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
            set { _selectedSet = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
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

        public RelayCommand SetTitelBearbeitenCommand { get; }
        public RelayCommand SetLoeschenCommand { get; }
        public RelayCommand SetAbschliessenCommand { get; }
        public RelayCommand SetWiederOeffnenCommand { get; }

        // NEU: Set-Typ setzen (Gutschrift/Belastung)
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

            NeuesSetAusTransaktionCommand = new RelayCommand(_ => NeuesSetAusTransaktion(), _ => SelectedLiegenschaft != null);

            // Verteilung/Anzeige darf auch bei Closed geöffnet werden (Dialog ist dann read-only).
            SetVerteilenCommand = new RelayCommand(_ => SetVerteilen(), _ => SelectedSet != null);

            ShowAuswertungCommand = new RelayCommand(_ => ShowAuswertung(), _ => SelectedLiegenschaft != null);

            SetTitelBearbeitenCommand = new RelayCommand(_ => SetTitelBearbeiten(), _ => SelectedSet != null && !SelectedSet.IsClosed);
            SetLoeschenCommand = new RelayCommand(_ => SetLoeschen(), _ => SelectedSet != null && !SelectedSet.IsClosed);

            SetAbschliessenCommand = new RelayCommand(_ => SetStatus(true), _ => CanCloseSet());
            SetWiederOeffnenCommand = new RelayCommand(_ => SetStatus(false), _ => SelectedSet != null && SelectedSet.IsClosed);

            SetAlsGutschriftCommand = new RelayCommand(_ => SetType(true), _ => SelectedSet != null && !SelectedSet.IsClosed && SelectedSet.IsCredit == false);
            SetAlsBelastungCommand = new RelayCommand(_ => SetType(false), _ => SelectedSet != null && !SelectedSet.IsClosed && SelectedSet.IsCredit == true);

            LoadLiegenschaften();
        }

        private void LoadLiegenschaften()
        {
            Liegenschaften.Clear();
            foreach (var l in _db.StweLiegenschaftenGetAll())
                Liegenschaften.Add(l);

            if (Liegenschaften.Count == 0)
            {
                StatusText = "Keine Liegenschaften vorhanden. Bitte zuerst unter „Liegenschaften“ Stammdaten erfassen.";
                SelectedLiegenschaft = null;
                Sets.Clear();
                return;
            }

            if (SelectedLiegenschaft == null)
                SelectedLiegenschaft = Liegenschaften[0];
            else
                LoadSets();
        }

        private void LoadSets()
        {
            Sets.Clear();
            SelectedSet = null;

            if (SelectedLiegenschaft == null) return;

            foreach (var s in _db.StweSetsGetByLiegenschaft(SelectedLiegenschaft.Id))
                Sets.Add(s);

            StatusText = Sets.Count == 0 ? "Noch keine Sets. Klicke auf „Set aus Transaktion“." : $"{Sets.Count} Set(s) gefunden.";
            if (Sets.Count > 0) SelectedSet = Sets[0];
        }

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

            // IsCredit startet bewusst mit false (Belastung). Falls es eine Gutschrift ist:
            // im Grid markieren -> "Als Gutschrift" klicken.
            _db.StweSetInsert(SelectedLiegenschaft.Id, t.Id, titel);

            LoadSets();
        }

        private void SetType(bool isCredit)
        {
            if (SelectedSet == null) return;
            if (SelectedSet.IsClosed)
            {
                MessageBox.Show("Dieses Set ist geschlossen. Bitte zuerst „Wieder öffnen“.",
                    "Set-Typ ändern", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var text = isCredit
                ? "Als Gutschrift markieren?\n\nAb jetzt werden Verteilzeilen NEGATIV geführt."
                : "Als Belastung markieren?\n\nAb jetzt werden Verteilzeilen POSITIV geführt.";

            var res = MessageBox.Show(text, "Set-Typ ändern", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            _db.StweSetFlipCreditAndLines(SelectedSet.Id, isCredit);

            LoadSets();
            StatusText = isCredit ? "Set als Gutschrift markiert." : "Set als Belastung markiert.";
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
            if (SelectedSet == null) return;
            if (SelectedSet.IsClosed)
            {
                MessageBox.Show("Dieses Set ist geschlossen. Bitte zuerst „Wieder öffnen“.",
                    "Titel ändern", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SchluesselUmbenennenDialog(SelectedSet.Titel);
            TrySetOwner(dlg);
            dlg.Title = "Set-Titel ändern";

            if (dlg.ShowDialog() == true)
            {
                var neu = (dlg.NeueBezeichnung ?? "").Trim();
                _db.StweSetUpdateTitel(SelectedSet.Id, neu);
                LoadSets();
                StatusText = "Set-Titel aktualisiert.";
            }
        }

        private void SetLoeschen()
        {
            if (SelectedSet == null) return;

            if (SelectedSet.IsClosed)
            {
                MessageBox.Show("Dieses Set ist geschlossen. Bitte zuerst „Wieder öffnen“.",
                    "Löschen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var res = MessageBox.Show(
                $"Set wirklich löschen?\n\n{SelectedSet.Titel}\nDatum: {SelectedSet.Datum:dd.MM.yyyy}\nTotal: {SelectedSet.Betrag:N2}",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;

            _db.StweSetDelete(SelectedSet.Id);
            LoadSets();
            StatusText = "Set gelöscht.";
        }

        private bool CanCloseSet()
        {
            if (SelectedSet == null) return false;
            if (SelectedSet.IsClosed) return false;

            return Math.Abs((double)SelectedSet.Rest) < 0.0001;
        }

        private void SetStatus(bool close)
        {
            if (SelectedSet == null) return;

            if (close)
            {
                if (!CanCloseSet())
                {
                    MessageBox.Show("Set kann nur abgeschlossen werden, wenn Rest = 0.00 ist.",
                        "Abschliessen", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var res = MessageBox.Show(
                    "Set abschliessen? Danach sind Änderungen nur nach „Wieder öffnen“ möglich.",
                    "Abschliessen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes) return;

                _db.StweSetSetClosed(SelectedSet.Id, true);
                LoadSets();
                StatusText = "Set abgeschlossen.";
            }
            else
            {
                var res = MessageBox.Show(
                    "Set wieder öffnen? Danach sind Änderungen wieder möglich.",
                    "Wieder öffnen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes) return;

                _db.StweSetSetClosed(SelectedSet.Id, false);
                LoadSets();
                StatusText = "Set wieder geöffnet.";
            }
        }

        private void ShowAuswertung()
        {
            if (SelectedLiegenschaft == null) return;

            var dlg = new StweAuswertungDialog(SelectedLiegenschaft);
            TrySetOwner(dlg);
            dlg.ShowDialog();
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
