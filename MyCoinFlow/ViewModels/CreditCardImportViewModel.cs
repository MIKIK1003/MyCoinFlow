using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.Views;

namespace MyCoinFlow.ViewModels
{
    public class CreditCardImportViewModel : BaseNotify
    {
        private readonly DatabaseService _db = new();

        // --- Daten fürs Grid ---
        public ObservableCollection<CreditCardImportRow> Zeilen { get; } = new();
        public ICollectionView ZeilenView { get; }

        // --- Konto-/Institut-Listen ---
        public ObservableCollection<KontoLookup> KontoListe { get; } = new();           // Anzeige/Zuordnungsdialog
        public ObservableCollection<KontoLookup> KreditkartenKonten { get; } = new();   // Verrechnungskonto
        public ObservableCollection<Geldinstitut> Geldinstitute { get; } = new();       // optional

        // --- Datei ---
        private string? _ausgewaehlteDatei;
        public string? AusgewaehlteDatei
        {
            get => _ausgewaehlteDatei;
            set => SetProperty(ref _ausgewaehlteDatei, value);
        }

        // --- Auswahlen ---
        private int? _ausgleichsKontoId;
        public int? AusgleichsKontoId
        {
            get => _ausgleichsKontoId;
            set
            {
                if (SetProperty(ref _ausgleichsKontoId, value))
                {
                    UpdateAbrechnungssumme();
                    (BuchenCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private int? _ausgewaehltesGeldinstitutId;
        public int? AusgewaehltesGeldinstitutId
        {
            get => _ausgewaehltesGeldinstitutId;
            set => SetProperty(ref _ausgewaehltesGeldinstitutId, value);
        }

        // --- Filter ---
        private string _filterModus = "Alle"; // "Alle" | "Offen" | "Zugewiesen"
        public string FilterModus
        {
            get => _filterModus;
            set
            {
                if (SetProperty(ref _filterModus, value))
                {
                    ZeilenView.Refresh();
                    Recompute();
                }
            }
        }

        // --- Summen ---
        private decimal? _abrechnungssumme;
        public decimal? Abrechnungssumme
        {
            get => _abrechnungssumme;
            set
            {
                if (SetProperty(ref _abrechnungssumme, value))
                    OnPropertyChanged(nameof(DifferenzZurAbrechnung));
            }
        }

        public decimal SummeListe => Zeilen.Sum(z => z.Betrag);
        public decimal SummeZugeordnet => Zeilen.Where(z => z.KontoId.HasValue).Sum(z => z.Betrag);
        public decimal SummeOffen => Zeilen.Where(z => !z.KontoId.HasValue).Sum(z => z.Betrag);
        public decimal DifferenzZurAbrechnung => (Abrechnungssumme ?? 0m) - SummeZugeordnet;

        // --- Zähler ---
        public int AnzahlZuweisbar => Zeilen.Count(z => z.KontoId.HasValue);
        public int AnzahlOffen => Zeilen.Count(z => !z.KontoId.HasValue);

        // --- Commands ---
        public ICommand DateiWaehlenCommand { get; }
        public ICommand ZuweisenCommand { get; }
        public ICommand BuchenCommand { get; }

        public CreditCardImportViewModel()
        {
            // Basis-Commands
            DateiWaehlenCommand = new RelayCommand(_ => DateiWaehlen());
            ZuweisenCommand = new RelayCommand(p => Zuweisen(p as CreditCardImportRow), p => p is CreditCardImportRow);
            BuchenCommand = new RelayCommand(
                                      _ => Buchen(),
                                      _ => Zeilen.Any(z => z.KontoId.HasValue) && AusgleichsKontoId.HasValue);

            // NEU: Batch-/Lösch-Commands
            BatchVerwerfenCommand = new RelayCommand(_ => BatchVerwerfen(), _ => CurrentBatchId.HasValue);
            ZeileLoeschenCommand = new RelayCommand(p => ZeileLoeschen(p as CreditCardImportRow), p => p is CreditCardImportRow);
            BatchesNeuLadenCommand = new RelayCommand(_ => LadeBatches());
            BatchLadenCommand = new RelayCommand(_ => BatchLaden(), _ => AusgewaehlterBatch != null);

            // Lookups laden
            foreach (var k in _db.LadeKontoLookup()) KontoListe.Add(k);

            var kk = _db.LadeKreditkartenKonten();
            foreach (var k in kk) KreditkartenKonten.Add(k);
            if (KreditkartenKonten.Count == 0)
                foreach (var k in KontoListe) KreditkartenKonten.Add(k);

            if (KreditkartenKonten.Count == 1)
            {
                AusgleichsKontoId = KreditkartenKonten[0].Id;
                UpdateAbrechnungssumme(); // Komfort: Saldo sofort anzeigen
            }

            foreach (var g in _db.LadeGeldinstitute()) Geldinstitute.Add(g);

            // View/Filter für die Zeilen
            ZeilenView = CollectionViewSource.GetDefaultView(Zeilen);
            ZeilenView.Filter = ZeilenFilter;

            // WICHTIG: Beim Öffnen KEINE Zeilen laden – nur die Batchliste
            LadeBatches();
        }


        // ================== UI-Aktionen ==================

        private void DateiWaehlen()
        {
            var dlg = new OpenFileDialog { Filter = "Excel (*.xlsx;*.xls)|*.xlsx;*.xls" };
            if (dlg.ShowDialog() != true) return;

            AusgewaehlteDatei = dlg.FileName;

            try
            {
                var rows = _db.LeseCreditCardExcel(AusgewaehlteDatei);

                // 1) Batch anlegen (merkt Verrechnungskonto/Institut zum Zeitpunkt des Imports)
                CurrentBatchId = _db.CreateCcBatch(System.IO.Path.GetFileName(AusgewaehlteDatei),
                                                   AusgleichsKontoId, AusgewaehltesGeldinstitutId);

                LadeBatches();                    // Batch-Liste neu aufbauen
                AusgewaehlterBatch = Batches.FirstOrDefault(b => b.Id == CurrentBatchId);
                BatchLaden();                     // neuen Batch sofort anzeigen (oder Zeile entfernen, wenn du manuell laden willst)


                // 2) Zeilen ins Staging schreiben (inkl. MappingKey, ImportHash, Autokonto per Mapping)
                var (ins, skip, dup) = _db.SaveExcelRowsToStaging(CurrentBatchId.Value, rows);

                // 3) Staging laden & anzeigen
                ReloadStaging();

                // optional Info
                MessageBox.Show($"Importiert: {ins}\nÜbersprungen (keine Belastung): {skip}\nDuplikate: {dup}",
                    "Excel-Import", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Excel konnte nicht importiert werden:\n" + ex.Message,
                    "Import", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Zuweisen(CreditCardImportRow? row)
        {
            if (row == null) return;

            var kat = row.Kategorie ?? "(ohne Kategorie)";
            var dlg = new KategorieZuweisenDialog(kat, KontoListe)
            {
                Owner = Application.Current?.MainWindow
            };

            if (dlg.ShowDialog() == true && dlg.AusgewaehlterKontoId.HasValue)
            {
                var kontoId = dlg.AusgewaehlterKontoId.Value;
                var key = _db.BaueMappingSchluessel(row.Beschreibung, row.Haendler, row.Kategorie);

                if (dlg.MappingDauerhaftSpeichern)
                    _db.UpsertKategorieMapping(key, kontoId);  // einziges Mapping-System

                // Staging aktualisieren (diese Zeile + optional alle mit gleichem Key)
                _db.UpdateCcStagingZuordnung(row.Id, key, kontoId, applyToSameKey: dlg.MappingDauerhaftSpeichern);

                // GUI neu laden
                ReloadStaging();
            }
        }

        private void Buchen()
        {
            if (!AusgleichsKontoId.HasValue)
            {
                MessageBox.Show("Bitte ein Verrechnungskonto auswählen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!CurrentBatchId.HasValue)
            {
                MessageBox.Show("Kein Batch vorhanden. Bitte zuerst eine Excel-Datei importieren.",
                    "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var (inserted, skipped, duplicates) = _db.VerbuchenCcStaging(
                CurrentBatchId.Value,
                kreditkartenKontoId: AusgleichsKontoId.Value,
                geldinstitutId: AusgewaehltesGeldinstitutId);

            MessageBox.Show(
                $"Verbucht: {inserted}\nIgnoriert: {skipped}\nDuplikate: {duplicates}",
                "Kreditkarten-Import", MessageBoxButton.OK, MessageBoxImage.Information);

            // Saldo & Liste aktualisieren
            UpdateAbrechnungssumme();
            ReloadStaging();
        }

        public ICommand BatchVerwerfenCommand { get; }
        public ICommand ZeileLoeschenCommand { get; }
                
        private void BatchVerwerfen()
        {
            if (!CurrentBatchId.HasValue) return;

            var ok = MessageBox.Show(
                "Den gesamten Import (alle Staging-Zeilen) verwerfen?\nBereits gebuchte/archivierte Positionen bleiben unangetastet.",
                "Batch verwerfen", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (ok != MessageBoxResult.Yes) return;

            var (deletedStaging, affectedArchive) = _db.DeleteCcBatchAndStaging(CurrentBatchId.Value);

            CurrentBatchId = null;
            Zeilen.Clear();
            Recompute();
            UpdateAbrechnungssumme();

            MessageBox.Show(
                $"Gelöscht (Staging): {deletedStaging}\nIm Archiv referenziert: {affectedArchive}",
                "Batch verworfen", MessageBoxButton.OK, MessageBoxImage.Information);

            LadeBatches();
            AusgewaehlterBatch = null;

        }

        private void ZeileLoeschen(CreditCardImportRow? row)
        {
            if (row == null) return;

            var ok = MessageBox.Show(
                "Diese Position aus dem Staging löschen?",
                "Zeile löschen", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (ok != MessageBoxResult.Yes) return;

            _db.DeleteCcStagingRows(new[] { row.Id });
            ReloadStaging();
        }

        public ObservableCollection<CreditCardBatchInfo> Batches { get; } = new();

        private CreditCardBatchInfo? _ausgewaehlterBatch;
        public CreditCardBatchInfo? AusgewaehlterBatch
        {
            get => _ausgewaehlterBatch;
            set => SetProperty(ref _ausgewaehlterBatch, value);
        }

        public ICommand BatchesNeuLadenCommand { get; }
        public ICommand BatchLadenCommand { get; }

        private void LadeBatches()
        {
            Batches.Clear();
            foreach (var b in _db.LadeCcBatches(nurMitOffenen: true))
                Batches.Add(b);

            // Buttons neu bewerten
            (BatchLadenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BatchVerwerfenCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void BatchLaden()
        {
            if (AusgewaehlterBatch == null) return;
            CurrentBatchId = AusgewaehlterBatch.Id;
            ReloadStaging();
            UpdateAbrechnungssumme();
        }



        // ================== Helfer ==================

        private bool ZeilenFilter(object obj)
        {
            if (obj is not CreditCardImportRow r) return false;
            return FilterModus switch
            {
                "Offen" => !r.KontoId.HasValue,
                "Zugewiesen" => r.KontoId.HasValue,
                _ => true, // "Alle"
            };
        }

        private void Recompute()
        {
            OnPropertyChanged(nameof(AnzahlZuweisbar));
            OnPropertyChanged(nameof(AnzahlOffen));
            OnPropertyChanged(nameof(SummeListe));
            OnPropertyChanged(nameof(SummeZugeordnet));
            OnPropertyChanged(nameof(SummeOffen));
            OnPropertyChanged(nameof(DifferenzZurAbrechnung));
            (BuchenCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private static bool IstBelastung(string? dk)
        {
            var s = (dk ?? "").Trim().ToUpperInvariant();
            return s is "BELASTUNG" or "DEBIT" or "SOLL" or "CHARGE" or "AUSGABE" or "DEBITO";
        }

        private void UpdateAbrechnungssumme()
        {
            if (AusgleichsKontoId.HasValue)
                Abrechnungssumme = Math.Round(_db.HoleSaldoFuerKonto(AusgleichsKontoId.Value), 2);
            else
                Abrechnungssumme = null;

            OnPropertyChanged(nameof(DifferenzZurAbrechnung));
        }

        private int? _currentBatchId;
        public int? CurrentBatchId
        {
            get => _currentBatchId;
            private set => SetProperty(ref _currentBatchId, value);
        }

        private void ReloadStaging()
        {
            Zeilen.Clear();
            var list = _db.LadeCcStaging(CurrentBatchId); // nur aktueller Batch
            foreach (var r in list) Zeilen.Add(r);

            ZeilenView?.Refresh();
            Recompute();
        }


    }
}
