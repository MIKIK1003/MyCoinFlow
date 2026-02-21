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
    public class AccountsViewModel : BaseViewModel
    {
        private readonly DatabaseService _db = new();

        // ===== Commands (werden im XAML gebunden) =====
        public ICommand NeuerEintragCommand { get; }
        public ICommand BearbeitenEintragCommand { get; }
        public ICommand LoeschenEintragCommand { get; }

        // ===== Ansicht =====
        private bool _istTabellenAnsicht = false; // Standard: Baum
        public bool IstTabellenAnsicht
        {
            get => _istTabellenAnsicht;
            set
            {
                if (_istTabellenAnsicht == value) return;
                _istTabellenAnsicht = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IstBaumAnsicht));

                // Buttons sollen sofort korrekt aktiv/deaktiv sein
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IstBaumAnsicht => !IstTabellenAnsicht;

        // ===== Daten =====
        private ObservableCollection<KontoplanEintrag> _kontenListeFlach = new();
        public ObservableCollection<KontoplanEintrag> KontenListeFlach
        {
            get => _kontenListeFlach;
            private set { _kontenListeFlach = value; OnPropertyChanged(); }
        }

        private ObservableCollection<KontoplanKnoten> _kontoplanKnotenListe = new();
        public ObservableCollection<KontoplanKnoten> KontoplanKnotenListe
        {
            get => _kontoplanKnotenListe;
            private set { _kontoplanKnotenListe = value; OnPropertyChanged(); }
        }

        // ===== Selektion (Grid + Tree) =====

        // Wird vom Code-behind (AccountsGrid_SelectionChanged) gesetzt, wenn vorhanden.
        private KontoplanEintrag? _ausgewaehlterEintrag;
        public KontoplanEintrag? AusgewaehlterEintrag
        {
            get => _ausgewaehlterEintrag;
            set
            {
                if (Equals(_ausgewaehlterEintrag, value)) return;
                _ausgewaehlterEintrag = value;
                OnPropertyChanged();

                CommandManager.InvalidateRequerySuggested();
            }
        }

        // Wird vom TreeView_SelectedItemChanged gesetzt.
        private object? _ausgewaehlterKnoten;
        public object? AusgewaehlterKnoten
        {
            get => _ausgewaehlterKnoten;
            set
            {
                if (Equals(_ausgewaehlterKnoten, value)) return;
                _ausgewaehlterKnoten = value;
                OnPropertyChanged();

                // Falls ein Leaf-Knoten gewählt wird, kommt hierüber die Selektion rein.
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public AccountsViewModel()
        {
            // 1) Daten laden
            LadeKontenplan();

            // 2) Commands verdrahten (das war "verloren gegangen")
            NeuerEintragCommand = new RelayCommand(_ => NeuerEintrag());
            BearbeitenEintragCommand = new RelayCommand(p => BearbeitenEintrag(p), p => CanBearbeitenOderLoeschen(p));
            LoeschenEintragCommand = new RelayCommand(p => LoeschenEintrag(p), p => CanBearbeitenOderLoeschen(p));

            // 3) Zentralen Reload-Hook aktivieren (UiEvents.cs Kommentar sagt: AccountsViewModel hört darauf)
            UiEvents.ReloadKontenplanRequested -= OnReloadKontenplanRequested; // defensiv: doppelte Registrierung verhindern
            UiEvents.ReloadKontenplanRequested += OnReloadKontenplanRequested;
        }

        private void OnReloadKontenplanRequested()
        {
            LadeKontenplan();
            CommandManager.InvalidateRequerySuggested();
        }

        private void LadeKontenplan()
        {
            var daten = _db.LadeKontenplan();

            KontenListeFlach = new ObservableCollection<KontoplanEintrag>(daten);
            KontoplanKnotenListe = new ObservableCollection<KontoplanKnoten>(BuildTree(daten));
        }

        // ===== Command-Implementierung =====

        private void NeuerEintrag()
        {
            var dlg = new NeuerEintragDialog(null);
            SetOwnerSafe(dlg);

            bool? ok = dlg.ShowDialog();
            if (ok == true)
            {
                LadeKontenplan();
                UiEvents.RaiseReloadKontenplan();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void BearbeitenEintrag(object? parameter)
        {
            var eintrag = ResolveSelectedEintrag(parameter);

            var p = parameter?.GetType().FullName ?? "(null)";
            var n = AusgewaehlterKnoten?.GetType().FullName ?? "(null)";
            var e = AusgewaehlterEintrag?.Id.ToString() ?? "(null)";
            MessageBox.Show($"PARAM={p}\nNODE={n}\nAusgewEntryId={e}", "DEBUG Auswahl");



            MessageBox.Show(
    $"Bearbeiten: Id={eintrag?.Id}, Nr={eintrag?.Kontonummer}, Detail={eintrag?.Detail}",
    "DEBUG",
    MessageBoxButton.OK,
    MessageBoxImage.Information);


            if (eintrag == null) return;

            var dlg = new NeuerEintragDialog(eintrag);
            SetOwnerSafe(dlg);

            bool? ok = dlg.ShowDialog();
            if (ok == true)
            {
                LadeKontenplan();
                UiEvents.RaiseReloadKontenplan();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void LoeschenEintrag(object? parameter)
        {
            var eintrag = ResolveSelectedEintrag(parameter);
            if (eintrag == null) return;

            var confirm = MessageBox.Show(
                $"Konto wirklich löschen?\n\n{eintrag.Kontonummer:D4} — {eintrag.Detail}",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            // DatabaseService.KontenplanEintragLoeschen(...) enthält bereits die FK/Mapping-Checks.
            _db.KontenplanEintragLoeschen(eintrag.Id);

            LadeKontenplan();
            UiEvents.RaiseReloadKontenplan();
            CommandManager.InvalidateRequerySuggested();
        }

        private bool CanBearbeitenOderLoeschen(object? parameter)
        {
            var eintrag = ResolveSelectedEintrag(parameter);
            return eintrag != null && eintrag.Id > 0;
        }

        /// <summary>
        /// Nimmt bevorzugt den CommandParameter (Grid SelectedItem),
        /// fällt zurück auf VM-Selektion (AusgewaehlterEintrag / Tree Leaf-Knoten).
        /// </summary>
        private KontoplanEintrag? ResolveSelectedEintrag(object? parameter)
        {
            if (parameter is KontoplanEintrag pEntry) return pEntry;

            if (AusgewaehlterEintrag != null) return AusgewaehlterEintrag;

            // TreeView selektiert KontoplanKnoten: Leaf-Knoten trägt OriginalEintrag
            if (AusgewaehlterKnoten is KontoplanKnoten knoten)
                return knoten.OriginalEintrag;

            // Fallback: falls irgendwo doch direkt ein KontoplanEintrag gesetzt wird
            if (AusgewaehlterKnoten is KontoplanEintrag kEntry)
                return kEntry;

            return null;
        }

        private static void SetOwnerSafe(Window dlg)
        {
            var owner = Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive)
                ?? Application.Current?.MainWindow;

            if (owner != null && !ReferenceEquals(owner, dlg))
                dlg.Owner = owner;
        }

        /// <summary>
        /// Baut den Kontenplan-Baum: Art → Gruppe → Untergruppe → Konto
        /// </summary>
        private static KontoplanKnoten[] BuildTree(System.Collections.Generic.List<KontoplanEintrag> daten)
        {
            return daten
                .GroupBy(k => (k.Art ?? "").Trim())
                .OrderBy(g => g.Key)
                .Select(gArt =>
                {
                    var artNode = new KontoplanKnoten(
                        string.IsNullOrWhiteSpace(gArt.Key) ? "(ohne Art)" : gArt.Key
                    );

                    foreach (var gGrp in gArt.GroupBy(k => (k.Gruppe ?? "").Trim()).OrderBy(g => g.Key))
                    {
                        var grpNode = new KontoplanKnoten(
                            string.IsNullOrWhiteSpace(gGrp.Key) ? "(ohne Gruppe)" : gGrp.Key
                        );
                        artNode.Kinder.Add(grpNode);

                        foreach (var gUg in gGrp.GroupBy(k => (k.Untergruppe ?? "").Trim()).OrderBy(g => g.Key))
                        {
                            var ugNode = new KontoplanKnoten(
                                string.IsNullOrWhiteSpace(gUg.Key) ? "(ohne Untergruppe)" : gUg.Key
                            );
                            grpNode.Kinder.Add(ugNode);

                            foreach (var konto in gUg.OrderBy(k => k.Kontonummer).ThenBy(k => k.Detail))
                            {
                                var leaf = new KontoplanKnoten(
                                    bezeichnung: konto.Detail ?? "(ohne Detail)",
                                    originalEintrag: konto
                                );

                                ugNode.Kinder.Add(leaf);
                            }
                        }
                    }

                    return artNode;
                })
                .ToArray();
        }
    }
}