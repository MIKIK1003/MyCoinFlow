using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using MyCoinFlow.Models;
using MyCoinFlow.Helpers;   // UiEvents
using MyCoinFlow.Services; // DatabaseService
using System.Linq;

namespace MyCoinFlow.ViewModels
{
    public class KontenArtViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<KontenArt> KontenArten { get; set; } = new();

        private KontenArt? _ausgewaehlteArt;
        public KontenArt? AusgewaehlteArt
        {
            get => _ausgewaehlteArt;
            set
            {
                _ausgewaehlteArt = value;
                // Auswahl in Bearbeitungsfeld übernehmen
                BearbeiteteBezeichnung = value?.Bezeichnung ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BearbeiteteBezeichnung));
                // CanExecute neu bewerten
                (BearbeitenCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (LoeschenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string NeueBezeichnung { get; set; } = string.Empty;

        private string _bearbeiteteBezeichnung = string.Empty;
        public string BearbeiteteBezeichnung
        {
            get => _bearbeiteteBezeichnung;
            set
            {
                _bearbeiteteBezeichnung = value;
                OnPropertyChanged();
                // CanExecute neu bewerten
                (BearbeitenCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (HinzufuegenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ICommand HinzufuegenCommand { get; }
        public ICommand LoeschenCommand { get; }
        public ICommand BearbeitenCommand { get; }

        public KontenArtViewModel()
        {
            HinzufuegenCommand = new RelayCommand(
                _ => Hinzufuegen(),
                _ => !string.IsNullOrWhiteSpace(NeueBezeichnung));

            LoeschenCommand = new RelayCommand(
                _ => Loeschen(),
                _ => AusgewaehlteArt != null);

            BearbeitenCommand = new RelayCommand(
                _ => Bearbeiten(),
                _ => AusgewaehlteArt != null
                     && !string.IsNullOrWhiteSpace(BearbeiteteBezeichnung)
                     && !string.Equals(AusgewaehlteArt?.Bezeichnung, BearbeiteteBezeichnung.Trim(), StringComparison.CurrentCulture));

            LadeDaten();
        }

        private void LadeDaten()
        {
            KontenArten.Clear();

            var db = new DatabaseService();
            var list = db.LadeKontenArten();

            foreach (var art in list
                .OrderBy(a => TrailingNumberOrMax(a?.Bezeichnung))
                .ThenBy(a => a?.Bezeichnung ?? string.Empty, StringComparer.CurrentCultureIgnoreCase))
            {
                KontenArten.Add(art);
            }
        }

        private static int TrailingNumberOrMax(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return int.MaxValue;

            text = text.Trim();
            int i = text.Length - 1;

            // von rechts: alle Ziffern einsammeln
            while (i >= 0 && char.IsDigit(text[i]))
                i--;

            // keine Ziffer am Ende gefunden
            if (i == text.Length - 1) return int.MaxValue;

            var numStr = text.Substring(i + 1);
            return int.TryParse(numStr, out var n) ? n : int.MaxValue;
        }

        private void Hinzufuegen()
        {
            var text = NeueBezeichnung?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            var db = new DatabaseService();
            db.SpeichereKontenArt(text);

            NeueBezeichnung = string.Empty;
            OnPropertyChanged(nameof(NeueBezeichnung));
            LadeDaten();
        }

        private void Loeschen()
        {
            if (AusgewaehlteArt is null) return;

            var ask = MessageBox.Show(
                $"Soll die Art „{AusgewaehlteArt.Bezeichnung}“ wirklich gelöscht werden?\n" +
                "Hinweis: Konten im Kontenplan behalten ihren Text – es erfolgt kein Automatik-Umbennen, nur die Stammdatensatz-Zeile wird gelöscht.",
                "Löschen bestätigen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (ask != MessageBoxResult.Yes) return;

            var db = new DatabaseService();
            db.LoescheKontenArt(AusgewaehlteArt.Id);

            LadeDaten();
            // Baum muss hier nicht neu geladen werden, da Kontenplan-Texte unverändert bleiben
        }

        private void Bearbeiten()
        {
            if (AusgewaehlteArt is null) return;

            var oldName = AusgewaehlteArt.Bezeichnung;
            var newName = BearbeiteteBezeichnung?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newName) ||
                string.Equals(oldName, newName, StringComparison.CurrentCulture))
                return;

            var ask = MessageBox.Show(
                $"Soll „{oldName}“ in „{newName}“ umbenannt werden?\n\n" +
                "Diese Änderung wirkt sich auf:\n" +
                "• die Stammdaten-Tabelle KontenArt (Bezeichnung)\n" +
                "• ALLE betroffenen Zeilen im Kontenplan (Feld Art)\n" +
                "aus. Fortfahren?",
                "Umbenennen bestätigen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (ask != MessageBoxResult.Yes) return;

            try
            {
                var db = new DatabaseService();

                // 1) Stammdaten + Kontenplan in EINER Transaktion umbenennen
                db.RenameKontenArt(oldName, newName);

                // 2) UI aktualisieren
                MessageBox.Show("Art wurde erfolgreich umbenannt und im Kontenplan aktualisiert.", "Erfolg",
                                MessageBoxButton.OK, MessageBoxImage.Information);

                // Stammliste neu laden
                LadeDaten();

                // Auswahl + Eingabefeld zurücksetzen (optional)
                AusgewaehlteArt = null;
                BearbeiteteBezeichnung = string.Empty;

                // 3) Kontenplan-Baum neu laden
                UiEvents.RaiseReloadKontenplan();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Umbenennen: " + ex.Message, "Fehler",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
