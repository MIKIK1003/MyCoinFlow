using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.Views;

namespace MyCoinFlow.ViewModels
{
    public class DmsViewModel : BaseViewModel, IDisposable
    {
        private readonly DatabaseService _db = new();
        private readonly AttachmentService _attachSvc = new();
        private List<DmsDocument> _allDocuments = new();

        private ObservableCollection<DmsDocument> _dokumente = new();
        public ObservableCollection<DmsDocument> Dokumente
        {
            get => _dokumente;
            private set { _dokumente = value; OnPropertyChanged(); }
        }

        private ObservableCollection<DmsDocumentGroup> _gruppen = new();
        public ObservableCollection<DmsDocumentGroup> Gruppen
        {
            get => _gruppen;
            private set { _gruppen = value; OnPropertyChanged(); }
        }

        private ObservableCollection<DmsVersionEntry> _versionen = new();
        public ObservableCollection<DmsVersionEntry> Versionen
        {
            get => _versionen;
            private set { _versionen = value; OnPropertyChanged(); }
        }

        private ObservableCollection<DmsActivityEntry> _aktivitaeten = new();
        public ObservableCollection<DmsActivityEntry> Aktivitaeten
        {
            get => _aktivitaeten;
            private set { _aktivitaeten = value; OnPropertyChanged(); }
        }

        private DmsDocument? _ausgewaehltesDokument;
        public DmsDocument? AusgewaehltesDokument
        {
            get => _ausgewaehltesDokument;
            set
            {
                if (ReferenceEquals(_ausgewaehltesDokument, value)) return;
                _ausgewaehltesDokument = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HatAuswahl));

