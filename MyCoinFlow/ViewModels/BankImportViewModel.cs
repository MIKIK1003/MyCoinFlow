using Microsoft.Win32;
using MyCoinFlow.Helpers;
using MyCoinFlow.Import;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace MyCoinFlow.ViewModels
{
    public class BankImportViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new();
        private readonly ImportService _import = new();
        private readonly AdressErkennungService _matcher = new();

        public ObservableCollection<BankImportItem> Items { get; } = new();

        public ICollectionView ItemsView { get; }

        public RelayCommand OpenFileCommand { get; }
        public RelayCommand ClearCommand { get; }
        public RelayCommand SaveToDbCommand { get; }
        public RelayCommand LoadPendingFromDbCommand { get; }
        public RelayCommand RefreshRecognitionCommand { get; }

        public RelayCommand BulkUebernehmenCommand { get; }
        public RelayCommand EinzelBuchenCommand { get; }
        public RelayCommand AnlernenCommand { get; }
        public RelayCommand BearbeitenCommand { get; }
        public RelayCommand DeleteImportedRowCommand { get; }


        private string _filePath = "";
        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
        }

        public string StatusText => string.IsNullOrWhiteSpace(FilePath)
            ? "Quelle: (DB oder Datei)  "
            : $"Datei: {Path.GetFileName(FilePath)}";

        private bool _onlyIncomplete;
        public bool OnlyIncomplete
        {
            get => _onlyIncomplete;
            set { _onlyIncomplete = value; OnPropertyChanged(); ApplyFilter(); }
        }

        public BankImportViewModel()
        {
            ItemsView = CollectionViewSource.GetDefaultView(Items);
            ItemsView.Filter = FilterItem;

            OpenFileCommand = new RelayCommand(OpenFile);
            ClearCommand = new RelayCommand(_ => { Items.Clear(); FilePath = ""; SaveToDbCommand?.RaiseCanExecuteChanged(); RefreshItemsView(); });
            SaveToDbCommand = new RelayCommand(_ => SaveToDb(), _ => Items.Count > 0);
            LoadPendingFromDbCommand = new RelayCommand(_ => LoadPendingFromDb());

            DeleteImportedRowCommand = new RelayCommand(
            p => { if (p is BankImportItem it) DeleteImportedRow(it); },
            p => p is BankImportItem it && (it != null)    // defensiv
            );


            RefreshRecognitionCommand = new RelayCommand(_ =>
            {
                BankImportLabelCache.Refresh();
                foreach (var it in Items) it.NotifyLabelPropertiesChanged();
                _matcher.Reload();
                AutoMatchAdressen();
                RefreshItemsView();
            });

            AnlernenCommand = new RelayCommand(p => { if (p is BankImportItem it) Anlernen(it); });
            BearbeitenCommand = new RelayCommand(p => { if (p is BankImportItem it) Anlernen(it); });

            EinzelBuchenCommand = new RelayCommand(
                p => { if (p is BankImportItem it) BuchenEinzeln(it); },
                p => p is BankImportItem it && CanBookSimple(it));

            BulkUebernehmenCommand = new RelayCommand(
                _ => BulkUebernehmenAlleZugeordneten(),
                _ => Items.Any(CanBookSimple));
        }

        private void OpenFile(object? _)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Bankdatei wählen (camt.053 XML)",
                    Filter = "CAMT XML (*.xml)|*.xml|Alle Dateien (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dlg.ShowDialog() == true)
                {
                    var path = dlg.FileName;
                    var list = Camt53Parser.ParseFromFile(path);

                    Items.Clear();
                    foreach (var it in list) Items.Add(it);

                    FilePath = path;
                    SaveToDbCommand.RaiseCanExecuteChanged();

                    _matcher.Reload();
                    AutoMatchAdressen();

                    BankImportLabelCache.Refresh();
                    foreach (var it in Items) it.NotifyLabelPropertiesChanged();

                    RefreshItemsView();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Datei konnte nicht geladen werden:\n" + ex.Message,
                    "Importfehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveToDb()
        {
            if (Items.Count == 0) return;

            try
            {
                var first = Items[0];
                var hash = !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath)
                    ? ImportService.ComputeSha256(FilePath)
                    : null;

                int batchId = _import.CreateBatch(
                    sourceFormat: "CAMT",
                    fileName: string.IsNullOrWhiteSpace(FilePath) ? "(unbenannt)" : Path.GetFileName(FilePath),
                    fileHash: hash,
                    accountIban: first.AccountIban,
                    currency: first.Currency);

                var (inserted, skipped) = _import.UpsertItems(batchId, Items);

                LoadPendingFromDb();

                MessageBox.Show($"Staging gespeichert.\nNeu: {inserted}\nÜbersprungen: {skipped}",
                    "Bankimport", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Speichern fehlgeschlagen:\n" + ex.Message,
                    "Bankimport", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadPendingFromDb()
        {
            try
            {
                var list = _import.LoadPending();
                Items.Clear();
                foreach (var it in list) Items.Add(it);

                FilePath = "";
                SaveToDbCommand.RaiseCanExecuteChanged();

                _matcher.Reload();
                AutoMatchAdressen();

                BankImportLabelCache.Refresh();
                foreach (var it in Items) it.NotifyLabelPropertiesChanged();

                RefreshItemsView();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Laden fehlgeschlagen:\n" + ex.Message,
                    "Bankimport", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------------------------------------------
        // Auto-Match: erkennt Bank↔Bank und setzt Durchlaufkonto (Adresse.DefaultKontoId) als Vorschlag
        // ---------------------------------------------
        private void AutoMatchAdressen()
        {
            // Geldinstitute für Gegen-IBAN-Abgleich (Bank↔Bank)
            var giList = _db.LadeGeldinstitute(); // Id, Name, IBAN … :contentReference[oaicite:0]{index=0}
            var giIbans = new HashSet<string>(
                giList.Where(g => !string.IsNullOrWhiteSpace(g.IBAN))
                      .Select(g => NormalizeIban(g.IBAN!)));

            // Batch-Account-IBANs als Fallback (falls Zielbank in DB noch nicht angelegt)
            var batchAccountIbans = new HashSet<string>(
                Items.Where(x => !string.IsNullOrWhiteSpace(x.AccountIban))
                     .Select(x => NormalizeIban(x.AccountIban)));

            foreach (var it in Items)
            {
                bool changed = false;

                // eigenes Geldinstitut aus Account-IBAN
                if (!it.VorschlagGeldinstitutId.HasValue && !string.IsNullOrWhiteSpace(it.AccountIban))
                {
                    var gi = TryFindGeldinstitutId(it.AccountIban);
                    if (gi.HasValue) { it.VorschlagGeldinstitutId = gi.Value; changed = true; }
                }

                // Umbuchung? Gegen-IBAN ist unsere IBAN (DB) oder taucht als AccountIban im Batch auf
                bool isTransfer = !string.IsNullOrWhiteSpace(it.CounterpartyIban)
                                  && (giIbans.Contains(NormalizeIban(it.CounterpartyIban))
                                      || batchAccountIbans.Contains(NormalizeIban(it.CounterpartyIban)));

                if (isTransfer)
                {
                    // Adresse „Interne Umbuchung – …“ holen/erzeugen
                    string label = FindGiLabelForCounterparty(it.CounterpartyIban, giList)
                                   ?? $"****{Last4(it.CounterpartyIban)}";
                    string umbName = $"Interne Umbuchung – {label}";

                    var adrId = _db.FindeOderErzeugeAdresseByName(umbName); // speichert nur Name; DefaultKontoId setzt der User beim ersten Anlernen:contentReference[oaicite:1]{index=1}
                    if (adrId.HasValue && it.VorschlagAdresseId != adrId.Value)
                    {
                        it.VorschlagAdresseId = adrId.Value;
                        changed = true;
                    }

                    // Wenn das Durchlaufkonto bereits an der Adresse hinterlegt ist → für DBIT gleich vollständig machen
                    if (adrId.HasValue)
                    {
                        var adr = _db.HoleAdresse(adrId.Value);
                        if (adr?.DefaultKontoId.HasValue == true && it.Direction == KreditDebit.Debit)
                        {
                            if (it.VorschlagNachKontoId != adr.DefaultKontoId.Value)
                            {
                                it.VorschlagNachKontoId = adr.DefaultKontoId.Value;
                                changed = true;
                            }
                        }
                        else
                        {
                            // bei CRDT kein NachKonto setzen (VonKonto kommt später aus Durchlaufkonto)
                            if (it.VorschlagNachKontoId.HasValue)
                            {
                                it.VorschlagNachKontoId = null;
                                changed = true;
                            }
                        }
                    }
                }
                else
                {
                    // normales Adress-Matching (IBAN/Name/Text)
                    var adrId = it.VorschlagAdresseId;
                    if (!adrId.HasValue)
                        adrId = _matcher.TryMatch(it.CounterpartyIban, it.CounterpartyName, it.Text);

                    if (adrId.HasValue && it.VorschlagAdresseId != adrId.Value)
                    {
                        it.VorschlagAdresseId = adrId.Value;
                        changed = true;
                    }

                    // Konto-Vorschlag (Default der Adresse oder via Gegen-IBAN)
                    if (adrId.HasValue && !it.VorschlagNachKontoId.HasValue)
                    {
                        int? kontoId =
                            _db.HoleDefaultKontoIdByAdresse(adrId.Value)
                            ?? _db.HoleDefaultKontoIdByIban(it.CounterpartyIban);

                        if (kontoId.HasValue)
                        {
                            it.VorschlagNachKontoId = kontoId.Value;
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    PersistSuggestionsIfStaged(it);
                    it.NotifyLabelPropertiesChanged();
                }
            }
            RefreshItemsView();
        }

        private void Anlernen(BankImportItem item)
        {
            if (item == null) return;

            try
            {
                var dlg = new MyCoinFlow.Views.ZuordnungDialog(item)
                {
                    Owner = Application.Current?.MainWindow
                };

                if (dlg.ShowDialog() == true)
                {
                    bool changed = false;

                    if (dlg.SelectedAdresseId.HasValue && item.VorschlagAdresseId != dlg.SelectedAdresseId.Value)
                    {
                        item.VorschlagAdresseId = dlg.SelectedAdresseId.Value;
                        changed = true;
                    }

                    if (dlg.SelectedKontoId.HasValue)
                    {
                        if (item.Direction == KreditDebit.Credit)
                        {
                            if (item.VorschlagAdresseId.HasValue)
                            {
                                var adr = _db.LadeAdresseById(item.VorschlagAdresseId.Value);

                                if (IstUmbuchungsAdresse(adr?.Name))
                                {
                                    // CRDT (Eingang): Durchlaufkonto als VON
                                    item.VorschlagVonKontoId = dlg.SelectedKontoId.Value;
                                    item.VorschlagNachKontoId = null;
                                    changed = true;
                                }
                                else if (adr.IstBudgetiert)
                                {
                                    // Echte Einnahmen
                                    if (item.VorschlagNachKontoId != dlg.SelectedKontoId.Value)
                                    {
                                        item.VorschlagNachKontoId = dlg.SelectedKontoId.Value;
                                        changed = true;
                                    }
                                    item.VorschlagVonKontoId = null;
                                }
                                else
                                {
                                    // Refund
                                    if (item.VorschlagVonKontoId != dlg.SelectedKontoId.Value)
                                    {
                                        item.VorschlagVonKontoId = dlg.SelectedKontoId.Value;
                                        changed = true;
                                    }
                                    item.VorschlagNachKontoId = null;
                                }
                            }
                        }
                        else
                        {
                            // DBIT (Ausgang)
                            if (item.VorschlagAdresseId.HasValue)
                            {
                                var adr = _db.LadeAdresseById(item.VorschlagAdresseId.Value);
                                if (IstUmbuchungsAdresse(adr?.Name))
                                {
                                    // DBIT: Durchlaufkonto als NACH
                                    if (item.VorschlagNachKontoId != dlg.SelectedKontoId.Value)
                                    {
                                        item.VorschlagNachKontoId = dlg.SelectedKontoId.Value;
                                        changed = true;
                                    }
                                    item.VorschlagVonKontoId = null;
                                }
                                else
                                {
                                    // normale Ausgabe
                                    if (item.VorschlagNachKontoId != dlg.SelectedKontoId.Value)
                                    {
                                        item.VorschlagNachKontoId = dlg.SelectedKontoId.Value;
                                        changed = true;
                                    }
                                    item.VorschlagVonKontoId = null;
                                }
                            }
                        }
                    }

                    if (!item.VorschlagGeldinstitutId.HasValue && !string.IsNullOrWhiteSpace(item.AccountIban))
                    {
                        var gi = TryFindGeldinstitutId(item.AccountIban);
                        if (gi.HasValue) { item.VorschlagGeldinstitutId = gi.Value; changed = true; }
                    }

                    if (item.VorschlagAdresseId.HasValue)
                        _matcher.AddAliasFromTextIfNew(item.VorschlagAdresseId.Value, item.Text);

                    if (changed)
                    {
                        PersistSuggestionsIfStaged(item);
                        item.NotifyLabelPropertiesChanged();
                    }

                    _matcher.Reload();
                    AutoMatchAdressen();

                    BankImportLabelCache.Refresh();
                    foreach (var it2 in Items) it2.NotifyLabelPropertiesChanged();

                    RefreshItemsView();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Anlernen fehlgeschlagen:\n" + ex.Message,
                    "Anlernen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Von/Nach ableiten – inkl. Durchlaufkonto-Logik für Umbuchungen
        private (int? vonKontoId, int? nachKontoId) BestimmeVonNach(BankImportItem it)
        {
            int? von = null;
            int? nach = null;

            Adresse? adr = null;
            if (it.VorschlagAdresseId.HasValue)
            {
                try { adr = _db.HoleAdresse(it.VorschlagAdresseId.Value); }
                catch { adr = null; }
            }

            // Umbuchungsadresse? → Durchlaufkonto in DefaultKontoId
            if (IstUmbuchungsAdresse(adr?.Name))
            {
                var dlKonto = adr?.DefaultKontoId; // vom User beim ersten Anlernen gesetzt
                if (it.Direction == KreditDebit.Debit)
                {
                    // Bank -> Durchlaufkonto
                    nach = dlKonto;
                }
                else
                {
                    // Durchlaufkonto -> Bank
                    von = dlKonto;
                }
                return (von, nach);
            }

            // normale Regeln
            if (it.Direction == KreditDebit.Debit)
            {
                nach = it.VorschlagNachKontoId ?? adr?.DefaultKontoId;
            }
            else
            {
                if (adr?.IstBudgetiert == true && adr.StandardEinnahmenKontoId.HasValue)
                {
                    nach = adr.StandardEinnahmenKontoId.Value;
                }
                else if (adr?.DefaultKontoId.HasValue == true)
                {
                    von = adr.DefaultKontoId.Value;
                }
                else if (it.VorschlagNachKontoId.HasValue)
                {
                    nach = it.VorschlagNachKontoId.Value;
                }
            }

            return (von, nach);
        }

        private void BuchenEinzeln(BankImportItem it)
        {
            if (!CanBook(it, out var reason))
            {
                MessageBox.Show(reason ?? "Diese Zeile kann noch nicht gebucht werden.",
                    "Einzelbuchung", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                int giId = it.VorschlagGeldinstitutId ?? EnsureGeldinstitut(it);

                var (vonKontoId, nachKontoId) = BestimmeVonNach(it);

                DateTime datum = it.BookingDate.Date;
                decimal betrag = Math.Abs(it.Amount);
                string? notiz = string.IsNullOrWhiteSpace(it.ServiceRef) ? it.Text : $"{it.Text} [{it.ServiceRef}]";
                int? adresseId = it.VorschlagAdresseId;

                var importHash = BuildBankImportHash(it);
                var exist = _db.SucheTransaktionIdByHash(importHash);
                if (exist.HasValue)
                {
                    _import.MoveToArchive(it.StagingId!.Value, exist.Value, "duplicate");
                    LoadPendingFromDb();
                    MessageBox.Show("Bereits verbucht (Duplikat erkannt).", "Einzelbuchung",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int tid = _db.InsertTransaktionMitImport(
                    datum,
                    vonKontoId,
                    nachKontoId,
                    betrag,
                    notiz,
                    adresseId,
                    giId,
                    importQuelle: "CAMT",
                    importHash: importHash
                );

                _import.MoveToArchive(it.StagingId!.Value, tid, "single-booked");

                LoadPendingFromDb();
                MessageBox.Show("Einzelbuchung übernommen.", "Einzelbuchung",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Einzelbuchung fehlgeschlagen:\n" + ex.Message,
                    "Einzelbuchung", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteImportedRow(BankImportItem it)
        {
            if (it == null) return;

            var preview = (it.Text ?? it.CounterpartyName ?? "").Trim();
            if (preview.Length > 80) preview = preview.Substring(0, 80) + "…";

            var ask = MessageBox.Show(
                $"Soll diese Importzeile gelöscht werden?\n\nDatum: {it.BookingDate:dd.MM.yyyy}\nText: {preview}",
                "Importzeile löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (ask != MessageBoxResult.Yes) return;

            try
            {
                // Falls bereits im Staging (DB) gespeichert: dort löschen
                if (it.StagingId.HasValue)
                {
                    _db.DeleteBankImportItem(it.StagingId.Value);
                }

                // Aus der aktuellen Ansicht entfernen
                Items.Remove(it);

                // UI aktualisieren
                RefreshItemsView();

                // Label-/Status-Cache refreshen wie an anderen Stellen
                BankImportLabelCache.Refresh();
                foreach (var it2 in Items)
                    it2.NotifyLabelPropertiesChanged();

                SaveToDbCommand?.RaiseCanExecuteChanged();
                BulkUebernehmenCommand?.RaiseCanExecuteChanged();
                EinzelBuchenCommand?.RaiseCanExecuteChanged();


            }
            catch (Exception ex)
            {
                MessageBox.Show("Zeile konnte nicht gelöscht werden:\n" + ex.Message,
                    "Bankimport", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void BulkUebernehmenAlleZugeordneten()
        {
            var alle = Items.Where(CanBookSimple).ToList();
            if (alle.Count == 0)
            {
                MessageBox.Show("Keine vollständig zugeordneten Zeilen gefunden.", "Bankimport",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ask = MessageBox.Show(
                $"Sollen {alle.Count} vollständig zugeordnete Buchung(en) automatisch übernommen werden?",
                "Alle zugeordneten übernehmen",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (ask != MessageBoxResult.Yes) return;

            int ok = 0, dup = 0, err = 0;

            foreach (var it in alle)
            {
                try
                {
                    int giId = it.VorschlagGeldinstitutId ?? EnsureGeldinstitut(it);

                    var (vonKontoId, nachKontoId) = BestimmeVonNach(it);

                    DateTime datum = it.BookingDate.Date;
                    decimal betrag = Math.Abs(it.Amount);
                    string? notiz = string.IsNullOrWhiteSpace(it.ServiceRef) ? it.Text : $"{it.Text} [{it.ServiceRef}]";
                    int? adresseId = it.VorschlagAdresseId;

                    var importHash = BuildBankImportHash(it);
                    var exist = _db.SucheTransaktionIdByHash(importHash);
                    if (exist.HasValue)
                    {
                        _import.MoveToArchive(it.StagingId!.Value, exist.Value, "duplicate");
                        ok++; dup++;
                        continue;
                    }

                    int tid = _db.InsertTransaktionMitImport(
                        datum,
                        vonKontoId,
                        nachKontoId,
                        betrag,
                        notiz,
                        adresseId,
                        giId,
                        importQuelle: "CAMT",
                        importHash: importHash
                    );

                    _import.MoveToArchive(it.StagingId!.Value, tid, "bulk-booked");
                    ok++;
                }
                catch
                {
                    err++;
                }
            }

            LoadPendingFromDb();

            MessageBox.Show(
                $"Übernommen: {ok}\nDuplikate: {dup}\nFehler: {err}",
                "Bankimport", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Regeln: Debit verlangt NachKonto (auch bei Umbuchung – kommt aus Adresse.DefaultKontoId), Credit braucht nur Adresse
        private bool CanBook(BankImportItem it, out string? reason)
        {
            reason = null;

            if (it == null) { reason = "Kein Datensatz."; return false; }
            if (!it.StagingId.HasValue) { reason = "Noch nicht gespeichert (StagingId fehlt)."; return false; }
            if (!it.IstVollstaendig && it.Direction == KreditDebit.Debit) { reason = "Nicht vollständig zugeordnet (Zielkonto)."; return false; }

            bool hatOderKannBank = it.VorschlagGeldinstitutId.HasValue || !string.IsNullOrWhiteSpace(it.AccountIban);
            if (!hatOderKannBank) { reason = "Kein Geldinstitut/IBAN erkannt."; return false; }

            if (!it.VorschlagAdresseId.HasValue) { reason = "Adresse fehlt."; return false; }

            if (it.Direction == KreditDebit.Debit && !it.VorschlagNachKontoId.HasValue)
            { reason = "Zielkonto (Nach) fehlt."; return false; }

            return true;
        }

        private bool CanBookSimple(BankImportItem it)
        {
            string? _;
            return CanBook(it, out _);
        }

        // ---------------- Helpers ----------------

        private static string NormalizeIban(string? iban)
            => string.IsNullOrWhiteSpace(iban) ? "" : iban.Replace(" ", "").ToUpperInvariant();

        private static string Last4(string? iban)
        {
            var f = NormalizeIban(iban);
            return f.Length >= 4 ? f[^4..] : "????";
        }

        private string? FindGiLabelForCounterparty(string? counterpartyIban, List<Geldinstitut> giList)
        {
            if (string.IsNullOrWhiteSpace(counterpartyIban)) return null;
            var norm = NormalizeIban(counterpartyIban);
            var gi = giList.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.IBAN) && NormalizeIban(g.IBAN) == norm);
            if (gi == null) return null;

            var flat = NormalizeIban(gi.IBAN);
            var last4 = flat.Length >= 4 ? flat[^4..] : null;
            return last4 != null ? $"{gi.Name} (****{last4})" : gi.Name;
        }

        // Erkennt unsere Umbuchungs-Adressen, z. B. "Interne Umbuchung – Bankkonto ****1234"
        private static bool IstUmbuchungsAdresse(string? adrName)
        {
            if (string.IsNullOrWhiteSpace(adrName)) return false;
            var n = adrName.Trim();

            // robust gegen verschiedene Gedankenstriche/Leerzeichen
            // reicht hier: beginnt mit "Interne Umbuchung"
            return n.StartsWith("Interne Umbuchung", StringComparison.CurrentCultureIgnoreCase);
        }



        private void PersistSuggestionsIfStaged(BankImportItem item)
        {
            if (item.StagingId.HasValue)
            {
                _import.UpdateSuggestions(item.StagingId.Value,
                    item.VorschlagAdresseId,
                    item.VorschlagNachKontoId,
                    item.VorschlagVonKontoId,
                    item.VorschlagGeldinstitutId);
            }
        }

        private int? TryFindGeldinstitutId(string? accountIban)
        {
            if (string.IsNullOrWhiteSpace(accountIban)) return null;

            string norm = new string(accountIban.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();

            var alle = _db.LadeGeldinstitute();
            foreach (var g in alle)
            {
                if (!string.IsNullOrWhiteSpace(g.IBAN))
                {
                    string gNorm = new string(g.IBAN.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();
                    if (gNorm == norm) return g.Id;
                }
            }
            return null;
        }

        private int EnsureGeldinstitut(BankImportItem item)
        {
            var alle = _db.LadeGeldinstitute();
            var found = alle.FirstOrDefault(g =>
                !string.IsNullOrWhiteSpace(g.IBAN) &&
                !string.IsNullOrWhiteSpace(item.AccountIban) &&
                string.Equals(g.IBAN.Replace(" ", ""), item.AccountIban.Replace(" ", ""),
                    StringComparison.OrdinalIgnoreCase));

            if (found != null) return found.Id;

            var ibanFlat = item.AccountIban?.Replace(" ", "");
            var last4 = !string.IsNullOrEmpty(ibanFlat) && ibanFlat.Length >= 4 ? ibanFlat[^4..] : "neu";
            var ginst = new Geldinstitut
            {
                Name = $"Bankkonto ****{last4}",
                IBAN = string.IsNullOrWhiteSpace(item.AccountIban) ? null : item.AccountIban,
                Notiz = "aus Bankimport angelegt"
            };
            return _db.SpeichereGeldinstitut(ginst);
        }

        private void RefreshItemsView()
        {
            ItemsView?.Refresh();
            BulkUebernehmenCommand?.RaiseCanExecuteChanged();
            EinzelBuchenCommand?.RaiseCanExecuteChanged();
        }

        private void ApplyFilter()
        {
            ItemsView?.Refresh();
            BulkUebernehmenCommand?.RaiseCanExecuteChanged();
            EinzelBuchenCommand?.RaiseCanExecuteChanged();
        }

        private bool FilterItem(object obj)
        {
            if (obj is not BankImportItem it) return false;
            if (!OnlyIncomplete) return true;

            return !it.VorschlagAdresseId.HasValue
                || !it.VorschlagGeldinstitutId.HasValue
                || (it.Direction == KreditDebit.Debit && !it.VorschlagNachKontoId.HasValue);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static string BuildBankImportHash(BankImportItem it)
        {
            var key = string.Join("|",
                "CAMT",
                it.BookingDate.ToString("yyyy-MM-dd"),
                Math.Abs(it.Amount).ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                (it.Text ?? "").Trim().ToUpperInvariant(),
                (it.CounterpartyName ?? "").Trim().ToUpperInvariant(),
                (it.AccountIban ?? "").Replace(" ", "").ToUpperInvariant(),
                (it.ServiceRef ?? "").Trim().ToUpperInvariant()
            );

            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(key);
            var hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
    }
}
