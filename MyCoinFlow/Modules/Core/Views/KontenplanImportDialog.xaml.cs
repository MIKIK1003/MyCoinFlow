using Microsoft.Win32;
using MyCoinFlow.Importing;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class KontenplanImportDialog : Window
    {
        private List<KontenplanExcelImporter.PreviewRow> _preview = new();
        private readonly KontenplanExcelImporter _importer = new();
        private readonly DatabaseService _db = new();

        public KontenplanImportDialog()
        {
            InitializeComponent();
            PreviewGrid.ItemsSource = _preview;
            LoadZeitraeume();
            UpdateSummary();
        }

        private void LoadZeitraeume()
        {
            try
            {
                var z = _db.LadeBudgetzeitraeume()
                           .OrderByDescending(x => x.IstAktiv)
                           .ThenByDescending(x => x.Startdatum)
                           .ToList();

                // Anzeige hübscher: "Bezeichnung (dd.MM.yyyy – dd.MM.yyyy) [aktiv]"
                foreach (var e in z)
                {
                    var tag = e.IstAktiv ? " [aktiv]" : "";
                    e.Bezeichnung = $"{e.Bezeichnung} ({e.Startdatum:dd.MM.yyyy} – {e.Enddatum:dd.MM.yyyy}){tag}";
                }

                ZeitraumBox.ItemsSource = z;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Budgetzeiträume konnten nicht geladen werden:\n" + ex.Message,
                    "Budget", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DateiWaehlen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Kontenplan Excel wählen",
                Filter = "Excel-Dateien (*.xlsx;*.xls)|*.xlsx;*.xls|Alle Dateien (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dlg.ShowDialog() == true)
            {
                PfadBox.Text = dlg.FileName;
                LoadPreview(dlg.FileName);
            }
        }

        private void Vorschau_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PfadBox.Text) || !File.Exists(PfadBox.Text))
            {
                MessageBox.Show("Bitte eine Excel-Datei wählen.");
                return;
            }
            LoadPreview(PfadBox.Text);
        }

        private void LoadPreview(string path)
        {
            try
            {
                var res = _importer.Analyze(path);
                _preview = res.Rows;
                PreviewGrid.ItemsSource = _preview;
                PreviewGrid.Items.Refresh();
                UpdateSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Vorschau fehlgeschlagen:\n" + ex.Message,
                    "Import", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateSummary()
        {
            int total = _preview.Count;
            int errors = _preview.Count(r => r.HasError);
            int dup = _preview.Count(r => r.DuplicateKontoInFile);
            int artCreate = _preview.Count(r => !r.ExistsArt && !string.IsNullOrWhiteSpace(r.ArtBezeichnung));
            int grpCreate = _preview.Count(r => !r.ExistsGruppe && !string.IsNullOrWhiteSpace(r.Gruppe));
            int ugrCreate = _preview.Count(r => !r.ExistsUntergruppe && !string.IsNullOrWhiteSpace(r.Untergruppe));
            int accCreate = _preview.Count(r => !r.ExistsKonto && r.Konto.HasValue);
            int withBudget = _preview.Count(r => r.BudgetJ.HasValue);

            var sb = new StringBuilder();
            sb.Append($"Zeilen: {total}");
            if (errors > 0) sb.Append($"  |  Fehler: {errors}");
            if (dup > 0) sb.Append($"  |  Duplikate (Datei): {dup}");
            sb.Append($"  |  Neu → Art:{artCreate} / Gruppe:{grpCreate} / Untergruppe:{ugrCreate} / Konto:{accCreate}");
            if (withBudget > 0) sb.Append($"  |  BudgetJ: {withBudget} Werte");

            SummaryText.Text = sb.ToString();
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_preview.Count == 0)
            {
                MessageBox.Show("Keine Vorschau-Daten. Bitte Excel wählen und Vorschau laden.");
                return;
            }

            if (BudgetChk.IsChecked == true && ZeitraumBox.SelectedValue == null)
            {
                MessageBox.Show("Bitte einen Budgetzeitraum wählen oder Budget-Import deaktivieren.");
                return;
            }

            if (_preview.Any(r => r.HasError))
            {
                var ask = MessageBox.Show("Es liegen fehlerhafte Zeilen vor. Diese werden übersprungen.\nFortfahren?",
                    "Bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ask != MessageBoxResult.Yes) return;
            }

            try
            {
                bool onlyNew = OnlyNewBox.IsChecked == true;
                int? zeitraumId = BudgetChk.IsChecked == true ? (int?)ZeitraumBox.SelectedValue : null;

                var result = _importer.ImportFromPreview(_preview, onlyNew, zeitraumId);

                MessageBox.Show(
                    $"Import abgeschlossen.\n" +
                    $"- Zeilen verarbeitet: {result.RowsProcessed}\n" +
                    $"- Übersprungen:       {result.RowsSkipped}\n" +
                    $"- Fehlerhafte Zeilen:  {result.RowsWithErrors}\n\n" +
                    $"Neu angelegt:\n" +
                    $"  • Kontenarten:       {result.ArtenNeu}\n" +
                    $"  • Gruppen:           {result.GruppenNeu}\n" +
                    $"  • Untergruppen:      {result.UntergruppenNeu}\n" +
                    $"  • Konten (Plan):     {result.KontenNeu}\n" +
                    (zeitraumId.HasValue ? $"\nBudgetwerte gesetzt (Zeitraum #{zeitraumId}): {result.BudgetsGesetzt}" : ""),
                    "Import", MessageBoxButton.OK, MessageBoxImage.Information);

                // Optional: nach Erfolg schließen
                // Close();

                // Optional: Kontenplan-Ansichten aktualisieren (falls Event vorhanden)
                try
                {
                    var uiEventsType = Type.GetType("MyCoinFlow.Helpers.UiEvents, MyCoinFlow");
                    var method = uiEventsType?.GetMethod("ReloadKontenplanRequested");
                    method?.Invoke(null, null);
                }
                catch { /* ignorieren */ }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Import fehlgeschlagen:\n" + ex.Message,
                    "Import", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BudgetChk_Toggled(object sender, RoutedEventArgs e)
        {
            ZeitraumBox.IsEnabled = BudgetChk.IsChecked == true;
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e) => Close();
    }
}
