using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.Views;

namespace MyCoinFlow.ViewModels
{
    public class BudgetsViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<Budgetzeitraum> Budgetzeitraeume { get; set; } = new ObservableCollection<Budgetzeitraum>();

        private Budgetzeitraum? _ausgewaehlterZeitraum;
        public Budgetzeitraum? AusgewaehlterZeitraum
        {
            get => _ausgewaehlterZeitraum;
            set
            {
                _ausgewaehlterZeitraum = value;
                OnPropertyChanged();

                // Buttons neu bewerten
                (BearbeitenZeitraumCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (LoeschenEintragCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (BudgetwerteErfassenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        // Commands
        public ICommand NeuerZeitraumCommand { get; }
        public ICommand BearbeitenZeitraumCommand { get; }
        public ICommand LoeschenEintragCommand { get; }
        public ICommand BudgetwerteErfassenCommand { get; }

        public BudgetsViewModel()
        {
            LadeBudgetzeitraeume();

            NeuerZeitraumCommand = new RelayCommand(_ => NeuenZeitraumHinzufuegen());
            BearbeitenZeitraumCommand = new RelayCommand(_ => ZeitraumBearbeiten(), _ => AusgewaehlterZeitraum != null);
            LoeschenEintragCommand = new RelayCommand(_ => ZeitraumLoeschen(), _ => AusgewaehlterZeitraum != null);

            // NEU: Budgetwerte erfassen
            BudgetwerteErfassenCommand = new RelayCommand(
                _ => BudgetwerteErfassen(),
                _ => AusgewaehlterZeitraum != null
            );
        }

        private void LadeBudgetzeitraeume()
        {
            Budgetzeitraeume.Clear();
            DatabaseService dbService = new DatabaseService();
            var zeitraeume = dbService.LadeBudgetzeitraeume();

            foreach (var z in zeitraeume)
                Budgetzeitraeume.Add(z);
        }

        private void NeuenZeitraumHinzufuegen()
        {
            var dialog = new MyCoinFlow.Views.BudgetzeitraumDialog();

            if (dialog.ShowDialog() == true)
            {
                DatabaseService dbService = new DatabaseService();
                dbService.BudgetzeitraumSpeichern(dialog.BezeichnungBox.Text,
                                                  dialog.StartdatumPicker.SelectedDate ?? DateTime.Now,
                                                  dialog.EnddatumPicker.SelectedDate ?? DateTime.Now,
                                                  dialog.AktivCheckBox.IsChecked ?? false);

                LadeBudgetzeitraeume();
            }
        }

        private void ZeitraumBearbeiten()
        {
            if (AusgewaehlterZeitraum == null) return;

            var dialog = new MyCoinFlow.Views.BudgetzeitraumDialog();

            // Felder vorausfüllen
            dialog.BezeichnungBox.Text = AusgewaehlterZeitraum.Bezeichnung;
            dialog.StartdatumPicker.SelectedDate = AusgewaehlterZeitraum.Startdatum;
            dialog.EnddatumPicker.SelectedDate = AusgewaehlterZeitraum.Enddatum;
            dialog.AktivCheckBox.IsChecked = AusgewaehlterZeitraum.IstAktiv;

            if (dialog.ShowDialog() == true)
            {
                DatabaseService dbService = new DatabaseService();
                dbService.BudgetzeitraumAktualisieren(AusgewaehlterZeitraum.Id,
                                                      dialog.BezeichnungBox.Text,
                                                      dialog.StartdatumPicker.SelectedDate ?? DateTime.Now,
                                                      dialog.EnddatumPicker.SelectedDate ?? DateTime.Now,
                                                      dialog.AktivCheckBox.IsChecked ?? false);

                LadeBudgetzeitraeume();
            }
        }

        // BudgetsViewModel.cs
        // Vollständige Methode zum Ersetzen
        private void ZeitraumLoeschen()
        {
            if (AusgewaehlterZeitraum == null)
                return;

            // Spezialregel: Aktiven Zeitraum nie löschen
            if (AusgewaehlterZeitraum.IstAktiv)
            {
                System.Windows.MessageBox.Show(
                    "Der aktive Budgetzeitraum kann nicht gelöscht werden.\n\n" +
                    "Bitte zuerst einen anderen Zeitraum aktivieren oder diesen deaktivieren.",
                    "Löschen nicht möglich",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            // Bestätigung
            var text = $"{AusgewaehlterZeitraum.Bezeichnung}  ({AusgewaehlterZeitraum.Startdatum:dd.MM.yyyy} – {AusgewaehlterZeitraum.Enddatum:dd.MM.yyyy})";
            var result = System.Windows.MessageBox.Show(
                $"Möchten Sie den Budgetzeitraum „{text}“ wirklich löschen?",
                "Löschen bestätigen",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            try
            {
                var dbService = new MyCoinFlow.Services.DatabaseService();
                dbService.BudgetzeitraumLoeschen(AusgewaehlterZeitraum.Id);
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show("Budgetzeitraum konnte nicht gelöscht werden:\n" + ex.Message,
                    "Löschen fehlgeschlagen",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            // Liste neu laden (dein bestehender Mechanismus)
            LadeBudgetzeitraeume();  // nutzt DatabaseService.LadeBudgetzeitraeume() im Hintergrund
            AusgewaehlterZeitraum = null;
        }


        // NEU: Fenster zur Budgetwerterfassung öffnen
        // NEU: Fenster zur Budgetwerterfassung öffnen (robustes Owner-Handling)
        private void BudgetwerteErfassen()
        {
            if (AusgewaehlterZeitraum == null) return;

            try
            {
                var dlg = new BudgetDetailWindow(AusgewaehlterZeitraum.Id);

                // Robuster Owner: aktives Window > MainWindow; nur setzen, wenn sichtbar
                Window? owner = null;
                try
                {
                    owner = Application.Current?.Windows?.OfType<Window>().FirstOrDefault(w => w.IsActive)
                            ?? Application.Current?.MainWindow;
                }
                catch { /* still */ }

                if (owner != null && owner.IsVisible && !ReferenceEquals(owner, dlg))
                {
                    dlg.Owner = owner;
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                else
                {
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                dlg.ShowDialog();

                // Optional: nach Rückkehr neu laden, falls der Dialog Werte verändert hat
                // LadeBudgetzeitraeume();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unerwarteter Fehler (UI): " + ex.GetType().Name + "\n" + ex.Message,
                    "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
