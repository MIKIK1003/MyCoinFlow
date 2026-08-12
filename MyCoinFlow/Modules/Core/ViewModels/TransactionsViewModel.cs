using Microsoft.Data.SqlClient;
using Microsoft.Win32; // für OpenFileDialog
using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace MyCoinFlow.ViewModels
{
    public class TransaktionRowExt
    {
        public int Id { get; set; }
        public DateTime Datum { get; set; }

        public DateTime? BudgetDatum { get; set; } // NEU

        public bool HasAttachments { get; set; }      // PDF/Bilder
        public string? AttachmentsTooltip { get; set; }

        public string VonAnzeige { get; set; } = "";
        public string NachAnzeige { get; set; } = "";
        public decimal Betrag { get; set; }

        public string? AdresseName { get; set; }
        public string? BankName { get; set; }
        public string? Notiz { get; set; }

        // Für Bearbeiten
        public int? VonKontoId { get; set; }
        public int? NachKontoId { get; set; }
        public int? AdresseId { get; set; }
        public int? GeldinstitutId { get; set; }

        public int AttachmentCount { get; set; }
        public bool HasMultipleAttachments => AttachmentCount > 1;

        public string? SearchHitInfo { get; set; } // „Treffer in: …“

        public bool HasBudgetDatumOverride { get; set; }      // NEU: Icon sichtbar
        public string? BudgetDatumTooltip { get; set; }       // NEU: Tooltip


    }

    public class TransactionsViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new();

        public ObservableCollection<TransaktionRowExt> Transaktionen { get; } = new();

        private TransaktionRowExt? _ausgewaehlteTransaktion;
        public TransaktionRowExt? AusgewaehlteTransaktion
        {
            get => _ausgewaehlteTransaktion;
            set
            {
                _ausgewaehlteTransaktion = value;
                OnPropertyChanged();
                (BearbeitenCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (LoeschenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ICommand NeuBuchungCommand { get; }
        public ICommand BearbeitenCommand { get; }
        public ICommand LoeschenCommand { get; }
        public ICommand AttachPdfCommand { get; }
        public ICommand OpenAttachmentCommand { get; }
        public ICommand ManageAttachmentsCommand { get; }
        public ICommand OpenBankImportCommand { get; }
        public ICommand OpenReportCommand { get; }
        public ICommand WebRechercheCommand { get; }
        public ICommand DuplikateCommand { get; }

        private string? _searchText;

        private DateTime? _filterVon;

        private DateTime? _activeStart; // NEU
        private DateTime? _activeEnd;   // NEU

        public DateTime? FilterVon
        {
            get => _filterVon;
            set
            {
                if (_filterVon != value)
                {
                    _filterVon = value;
                    OnPropertyChanged();
                    (ClearSearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private DateTime? _filterBis;
        public DateTime? FilterBis
        {
            get => _filterBis;
            set
            {
                if (_filterBis != value)
                {
                    _filterBis = value;
                    OnPropertyChanged();
                    (ClearSearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private string? _filterAdresse;
        public string? FilterAdresse
        {
            get => _filterAdresse;
            set
            {
                if (_filterAdresse != value)
                {
                    _filterAdresse = value;
                    OnPropertyChanged();
                    (ClearSearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string? SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged();
                (ClearSearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ICommand ApplySearchCommand { get; }
        public ICommand ClearSearchCommand { get; }

        /// <param name="fokusTransaktionId">
        /// Optional: Beim Öffnen gezielt diese Buchung anzeigen und markieren
        /// (Sprung aus einem anderen Modul, z.B. aus dem DMS).
        /// </param>
        public TransactionsViewModel(int? fokusTransaktionId = null)
        {
            NeuBuchungCommand = new RelayCommand(_ => NeueBuchung());
            BearbeitenCommand = new RelayCommand(_ => Bearbeiten(), _ => AusgewaehlteTransaktion != null);
            LoeschenCommand = new RelayCommand(_ => Loeschen(), _ => AusgewaehlteTransaktion != null);

            AttachPdfCommand = new RelayCommand(p => AttachPdfFromRow(p), _ => true);
            OpenAttachmentCommand = new RelayCommand(p => OpenAttachmentFromRow(p), _ => true);
            ManageAttachmentsCommand = new RelayCommand(p => ManageAttachmentsFromRow(p), _ => true);

            OpenBankImportCommand = new RelayCommand(_ => OpenBankImport());
            OpenReportCommand = new RelayCommand(_ => OpenReport());
            WebRechercheCommand = new RelayCommand(p => WebRecherche(p));
            DuplikateCommand = new RelayCommand(_ => DuplikateSuchen());

            ApplySearchCommand = new RelayCommand(_ => LadeListe(), _ => true);

            ClearSearchCommand = new RelayCommand(_ =>
            {
                SearchText = string.Empty;
                FilterAdresse = string.Empty;

                // ✅ Reset auf aktiven Budgetzeitraum (wie Default)
                PrefillDateRangeFromActiveBudget();

                LadeListe();
            },
            _ => !string.IsNullOrWhiteSpace(SearchText)
              || FilterVon.HasValue
              || FilterBis.HasValue
              || !string.IsNullOrWhiteSpace(FilterAdresse));

            if (fokusTransaktionId.HasValue)
            {
                // Gezielter Sprung: Datumsfilter bewusst offen lassen, damit die Buchung
                // auch ausserhalb des aktiven Budgetzeitraums gefunden wird. Die Nummer
                // steht im Suchfeld, damit nachvollziehbar ist, warum die Liste gefiltert ist.
                FilterVon = null;
                FilterBis = null;
                SearchText = fokusTransaktionId.Value.ToString();

                LadeListe();

                AusgewaehlteTransaktion = Transaktionen
                    .FirstOrDefault(r => r.Id == fokusTransaktionId.Value);
            }
            else
            {
                // ✅ Default: aktiver Budgetzeitraum
                PrefillDateRangeFromActiveBudget();

                LadeListe();
            }
        }

        private void PrefillDateRangeFromActiveBudget()
        {
            try
            {
                var activeId = _db.HoleAktivenBudgetzeitraumId();
                if (activeId.HasValue)
                {
                    var bz = _db.HoleBudgetzeitraum(activeId.Value);
                    if (bz != null)
                    {
                        FilterVon = bz.Startdatum.Date;
                        FilterBis = bz.Enddatum.Date;

                        // NEU: aktiven Zeitraum merken (für Zeilen-Icon)
                        _activeStart = bz.Startdatum.Date;
                        _activeEnd = bz.Enddatum.Date;

                        return;
                    }
                }

                // Falls kein aktiver Zeitraum existiert:
                // Filter bewusst leer lassen (zeigt alle Daten)
                FilterVon = null;
                FilterBis = null;

                // NEU: kein aktiver Zeitraum => kein Override-Icon
                _activeStart = null;
                _activeEnd = null;
            }
            catch
            {
                // defensiv: Filter leer lassen
                FilterVon = null;
                FilterBis = null;

                // NEU
                _activeStart = null;
                _activeEnd = null;
            }
        }

        private void NeueBuchung()
        {
            try
            {
                var dlg = new TransactionsDialog();

                Window? owner = null;
                try
                {
                    owner = Application.Current?.Windows
                                .OfType<Window>()
                                .FirstOrDefault(w => w.IsActive)
                         ?? Application.Current?.MainWindow;
                }
                catch { }

                if (owner != null && !ReferenceEquals(owner, dlg))
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                if (dlg.ShowDialog() == true)
                    LadeListe();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dialogfehler (Neue Buchung): {ex.Message}", "Transaktionen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Bearbeiten()
        {
            try
            {
                if (AusgewaehlteTransaktion == null) return;

                var t = new Transaktion
                {
                    Id = AusgewaehlteTransaktion.Id,
                    Datum = AusgewaehlteTransaktion.Datum,

                    BudgetDatum = AusgewaehlteTransaktion.BudgetDatum, // NEU: Budgetdatum übernehmen

                    VonKontoId = AusgewaehlteTransaktion.VonKontoId,
                    NachKontoId = AusgewaehlteTransaktion.NachKontoId,
                    Betrag = AusgewaehlteTransaktion.Betrag,
                    AdresseId = AusgewaehlteTransaktion.AdresseId,
                    GeldinstitutId = AusgewaehlteTransaktion.GeldinstitutId,
                    Notiz = AusgewaehlteTransaktion.Notiz
                };

                var dlg = new TransactionsDialog(t);

                Window? owner = null;
                try
                {
                    owner = Application.Current?.Windows
                                .OfType<Window>()
                                .FirstOrDefault(w => w.IsActive)
                         ?? Application.Current?.MainWindow;
                }
                catch { }

                if (owner != null && !ReferenceEquals(owner, dlg))
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                if (dlg.ShowDialog() == true)
                    LadeListe();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dialogfehler (Bearbeiten): {ex.Message}", "Transaktionen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Loeschen()
        {
            if (AusgewaehlteTransaktion == null) return;

            var ask = MessageBox.Show("Ausgewählte Transaktion wirklich löschen?",
                                      "Löschen bestätigen",
                                      MessageBoxButton.YesNo,
                                      MessageBoxImage.Warning);
            if (ask != MessageBoxResult.Yes) return;

            try
            {
                _db.LoescheTransaktion(AusgewaehlteTransaktion.Id);
                LadeListe();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Löschen nicht möglich",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (SqlException ex)
            {
                MessageBox.Show($"Datenbankfehler beim Löschen: {ex.Message}", "Transaktionen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unerwarteter Fehler beim Löschen: {ex.Message}", "Transaktionen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void LadeListe()
        {
            Transaktionen.Clear();

            // Kontonamen cachen
            var kontoMap = new Dictionary<int, string>();
            foreach (var k in _db.LadeKontenplan())
            {
                string unter = string.IsNullOrWhiteSpace(k.Untergruppe) ? "" : $"  {k.Untergruppe}";
                kontoMap[k.Id] = $"{k.Kontonummer:D4}{unter}  {k.Detail}";
            }

            var term = (SearchText ?? string.Empty).Trim();
            var list = _db.SucheTransaktionen(term, FilterVon, FilterBis, FilterAdresse);

            // Tokens nur für Treffer-Hinweis
            var tokens = term.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var t in list)
            {
                string von = t.VonKontoId.HasValue
                    ? (kontoMap.TryGetValue(t.VonKontoId.Value, out var vk) ? vk : $"Konto #{t.VonKontoId}")
                    : (!t.GeldinstitutId.HasValue && !string.IsNullOrWhiteSpace(t.AdresseName)
                        ? t.AdresseName
                        : (t.BankName ?? "Bank"));

                string nach = t.NachKontoId.HasValue
                    ? (kontoMap.TryGetValue(t.NachKontoId.Value, out var nk) ? nk : $"Konto #{t.NachKontoId}")
                    : (t.BankName ?? "Bank");

                var details = _db.LoadAttachmentDetailsByTransaktionId(t.Id);

                string? tooltip = null;
                if (details.Count > 0)
                {
                    var names = details.Select(d => d.FileName).Take(3).ToList();
                    tooltip = string.Join("\n", names);
                    if (details.Count > names.Count) tooltip += $"\n… (+{details.Count - names.Count})";
                }

                string? hitInfo = null;
                if (tokens.Length > 0)
                {
                    bool noteHit = !string.IsNullOrWhiteSpace(t.Notiz) && tokens.Any(tok => t.Notiz!.IndexOf(tok, StringComparison.CurrentCultureIgnoreCase) >= 0);
                    bool adrHit = !string.IsNullOrWhiteSpace(t.AdresseName) && tokens.Any(tok => t.AdresseName!.IndexOf(tok, StringComparison.CurrentCultureIgnoreCase) >= 0);
                    bool bankHit = !string.IsNullOrWhiteSpace(t.BankName) && tokens.Any(tok => t.BankName!.IndexOf(tok, StringComparison.CurrentCultureIgnoreCase) >= 0);

                    // Zahlen-Treffer: Transaktions-Nr. bzw. Betrag
                    bool idHit = tokens.Any(tok => tok == t.Id.ToString());

                    bool betragHit = tokens.Any(tok =>
                    {
                        var clean = tok.Replace("'", "");
                        return (decimal.TryParse(clean, System.Globalization.NumberStyles.Number,
                                    System.Globalization.CultureInfo.CurrentCulture, out var d)
                             || decimal.TryParse(clean, System.Globalization.NumberStyles.Number,
                                    System.Globalization.CultureInfo.InvariantCulture, out d))
                            && Math.Abs(t.Betrag) == d;
                    });

                    var (total, fileHits, textHits) = _db.GetAttachmentHitCountsForTokens(t.Id, tokens);
                    var parts = new List<string>(7);
                    if (fileHits > 0) parts.Add($"Datei({fileHits})");
                    if (textHits > 0) parts.Add($"OCR({textHits})");
                    if (noteHit) parts.Add("Notiz");
                    if (adrHit) parts.Add("Adresse");
                    if (bankHit) parts.Add("Bank");
                    if (idHit) parts.Add("Nr.");
                    if (betragHit) parts.Add("Betrag");
                    if (parts.Count > 0) hitInfo = "Treffer in: " + string.Join(", ", parts);
                }

                Transaktionen.Add(new TransaktionRowExt
                {
                    Id = t.Id,
                    Datum = t.Datum,

                    BudgetDatum = t.BudgetDatum, // NEU: Budgetdatum aus DB übernehmen

                    // NEU: Icon, wenn Bankdatum außerhalb aktivem Zeitraum liegt, aber BudgetDatum gesetzt ist
                    HasBudgetDatumOverride =
                    t.BudgetDatum.HasValue
                    && _activeStart.HasValue && _activeEnd.HasValue
                    && (t.Datum.Date < _activeStart.Value || t.Datum.Date > _activeEnd.Value),

                    // NEU: Tooltip
                    BudgetDatumTooltip =
                    t.BudgetDatum.HasValue
                    ? $"Budgetdatum: {t.BudgetDatum:dd.MM.yyyy}\nBankdatum: {t.Datum:dd.MM.yyyy}"
                    : null,

                    VonAnzeige = von,
                    NachAnzeige = nach,
                    Betrag = t.Betrag,
                    AdresseName = t.AdresseName,
                    BankName = t.BankName,
                    Notiz = t.Notiz,

                    VonKontoId = t.VonKontoId,
                    NachKontoId = t.NachKontoId,
                    AdresseId = t.AdresseId,
                    GeldinstitutId = t.GeldinstitutId,

                    AttachmentCount = details.Count,
                    HasAttachments = details.Count > 0,
                    AttachmentsTooltip = tooltip,

                    SearchHitInfo = hitInfo
                });
            }
        }


        private void WebRecherche(object? p)
        {
            var row = p as TransaktionRowExt ?? AusgewaehlteTransaktion;
            if (row == null) return;

            try
            {
                if (!WebRechercheService.OpenSearch(row.Notiz, row.AdresseName))
                    MessageBox.Show("Kein verwertbarer Buchungstext für die Recherche vorhanden.",
                        "Web-Recherche", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Recherche konnte nicht geöffnet werden:\n" + ex.Message,
                    "Web-Recherche", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DuplikateSuchen()
        {
            try
            {
                var dlg = new DuplikateDialog
                {
                    Owner = Application.Current?.Windows
                                .OfType<Window>()
                                .FirstOrDefault(w => w.IsActive)
                            ?? Application.Current?.MainWindow
                };

                dlg.ShowDialog();

                if (dlg.HatGeloescht)
                    LadeListe();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dialogfehler (Duplikate): {ex.Message}", "Transaktionen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenBankImport()
        {
            try
            {
                var wnd = new BankImportWindow();

                Window? owner = null;
                try
                {
                    owner = Application.Current?.Windows
                                .OfType<Window>()
                                .FirstOrDefault(w => w.IsActive)
                         ?? Application.Current?.MainWindow;
                }
                catch { }

                if (owner != null && !ReferenceEquals(owner, wnd))
                    wnd.Owner = owner;
                else
                    wnd.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                wnd.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fensterfehler (Bankimport): {ex.Message}", "Transaktionen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenReport()
        {
            try
            {
                var window = new TransactionReportWindow();

                Window? owner = null;
                try
                {
                    owner = Application.Current?.Windows
                                .OfType<Window>()
                                .FirstOrDefault(w => w.IsActive)
                         ?? Application.Current?.MainWindow;
                }
                catch { }

                if (owner != null && owner.IsVisible && !ReferenceEquals(owner, window))
                {
                    window.Owner = owner;
                    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                else
                {
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fensterfehler (Transaktionsbericht): {ex.Message}",
                    "Transaktionen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private int AsTransaktionIdFromParam(object? p)
        {
            if (p is int i && i > 0) return i;
            if (p is string s && int.TryParse(s, out var j) && j > 0) return j;
            return AusgewaehlteTransaktion?.Id ?? 0;
        }

        private void AttachPdfFromRow(object? p)
        {
            int id = AsTransaktionIdFromParam(p);
            if (id <= 0) return;

            try
            {
                // Mit DMS-Lizenz: zuerst die freien DMS-Dokumente anbieten
                // (mit Ausweich-Button auf die klassische Explorer-Auswahl).
                // Ohne DMS-Lizenz: direkt Explorer wie bisher.
                if (AppModules.IsDmsEnabled)
                {
                    var dmsDlg = new DmsDokumentWahlDialog
                    {
                        Owner = Application.Current?.Windows
                                    .OfType<Window>()
                                    .FirstOrDefault(w => w.IsActive)
                                ?? Application.Current?.MainWindow
                    };

                    if (dmsDlg.ShowDialog() != true) return;

                    if (!dmsDlg.ExplorerGewaehlt && dmsDlg.AusgewaehltesDokumentId > 0)
                    {
                        var service = new AttachmentService();
                        service.LinkToTransaktion(dmsDlg.AusgewaehltesDokumentId, id);

                        try { _db.MarkDocumentSeen(dmsDlg.AusgewaehltesDokumentId); } catch { }

                        RefreshUndReselect(id);
                        return;
                    }
                    // ExplorerGewaehlt: unten normal weiter
                }

                AttachPdfViaExplorer(id);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Anhängen fehlgeschlagen: " + ex.Message, "Datei anhängen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AttachPdfViaExplorer(int id)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Dokumente und Bilder|*.pdf;*.jpg;*.jpeg;*.png|PDF|*.pdf|Bilder|*.jpg;*.jpeg;*.png|Alle Dateien|*.*",
                Title = "Datei anhängen",
                Multiselect = false,
                CheckFileExists = true
            };
            var ok = dlg.ShowDialog();
            if (ok != true) return;

            var service = new AttachmentService();
            service.AttachAndSave(id, dlg.FileName);

            RefreshUndReselect(id);
        }

        private void RefreshUndReselect(int transaktionId)
        {
            LadeListe();
            foreach (var row in Transaktionen)
                if (row.Id == transaktionId) { AusgewaehlteTransaktion = row; break; }
        }

        private void OpenAttachmentFromRow(object? p)
        {
            int id = AsTransaktionIdFromParam(p);
            if (id <= 0) return;

            try
            {
                var details = _db.LoadAttachmentDetailsByTransaktionId(id);
                if (details.Count == 0)
                {
                    MessageBox.Show("Zu dieser Buchung sind keine Anhänge vorhanden.", "Anhänge öffnen",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var (root, _) = _db.GetAttachmentSettings();
                if (string.IsNullOrWhiteSpace(root))
                {
                    var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    root = System.IO.Path.Combine(doc, "MyCoinFlow", "Attachments");
                }

                if (details.Count == 1)
                {
                    var d = details[0];
                    var full = System.IO.Path.Combine(root, d.FolderRel, d.FileName);
                    if (System.IO.File.Exists(full))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(full) { UseShellExecute = true });
                    }
                    else
                    {
                        var dir = System.IO.Path.Combine(root, d.FolderRel);
                        if (System.IO.Directory.Exists(dir))
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                    }
                    return;
                }

                var dlg = new MyCoinFlow.Views.AttachmentsDialog(id, allowDelete: false)
                {
                    Owner = Application.Current?.Windows?.OfType<Window>()?.FirstOrDefault(w => w.IsActive)
                            ?? Application.Current?.MainWindow
                };
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Öffnen fehlgeschlagen: " + ex.Message, "Anhänge öffnen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ManageAttachmentsFromRow(object? p)
        {
            int id = AsTransaktionIdFromParam(p);
            if (id <= 0) return;

            try
            {
                var dlg = new AttachmentsDialog(id)
                {
                    Owner = Application.Current?.Windows?.OfType<Window>()?.FirstOrDefault(w => w.IsActive)
                            ?? Application.Current?.MainWindow
                };

                var result = dlg.ShowDialog();
                if (result == true)
                {
                    var keepId = id;
                    LadeListe();
                    foreach (var row in Transaktionen)
                        if (row.Id == keepId) { AusgewaehlteTransaktion = row; break; }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Verwalten fehlgeschlagen: " + ex.Message, "Anhänge verwalten",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
