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
using MyCoinFlow.Import;
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
        public ICommand WebRechercheCommand { get; }

        public CreditCardImportViewModel()
        {
            // Basis-Commands
            DateiWaehlenCommand = new RelayCommand(_ => DateiWaehlen());
            ZuweisenCommand = new RelayCommand(p => Zuweisen(p as CreditCardImportRow), p => p is CreditCardImportRow);

            WebRechercheCommand = new RelayCommand(
                p =>
                {
                    if (p is not CreditCardImportRow row) return;
                    try
                    {
                        if (!WebRechercheService.OpenSearch(row.Beschreibung, row.Haendler))
                            MessageBox.Show("Kein verwertbarer Buchungstext für die Recherche vorhanden.",
                                "Web-Recherche", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Recherche konnte nicht geöffnet werden:\n" + ex.Message,
                            "Web-Recherche", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                },
                p => p is CreditCardImportRow);
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

            // Beim Öffnen keine Zeilen laden – nur die Batchliste
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
                // GUARD: Master-Schema sicherstellen (idempotent, verhindert Duplicate-Key)
                _db.EnsureImportMasterSchemaExists();

                var rows = _db.LeseCreditCardExcel(AusgewaehlteDatei);  // <- unverändert :contentReference[oaicite:3]{index=3}

                // 1) Batch anlegen
                CurrentBatchId = _db.CreateCcBatch(System.IO.Path.GetFileName(AusgewaehlteDatei),
                                                   AusgleichsKontoId, AusgewaehltesGeldinstitutId);

                LadeBatches();
                AusgewaehlterBatch = Batches.FirstOrDefault(b => b.Id == CurrentBatchId);
                BatchLaden();

                // 2) Staging schreiben (inkl. Mapping/Autokonto)
                var (ins, skip, dup) = _db.SaveExcelRowsToStaging(CurrentBatchId.Value, rows);

                // 3) Staging laden & anzeigen
                ReloadStaging();

                MessageBox.Show($"Importiert: {ins}\nÜbersprungen (keine Belastung): {skip}\nDuplikate: {dup}",
                    "Excel-Import", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Excel konnte nicht importiert werden:\n" + ex.Message,
                    "Import", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Gleicher Zuordnungsdialog wie beim CAMT-Bank-Import (inkl. Adress-Erkennung,
        // Schnellwahl-Konten und Contains-Suche) - dazu wird die Kreditkarten-Zeile
        // in ein BankImportItem gewrappt, das der Dialog erwartet.
        private void Zuweisen(CreditCardImportRow? row)
        {
            if (row == null) return;

            bool istEinnahme = !IstBelastung(row.DebitKredit);

            var item = new BankImportItem
            {
                BookingDate = row.Datum,
                Amount = row.Betrag,
                Direction = istEinnahme ? KreditDebit.Credit : KreditDebit.Debit,
                Currency = "CHF",
                Text = row.Beschreibung,
                ServiceRef = row.Kategorie ?? "",
                CounterpartyName = row.Haendler,
                VorschlagAdresseId = row.AdresseId,
                VorschlagNachKontoId = row.KontoId
            };

            var dlg = new ZuordnungDialog(item)
            {
                Owner = Application.Current?.MainWindow
            };

            if (dlg.ShowDialog() == true && dlg.SelectedKontoId.HasValue)
            {
                UebernehmeZuordnung(row, dlg.SelectedAdresseId, dlg.SelectedKontoId.Value);
            }
        }

        /// <summary>
        /// Führt nach einer bestätigten Zuordnungsmaske exakt denselben Ablauf aus,
        /// unabhängig davon, ob die Maske aus WPF oder WinUI stammt.
        /// </summary>
        public void UebernehmeZuordnung(CreditCardImportRow row, int? adresseId, int kontoId)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));

            _db.UpdateCcStagingZuordnung(row.Id, adresseId, kontoId);

            // Gelernte Adresse/Regel sofort auf übrige offene Zeilen dieses Batches anwenden
            AutoMatchOffeneZeilenImBatch();

            ReloadStaging();
        }

        private void AutoMatchOffeneZeilenImBatch()
        {
            var matcher = new AdressErkennungService();

            foreach (var z in Zeilen.Where(z => !z.KontoId.HasValue).ToList())
            {
                bool istEinnahme = !IstBelastung(z.DebitKredit);
                var (adrId, kontoId) = _db.ResolveCcZeile(matcher, z.Haendler, z.Beschreibung, z.Kategorie, Math.Abs(z.Betrag), istEinnahme);

                if (kontoId.HasValue)
                    _db.UpdateCcStagingZuordnung(z.Id, adrId, kontoId.Value);
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

            UpdateAbrechnungssumme();
            ReloadStaging();
        }

        public ICommand BatchVerwerfenCommand { get; }
        public ICommand ZeileLoeschenCommand { get; }
        public ICommand BatchesNeuLadenCommand { get; }
        public ICommand BatchLadenCommand { get; }

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

        private void LadeBatches()
        {
            Batches.Clear();
            foreach (var b in _db.LadeCcBatches(nurMitOffenen: true))
                Batches.Add(b);

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
            var list = _db.LadeCcStaging(CurrentBatchId);
            foreach (var r in list) Zeilen.Add(r);

            ZeilenView?.Refresh();
            Recompute();
        }
    }
}
