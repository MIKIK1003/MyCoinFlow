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
    /// Tages-Workflow: Transaktion auswählen -> Set anlegen -> (Verteilung folgt in Schritt 6).
    /// </summary>
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
            set { _selectedSet = value; OnPropertyChanged(); }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        public RelayCommand NeuesSetAusTransaktionCommand { get; }

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

            if (Sets.Count == 0)
                StatusText = "Noch keine Sets. Klicke auf „Set aus Transaktion“.";
            else
                StatusText = $"{Sets.Count} Set(s) gefunden.";

            if (Sets.Count > 0)
                SelectedSet = Sets[0];
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
