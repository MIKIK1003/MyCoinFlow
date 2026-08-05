using System;
using System.Collections.ObjectModel;
using System.Linq;
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

        private ObservableCollection<DmsDocument> _dokumente = new();
        public ObservableCollection<DmsDocument> Dokumente
        {
            get => _dokumente;
            set { _dokumente = value; OnPropertyChanged(); }
        }

        private DmsDocument? _ausgewaehltesDokument;
        public DmsDocument? AusgewaehltesDokument
        {
            get => _ausgewaehltesDokument;
            set
            {
                _ausgewaehltesDokument = value;
                OnPropertyChanged();

                // Anklicken einer Zeile = Dokument gesehen -> Neu-Icon entfernen
                if (value != null && value.IstNeu)
                {
                    try { _db.MarkDocumentSeen(value.Id); } catch { /* Anzeige-Komfort, nie blockieren */ }
                    value.IstNeu = false;
                }
            }
        }

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
            set { _kategorieFilter = value; OnPropertyChanged(); Laden(); }
        }

        private ObservableCollection<string> _kategorien = new();
        public ObservableCollection<string> Kategorien
        {
            get => _kategorien;
            set { _kategorien = value; OnPropertyChanged(); }
        }

        // ---------------- DMS-Arbeitsordner: Fortschrittsanzeige ----------------
        // Reine Pass-Through-Properties auf den Singleton-Service, damit die View sich
        // wie gewohnt gegen das ViewModel bindet (kein direkter View->Service-Zugriff).

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
        public ICommand SucheErneutCommand { get; }
        public ICommand AlleErneutSuchenCommand { get; }
        public ICommand VerlaufAnzeigenCommand { get; }

        public DmsViewModel()
        {
            _db.EnsureAttachmentsSchema();

            SuchenCommand = new RelayCommand(_ => Laden());
            ScannenCommand = new RelayCommand(_ => Scannen());
            HochladenCommand = new RelayCommand(_ => Hochladen());
            BearbeitenCommand = new RelayCommand(p => Bearbeiten(p as DmsDocument ?? AusgewaehltesDokument));
            OeffnenCommand = new RelayCommand(p => Oeffnen(p as DmsDocument ?? AusgewaehltesDokument));
            LoeschenCommand = new RelayCommand(p => Loeschen(p as DmsDocument ?? AusgewaehltesDokument));
            TransaktionZuweisenCommand = new RelayCommand(p => TransaktionZuweisen(p as DmsDocument ?? AusgewaehltesDokument));
            SucheErneutCommand = new RelayCommand(p => SucheErneut(p as DmsDocument ?? AusgewaehltesDokument));
            AlleErneutSuchenCommand = new RelayCommand(_ => DmsWatcherService.Instance.RequeueAllUnmatched());
            VerlaufAnzeigenCommand = new RelayCommand(_ => DmsHistoryWindow.ShowOrActivate(Application.Current?.MainWindow));

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

        private void Watcher_DocumentProcessed(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                LadeKategorien();
                Laden();
            });
        }

        public void Dispose()
        {
            DmsWatcherService.Instance.PropertyChanged -= Watcher_PropertyChanged;
            DmsWatcherService.Instance.DocumentProcessed -= Watcher_DocumentProcessed;
        }

        private void LadeKategorien()
        {
            var liste = new ObservableCollection<string> { "Alle" };
            foreach (var k in _db.GetDistinctKategorien())
                liste.Add(k);
            Kategorien = liste;
        }

        private void Laden()
        {
            var kategorie = KategorieFilter == "Alle" ? null : KategorieFilter;
            var rows = _db.LoadAllDocuments(SearchText, kategorie);
            Dokumente = new ObservableCollection<DmsDocument>(rows);
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

            try
            {
                ScannerService.ScanToFolder(folder);
                // Kein sofortiges Laden() nötig: der Watcher (falls aktiv) greift automatisch,
                // sobald die Scan-Datei im Arbeitsordner liegt, und aktualisiert die Liste über
                // Watcher_DocumentProcessed, sobald die Verarbeitung fertig ist.
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(Application.Current?.MainWindow, "Scannen fehlgeschlagen: " + ex.Message,
                    "Scannen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Hochladen()
        {
            var dlg = new DmsDocumentDialog(null) { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var (_, attachmentId) = _attachSvc.AttachFreestanding(dlg.AusgewaehlteDateiPfad!, dlg.Titel, dlg.Kategorie);
                if (dlg.IstGarantieschein)
                    _db.UpdateAttachmentGarantie(attachmentId, true, dlg.GarantieAblaufDatum);
                LadeKategorien();
                Laden();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(Application.Current?.MainWindow, "Hochladen fehlgeschlagen: " + ex.Message,
                    "DMS", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Bearbeiten(DmsDocument? doc)
        {
            if (doc == null) return;

            var dlg = new DmsDocumentDialog(doc) { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;

            try
            {
                _db.UpdateAttachmentMeta(doc.Id, dlg.Titel, dlg.Kategorie);
                _db.UpdateAttachmentGarantie(doc.Id, dlg.IstGarantieschein, dlg.GarantieAblaufDatum);
                LadeKategorien();
                Laden();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(Application.Current?.MainWindow, "Speichern fehlgeschlagen: " + ex.Message,
                    "DMS", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Oeffnen(DmsDocument? doc)
        {
            if (doc == null) return;
            try
            {
                _attachSvc.OpenAttachment(doc.Id);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(Application.Current?.MainWindow, "Öffnen fehlgeschlagen: " + ex.Message,
                    "DMS", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Loeschen(DmsDocument? doc)
        {
            if (doc == null) return;

            var ask = MessageBox.Show(Application.Current?.MainWindow,
                $"Dokument „{doc.TitelAnzeige}“ wirklich löschen?\nDie Datei wird vom Datenträger entfernt.",
                "Löschen bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) return;

            try
            {
                _attachSvc.DeleteAttachment(doc.Id);
                Laden();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(Application.Current?.MainWindow, "Löschen fehlgeschlagen: " + ex.Message,
                    "DMS", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TransaktionZuweisen(DmsDocument? doc)
        {
            if (doc == null) return;

            var dlg = new DmsAssignTransactionDialog(null) { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true || !dlg.AusgewaehlteTransaktionId.HasValue) return;

            try
            {
                _attachSvc.LinkToTransaktion(doc.Id, dlg.AusgewaehlteTransaktionId.Value);
                LadeKategorien();
                Laden();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(Application.Current?.MainWindow, "Zuweisen fehlgeschlagen: " + ex.Message,
                    "DMS", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SucheErneut(DmsDocument? doc)
        {
            if (doc == null) return;
            DmsWatcherService.Instance.RequeueForMatching(doc.Id, doc.TitelAnzeige);
        }
    }
}