                if (value != null && value.IstNeu)
                {
                    try { _db.MarkDocumentSeen(value.Id); } catch { }
                    value.IstNeu = false;
                }
                LoadDocumentFile();
            }
        }

        public bool HatAuswahl => AusgewaehltesDokument != null;

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); }
        }

        private string _kategorieFilter = "Alle";
        public string KategorieFilter
        {
            get => _kategorieFilter;
            set { _kategorieFilter = value; OnPropertyChanged(); ApplyFilters(); }
        }

        private string _bearbeitungsstatusFilter = "Alle";
        public string BearbeitungsstatusFilter
        {
            get => _bearbeitungsstatusFilter;
            set { _bearbeitungsstatusFilter = value; OnPropertyChanged(); ApplyFilters(); }
        }

        private string _gruppierung = "Kategorie";
        public string Gruppierung
        {
            get => _gruppierung;
            set { _gruppierung = value; OnPropertyChanged(); BuildGroups(Dokumente); }
        }

        private bool _nurFavoriten;
        public bool NurFavoriten
        {
            get => _nurFavoriten;
            set { _nurFavoriten = value; OnPropertyChanged(); ApplyFilters(); }
        }

        private bool _nurUeberfaellige;
        public bool NurUeberfaellige
        {
            get => _nurUeberfaellige;
            set { _nurUeberfaellige = value; OnPropertyChanged(); ApplyFilters(); }
        }

        private ObservableCollection<string> _kategorien = new();
        public ObservableCollection<string> Kategorien
        {
            get => _kategorien;
            private set { _kategorien = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> Gruppierungen { get; } = new() { "Kategorie", "Belegart", "Bearbeitungsstatus" };
        public ObservableCollection<string> BearbeitungsstatusOptionen { get; } = new() { "Alle", "Neu", "In Prüfung", "Freigegeben", "Erledigt" };

        public int AnzahlDokumente => _allDocuments.Count;
        public int AnzahlTreffer => Dokumente.Count;
        public int AnzahlNeu => _allDocuments.Count(d => d.Bearbeitungsstatus == DmsBearbeitungsstatus.Neu);
        public int AnzahlUeberfaellig => _allDocuments.Count(d => d.IstUeberfaellig);
        public int AnzahlFavoriten => _allDocuments.Count(d => d.IstFavorit);

        public bool WatcherIsRunning => DmsWatcherService.Instance.IsRunning;
        public bool WatcherIsBusy => DmsWatcherService.Instance.IsBusy;
        public string WatcherCurrentFile => DmsWatcherService.Instance.CurrentFileName;
        public string WatcherPhase => DmsWatcherService.Instance.CurrentPhase;
        public int WatcherQueueCount => DmsWatcherService.Instance.QueueCount;

        public ICommand SuchenCommand { get; }
        public ICommand ScannenCommand { get; }
        public ICommand HochladenCommand { get; }
        public ICommand BearbeitenCommand { get; }
        public ICommand OeffnenCommand { get; }
        public ICommand LoeschenCommand { get; }
        public ICommand TransaktionZuweisenCommand { get; }
        public ICommand VerknuepfungLoesenCommand { get; }
        public ICommand SucheErneutCommand { get; }
        public ICommand ZurTransaktionCommand { get; }
        public ICommand AlleErneutSuchenCommand { get; }
        public ICommand VerlaufAnzeigenCommand { get; }
        public ICommand FavoritUmschaltenCommand { get; }
        public ICommand NeueVersionCommand { get; }
        public ICommand VersionOeffnenCommand { get; }

        public DmsViewModel()
        {
            _db.EnsureAttachmentsSchema();
            _attachSvc.InitializeExistingDocumentHashes();

            SuchenCommand = new RelayCommand(_ => Laden());
            ScannenCommand = new RelayCommand(_ => Scannen());
            HochladenCommand = new RelayCommand(_ => Hochladen());
            BearbeitenCommand = new RelayCommand(p => Bearbeiten(p as DmsDocument ?? AusgewaehltesDokument));
            OeffnenCommand = new RelayCommand(p => Oeffnen(p as DmsDocument ?? AusgewaehltesDokument));
            LoeschenCommand = new RelayCommand(p => Loeschen(p as DmsDocument ?? AusgewaehltesDokument));
            TransaktionZuweisenCommand = new RelayCommand(p => TransaktionZuweisen(p as DmsDocument ?? AusgewaehltesDokument));
            VerknuepfungLoesenCommand = new RelayCommand(p => VerknuepfungLoesen(p as DmsDocument ?? AusgewaehltesDokument));
            SucheErneutCommand = new RelayCommand(p => SucheErneut(p as DmsDocument ?? AusgewaehltesDokument));
            ZurTransaktionCommand = new RelayCommand(p => ZurTransaktion(p as DmsDocument ?? AusgewaehltesDokument));
            AlleErneutSuchenCommand = new RelayCommand(_ => DmsWatcherService.Instance.RequeueAllUnmatched());
            VerlaufAnzeigenCommand = new RelayCommand(_ => DmsHistoryWindow.ShowOrActivate(Application.Current?.MainWindow));
            FavoritUmschaltenCommand = new RelayCommand(p => FavoritUmschalten(p as DmsDocument ?? AusgewaehltesDokument));
            NeueVersionCommand = new RelayCommand(_ => NeueVersion());
            VersionOeffnenCommand = new RelayCommand(p => VersionOeffnen(p as DmsVersionEntry));

            DmsWatcherService.Instance.PropertyChanged += Watcher_PropertyChanged;
            DmsWatcherService.Instance.DocumentProcessed += Watcher_DocumentProcessed;
            LadeKategorien();
            Laden();
        }

        private void Watcher_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(DmsWatcherService.IsRunning): OnPropertyChanged(nameof(WatcherIsRunning)); break;
                case nameof(DmsWatcherService.IsBusy): OnPropertyChanged(nameof(WatcherIsBusy)); break;
                case nameof(DmsWatcherService.CurrentFileName): OnPropertyChanged(nameof(WatcherCurrentFile)); break;
                case nameof(DmsWatcherService.CurrentPhase): OnPropertyChanged(nameof(WatcherPhase)); break;
                case nameof(DmsWatcherService.QueueCount): OnPropertyChanged(nameof(WatcherQueueCount)); break;
            }
        }

        private void Watcher_DocumentProcessed(object? sender, EventArgs e) =>
            Application.Current?.Dispatcher.Invoke(() => { LadeKategorien(); Laden(); });

        public void Dispose()
        {
            DmsWatcherService.Instance.PropertyChanged -= Watcher_PropertyChanged;
            DmsWatcherService.Instance.DocumentProcessed -= Watcher_DocumentProcessed;
        }

        private void LadeKategorien()
        {
            var values = new ObservableCollection<string> { "Alle" };
            foreach (var category in _db.GetDistinctKategorien()) values.Add(category);
            Kategorien = values;
        }

        private void Laden()
        {
            var selectedId = AusgewaehltesDokument?.Id;
            _allDocuments = _db.LoadAllDocuments(SearchText, null);
            ApplyFilters();
            AusgewaehltesDokument = selectedId.HasValue
                ? Dokumente.FirstOrDefault(d => d.Id == selectedId.Value)
                : null;
            NotifyCounters();
        }

        private void ApplyFilters()
        {
            IEnumerable<DmsDocument> result = _allDocuments;
            if (KategorieFilter != "Alle") result = result.Where(d => d.Kategorie == KategorieFilter);
            if (BearbeitungsstatusFilter != "Alle")
                result = result.Where(d => d.BearbeitungsstatusAnzeige == BearbeitungsstatusFilter);
            if (NurFavoriten) result = result.Where(d => d.IstFavorit);
            if (NurUeberfaellige) result = result.Where(d => d.IstUeberfaellig);

            Dokumente = new ObservableCollection<DmsDocument>(result);
            BuildGroups(Dokumente);
            OnPropertyChanged(nameof(AnzahlTreffer));
        }

        private void BuildGroups(IEnumerable<DmsDocument> documents)
        {
            Func<DmsDocument, string> keySelector = Gruppierung switch
            {
                "Belegart" => d => d.BelegartAnzeige,
                "Bearbeitungsstatus" => d => d.BearbeitungsstatusAnzeige,
                _ => d => d.KategorieAnzeige
            };

            Gruppen = new ObservableCollection<DmsDocumentGroup>(documents
                .GroupBy(keySelector)
                .OrderBy(group => group.Key)
                .Select(group => new DmsDocumentGroup(group.Key, group.Key,
                    group.OrderByDescending(d => d.IstFavorit).ThenByDescending(d => d.DokumentDatum ?? d.ImportedAtUtc))));
        }

        private void NotifyCounters()
        {
            OnPropertyChanged(nameof(AnzahlDokumente));
            OnPropertyChanged(nameof(AnzahlNeu));
            OnPropertyChanged(nameof(AnzahlUeberfaellig));
            OnPropertyChanged(nameof(AnzahlFavoriten));
            OnPropertyChanged(nameof(AnzahlTreffer));
        }

        private void LoadDocumentFile()
        {
            if (AusgewaehltesDokument == null)
            {
                Versionen = new();
                Aktivitaeten = new();
                return;
            }

            var document = AusgewaehltesDokument;
            var versions = _db.LoadDmsVersions(document.Id);
            versions.Insert(0, new DmsVersionEntry
            {
                AttachmentId = document.Id,
                VersionNumber = document.AktuelleVersion,
                FileName = document.FileName,
                FolderRel = document.FolderRel,
                SizeBytes = document.SizeBytes ?? 0,
                CreatedAtUtc = document.LetzteAenderungAmUtc ?? document.ImportedAtUtc,
                CreatedBy = CurrentUserContext.Username,
                Comment = "Aktuelle Fassung",
                IsCurrent = true
            });
            Versionen = new ObservableCollection<DmsVersionEntry>(versions);
            Aktivitaeten = new ObservableCollection<DmsActivityEntry>(_db.LoadDmsActivities(document.Id));
        }

        private void Scannen()
        {
            var folder = DmsWatcherService.Instance.GetWorkingFolder();
            if (string.IsNullOrWhiteSpace(folder))
            {
                MessageBox.Show(Application.Current?.MainWindow,
                    "Bitte zuerst in den Einstellungen (Dateianhänge und OCR > Verzeichnisse) einen Arbeitsordner festlegen.",
                    "Scannen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try { ScannerService.ScanToFolder(folder); }
            catch (Exception ex) { ShowError("Scannen fehlgeschlagen", ex); }
        }

        private void Hochladen()
        {
            var dialog = new DmsDocumentDialog { Owner = Application.Current?.MainWindow };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var (_, attachmentId) = _attachSvc.AttachFreestanding(dialog.AusgewaehlteDateiPfad!, dialog.Titel, dialog.Kategorie);
                _db.UpdateDmsDocument(attachmentId, CreateChanges(dialog, dialog.Betrag));
                LadeKategorien();
                Laden();
            }
            catch (Exception ex) { ShowError("Hochladen fehlgeschlagen", ex); }
        }

        private void Bearbeiten(DmsDocument? document)
        {
            if (document == null) return;
            var dialog = new DmsDocumentDialog(document) { Owner = Application.Current?.MainWindow };
            if (dialog.ShowDialog() != true) return;
            try
            {
                // Bei verknüpften Dokumenten bleibt der erkannte Dokumentbetrag unverändert;
                // der schreibgeschützte Dialogwert zeigt dort den Betrag der Transaktion.
                var recognizedAmount = document.EntityType != null ? document.ErkannterBetrag : dialog.Betrag;
                _db.UpdateDmsDocument(document.Id, CreateChanges(dialog, recognizedAmount));
                LadeKategorien();
                Laden();
            }
            catch (Exception ex) { ShowError("Speichern fehlgeschlagen", ex); }
        }

        private void Oeffnen(DmsDocument? document)
        {
            if (document == null) return;
            try { _attachSvc.OpenAttachment(document.Id); LoadDocumentFile(); }
            catch (Exception ex) { ShowError("Öffnen fehlgeschlagen", ex); }
        }

        private void FavoritUmschalten(DmsDocument? document)
        {
            if (document == null) return;
            try { _db.SetDmsFavorite(document.Id, !document.IstFavorit); Laden(); }
            catch (Exception ex) { ShowError("Favorit konnte nicht geändert werden", ex); }
        }

        private void NeueVersion()
        {
            if (AusgewaehltesDokument == null) return;
            var dialog = new DmsNewVersionDialog { Owner = Application.Current?.MainWindow };
            if (dialog.ShowDialog() != true) return;
            try { _attachSvc.ReplaceWithNewVersion(AusgewaehltesDokument.Id, dialog.SelectedFilePath!, dialog.Comment); Laden(); }
            catch (Exception ex) { ShowError("Neue Version konnte nicht eingespielt werden", ex); }
        }

        private void VersionOeffnen(DmsVersionEntry? version)
        {
            if (version == null) return;
            try { _attachSvc.OpenVersion(version); }
            catch (Exception ex) { ShowError("Version konnte nicht geöffnet werden", ex); }
        }

        private void Loeschen(DmsDocument? document)
        {
            if (document == null) return;
            var result = MessageBox.Show(Application.Current?.MainWindow,
                $"Dokument „{document.TitelAnzeige}“ aus dem aktiven DMS entfernen?\n\n" +
                "Die aktuelle Datei und alle Versionen werden in das wiederherstellbare Archiv verschoben.",
                "Entfernen bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            try { _attachSvc.DeleteAttachment(document.Id); Laden(); }
            catch (Exception ex) { ShowError("Dokument konnte nicht entfernt werden", ex); }
        }

        private void TransaktionZuweisen(DmsDocument? document)
        {
            if (document == null) return;
            var dialog = new DmsAssignTransactionDialog(null) { Owner = Application.Current?.MainWindow };
            if (dialog.ShowDialog() != true || !dialog.AusgewaehlteTransaktionId.HasValue) return;
            try { _attachSvc.LinkToTransaktion(document.Id, dialog.AusgewaehlteTransaktionId.Value); LadeKategorien(); Laden(); }
            catch (Exception ex) { ShowError("Zuweisen fehlgeschlagen", ex); }
        }

        private void VerknuepfungLoesen(DmsDocument? document)
        {
            if (document?.EntityType != "Transaktion") return;
            if (MessageBox.Show(Application.Current?.MainWindow,
                    "Verknüpfung zur Transaktion lösen? Das Dokument bleibt vollständig im DMS erhalten.",
                    "Verknüpfung lösen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try { _attachSvc.UnlinkFromTransaktion(document.Id); Laden(); }
            catch (Exception ex) { ShowError("Verknüpfung konnte nicht gelöst werden", ex); }
        }

        private void ZurTransaktion(DmsDocument? document)
        {
            if (document == null) return;
            var transactionId = document.EntityType == "Transaktion" ? document.EntityId ?? document.TransaktionId : document.TransaktionId;
            if (transactionId is not > 0)
            {
                MessageBox.Show(Application.Current?.MainWindow, "Dieses Dokument ist keiner Transaktion zugeordnet.",
                    "Zur Transaktion springen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            AppNavigation.ZeigeTransaktion(transactionId.Value);
        }

        private void SucheErneut(DmsDocument? document)
        {
            if (document != null) DmsWatcherService.Instance.RequeueForMatching(document.Id, document.TitelAnzeige);
        }

        private static void ShowError(string title, Exception exception) =>
            MessageBox.Show(Application.Current?.MainWindow, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);

        private static DmsDocumentChanges CreateChanges(DmsDocumentDialog dialog, decimal? recognizedAmount) => new(
            dialog.Titel,
            dialog.Kategorie,
            dialog.Belegart,
            dialog.Beschreibung,
            dialog.Schlagwoerter,
            dialog.Notiz,
            dialog.Bearbeitungsstatus,
            dialog.Verantwortlich,
            dialog.DokumentDatum,
            recognizedAmount,
            dialog.AdresseId,
            dialog.IstGarantieschein,
            dialog.GarantieAblaufDatum,
            dialog.FaelligAm,
            dialog.AufbewahrenBis);
    }
}
