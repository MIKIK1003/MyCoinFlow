using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace MyCoinFlow.ViewModels
{
    public class AccountsViewModel : INotifyPropertyChanged
    {
        // Sammlung für den hierarchischen Baum
        public ObservableCollection<KontoplanKnoten> KontoplanKnotenListe { get; set; } = new();

        // NEU: flache Liste für die Tabellenansicht
        private ObservableCollection<KontoplanEintrag> _kontenListeFlach = new();
        public ObservableCollection<KontoplanEintrag> KontenListeFlach
        {
            get => _kontenListeFlach;
            set { _kontenListeFlach = value; OnPropertyChanged(); }
        }

        // NEU: Umschalter zwischen Baum und Tabelle
        private bool _istTabellenAnsicht;
        public bool IstTabellenAnsicht
        {
            get => _istTabellenAnsicht;
            set { _istTabellenAnsicht = value; OnPropertyChanged(); OnPropertyChanged(nameof(IstBaumAnsicht)); }
        }

        // NEU: Abgeleitet – true, wenn Baum sichtbar sein soll
        public bool IstBaumAnsicht => !_istTabellenAnsicht;

        // Aktuell ausgewählter Knoten im Baum
        private KontoplanKnoten? _ausgewaehlterKnoten;
        public KontoplanKnoten? AusgewaehlterKnoten
        {
            get => _ausgewaehlterKnoten;
            set { _ausgewaehlterKnoten = value; OnPropertyChanged(); }
        }

        // Commands für das UI
        public ICommand NeuerEintragCommand { get; }
        public ICommand BearbeitenEintragCommand { get; }
        public ICommand LoeschenEintragCommand { get; }

        public AccountsViewModel()
        {
            // Daten laden
            LadeKontenplan();

            // Commands
            NeuerEintragCommand = new RelayCommand(_ => NeuenEintragHinzufuegen());
            BearbeitenEintragCommand = new RelayCommand(_ => EintragBearbeiten(), _ => AusgewaehlterKnoten?.OriginalEintrag != null);
            LoeschenEintragCommand = new RelayCommand(_ => EintragLoeschen(), _ => AusgewaehlterKnoten?.OriginalEintrag != null);
        }

        private void LadeKontenplan()
        {
            KontoplanKnotenListe.Clear();

            // wenn Admin-View umbenennt -> Baum neu laden
            UiEvents.ReloadKontenplanRequested += () => LadeKontenplan();

            DatabaseService dbService = new DatabaseService();
            var eintraege = dbService.LadeKontenplan(); // <- liefert flache Kontenliste

            // NEU: flache Liste für DataGrid bereitstellen
            KontenListeFlach = new ObservableCollection<KontoplanEintrag>(eintraege);

            // gruppiert + sortiert nach Art
            var gruppiertNachArt = eintraege
                .GroupBy(e => e.Art ?? string.Empty)
                .OrderBy(g => g.Key, System.StringComparer.CurrentCultureIgnoreCase);

            foreach (var artGruppe in gruppiertNachArt)
            {
                var artKnoten = new KontoplanKnoten(string.IsNullOrEmpty(artGruppe.Key) ? "(Keine Art)" : artGruppe.Key);

                // gruppiert + sortiert nach Gruppe
                var gruppiertNachGruppe = artGruppe
                    .GroupBy(e => e.Gruppe ?? string.Empty)
                    .OrderBy(g => g.Key, System.StringComparer.CurrentCultureIgnoreCase);

                foreach (var gruppe in gruppiertNachGruppe)
                {
                    var gruppenKnoten = new KontoplanKnoten(string.IsNullOrEmpty(gruppe.Key) ? "(Keine Gruppe)" : gruppe.Key);

                    // gruppiert + sortiert nach Untergruppe
                    var gruppiertNachUntergruppe = gruppe
                        .GroupBy(e => string.IsNullOrWhiteSpace(e.Untergruppe) ? "" : e.Untergruppe.Trim())
                        .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

                    foreach (var untergruppe in gruppiertNachUntergruppe)
                    {
                        if (!string.IsNullOrEmpty(untergruppe.Key))
                        {
                            var untergruppenKnoten = new KontoplanKnoten(untergruppe.Key);

                            // Konten (Details) sortiert: zuerst nach Nummer, dann nach Detail
                            var detailsSortiert = untergruppe
                                .OrderBy(d => d.Kontonummer)
                                .ThenBy(d => d.Detail);

                            foreach (var detail in detailsSortiert)
                            {
                                if (!string.IsNullOrEmpty(detail.Detail))
                                {
                                    var detailKnoten = new KontoplanKnoten(detail.Detail, detail);
                                    untergruppenKnoten.Kinder.Add(detailKnoten);
                                }
                            }

                            gruppenKnoten.Kinder.Add(untergruppenKnoten);
                        }
                        else
                        {
                            // ohne Untergruppe: Konten direkt unter Gruppe – ebenfalls sortieren
                            var detailsOhneUntergruppe = untergruppe
                                .OrderBy(d => d.Kontonummer)
                                .ThenBy(d => d.Detail);

                            foreach (var detail in detailsOhneUntergruppe)
                            {
                                var detailKnoten = new KontoplanKnoten(detail.Detail ?? "(Kein Detail)", detail);
                                gruppenKnoten.Kinder.Add(detailKnoten);
                            }
                        }
                    }

                    artKnoten.Kinder.Add(gruppenKnoten);
                }

                KontoplanKnotenListe.Add(artKnoten);
            }
        }

        private void NeuenEintragHinzufuegen()
        {
            try
            {
                var dialog = new MyCoinFlow.Views.NeuerEintragDialog();

                // Owner robust ermitteln
                System.Windows.Window? owner = null;
                try
                {
                    owner = System.Windows.Application.Current?.Windows
                                .OfType<System.Windows.Window>()
                                .FirstOrDefault(w => w.IsActive)
                         ?? System.Windows.Application.Current?.MainWindow;
                }
                catch { }

                if (owner != null && !ReferenceEquals(owner, dialog))
                    dialog.Owner = owner;
                else
                    dialog.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

                if (dialog.ShowDialog() == true)
                {
                    DatabaseService dbService = new DatabaseService();
                    dbService.NeuenKontoplanEintragSpeichern(dialog.Kontonummer, dialog.Art, dialog.Gruppe, dialog.Untergruppe, dialog.Detail);
                    LadeKontenplan();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Dialogfehler (Neuer Eintrag): {ex.Message}", "Kontenplan",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EintragBearbeiten()
        {
            try
            {
                if (AusgewaehlterKnoten?.OriginalEintrag == null)
                    return;

                var eintrag = AusgewaehlterKnoten.OriginalEintrag;
                var dialog = new MyCoinFlow.Views.NeuerEintragDialog(eintrag)
                {
                    // Vorbefüllung (wie bisher)
                };
                dialog.KontonummerBox.Text = eintrag.Kontonummer.ToString();
                dialog.ArtComboBox.Text = eintrag.Art;
                dialog.GruppeComboBox.Text = eintrag.Gruppe;
                dialog.UntergruppeComboBox.Text = eintrag.Untergruppe;
                dialog.DetailBox.Text = eintrag.Detail;

                // Owner robust ermitteln
                System.Windows.Window? owner = null;
                try
                {
                    owner = System.Windows.Application.Current?.Windows
                                .OfType<System.Windows.Window>()
                                .FirstOrDefault(w => w.IsActive)
                         ?? System.Windows.Application.Current?.MainWindow;
                }
                catch { }

                if (owner != null && !ReferenceEquals(owner, dialog))
                    dialog.Owner = owner;
                else
                    dialog.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

                if (dialog.ShowDialog() == true)
                {
                    DatabaseService dbService = new DatabaseService();
                    dbService.KontenplanEintragAktualisieren(eintrag.Id, dialog.Kontonummer, dialog.Art, dialog.Gruppe, dialog.Untergruppe, dialog.Detail);
                    LadeKontenplan();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Dialogfehler (Bearbeiten): {ex.Message}", "Kontenplan",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void EintragLoeschen()
        {
            // 1) Auswahl prüfen
            var kn = AusgewaehlterKnoten;
            var eintrag = kn?.OriginalEintrag; // KontoplanEintrag hinter dem Knoten
            if (eintrag == null) return;

            // 2) Bestätigen
            var label = $"{eintrag.Kontonummer} {eintrag.Detail}".Trim();
            var ask = MessageBox.Show(
                $"Konto „{label}“ wirklich löschen?",
                "Löschen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (ask != MessageBoxResult.Yes) return;

            // 3) Löschen (DatabaseService zeigt ggf. freundliche Blocker-Meldungen)
            try
            {
                var db = new DatabaseService();
                db.KontenplanEintragLoeschen(eintrag.Id);
            }
            catch
            {
                // DatabaseService hat bereits eine Meldung gezeigt; kein Programmstop hier.
            }

            // 4) Liste sofort korrekt neu aufbauen (kein optimistisches Remove)
            LadeKontenplan();

            // 5) Auswahl & Commands refreshen
            AusgewaehlterKnoten = null;
            (BearbeitenEintragCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (LoeschenEintragCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }


        // INotifyPropertyChanged Standard
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
