using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using MyCoinFlow.Models;      // KontenUnterGruppe
using MyCoinFlow.Helpers;     // RelayCommand, UiEvents
using MyCoinFlow.Services;    // DatabaseService
using System.Linq;

namespace MyCoinFlow.ViewModels
{
    public class KontenUnterGruppeViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<KontenUnterGruppe> KontenUnterGruppen { get; set; } = new();

        private KontenUnterGruppe? _ausgewaehlteUnterGruppe;
        public KontenUnterGruppe? AusgewaehlteUnterGruppe
        {
            get => _ausgewaehlteUnterGruppe;
            set
            {
                _ausgewaehlteUnterGruppe = value;
                BearbeiteteBezeichnung = value?.Bezeichnung ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BearbeiteteBezeichnung));
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
                (BearbeitenCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (HinzufuegenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ICommand HinzufuegenCommand { get; }
        public ICommand LoeschenCommand { get; }
        public ICommand BearbeitenCommand { get; }

        public KontenUnterGruppeViewModel()
        {
            HinzufuegenCommand = new RelayCommand(
                _ => Hinzufuegen(),
                _ => !string.IsNullOrWhiteSpace(NeueBezeichnung));

            LoeschenCommand = new RelayCommand(
                _ => Loeschen(),
                _ => AusgewaehlteUnterGruppe != null);

            BearbeitenCommand = new RelayCommand(
                _ => Bearbeiten(),
                _ => AusgewaehlteUnterGruppe != null
                     && !string.IsNullOrWhiteSpace(BearbeiteteBezeichnung)
                     && !string.Equals(AusgewaehlteUnterGruppe?.Bezeichnung, BearbeiteteBezeichnung.Trim(), StringComparison.CurrentCulture));

            LadeDaten();
        }

        private void LadeDaten()
        {
            KontenUnterGruppen.Clear();

            var db = new DatabaseService();
            var list = db.LadeKontenUnterGruppen();

            foreach (var ug in list
                .OrderBy(x => TrailingNumberOrMax(x?.Bezeichnung))
                .ThenBy(x => x?.Bezeichnung ?? string.Empty, StringComparer.CurrentCultureIgnoreCase))
            {
                KontenUnterGruppen.Add(ug);
            }
        }

        private static int TrailingNumberOrMax(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return int.MaxValue;

            text = text.Trim();
            int i = text.Length - 1;

            while (i >= 0 && char.IsDigit(text[i]))
                i--;

            if (i == text.Length - 1) return int.MaxValue;

            var numStr = text.Substring(i + 1);
            return int.TryParse(numStr, out var n) ? n : int.MaxValue;
        }

        private void Hinzufuegen()
        {
            var text = NeueBezeichnung?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            var db = new DatabaseService();
            db.SpeichereKontenUnterGruppe(text);

            NeueBezeichnung = string.Empty;
            OnPropertyChanged(nameof(NeueBezeichnung));
            LadeDaten();
        }

        private void Loeschen()
        {
            if (AusgewaehlteUnterGruppe is null) return;

            var ask = MessageBox.Show(
                $"Soll die Untergruppe „{AusgewaehlteUnterGruppe.Bezeichnung}“ wirklich gelöscht werden?\n" +
                "Hinweis: Konten im Kontenplan behalten ihren Text – es erfolgt kein Automatik-Umbennen, nur die Stammdatensatz-Zeile wird gelöscht.",
                "Löschen bestätigen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (ask != MessageBoxResult.Yes) return;

            var db = new DatabaseService();
            db.LoescheKontenUnterGruppe(AusgewaehlteUnterGruppe.Id);

            LadeDaten();
            // Baum muss hier nicht neu geladen werden (Texte im Kontenplan bleiben).
        }

        private void Bearbeiten()
        {
            if (AusgewaehlteUnterGruppe is null) return;

            var oldName = AusgewaehlteUnterGruppe.Bezeichnung;
            var newName = BearbeiteteBezeichnung?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newName) ||
                string.Equals(oldName, newName, StringComparison.CurrentCulture))
                return;

            var ask = MessageBox.Show(
                $"Soll „{oldName}“ in „{newName}“ umbenannt werden?\n\n" +
                "Diese Änderung wirkt sich auf:\n" +
                "• die Stammdaten-Tabelle KontenUnterGruppe (Bezeichnung)\n" +
                "• ALLE betroffenen Zeilen im Kontenplan (Feld Untergruppe)\n" +
                "aus. Fortfahren?",
                "Umbenennen bestätigen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (ask != MessageBoxResult.Yes) return;

            try
            {
                var db = new DatabaseService();

                // 1) Stammdaten + Kontenplan in EINER Transaktion umbenennen
                db.RenameKontenUnterGruppe(oldName, newName);

                // 2) UI aktualisieren
                MessageBox.Show("Untergruppe wurde erfolgreich umbenannt und im Kontenplan aktualisiert.", "Erfolg",
                                MessageBoxButton.OK, MessageBoxImage.Information);

                // Stammliste neu laden
                LadeDaten();

                // Auswahl + Eingabefeld zurücksetzen (optional)
                AusgewaehlteUnterGruppe = null;
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
