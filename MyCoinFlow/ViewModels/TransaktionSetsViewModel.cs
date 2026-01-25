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

        // NEU
        public RelayCommand SetVerteilenCommand { get; }

        public RelayCommand ShowAuswertungCommand { get; }


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
            SetVerteilenCommand = new RelayCommand(_ => SetVerteilen(), _ => SelectedSet != null);

            ShowAuswertungCommand = new RelayCommand(_ => ShowAuswertung(), _ => SelectedLiegenschaft != null);

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

            _db.StweSetInsert(SelectedLiegenschaft.Id, t.Id, titel);

            LoadSets();
        }

        private void SetVerteilen()
        {
            if (SelectedSet == null) return;

            var dlg = new SetVerteilenDialog(SelectedSet);
            TrySetOwner(dlg);
            dlg.ShowDialog();

            // nach Dialog: Sets neu laden (Rest/Verteilt aktualisiert sich über OUTER APPLY)
            LoadSets();
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

        private void ShowAuswertung()
        {
            if (SelectedLiegenschaft == null) return;

            var dlg = new StweAuswertungDialog(SelectedLiegenschaft);
            try
            {
                if (Application.Current?.MainWindow != null)
                    dlg.Owner = Application.Current.MainWindow;
            }
            catch { /* still */ }

            dlg.ShowDialog();
        }

    }
}
