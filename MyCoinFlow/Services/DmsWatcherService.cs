using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MyCoinFlow.Models;
using MyCoinFlow.Views;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Überwacht den in den Einstellungen konfigurierten DMS-Arbeitsordner und verarbeitet neu
    /// eintreffende Dateien sequenziell: OCR/Textlayer -> Datums-/Titel-Erkennung -> Ablage im
    /// Ablageordner (AttachmentService.AttachFromWatcher) -> Transaktions-Matching
    /// (DatabaseService.FindCandidateTransaktionenForMatch). Läuft als Singleton über die
    /// gesamte App-Laufzeit; Start/Stop wird aus App.xaml.cs bzw. nach dem Speichern der
    /// Einstellungen (AdminPathsView) gesteuert.
    /// Über dieselbe Warteschlange lässt sich auch das Matching für bereits archivierte,
    /// noch unverknüpfte Dokumente erneut anstossen (RequeueForMatching/RequeueAllUnmatched) –
    /// z. B. wenn eine Rechnung im Arbeitsordner lag, bevor die passende Transaktion verbucht war.
    /// </summary>
    public sealed class DmsWatcherService : INotifyPropertyChanged
    {
        public static DmsWatcherService Instance { get; } = new DmsWatcherService();

        private const string KeyWorkingFolder = "DmsWorkingFolder";
        private const string KeyEnabled = "DmsWatcherEnabled";

        private readonly DatabaseService _db = new();
        private readonly AttachmentService _attachSvc = new();
        private readonly AdressErkennungService _adressSvc = new();

        private sealed record WorkItem(string DedupKey, string DisplayName, Action Run);

        private FileSystemWatcher? _fsWatcher;
        private BlockingCollection<WorkItem>? _queue;
        private CancellationTokenSource? _cts;
        private Task? _consumerTask;
        private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _inFlightLock = new();

        private static readonly HashSet<string> AllowedExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png" };

        private DmsWatcherService() { }

        // ---------------- Bindbare Statuswerte (Fortschrittsanzeige) ----------------

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string name)
        {
            var dispatcher = Application.Current?.Dispatcher;

            // Beim App-Beenden ist der Dispatcher bereits im Shutdown – ein Invoke
            // würde dann eine TaskCanceledException werfen (und im Debugger anhalten,
            // statt die Debug-Sitzung mit der App zu beenden). Status-Updates sind
            // zu diesem Zeitpunkt ohnehin bedeutungslos.
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            // Auch die ABONNENTEN des Events können beim Herunterfahren in den Dispatcher
            // laufen (z.B. Bindings im Hauptfenster) – deren TaskCanceled-/OperationCanceled-
            // Exceptions dürfen den Verarbeitungs-Thread nicht hochkommen lassen.
            void Feuern()
            {
                try { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
                catch (OperationCanceledException) { /* Shutdown-Rennen – still */ }
            }

            try
            {
                if (dispatcher.CheckAccess())
                    Feuern();
                else
                    dispatcher.BeginInvoke(new Action(Feuern));
            }
            catch (OperationCanceledException) { /* Shutdown-Rennen – still */ }
        }

        /// <summary>Ausgelöst, sobald ein Dokument fertig verarbeitet (abgelegt, ggf. verknüpft) wurde.</summary>
        public event EventHandler? DocumentProcessed;

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set { if (_isRunning == value) return; _isRunning = value; Raise(nameof(IsRunning)); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set { if (_isBusy == value) return; _isBusy = value; Raise(nameof(IsBusy)); }
        }

        private string _currentFileName = "";
        public string CurrentFileName
        {
            get => _currentFileName;
            private set { _currentFileName = value; Raise(nameof(CurrentFileName)); }
        }

        private string _currentPhase = "";
        public string CurrentPhase
        {
            get => _currentPhase;
            private set { _currentPhase = value; Raise(nameof(CurrentPhase)); }
        }

        private int _queueCount;
        public int QueueCount
        {
            get => _queueCount;
            private set { if (_queueCount == value) return; _queueCount = value; Raise(nameof(QueueCount)); }
        }

        // ---------------- Start/Stop/Restart ----------------

        public void Start()
        {
            if (IsRunning) return;

            string? folder = _db.GetAppSetting(KeyWorkingFolder);
            string? enabledSetting = _db.GetAppSetting(KeyEnabled);
            bool enabled = string.IsNullOrWhiteSpace(enabledSetting) || enabledSetting == "1";

            if (!enabled || string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                IsRunning = false;
                return;
            }

            _cts = new CancellationTokenSource();
            _queue = new BlockingCollection<WorkItem>();

            _fsWatcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                Filter = "*.*"
            };
            _fsWatcher.Created += OnFileEvent;
            _fsWatcher.Renamed += OnFileEvent;
            _fsWatcher.EnableRaisingEvents = true;

            _consumerTask = Task.Run(() => ConsumeQueue(_cts.Token));

            // Bereits vorhandene Dateien (z. B. während App geschlossen war abgelegt) auch erfassen.
            try
            {
                foreach (var file in Directory.GetFiles(folder))
                    EnqueueNewFile(file);
            }
            catch { /* Ordner evtl. nicht (mehr) lesbar – Watcher-Events greifen trotzdem künftig */ }

            IsRunning = true;
        }

        public void Stop()
        {
            if (!IsRunning && _fsWatcher == null) return;

            try
            {
                if (_fsWatcher != null)
                {
                    _fsWatcher.EnableRaisingEvents = false;
                    _fsWatcher.Created -= OnFileEvent;
                    _fsWatcher.Renamed -= OnFileEvent;
                    _fsWatcher.Dispose();
                    _fsWatcher = null;
                }

                _cts?.Cancel();
                _queue?.CompleteAdding();

                // Kurzer Timeout: reicht für den Normalfall (Queue leer/idle -> Consumer beendet
                // sich quasi sofort nach Cancel()). Läuft gerade eine lange Verarbeitung, warten
                // wir NICHT lange darauf (App-Beenden soll nicht spürbar hängen) - Dispose wird
                // dann einfach übersprungen (siehe unten), das ist beim Beenden folgenlos.
                bool finished;
                try { finished = _consumerTask == null || _consumerTask.Wait(TimeSpan.FromSeconds(2)); }
                catch { finished = true; /* Consumer ist (mit Fehler/Abbruch) bereits beendet */ }

                // Nur aufräumen, wenn der Hintergrund-Thread nachweislich fertig ist. Ist er das
                // nicht (z. B. noch mitten in einem langen OCR-Lauf oder einem offenen
                // Auswahl-Dialog), würde ein Dispose() hier eine ObjectDisposedException auf dem
                // Hintergrund-Thread auslösen, sobald der dort weiter auf _queue zugreift – das
                // hat genau das Hänge-/Fehlerbild beim Schliessen der App verursacht. Ein
                // ungenutztes Queue-Objekt beim Beenden liegen zu lassen ist dagegen harmlos,
                // der Prozess räumt gleich danach ohnehin alles auf.
                if (finished)
                {
                    try { _queue?.Dispose(); } catch { /* still */ }
                    _queue = null;
                    try { _cts?.Dispose(); } catch { /* still */ }
                    _cts = null;
                }

                lock (_inFlightLock) { _inFlight.Clear(); }
            }
            finally
            {
                IsRunning = false;
                IsBusy = false;
                CurrentFileName = "";
                CurrentPhase = "";
                QueueCount = 0;
            }
        }

        public void Restart()
        {
            Stop();
            Start();
        }

        /// <summary>Liefert den konfigurierten Arbeitsordner (oder null, falls nicht gesetzt).</summary>
        public string? GetWorkingFolder() => _db.GetAppSetting(KeyWorkingFolder);

        // ---------------- Manuelles erneutes Matching ----------------

        /// <summary>
        /// Stösst das Transaktions-Matching für ein bereits archiviertes, unverknüpftes Dokument
        /// erneut an (z. B. wenn die passende Buchung erst nachträglich erfasst wurde). Läuft
        /// über dieselbe Warteschlange wie neue Dateien, damit Fortschrittsanzeige und
        /// Mehrdeutigkeits-Dialog konsistent bleiben.
        /// </summary>
        public void RequeueForMatching(int attachmentId, string displayName)
        {
            EnqueueWork($"retry:{attachmentId}", displayName, () => RetryMatchForAttachment(attachmentId));
        }

        /// <summary>
        /// Stösst das Matching für alle aktuell unverknüpften DMS-Dokumente erneut an
        /// ("alles automatisch suchen").
        /// </summary>
        public void RequeueAllUnmatched()
        {
            var offene = _db.LoadAllDocuments(null, null).Where(d => d.EntityType == null).ToList();
            foreach (var doc in offene)
                RequeueForMatching(doc.Id, doc.TitelAnzeige);
        }

        private void RetryMatchForAttachment(int attachmentId)
        {
            var info = _db.GetAttachmentById(attachmentId);
            if (info == null) return;

            if (info.Value.TransaktionId.HasValue)
            {
                CurrentPhase = "Bereits einer Transaktion zugeordnet.";
                return;
            }

            CurrentPhase = "Lese gespeicherten Text…";
            var text = _db.GetAttachmentText(attachmentId);

            var retryFallback = info.Value.ImportedAtUtc.Date;
            var docDatum = DmsDocumentAnalyzer.ExtractDocumentDate(text, retryFallback);
            var (matchedAdresseId, _) = FindKnownAdresse(text);
            var betragsKandidaten = DmsDocumentAnalyzer.ExtractAmountCandidatesScored(text);

            // Ältere Dokumente (angelegt, bevor DokumentDatum eingeführt wurde) oder solche ohne
            // Treffer beim ersten Lauf haben evtl. noch kein DokumentDatum – hier nachtragen,
            // damit das Fälligkeits-Tracking ("Fällig am") auch für sie greift.
            _db.UpdateAttachmentDokumentDatum(attachmentId, docDatum);
            _db.UpdateAttachmentErkannterBetrag(attachmentId,
                betragsKandidaten.Count > 0 ? betragsKandidaten[0].Amount : (decimal?)null);

            CurrentPhase = "Suche passende Transaktion…";
            TryMatchTransaktion(attachmentId, docDatum, betragsKandidaten, matchedAdresseId,
                datumAusDokument: docDatum != retryFallback);
        }

        // ---------------- Queue ----------------

        private void OnFileEvent(object sender, FileSystemEventArgs e) => EnqueueNewFile(e.FullPath);

        private void EnqueueNewFile(string path) =>
            EnqueueWork(path, Path.GetFileName(path), () => ProcessFile(path));

        /// <summary>
        /// Legt Arbeit in die Warteschlange, falls der Watcher aktiv ist; läuft er nicht (z. B.
        /// kein Arbeitsordner konfiguriert), wird sofort direkt ausgeführt – manuelles Auslösen
        /// (Button) soll nicht von der Hintergrundüberwachung abhängen.
        /// </summary>
        private void EnqueueWork(string dedupKey, string displayName, Action action)
        {
            var item = new WorkItem(dedupKey, displayName, action);

            if (_queue == null || _queue.IsAddingCompleted)
            {
                RunItem(item);
                return;
            }

            lock (_inFlightLock)
            {
                if (_inFlight.Contains(dedupKey)) return;
                _inFlight.Add(dedupKey);
            }

            try
            {
                _queue.Add(item);
                QueueCount = _queue.Count;
            }
            catch (InvalidOperationException)
            {
                // Queue wurde inzwischen geschlossen (Stop() während Enqueue) – ignorieren.
            }
        }

        private void ConsumeQueue(CancellationToken ct)
        {
            // Lokale Referenz: _queue könnte von Stop() (anderer Thread) auf null gesetzt oder
            // disposed werden, während diese Schleife noch läuft (z. B. mitten in einem langen
            // OCR-Lauf). Über die lokale Variable bleibt die Schleife hier konsistent, auch wenn
            // das Feld selbst inzwischen zurückgesetzt wurde.
            var queue = _queue;
            if (queue == null) return;

            try
            {
                foreach (var item in queue.GetConsumingEnumerable(ct))
                {
                    QueueCount = queue.Count;
                    RunItem(item);

                    lock (_inFlightLock) { _inFlight.Remove(item.DedupKey); }
                    QueueCount = queue.Count;

                    if (ct.IsCancellationRequested) break;
                }
            }
            catch (OperationCanceledException)
            {
                // Erwarteter Abbruch: Stop() hat Cancel() aufgerufen, während GetConsumingEnumerable
                // auf das nächste Element wartete. Kein Fehler, einfach die Schleife verlassen.
            }

            IsBusy = false;
            CurrentFileName = "";
            CurrentPhase = "";
        }

        private void RunItem(WorkItem item)
        {
            IsBusy = true;
            CurrentFileName = item.DisplayName;

            try
            {
                item.Run();
            }
            catch (Exception ex)
            {
                CurrentPhase = "Fehler: " + ex.Message;
                LogError(item.DedupKey, ex);
            }
            finally
            {
                // Wichtig: GetConsumingEnumerable() blockiert nach diesem Element einfach weiter
                // auf das nächste (die Collection wird ja erst bei Stop() "completed"), die
                // Schleife läuft also im Normalbetrieb nie bis ans Ende durch. IsBusy muss
                // deshalb pro Element zurückgesetzt werden, sonst bleibt der Spinner nach der
                // letzten Datei für immer aktiv, obwohl nichts mehr passiert.
                try { _db.LogDmsProcessing(item.DisplayName, CurrentPhase); } catch { /* Historie ist best-effort */ }

                IsBusy = false;
                CurrentFileName = "";
                CurrentPhase = "";

                try { DocumentProcessed?.Invoke(this, EventArgs.Empty); }
                catch (OperationCanceledException) { /* App-Beenden während Verarbeitung – still */ }
            }
        }

        // ---------------- Verarbeitung einer einzelnen Datei ----------------

        private void ProcessFile(string path)
        {
            CurrentPhase = "Warte, bis Datei bereit ist…";
            if (!WaitUntilFileReady(path, TimeSpan.FromSeconds(30)))
            {
                CurrentPhase = "Übersprungen (Datei gesperrt)";
                return;
            }

            var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? "";
            if (!AllowedExtensions.Contains(ext))
            {
                CurrentPhase = "Übersprungen (Dateityp nicht unterstützt)";
                return;
            }

            CurrentPhase = "Texterkennung (OCR)…";
            var (text, textSource, ocrStatus) = ExtractText(path, ext);

            var fallbackDate = File.GetLastWriteTime(path).Date;
            var docDatum = DmsDocumentAnalyzer.ExtractDocumentDate(text, fallbackDate);
            var (matchedAdresseId, matchedAdresseName) = FindKnownAdresse(text);
            var titelSlug = DmsDocumentAnalyzer.ExtractTitle(text, matchedAdresseName,
                fallbackFromFileName: Path.GetFileNameWithoutExtension(path));
            var betragsKandidaten = DmsDocumentAnalyzer.ExtractAmountCandidatesScored(text);

            CurrentPhase = "Ablegen im Dokumentenarchiv…";
            var (_, attachmentId) = _attachSvc.AttachFromWatcher(path, docDatum, titelSlug, text, textSource, ocrStatus);

            // Erkannten Rechnungsbetrag (bester Kandidat) fürs DMS-Grid festhalten
            _db.UpdateAttachmentErkannterBetrag(attachmentId,
                betragsKandidaten.Count > 0 ? betragsKandidaten[0].Amount : (decimal?)null);

            CurrentPhase = "Suche passende Transaktion…";
            TryMatchTransaktion(attachmentId, docDatum, betragsKandidaten, matchedAdresseId,
                datumAusDokument: docDatum != fallbackDate);
        }

        /// <summary>
        /// Versucht, die im Dokument erwähnte Gegenpartei anhand des bestehenden Adressbuchs zu
        /// erkennen: zuerst über bereits gelernte Aliase (AdressErkennungService, wie beim
        /// Bank-Import), sonst per direktem Namensvergleich gegen alle bekannten Adressen. Ein
        /// Firmenlogo im PDF ist meist nur Bild/kein Text – ohne Treffer bleibt der Titel schlicht
        /// beim ursprünglichen Dateinamen (siehe DmsDocumentAnalyzer.ExtractTitle).
        /// </summary>
        private (int? AdresseId, string? Name) FindKnownAdresse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return (null, null);

            var adressen = _db.LadeAdressen();

            var viaAlias = _adressSvc.TryMatch(null, null, text);
            if (viaAlias.HasValue)
            {
                var byAlias = adressen.FirstOrDefault(a => a.Id == viaAlias.Value);
                if (byAlias != null) return (byAlias.Id, byAlias.Name);
            }

            foreach (var a in adressen)
            {
                if (!string.IsNullOrWhiteSpace(a.Name) && a.Name.Trim().Length >= 4 &&
                    text.IndexOf(a.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return (a.Id, a.Name);
            }

            return (null, null);
        }

        private static bool WaitUntilFileReady(string path, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (!File.Exists(path)) return false;
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(300);
                }
            }
            return false;
        }

        private (string? Text, string? TextSource, string OcrStatus) ExtractText(string path, string ext)
        {
            var (tessExe, langsDb) = _db.GetOcrSettings();
            bool canTesseract = !string.IsNullOrWhiteSpace(tessExe) && File.Exists(tessExe);
            string langs = string.IsNullOrWhiteSpace(langsDb) ? "deu+eng" : langsDb;

            if (ext == ".pdf")
            {
                var text = OcrService.ExtractTextFromPdf_NoOcr(path);
                if (!string.IsNullOrWhiteSpace(text))
                    return (text, "pdf", "Text");

                if (!canTesseract)
                    return (null, null, "Image");

                var ocrText = OcrService.ExtractTextFromPdf_ByImages(path, langs, maxPages: 50, maxImagesPerPage: 2);
                return string.IsNullOrWhiteSpace(ocrText)
                    ? (null, null, "Image")
                    : (ocrText, "pdf-ocr", "OCR");
            }

            // Bilddatei
            if (!canTesseract)
                return (null, null, "Image");

            var imgText = OcrService.ExtractTextWithTesseract(path, langs);
            return string.IsNullOrWhiteSpace(imgText)
                ? (null, null, "Image")
                : (imgText, "img", "OCR");
        }

        private void TryMatchTransaktion(int attachmentId, DateTime docDatum, List<(decimal Amount, int Score)> betragsKandidaten, int? matchedAdresseId, bool datumAusDokument)
        {
            if (betragsKandidaten.Count == 0)
            {
                CurrentPhase = "Kein Betrag erkannt – im DMS unter „Frei“ verfügbar.";
                return;
            }

            // Wurde das Rechnungsdatum aus dem Dokument gelesen, kann die Zahlung nicht
            // davor liegen (eine Rechnung vom 03.08. ist nicht am 30.07. bezahlt) –
            // Fenster nach hinten also 0 Tage. Nur wenn das Datum bloss ein Fallback
            // ist (Dateidatum), bleibt Spielraum: der Scan kann nach der Zahlung liegen.
            var tageVorher = datumAusDokument ? 0 : 10;

            // Die Gewichtung der Betragskandidaten (Nähe zu "Total" etc.) ist eine Heuristik und
            // kann danebenliegen (z. B. bei Mehrspalten-Layouts). Daher mehrere Kandidaten der
            // Reihe nach probieren, statt blind nur dem bestbewerteten zu vertrauen.
            var kandidaten = new List<Transaktion>();
            bool niedrigeZuversicht = false;
            foreach (var (betrag, score) in betragsKandidaten.Take(5))
            {
                kandidaten = _db.FindCandidateTransaktionenForMatch(betrag, docDatum, tageVorher: tageVorher, tageNachher: 60);
                if (kandidaten.Count > 0)
                {
                    // Nicht still verknüpfen, wenn der Treffer NICHT auf dem bestbewerteten
                    // Betrag (dem mutmasslichen Total) beruht: Ein Positionsbetrag kann
                    // zufällig auf eine fremde Transaktion passen (beobachteter Fall:
                    // 81.80 aus der Positionszeile statt Total 84.60).
                    niedrigeZuversicht = score <= 0 || betrag != betragsKandidaten[0].Amount;
                    break;
                }
            }

            if (kandidaten.Count > 1 && matchedAdresseId.HasValue)
            {
                var engere = kandidaten.Where(k => k.AdresseId == matchedAdresseId.Value).ToList();
                if (engere.Count >= 1)
                    kandidaten = engere;
            }

            if (kandidaten.Count == 0)
            {
                CurrentPhase = "Kein automatischer Treffer – im DMS unter „Frei“ verfügbar.";
                return;
            }

            if (kandidaten.Count == 1 && !niedrigeZuversicht)
            {
                _attachSvc.LinkToTransaktion(attachmentId, kandidaten[0].Id);
                CurrentPhase = $"Automatisch zugeordnet: Transaktion #{kandidaten[0].Id}";
                return;
            }

            // Entweder mehrdeutig, oder der einzige Treffer beruht auf einem Betrag ohne
            // erkennbaren Bezug zu "Total"/"Betrag" im Text (reine Fliesstext-Zahl) – dann nicht
            // still verknüpfen, sondern durch den User bestätigen lassen. Vermeidet z. B. eine
            // zufällig passende, aber falsche Transaktion (bereits beobachteter Fall: CHF 90 aus
            // einer Positionszeile statt des echten Totals CHF 421.15).
            CurrentPhase = kandidaten.Count > 1
                ? "Mehrere passende Transaktionen – Auswahl erforderlich…"
                : "Unsicherer Treffer – Bestätigung erforderlich…";

            var gewaehlt = AskUserToPickTransaktion(kandidaten);
            if (gewaehlt.HasValue)
            {
                _attachSvc.LinkToTransaktion(attachmentId, gewaehlt.Value);
                CurrentPhase = $"Zugeordnet: Transaktion #{gewaehlt.Value}";
            }
            else
            {
                CurrentPhase = "Zuordnung zurückgestellt – im DMS unter „Frei“ verfügbar.";
            }
        }

        private int? AskUserToPickTransaktion(List<Transaktion> kandidaten)
        {
            var dispatcher = Application.Current?.Dispatcher;

            // App fährt gerade herunter: kein Dialog mehr möglich –
            // Dokument bleibt unverknüpft und ist im DMS unter „Frei" verfügbar.
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return null;

            try
            {
                return dispatcher.Invoke(() =>
                {
                    var dlg = new DmsAssignTransactionDialog(kandidaten) { Owner = Application.Current?.MainWindow };
                    return dlg.ShowDialog() == true ? dlg.AusgewaehlteTransaktionId : null;
                });
            }
            catch (TaskCanceledException)
            {
                return null; // Shutdown während des Wartens auf den Dialog
            }
        }

        private static void LogError(string context, Exception ex)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyCoinFlow");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DMS-Watcher ({context}): {ex}\r\n");
            }
            catch { /* Logging darf nicht selbst crashen */ }
        }
    }
}
